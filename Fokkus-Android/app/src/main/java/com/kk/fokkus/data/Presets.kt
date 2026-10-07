package com.kk.fokkus.data

class Preset(val name: String, val items: List<String>)

object Presets {
    /** Sites you typically NEED while studying ("allow only my list" mode). */
    val allowSites = listOf(
        Preset("TUM", listOf("tum.de", "mytum.de", "tum.live", "lrz.de")),
        Preset("AI", listOf("chatgpt.com", "openai.com", "oaistatic.com", "oaiusercontent.com", "claude.ai", "claude.com",
            "anthropic.com", "perplexity.ai", "deepl.com", "copilot.microsoft.com", "mistral.ai", "deepseek.com")),
        Preset("Google Drive & Docs", listOf("google.com", "google.de", "gstatic.com", "googleapis.com", "googleusercontent.com")),
        Preset("Notes", listOf("notion.so", "notion.com", "notion.site", "obsidian.md", "onenote.com", "evernote.com",
            "goodnotes.com", "overleaf.com")),
        Preset("Microsoft 365", listOf("office.com", "microsoft365.com", "office.net", "live.com", "microsoftonline.com",
            "sharepoint.com", "onedrive.com", "msauth.net", "msftauth.net", "microsoft.com")),
        Preset("PDF tools", listOf("ilovepdf.com", "smallpdf.com", "pdf24.org", "sejda.com", "adobe.com", "adobelogin.com", "adobe.io")),
        Preset("Reference", listOf("wikipedia.org", "wikimedia.org", "wolframalpha.com", "desmos.com", "geogebra.org", "arxiv.org",
            "leo.org", "dict.cc", "stackoverflow.com", "stackexchange.com")),
        Preset("Coding", listOf("github.com", "githubusercontent.com", "githubassets.com", "gitlab.com", "python.org",
            "developer.mozilla.org", "w3schools.com", "cppreference.com", "readthedocs.io", "pypi.org")),
        Preset("Essentials", listOf("cloudflare.com", "jsdelivr.net", "unpkg.com", "recaptcha.net", "hcaptcha.com")),
    )

    /** Typical distractions ("block my list" mode). */
    val blockSites = listOf(
        Preset("Social", listOf("instagram.com", "cdninstagram.com", "facebook.com", "fbcdn.net", "messenger.com", "tiktok.com",
            "x.com", "twitter.com", "reddit.com", "redd.it", "snapchat.com", "pinterest.com", "tumblr.com", "threads.net",
            "threads.com", "bsky.app", "9gag.com")),
        Preset("Video", listOf("youtube.com", "youtu.be", "netflix.com", "twitch.tv", "primevideo.com", "disneyplus.com",
            "joyn.de", "crunchyroll.com")),
        Preset("Chat", listOf("whatsapp.com", "discord.com", "telegram.org")),
        Preset("Shopping", listOf("amazon.de", "amazon.com", "ebay.de", "ebay.com", "zalando.de", "aliexpress.com", "temu.com",
            "shein.com", "kleinanzeigen.de")),
        Preset("News", listOf("spiegel.de", "bild.de", "n-tv.de", "focus.de", "welt.de", "tagesschau.de", "cnn.com", "bbc.com")),
        Preset("Games", listOf("chess.com", "lichess.org", "poki.com", "crazygames.com", "roblox.com")),
    )

    val defaultAllowedSites: List<String> = collect(allowSites,
        "TUM", "AI", "Google Drive & Docs", "Notes", "Microsoft 365", "PDF tools", "Reference", "Essentials")
    val defaultBlockedSites: List<String> = collect(blockSites, "Social", "Video", "Chat", "Shopping")

    private fun collect(presets: List<Preset>, vararg names: String) =
        presets.filter { it.name in names }.flatMap { it.items }.distinct().sorted()

    /** Pre-ticked on first start if they are installed. */
    val recommendedApps = setOf(
        // Google & files
        "com.google.android.apps.docs", "com.google.android.apps.docs.editors.docs", "com.google.android.apps.docs.editors.sheets",
        "com.google.android.apps.docs.editors.slides", "com.google.android.keep", "com.google.android.calendar",
        "com.google.android.apps.nbu.files", "com.google.android.apps.translate", "com.google.android.apps.messaging",
        // calculators / clocks / calendars / files from various phone brands
        "com.google.android.calculator", "com.sec.android.app.popupcalculator", "com.miui.calculator", "com.android.calculator2",
        "com.oneplus.calculator", "com.coloros.calculator", "com.samsung.android.calendar", "com.sec.android.app.myfiles",
        "com.mi.android.globalFileexplorer", "com.samsung.android.messaging",
        // uni
        "com.moodle.moodlemobile", "de.tum.in.tumcampusapp", "de.tum.in.tumcampus", "app.eduroam.geteduroam",
        // AI
        "com.openai.chatgpt", "com.anthropic.claude", "com.google.android.apps.bard", "ai.perplexity.app.android",
        "com.deepl.mobiletranslator", "com.microsoft.copilot",
        // notes & documents
        "notion.id", "md.obsidian", "com.microsoft.office.onenote", "com.evernote", "com.adobe.reader", "com.xodo.pdf.reader",
        "com.microsoft.office.officehubrow", "com.microsoft.office.word", "com.microsoft.office.excel",
        "com.microsoft.office.powerpoint", "com.microsoft.teams", "com.microsoft.skydrive", "com.ichi2.anki",
        "com.quizlet.quizletandroid", "com.wolfram.android.alpha", "org.geogebra.android", "org.geogebra.android.calculator.suite",
        // 2-factor login
        "com.google.android.apps.authenticator2", "com.azure.authenticator",
    )
}
