package com.kk.fokkus.block

import android.content.Context
import android.content.Intent
import android.telecom.TelecomManager
import android.view.inputmethod.InputMethodManager
import com.kk.fokkus.data.SettingsStore
import com.kk.fokkus.timer.TimerEngine

object Blocking {

    /** Browsers whose address bar we can read, so they can stay usable with website filtering. */
    val browserUrlBars: Map<String, List<String>> = mapOf(
        "com.android.chrome" to listOf("com.android.chrome:id/url_bar"),
        "com.chrome.beta" to listOf("com.chrome.beta:id/url_bar"),
        "com.chrome.dev" to listOf("com.chrome.dev:id/url_bar"),
        "com.google.android.apps.chrome" to listOf("com.google.android.apps.chrome:id/url_bar"),
        "com.microsoft.emmx" to listOf("com.microsoft.emmx:id/url_bar"),
        "com.brave.browser" to listOf("com.brave.browser:id/url_bar"),
        "com.vivaldi.browser" to listOf("com.vivaldi.browser:id/url_bar"),
        "com.kiwibrowser.browser" to listOf("com.kiwibrowser.browser:id/url_bar"),
        "com.opera.browser" to listOf("com.opera.browser:id/url_field", "com.opera.browser:id/url_bar"),
        "com.opera.mini.native" to listOf("com.opera.mini.native:id/url_field"),
        "com.sec.android.app.sbrowser" to listOf(
            "com.sec.android.app.sbrowser:id/location_bar_edit_text",
            "com.sec.android.app.sbrowser:id/custom_tab_toolbar_url_bar_text",
        ),
        "org.mozilla.firefox" to listOf(
            "org.mozilla.firefox:id/mozac_browser_toolbar_url_view",
            "org.mozilla.firefox:id/url_bar_title",
        ),
        "org.mozilla.firefox_beta" to listOf("org.mozilla.firefox_beta:id/mozac_browser_toolbar_url_view"),
        "org.mozilla.focus" to listOf("org.mozilla.focus:id/mozac_browser_toolbar_url_view", "org.mozilla.focus:id/display_url"),
        "com.duckduckgo.mobile.android" to listOf("com.duckduckgo.mobile.android:id/omnibarTextInput"),
        "com.mi.globalbrowser" to listOf("com.mi.globalbrowser:id/url"),
    )

    fun isFilterableBrowser(pkg: String) = pkg in browserUrlBars

    private val fixedEssentials = setOf(
        "android", "com.android.systemui", "com.android.settings", "com.android.phone", "com.android.server.telecom",
        "com.android.dialer", "com.google.android.dialer", "com.samsung.android.dialer", "com.android.incallui",
        "com.samsung.android.incallui", "com.android.emergency", "com.google.android.apps.safetyhub",
        "com.android.permissioncontroller", "com.google.android.permissioncontroller",
        "com.android.packageinstaller", "com.google.android.packageinstaller",
        "com.android.intentresolver", "com.android.documentsui", "com.google.android.documentsui",
        "com.android.providers.media", "com.google.android.providers.media.module",
        "com.google.android.gms", "com.google.android.gsf",
        // alarm clocks must be able to ring
        "com.google.android.deskclock", "com.android.deskclock", "com.sec.android.app.clockpackage",
        "com.oneplus.deskclock", "com.android.alarmclock", "com.coloros.alarmclock",
    )

    @Volatile
    private var dynamicEssentials: Set<String>? = null

    /** Launcher(s), keyboard(s) and the default phone app are always allowed. */
    fun essentials(ctx: Context): Set<String> {
        dynamicEssentials?.let { return it }
        val set = HashSet(fixedEssentials)
        set += ctx.packageName
        try {
            val home = Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_HOME)
            ctx.packageManager.queryIntentActivities(home, 0).forEach { set += it.activityInfo.packageName }
        } catch (_: Exception) { }
        try {
            ctx.getSystemService(InputMethodManager::class.java)?.enabledInputMethodList?.forEach { set += it.packageName }
        } catch (_: Exception) { }
        try {
            ctx.getSystemService(TelecomManager::class.java)?.defaultDialerPackage?.let { set += it }
        } catch (_: Exception) { }
        dynamicEssentials = set
        return set
    }

    fun invalidate() {
        dynamicEssentials = null
    }

    fun isAppBlocked(ctx: Context, pkg: String): Boolean {
        val s = SettingsStore.current
        if (!TimerEngine.current.lockedIn) return false
        if (pkg in essentials(ctx)) return false
        if (s.blockAppsOn && pkg in s.blockedApps) return true // blocked list wins
        if (!s.allowAppsOn) return false
        if (pkg in s.allowedApps) return false
        if (s.anySiteFilter && isFilterableBrowser(pkg)) return false // usable, but websites are filtered
        return true
    }
}
