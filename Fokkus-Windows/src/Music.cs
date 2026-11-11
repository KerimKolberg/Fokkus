using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Fokkus
{
    public class CustomStation
    {
        public string Name { get; set; }
        public string Url { get; set; }
    }

    class Station
    {
        public readonly string Id, Name, Credit;
        public readonly string[] Urls;   // mirrors, tried in order; null = generated offline noise

        public Station(string id, string name, string credit, string[] urls) { Id = id; Name = name; Credit = credit; Urls = urls; }

        public bool Offline { get { return Urls == null; } }
    }

    static class Stations
    {
        // SomaFM: listener-supported internet radio; the mirrors are the ones in their official playlists.
        static string[] Soma(string channel)
        {
            return new[] { "ice6", "ice2", "ice5" }.Select(s => "https://" + s + ".somafm.com/" + channel + "-128-mp3").ToArray();
        }

        public static readonly Station[] BuiltIn =
        {
            new Station("lofi", "Lofi beats", "SomaFM Fluid", Soma("fluid")),
            new Station("chill", "Chill grooves", "SomaFM Groove Salad", Soma("groovesalad")),
            new Station("ambient", "Ambient", "SomaFM Drone Zone", Soma("dronezone")),
            new Station("space", "Deep space", "SomaFM Deep Space One", Soma("deepspaceone")),
            new Station("brown", "Brown noise", "made by Fokkus, works offline", null),
        };

        public static List<Station> All(AppSettings s)
        {
            var list = BuiltIn.ToList();
            foreach (var c in s.CustomStations)
                list.Add(new Station("custom:" + c.Url, string.IsNullOrWhiteSpace(c.Name) ? HostOf(c.Url) : c.Name, "your stream", new[] { c.Url }));
            return list;
        }

        public static Station Find(AppSettings s, string id)
        {
            return All(s).FirstOrDefault(x => x.Id == id) ?? BuiltIn[0];
        }

        public static string HostOf(string url)
        {
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u) ? u.Host : url;
        }

        // Domains the website filter has to let through while the music plays.
        public static IEnumerable<string> Hosts(AppSettings s)
        {
            if (!s.MusicOn) return new string[0];
            var st = Find(s, s.MusicStation);
            if (st.Offline) return new string[0];
            return st.Urls.Select(u => HostRules.BaseDomain(HostOf(u))).Where(h => h.Length > 0).Distinct().ToList();
        }

        public static bool IsStreamUrl(string url)
        {
            Uri u;
            return Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out u) && (u.Scheme == "http" || u.Scheme == "https");
        }
    }

    // Plays the focus music with Windows Media Player's engine (late-bound COM, nothing to install).
    class MusicPlayer : IDisposable
    {
        object wmp;
        Station current;
        int mirror, volume = 40;
        DateTime startedUtc;
        bool playing, wmpMissing;

        public string Error { get; private set; }
        public bool Playing { get { return playing; } }
        public Station Current { get { return current; } }

        bool Ensure()
        {
            if (wmp != null) return true;
            if (wmpMissing) return false;
            try
            {
                var t = Type.GetTypeFromProgID("WMPlayer.OCX.7") ?? Type.GetTypeFromProgID("WMPlayer.OCX");
                if (t == null) throw new InvalidOperationException("not registered");
                wmp = Activator.CreateInstance(t);
                Set(Get(wmp, "settings"), "autoStart", false);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                wmpMissing = true;
                Error = "Windows Media Player isn't installed (Settings → Apps → Optional features).";
                return false;
            }
        }

        public void Play(Station st, int vol)
        {
            volume = vol;
            if (playing && current == st) { ApplyVolume(); return; }
            if (!Ensure()) return;
            current = st;
            mirror = 0;
            Start();
        }

        void Start()
        {
            try
            {
                string url = current.Offline ? Noise.BrownNoiseFile() : current.Urls[mirror % current.Urls.Length];
                var settings = Get(wmp, "settings");
                Call(settings, "setMode", "loop", current.Offline);
                Set(wmp, "URL", url);
                ApplyVolume();
                Call(Get(wmp, "controls"), "play");
                playing = true;
                startedUtc = DateTime.UtcNow;
                Error = null;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                playing = false;
                Error = "Couldn't play " + current.Name + ".";
            }
        }

        void ApplyVolume()
        {
            try { if (wmp != null) Set(Get(wmp, "settings"), "volume", volume); } catch { }
        }

        public void SetVolume(int vol) { volume = vol; ApplyVolume(); }

        public void Stop()
        {
            if (!playing) return;
            playing = false;
            try { Call(Get(wmp, "controls"), "stop"); } catch (Exception ex) { Log.Error(ex); }
        }

        // Called every few seconds: a stream that doesn't get going is retried on the next mirror.
        public void Check()
        {
            if (!playing || current == null || current.Offline) return;
            int state;
            try { state = Convert.ToInt32(Get(wmp, "playState")); } catch { return; }
            if (state == 3) return;                                   // playing
            double secs = (DateTime.UtcNow - startedUtc).TotalSeconds;
            bool waiting = state == 6 || state == 7 || state == 9 || state == 11;   // buffering / waiting / transitioning / reconnecting
            if (waiting ? secs < 25 : secs < 8) return;
            mirror++;
            if (mirror >= current.Urls.Length * 2)
            {
                Stop();
                Error = "Can't reach “" + current.Name + "” right now. Try another station or Brown noise.";
                return;
            }
            Start();
        }

        public void Dispose()
        {
            Stop();
            if (wmp == null) return;
            try { Call(wmp, "close"); } catch { }
            try { Marshal.ReleaseComObject(wmp); } catch { }
            wmp = null;
        }

        static object Get(object o, string name) { return o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, null); }
        static void Set(object o, string name, object v) { o.GetType().InvokeMember(name, BindingFlags.SetProperty, null, o, new[] { v }); }
        static object Call(object o, string name, params object[] args) { return o.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, o, args); }
    }

    static class Noise
    {
        // 30 s of brown noise that loops without a click (the end fades into the start).
        public static byte[] BrownNoiseWav(int seconds = 30, int rate = 22050)
        {
            int n = seconds * rate, fade = rate / 2;
            var x = new double[n + fade];
            var rnd = new Random(42);
            double b = 0;
            for (int i = 0; i < x.Length; i++)
            {
                b = (b + 0.02 * (rnd.NextDouble() * 2 - 1)) / 1.02;
                x[i] = b * 3.5;
            }
            var y = new short[n];
            for (int i = 0; i < n; i++)
            {
                double v = x[i + fade];
                int j = i - (n - fade);
                if (j >= 0) { double t = j / (double)fade; v = v * (1 - t) + x[j] * t; }
                y[i] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, v * 0.9 * short.MaxValue));
            }
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                int data = n * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + data); w.Write(new[] { 'W', 'A', 'V', 'E' });
                w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(data);
                foreach (var s in y) w.Write(s);
                w.Flush();
                return ms.ToArray();
            }
        }

        public static string BrownNoiseFile()
        {
            string path = Path.Combine(AppSettings.Dir, "brown-noise.wav");
            if (!File.Exists(path)) File.WriteAllBytes(path, BrownNoiseWav());
            return path;
        }
    }
}
