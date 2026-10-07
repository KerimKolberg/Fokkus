using System;
using System.Collections.Generic;
using System.Linq;

namespace Fokkus
{
    class Preset
    {
        public readonly string Name;
        public readonly string[] Items;
        public Preset(string name, params string[] items) { Name = name; Items = items; }
    }

    static class Presets
    {
        // Sites you typically NEED while studying (used in "allow only my list" mode).
        public static readonly Preset[] AllowSites =
        {
            new Preset("TUM", "tum.de", "mytum.de", "tum.live", "lrz.de"),
            new Preset("AI", "chatgpt.com", "openai.com", "oaistatic.com", "oaiusercontent.com", "claude.ai", "claude.com",
                       "anthropic.com", "perplexity.ai", "deepl.com", "copilot.microsoft.com", "mistral.ai", "deepseek.com"),
            new Preset("Google Drive & Docs", "google.com", "google.de", "gstatic.com", "googleapis.com", "googleusercontent.com"),
            new Preset("Notes", "notion.so", "notion.com", "notion.site", "obsidian.md", "onenote.com", "evernote.com",
                       "goodnotes.com", "overleaf.com"),
            new Preset("Microsoft 365", "office.com", "microsoft365.com", "office.net", "live.com", "microsoftonline.com",
                       "sharepoint.com", "onedrive.com", "msauth.net", "msftauth.net", "microsoft.com"),
            new Preset("PDF tools", "ilovepdf.com", "smallpdf.com", "pdf24.org", "sejda.com", "adobe.com", "adobelogin.com", "adobe.io"),
            new Preset("Reference", "wikipedia.org", "wikimedia.org", "wolframalpha.com", "desmos.com", "geogebra.org", "arxiv.org",
                       "leo.org", "dict.cc", "stackoverflow.com", "stackexchange.com"),
            new Preset("Coding", "github.com", "githubusercontent.com", "githubassets.com", "gitlab.com", "python.org",
                       "developer.mozilla.org", "w3schools.com", "cppreference.com", "readthedocs.io", "pypi.org"),
            new Preset("Essentials", "cloudflare.com", "jsdelivr.net", "unpkg.com", "recaptcha.net", "hcaptcha.com", "msftconnecttest.com"),
        };

        public static readonly string[] DefaultAllowPresets = { "TUM", "AI", "Google Drive & Docs", "Notes", "Microsoft 365", "PDF tools", "Reference", "Essentials" };

        // Typical distractions (used in "block my list" mode, and to recognise distractions in allow mode).
        public static readonly Preset[] BlockSites =
        {
            new Preset("Social", "instagram.com", "cdninstagram.com", "facebook.com", "fbcdn.net", "messenger.com", "tiktok.com",
                       "tiktokcdn.com", "tiktokv.com", "x.com", "twitter.com", "twimg.com", "reddit.com", "redd.it", "redditmedia.com",
                       "redditstatic.com", "snapchat.com", "pinterest.com", "tumblr.com", "threads.net", "threads.com", "bsky.app", "9gag.com"),
            new Preset("Video", "youtube.com", "youtu.be", "ytimg.com", "googlevideo.com", "youtube-nocookie.com", "netflix.com",
                       "nflxvideo.net", "twitch.tv", "primevideo.com", "disneyplus.com", "joyn.de", "crunchyroll.com"),
            new Preset("Chat", "whatsapp.com", "whatsapp.net", "discord.com", "discord.gg", "discordapp.com", "telegram.org"),
            new Preset("Shopping", "amazon.de", "amazon.com", "ebay.de", "ebay.com", "zalando.de", "aliexpress.com", "temu.com",
                       "shein.com", "kleinanzeigen.de"),
            new Preset("News", "spiegel.de", "bild.de", "n-tv.de", "focus.de", "welt.de", "tagesschau.de", "cnn.com", "bbc.com"),
            new Preset("Games", "steampowered.com", "steamcommunity.com", "epicgames.com", "chess.com", "lichess.org", "poki.com",
                       "crazygames.com", "roblox.com"),
        };

        public static readonly string[] DefaultBlockPresets = { "Social", "Video", "Chat", "Shopping" };

        // Windows process names (without .exe).
        public static readonly Preset[] Apps =
        {
            new Preset("Chat", "Discord", "Telegram", "WhatsApp", "WhatsApp.Root", "Signal", "Messenger"),
            new Preset("Gaming", "steam", "EpicGamesLauncher", "Battle.net", "RiotClientServices", "LeagueClient", "EADesktop",
                       "GalaxyClient", "upc", "Minecraft.Windows"),
            new Preset("Music & video", "Spotify", "vlc", "PrimeVideo"),
        };

        public static readonly string[] DefaultAppPresets = { "Chat", "Gaming" };

        public static List<string> Collect(Preset[] presets, string[] names)
        {
            return presets.Where(p => names.Contains(p.Name)).SelectMany(p => p.Items)
                          .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        }

        static HostRules distractions;

        public static bool IsKnownDistraction(string host)
        {
            if (distractions == null)
                distractions = new HostRules(true, BlockSites.SelectMany(p => p.Items));
            return distractions.Matches(HostRules.CleanHost(host));
        }
    }
}
