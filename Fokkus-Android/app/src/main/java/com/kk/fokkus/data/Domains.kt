package com.kk.fokkus.data

/** Domain rules: "tum.de" matches tum.de and every subdomain (moodle.tum.de, campus.tum.de...). */
object Domains {

    /** "https://www.Moodle.tum.de/course?id=1" -> "moodle.tum.de"; returns null for junk. */
    fun normalize(input: String): String? {
        var s = input.trim().lowercase()
        if (s.isEmpty()) return null
        val scheme = s.indexOf("://")
        if (scheme >= 0) s = s.substring(scheme + 3)
        s = s.substringBefore('/').substringBefore('?').substringBefore('#').substringAfterLast('@')
        if (s.count { it == ':' } == 1) s = s.substringBefore(':')
        while (s.startsWith("*.") || s.startsWith(".")) s = s.removePrefix("*.").removePrefix(".")
        s = s.removePrefix("www.").trim('.')
        if (s.isEmpty() || s.length > 253 || s.any { it.isWhitespace() || it == '*' }) return null
        return s
    }

    fun matches(host: String, rules: Collection<String>): Boolean {
        val h = host.lowercase().trimEnd('.')
        return rules.any { r -> h == r || h.endsWith(".$r") }
    }

    fun siteAllowed(host: String, s: AppSettings): Boolean =
        if (s.webAllowMode) matches(host, s.activeAllowedSites) else !matches(host, s.activeBlockedSites)

    /** Reads the host out of what a browser shows in its address bar ("moodle.tum.de/course/..."). */
    fun hostFromAddressBar(raw: String): String? {
        var t = raw.trim().lowercase()
        if (t.isEmpty() || t.any { it.isWhitespace() }) return null
        val scheme = t.indexOf("://")
        if (scheme >= 0) {
            val sch = t.substring(0, scheme)
            if (sch != "http" && sch != "https") return null
            t = t.substring(scheme + 3)
        } else if (Regex("^[a-z-]+:").containsMatchIn(t) && !Regex("^[^/]+:\\d+").containsMatchIn(t)) {
            return null // chrome://, about:, file:, content:, data: ...
        }
        t = t.substringBefore('/').substringBefore('?').substringBefore('#').substringAfterLast('@').substringBefore(':')
        if (!t.contains('.')) return null
        if (!t.all { it.isLetterOrDigit() || it == '.' || it == '-' }) return null
        return t.trimEnd('.')
    }

    /** A page that is allowed during focus, used to replace a blocked tab. */
    fun safeUrl(s: AppSettings): String? {
        val candidates = listOf("google.com", "tum.de", "wikipedia.org") + s.allowedSites
        val ok = candidates.firstOrNull { siteAllowed(it, s) } ?: return null
        return "https://" + (if (ok.count { it == '.' } == 1) "www.$ok" else ok)
    }
}
