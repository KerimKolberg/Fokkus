using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Fokkus
{
    // System-wide shortcuts (Ctrl+Alt+key) that work from any app.
    // Letters were picked so they don't clash with AltGr characters on a German keyboard (AltGr = Ctrl+Alt).
    class Hotkeys : NativeWindow, IDisposable
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        const int WM_HOTKEY = 0x0312;
        const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;

        public class Binding
        {
            public int Id;
            public Keys Key;
            public string Label;
            public Action Run;
            public bool Registered;
            public string Combo { get { return "Ctrl+Alt+" + Key; } }
        }

        readonly List<Binding> bindings = new List<Binding>();

        public Hotkeys() { CreateHandle(new CreateParams()); }

        public IEnumerable<Binding> Bindings { get { return bindings; } }

        public void Add(Keys key, string label, Action run)
        {
            bindings.Add(new Binding { Id = 0x5F00 + bindings.Count, Key = key, Label = label, Run = run });
        }

        public void Apply(bool on)
        {
            foreach (var b in bindings)
            {
                if (b.Registered) { UnregisterHotKey(Handle, b.Id); b.Registered = false; }
                if (on) b.Registered = RegisterHotKey(Handle, b.Id, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)b.Key);
            }
        }

        // e.g. "Ctrl+Alt+S is used by another app"
        public string Problems(bool on)
        {
            if (!on) return null;
            var taken = bindings.Where(b => !b.Registered).Select(b => b.Combo).ToList();
            return taken.Count == 0 ? null : string.Join(", ", taken) + (taken.Count == 1 ? " is" : " are") + " already used by another app.";
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                var b = bindings.FirstOrDefault(x => x.Id == id);
                if (b != null)
                    try { b.Run(); } catch (Exception ex) { Log.Error(ex); }
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            Apply(false);
            DestroyHandle();
        }
    }
}
