package com.kk.fokkus.timer

import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.provider.Settings
import androidx.core.app.ServiceCompat
import androidx.core.content.ContextCompat
import com.kk.fokkus.data.SettingsStore
import com.kk.fokkus.data.TipKind
import com.kk.fokkus.data.Tips
import com.kk.fokkus.data.TimerDisplay

/**
 * Keeps the session alive while you use other apps: ongoing countdown notification,
 * the floating bubble and the periodic wellbeing tips.
 */
class TimerService : Service() {
    private val handler = Handler(Looper.getMainLooper())
    private var floating: FloatingTimer? = null
    private var lastKey: String? = null
    private var focusMsSinceTip = 0L
    private var lastTick = 0L
    private var lastPhaseKey: String? = null

    private val ticker = object : Runnable {
        override fun run() {
            onTick()
            handler.postDelayed(this, 1000L)
        }
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        instance = this
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val st = TimerEngine.current
        // startForeground must always be called after startForegroundService(), even if we stop right away.
        val n = Notifications.timerNotification(this, st)
        try {
            ServiceCompat.startForeground(
                this, Notifications.ID_TIMER, n,
                if (Build.VERSION.SDK_INT >= 34) ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE else 0,
            )
        } catch (e: Exception) {
            stopSelf()
            return START_NOT_STICKY
        }
        if (!st.inSession) {
            shutdown()
            return START_NOT_STICKY
        }
        lastKey = null
        handler.removeCallbacks(ticker)
        lastTick = System.currentTimeMillis()
        handler.post(ticker)
        return START_STICKY
    }

    fun refresh() {
        lastKey = null
        onTick()
    }

    private fun onTick() {
        TimerEngine.tick()
        val st = TimerEngine.current
        if (!st.inSession) {
            shutdown()
            return
        }
        val s = SettingsStore.current
        val now = System.currentTimeMillis()

        // Tips: count focused time (ignore gaps when the phone slept).
        val phaseKey = "${st.phase}|${st.completedInCycle}|${st.totalMs}"
        if (phaseKey != lastPhaseKey) {
            lastPhaseKey = phaseKey
            focusMsSinceTip = 0L
        }
        val delta = (now - lastTick).coerceIn(0L, 5_000L)
        lastTick = now
        if (st.phase == Phase.FOCUS && st.run == RunState.RUNNING) {
            focusMsSinceTip += delta
            if (s.tipsEnabled && focusMsSinceTip >= s.tipIntervalMin * 60_000L) {
                focusMsSinceTip = 0L
                if (st.remaining(now) > 90_000L) Notifications.tip(this, Tips.next(TipKind.MICRO, s.disabledTipCategories))
            }
        }

        // Floating bubble.
        val wantBubble = s.display != TimerDisplay.NOTIFICATION && !bubbleDismissed && Settings.canDrawOverlays(this)
        if (wantBubble) {
            if (floating == null) floating = FloatingTimer(this)
            floating?.show()
            floating?.update(st)
        } else {
            floating?.hide()
            floating = null
        }

        // Notification: rebuilt only when something visible changes (the countdown ticks by itself).
        val key = "${st.phase}|${st.run}|${st.endAt / 1000}|${st.remainingMs / 1000}|${st.remaining(now) / 60_000L}|${TimerEngine.strictLocked}|${s.display}"
        if (key != lastKey) {
            lastKey = key
            try {
                androidx.core.app.NotificationManagerCompat.from(this).notify(Notifications.ID_TIMER, Notifications.timerNotification(this, st))
            } catch (e: SecurityException) { }
        }
    }

    private fun shutdown() {
        handler.removeCallbacks(ticker)
        floating?.hide()
        floating = null
        bubbleDismissed = false
        ServiceCompat.stopForeground(this, ServiceCompat.STOP_FOREGROUND_REMOVE)
        stopSelf()
        if (instance === this) instance = null
    }

    override fun onDestroy() {
        handler.removeCallbacks(ticker)
        floating?.hide()
        floating = null
        if (instance === this) instance = null
        super.onDestroy()
    }

    companion object {
        @Volatile
        private var instance: TimerService? = null

        /** Set when you close the bubble with its × button; it comes back next session. */
        var bubbleDismissed = false

        fun sync(ctx: Context) {
            val st = TimerEngine.current
            val running = instance
            if (st.inSession) {
                if (running != null) running.refresh()
                else try {
                    ContextCompat.startForegroundService(ctx, Intent(ctx, TimerService::class.java))
                } catch (e: Exception) {
                    // Not allowed to start from the background right now; the next UI interaction or alarm will retry.
                }
            } else {
                running?.shutdown()
            }
        }
    }
}
