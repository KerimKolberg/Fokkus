package com.kk.fokkus.block

import android.accessibilityservice.AccessibilityService
import android.content.ComponentName
import android.content.Intent
import android.net.Uri
import android.os.SystemClock
import android.provider.Browser
import android.view.accessibility.AccessibilityEvent
import com.kk.fokkus.data.Domains
import com.kk.fokkus.data.SettingsStore
import com.kk.fokkus.timer.TimerEngine
import com.kk.fokkus.timer.TimerService
import kotlinx.coroutines.MainScope
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch

/**
 * Watches which app (and, in browsers, which website) is in front.
 * During focus it covers anything that isn't allowed with the lock screen.
 */
class BlockerService : AccessibilityService() {
    private val scope = MainScope()
    private var foreground: String? = null
    private var lastBlockedPkg: String? = null
    private var lastBlockAt = 0L
    private val lastUrlCheck = HashMap<String, Long>()
    private var lastSiteHost: String? = null
    private var lastSiteAt = 0L

    override fun onServiceConnected() {
        super.onServiceConnected()
        running = true
        Blocking.invalidate()
        scope.launch {
            TimerEngine.state.map { it.lockedIn }.distinctUntilChanged().collect { locked ->
                if (locked) recheckForeground()
            }
        }
        // After a reboot the accessibility service starts first: bring a running session back.
        if (TimerEngine.current.inSession) {
            TimerEngine.tick()
            TimerService.sync(this)
        }
    }

    override fun onAccessibilityEvent(event: AccessibilityEvent?) {
        event ?: return
        val pkg = event.packageName?.toString() ?: return
        when (event.eventType) {
            AccessibilityEvent.TYPE_WINDOW_STATE_CHANGED -> {
                val fg = activityPackage(pkg, event.className?.toString()) ?: return
                foreground = fg
                checkApp(fg)
                if (Blocking.isFilterableBrowser(fg)) checkBrowser(fg, force = true)
            }
            AccessibilityEvent.TYPE_WINDOW_CONTENT_CHANGED -> {
                if (pkg == foreground && Blocking.isFilterableBrowser(pkg)) checkBrowser(pkg, force = false)
            }
        }
    }

    /** Only real activity switches count (not dialogs, toasts or the notification shade). */
    private fun activityPackage(pkg: String, cls: String?): String? {
        if (cls == null) return null
        return try {
            packageManager.getActivityInfo(ComponentName(pkg, cls), 0)
            pkg
        } catch (e: Exception) {
            null
        }
    }

    private fun recheckForeground() {
        val pkg = try { rootInActiveWindow?.packageName?.toString() } catch (e: Exception) { null } ?: foreground ?: return
        foreground = pkg
        checkApp(pkg)
        if (Blocking.isFilterableBrowser(pkg)) checkBrowser(pkg, force = true)
    }

    private fun checkApp(pkg: String) {
        TimerEngine.tick()
        if (!Blocking.isAppBlocked(this, pkg)) return
        val now = SystemClock.elapsedRealtime()
        if (pkg == lastBlockedPkg && now - lastBlockAt < 1200) return
        lastBlockedPkg = pkg
        lastBlockAt = now
        BlockActivity.show(this, pkg, null)
    }

    private fun checkBrowser(pkg: String, force: Boolean) {
        val s = SettingsStore.current
        if (!TimerEngine.current.lockedIn || !s.webFilter) return
        val now = SystemClock.elapsedRealtime()
        if (!force && now - (lastUrlCheck[pkg] ?: 0L) < 700) return
        lastUrlCheck[pkg] = now
        val root = try { rootInActiveWindow } catch (e: Exception) { null } ?: return
        if (root.packageName?.toString() != pkg) return
        var text: String? = null
        for (id in Blocking.browserUrlBars[pkg].orEmpty()) {
            val node = try { root.findAccessibilityNodeInfosByViewId(id)?.firstOrNull() } catch (e: Exception) { null } ?: continue
            if (node.isFocused) return // the user is typing in the address bar
            text = node.text?.toString()
            if (text != null) break
        }
        val host = Domains.hostFromAddressBar(text ?: return) ?: return
        if (Domains.siteAllowed(host, s)) return
        if (host == lastSiteHost && now - lastSiteAt < 4000) return
        lastSiteHost = host
        lastSiteAt = now
        leaveBlockedSite(pkg, host)
    }

    /** Opens an allowed page in the same browser tab slot, then shows the lock screen on top. */
    private fun leaveBlockedSite(pkg: String, host: String) {
        val safe = Domains.safeUrl(SettingsStore.current)
        if (safe != null) {
            try {
                startActivity(
                    Intent(Intent.ACTION_VIEW, Uri.parse(safe))
                        .setPackage(pkg)
                        .putExtra(Browser.EXTRA_APPLICATION_ID, packageName)
                        .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                )
            } catch (e: Exception) {
                performGlobalAction(GLOBAL_ACTION_BACK)
            }
        } else {
            performGlobalAction(GLOBAL_ACTION_HOME)
        }
        BlockActivity.show(this, pkg, host)
    }

    override fun onInterrupt() {}

    override fun onDestroy() {
        running = false
        scope.cancel()
        super.onDestroy()
    }

    companion object {
        @Volatile
        var running = false
            private set
    }
}
