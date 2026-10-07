using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Fokkus
{
    // A tiny local HTTP/HTTPS proxy. During focus the system proxy points here; requests for hosts that
    // are not allowed are refused, everything else is tunnelled through untouched (no TLS interception).
    class ProxyServer
    {
        static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
        TcpListener listener;
        volatile bool running;

        public volatile HostRules Rules;
        public volatile string StatusText = "";
        public int Port { get; private set; }
        public event Action<string> Blocked; // raised on a thread-pool thread

        public void Start()
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(256);
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            running = true;
            Task.Run(() => AcceptLoop());
        }

        public void Stop()
        {
            running = false;
            try { listener.Stop(); } catch { }
        }

        async Task AcceptLoop()
        {
            while (running)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch
                {
                    if (!running) return;
                    continue;
                }
                Observe(HandleAsync(client));
            }
        }

        static void Observe(Task t)
        {
            t.ContinueWith(x => { var ignored = x.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }

        void OnBlocked(string host)
        {
            var h = Blocked;
            if (h != null) try { h(host); } catch { }
        }

        async Task HandleAsync(TcpClient client)
        {
            TcpClient upstream = null;
            try
            {
                client.NoDelay = true;
                var cs = client.GetStream();
                var head = await ReadHeadAsync(cs).ConfigureAwait(false);
                if (head == null) return;
                string[] lines = head.Text.Split(new[] { "\r\n" }, StringSplitOptions.None);
                string[] first = lines[0].Split(' ');
                if (first.Length < 3) return;
                string method = first[0].ToUpperInvariant(), target = first[1], version = first[2];
                var rules = Rules;

                if (method == "CONNECT")
                {
                    string host; int port;
                    if (!SplitHostPort(target, 443, out host, out port)) return;
                    if (rules != null && !rules.IsAllowed(host))
                    {
                        OnBlocked(host);
                        await WriteAsync(cs, "HTTP/1.1 403 Blocked by Fokkus\r\nContent-Length: 0\r\nConnection: close\r\n\r\n").ConfigureAwait(false);
                        return;
                    }
                    upstream = new TcpClient();
                    upstream.NoDelay = true;
                    await upstream.ConnectAsync(host, port).ConfigureAwait(false);
                    var us = upstream.GetStream();
                    await WriteAsync(cs, "HTTP/1.1 200 Connection Established\r\n\r\n").ConfigureAwait(false);
                    if (head.Extra.Length > 0) await us.WriteAsync(head.Extra, 0, head.Extra.Length).ConfigureAwait(false);
                    await Pipe(cs, us).ConfigureAwait(false);
                }
                else if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    Uri uri;
                    if (!Uri.TryCreate(target, UriKind.Absolute, out uri)) return;
                    string host = uri.Host;
                    if (rules != null && !rules.IsAllowed(host))
                    {
                        OnBlocked(host);
                        await WriteHtmlAsync(cs, "403 Forbidden", BlockPage(host)).ConfigureAwait(false);
                        return;
                    }
                    upstream = new TcpClient();
                    upstream.NoDelay = true;
                    await upstream.ConnectAsync(host, uri.Port).ConfigureAwait(false);
                    var us = upstream.GetStream();
                    var sb = new StringBuilder();
                    sb.Append(method).Append(' ').Append(uri.PathAndQuery).Append(' ').Append(version).Append("\r\n");
                    for (int i = 1; i < lines.Length; i++)
                    {
                        string l = lines[i];
                        if (l.Length == 0) continue;
                        string lower = l.ToLowerInvariant();
                        if (lower.StartsWith("proxy-connection:") || lower.StartsWith("proxy-authorization:") ||
                            lower.StartsWith("connection:") || lower.StartsWith("keep-alive:")) continue;
                        sb.Append(l).Append("\r\n");
                    }
                    sb.Append("Connection: close\r\n\r\n");
                    byte[] hb = Latin1.GetBytes(sb.ToString());
                    await us.WriteAsync(hb, 0, hb.Length).ConfigureAwait(false);
                    if (head.Extra.Length > 0) await us.WriteAsync(head.Extra, 0, head.Extra.Length).ConfigureAwait(false);
                    await Pipe(cs, us).ConfigureAwait(false);
                }
                else
                {
                    await WriteHtmlAsync(cs, "200 OK", InfoPage()).ConfigureAwait(false);
                }
            }
            catch { }
            finally
            {
                try { client.Close(); } catch { }
                if (upstream != null) try { upstream.Close(); } catch { }
            }
        }

        static async Task Pipe(Stream a, Stream b)
        {
            Task t1 = a.CopyToAsync(b, 32768);
            Task t2 = b.CopyToAsync(a, 32768);
            Observe(t1);
            Observe(t2);
            await Task.WhenAny(t1, t2).ConfigureAwait(false);
        }

        class Head
        {
            public string Text;
            public byte[] Extra;
        }

        static async Task<Head> ReadHeadAsync(Stream s)
        {
            var buf = new byte[8192];
            int len = 0;
            while (true)
            {
                if (len == buf.Length)
                {
                    if (buf.Length >= 65536) return null;
                    Array.Resize(ref buf, buf.Length * 2);
                }
                int n = await s.ReadAsync(buf, len, buf.Length - len).ConfigureAwait(false);
                if (n <= 0) return null;
                int from = Math.Max(0, len - 3);
                len += n;
                for (int i = from; i + 3 < len; i++)
                {
                    if (buf[i] == 13 && buf[i + 1] == 10 && buf[i + 2] == 13 && buf[i + 3] == 10)
                    {
                        var h = new Head();
                        h.Text = Latin1.GetString(buf, 0, i);
                        h.Extra = new byte[len - (i + 4)];
                        Buffer.BlockCopy(buf, i + 4, h.Extra, 0, h.Extra.Length);
                        return h;
                    }
                }
            }
        }

        static bool SplitHostPort(string t, int defaultPort, out string host, out int port)
        {
            host = t;
            port = defaultPort;
            if (t.StartsWith("["))
            {
                int end = t.IndexOf(']');
                if (end < 0) return false;
                host = t.Substring(1, end - 1);
                if (end + 2 < t.Length && t[end + 1] == ':' && !int.TryParse(t.Substring(end + 2), out port)) port = defaultPort;
                return host.Length > 0;
            }
            int c = t.LastIndexOf(':');
            if (c > 0)
            {
                host = t.Substring(0, c);
                if (!int.TryParse(t.Substring(c + 1), out port)) port = defaultPort;
            }
            return host.Length > 0;
        }

        static Task WriteAsync(Stream s, string text)
        {
            byte[] b = Latin1.GetBytes(text);
            return s.WriteAsync(b, 0, b.Length);
        }

        static async Task WriteHtmlAsync(Stream s, string status, string html)
        {
            byte[] body = Encoding.UTF8.GetBytes(html);
            string head = "HTTP/1.1 " + status + "\r\nContent-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nContent-Length: " +
                          body.Length + "\r\nConnection: close\r\n\r\n";
            await WriteAsync(s, head).ConfigureAwait(false);
            await s.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
        }

        string BlockPage(string host)
        {
            return Page("\U0001F512 Locked in",
                WebUtility.HtmlEncode(host) + " is blocked during your focus session.",
                WebUtility.HtmlEncode(StatusText));
        }

        string InfoPage()
        {
            return Page("Fokkus", "This is Fokkus' local focus filter.", WebUtility.HtmlEncode(StatusText));
        }

        static string Page(string title, string line, string sub)
        {
            return "<!doctype html><html><head><meta charset='utf-8'><title>Fokkus</title><style>" +
                   "body{margin:0;height:100vh;display:flex;align-items:center;justify-content:center;background:#0e1b30;color:#eaf1f6;font-family:'Segoe UI',sans-serif}" +
                   ".c{text-align:center;max-width:520px;padding:32px}h1{font-weight:600;font-size:34px;margin:0 0 12px}" +
                   "p{font-size:17px;color:#b8c6d6;margin:6px 0}.s{color:#3cbcb9;font-weight:600;margin-top:18px}</style></head>" +
                   "<body><div class='c'><h1>" + title + "</h1><p>" + line + "</p><p class='s'>" + sub + "</p></div></body></html>";
        }
    }
}
