using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Fokkus
{
    // Immutable set of domain rules. A rule "tum.de" matches tum.de and every subdomain (moodle.tum.de, ...).
    class HostRules
    {
        public readonly bool AllowMode;
        readonly string[] rules;

        public HostRules(bool allowMode, IEnumerable<string> list)
        {
            AllowMode = allowMode;
            rules = (list ?? new string[0]).Select(Normalize).Where(x => x != null).Distinct().ToArray();
        }

        public static HostRules From(AppSettings s)
        {
            return new HostRules(s.AllowListMode, s.AllowListMode ? s.AllowedSites : s.BlockedSites);
        }

        public int Count { get { return rules.Length; } }

        public bool IsAllowed(string host)
        {
            host = CleanHost(host);
            if (host.Length == 0 || IsLocal(host)) return true;
            bool match = Matches(host);
            return AllowMode ? match : !match;
        }

        public bool Matches(string host)
        {
            foreach (var r in rules)
                if (host == r || host.EndsWith("." + r, StringComparison.Ordinal)) return true;
            return false;
        }

        static bool IsLocal(string host)
        {
            if (host == "localhost" || host.EndsWith(".localhost") || host.EndsWith(".local")) return true;
            IPAddress ip;
            if (!IPAddress.TryParse(host, out ip)) return host.IndexOf('.') < 0; // single-label intranet names
            if (IPAddress.IsLoopback(ip)) return true;
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                var b = ip.GetAddressBytes();
                return b[0] == 10 || (b[0] == 192 && b[1] == 168) || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 169 && b[1] == 254);
            }
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal;
        }

        public static string CleanHost(string h)
        {
            h = (h ?? "").Trim().ToLowerInvariant();
            if (h.StartsWith("[") && h.EndsWith("]")) h = h.Substring(1, h.Length - 2);
            return h.TrimEnd('.');
        }

        // Turns user input like "https://www.Moodle.tum.de/course?id=1" into "moodle.tum.de".
        public static string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            string s = input.Trim().ToLowerInvariant();
            int scheme = s.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) s = s.Substring(scheme + 3);
            int cut = s.IndexOfAny(new[] { '/', '?', '#' });
            if (cut >= 0) s = s.Substring(0, cut);
            int at = s.LastIndexOf('@');
            if (at >= 0) s = s.Substring(at + 1);
            int colon = s.IndexOf(':');
            if (colon > 0 && colon == s.LastIndexOf(':')) s = s.Substring(0, colon);
            while (s.StartsWith("*.") || s.StartsWith(".")) s = s.Substring(s.StartsWith("*.") ? 2 : 1);
            if (s.StartsWith("www.")) s = s.Substring(4);
            s = s.Trim('.');
            if (s.Length == 0 || s.Length > 253 || s.IndexOf(' ') >= 0 || s.IndexOf('*') >= 0 || s.IndexOf(';') >= 0) return null;
            return s;
        }

        // Approximate registrable domain: "cdn.foo.example.co.uk" -> "example.co.uk".
        public static string BaseDomain(string host)
        {
            host = CleanHost(host);
            IPAddress ip;
            if (IPAddress.TryParse(host, out ip)) return host;
            var parts = host.Split('.');
            if (parts.Length <= 2) return host;
            string tld = parts[parts.Length - 1], sld = parts[parts.Length - 2];
            bool cc = tld.Length == 2 && (sld == "co" || sld == "com" || sld == "ac" || sld == "org" || sld == "net" || sld == "gov" || sld == "edu");
            int take = cc ? 3 : 2;
            if (parts.Length <= take) return host;
            return string.Join(".", parts, parts.Length - take, take);
        }

        // Hosts that bypass the proxy entirely (WinINet "ProxyOverride" syntax).
        public string BuildBypassList()
        {
            var sb = new StringBuilder("<local>;localhost;127.*;10.*;192.168.*;" + SystemProxy.Signature);
            if (AllowMode)
                foreach (var r in rules)
                    sb.Append(';').Append(r).Append(";*.").Append(r);
            return sb.ToString();
        }
    }
}
