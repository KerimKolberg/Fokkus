package com.kk.fokkus.timer

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.res.ColorStateList
import android.os.Build
import android.os.SystemClock
import android.widget.RemoteViews
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.kk.fokkus.MainActivity
import com.kk.fokkus.R
import com.kk.fokkus.data.Planner
import com.kk.fokkus.data.SettingsStore
import com.kk.fokkus.data.Tip
import com.kk.fokkus.data.TipKind
import com.kk.fokkus.data.Tips
import com.kk.fokkus.data.TimerDisplay
import java.text.DateFormat
import java.util.Date

object Notifications {
    const val CH_TIMER = "timer"
    const val CH_ALERTS = "alerts"
    const val CH_ALERTS_QUIET = "alerts_quiet"
    const val CH_TIPS = "tips"
    const val ID_TIMER = 1
    const val ID_ALERT = 2
    const val ID_TIP = 3

    const val FOCUS_COLOR = 0xFF3CBCB9.toInt()
    const val SHORT_COLOR = 0xFFF2B84B.toInt()
    const val LONG_COLOR = 0xFFA78BFA.toInt()

    fun phaseColor(p: Phase) = when (p) {
        Phase.FOCUS -> FOCUS_COLOR
        Phase.SHORT_BREAK -> SHORT_COLOR
        Phase.LONG_BREAK -> LONG_COLOR
    }

    fun phaseName(p: Phase) = when (p) {
        Phase.FOCUS -> "Focus"
        Phase.SHORT_BREAK -> "Short break"
        Phase.LONG_BREAK -> "Long break"
    }

    fun createChannels(ctx: Context) {
        val nm = ctx.getSystemService(NotificationManager::class.java)
        nm.createNotificationChannel(NotificationChannel(CH_TIMER, "Timer", NotificationManager.IMPORTANCE_LOW).apply {
            description = "The running focus timer"
            setShowBadge(false)
        })
        nm.createNotificationChannel(NotificationChannel(CH_ALERTS, "Phase changes", NotificationManager.IMPORTANCE_HIGH).apply {
            description = "When a focus session or break ends"
            enableVibration(true)
        })
        nm.createNotificationChannel(NotificationChannel(CH_ALERTS_QUIET, "Phase changes (silent)", NotificationManager.IMPORTANCE_HIGH).apply {
            description = "When a focus session or break ends, without sound"
            setSound(null, null)
            enableVibration(true)
        })
        nm.createNotificationChannel(NotificationChannel(CH_TIPS, "Wellbeing tips", NotificationManager.IMPORTANCE_HIGH).apply {
            description = "Reminders to drink, stretch and rest your eyes"
            setSound(null, null)
            enableVibration(true)
            vibrationPattern = longArrayOf(0, 60)
        })
    }

    private fun openApp(ctx: Context): PendingIntent = PendingIntent.getActivity(
        ctx, 0, Intent(ctx, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP),
        PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
    )

    private fun action(ctx: Context, action: String, code: Int): PendingIntent = PendingIntent.getBroadcast(
        ctx, code, Intent(ctx, TimerReceiver::class.java).setAction(action),
        PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
    )

    fun subtitle(st: TimerState, sessions: Int): String = when (st.run) {
        RunState.IDLE -> "Ready to lock in"
        RunState.PAUSED -> "Paused"
        RunState.READY -> "Next: ${phaseName(st.phase)} · tap Start"
        RunState.RUNNING -> {
            val ends = DateFormat.getTimeInstance(DateFormat.SHORT).format(Date(st.endAt))
            val subject = Planner.active(SettingsStore.current)?.name
            if (st.phase == Phase.FOCUS) (subject?.let { "$it · " } ?: "") + "${(st.completedInCycle + 1).coerceAtMost(sessions)}/$sessions · ends $ends"
            else "Relax · ends $ends"
        }
    }

    /** The ongoing notification with a big countdown (the system ticks it, no constant updates needed). */
    fun timerNotification(ctx: Context, st: TimerState): Notification {
        val s = SettingsStore.current
        val strict = TimerEngine.strictLocked
        val color = phaseColor(st.phase)
        val title = when {
            st.lockedIn -> "🔒 Locked in · Focus"
            st.run == RunState.READY -> "${phaseName(st.phase)} is ready"
            else -> phaseName(st.phase)
        }
        val sub = subtitle(st, s.sessionsBeforeLong)
        val remaining = st.remaining()

        val b = NotificationCompat.Builder(ctx, CH_TIMER)
            .setSmallIcon(R.drawable.ic_stat_lock)
            .setColor(color)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setSilent(true)
            .setCategory(NotificationCompat.CATEGORY_STOPWATCH)
            .setVisibility(NotificationCompat.VISIBILITY_PUBLIC)
            .setContentIntent(openApp(ctx))
            .setContentTitle(title)
            .setContentText(sub)

        if (s.display == TimerDisplay.FLOATING) {
            // The bubble shows the time; keep the notification small and quiet.
            b.setPriority(NotificationCompat.PRIORITY_MIN)
            if (st.run == RunState.RUNNING) b.setWhen(st.endAt).setUsesChronometer(true).setChronometerCountDown(true).setShowWhen(true)
        } else {
            val rv = RemoteViews(ctx.packageName, R.layout.notif_timer)
            rv.setTextViewText(R.id.phase, title)
            rv.setTextViewText(R.id.sub, sub)
            rv.setProgressBar(R.id.progress, 1000, (st.progress() * 1000).toInt(), false)
            rv.setChronometer(R.id.chrono, SystemClock.elapsedRealtime() + remaining, null, st.run == RunState.RUNNING)
            rv.setChronometerCountDown(R.id.chrono, true)
            b.setCustomContentView(rv)
                .setStyle(NotificationCompat.DecoratedCustomViewStyle())
                .setPriority(NotificationCompat.PRIORITY_LOW)
        }

        when (st.run) {
            RunState.RUNNING -> if (!strict) b.addAction(R.drawable.ic_pause, "Pause", action(ctx, TimerReceiver.ACTION_TOGGLE, 10))
            RunState.PAUSED -> b.addAction(R.drawable.ic_play, "Resume", action(ctx, TimerReceiver.ACTION_TOGGLE, 10))
            RunState.READY -> b.addAction(R.drawable.ic_play, if (st.phase == Phase.FOCUS) "Start focus" else "Start break", action(ctx, TimerReceiver.ACTION_START, 11))
            RunState.IDLE -> {}
        }
        if (!strict && st.run != RunState.IDLE) {
            b.addAction(R.drawable.ic_skip, "Skip", action(ctx, TimerReceiver.ACTION_SKIP, 12))
            b.addAction(R.drawable.ic_stop, "Stop", action(ctx, TimerReceiver.ACTION_STOP, 13))
        }
        return b.build()
    }

    fun phaseCompleted(ctx: Context, from: Phase, to: Phase, autoStarted: Boolean, note: String? = null) {
        val s = SettingsStore.current
        val minutes = (TimerEngine.durationOf(to) / 60_000L).toInt()
        val title: String
        var text: String
        var actionLabel: String? = null
        if (to == Phase.FOCUS) {
            title = if (autoStarted) "🔒 Back to focus" else "⏰ Break's over"
            text = if (autoStarted) "$minutes minutes. Distractions are locked again — you've got this."
            else "Ready for the next $minutes-minute focus session?"
            if (!autoStarted) actionLabel = "Start focus"
        } else {
            val long = to == Phase.LONG_BREAK
            title = if (long) "🎉 Cycle done — long break!" else "🎉 Focus done — nice work!"
            text = (if (autoStarted) "Your $minutes-minute break has started." else "Time for a $minutes-minute break.")
            if (s.breakSuggestions) {
                val tip = Tips.next(if (long) TipKind.LONG_BREAK else TipKind.BREAK, s.disabledTipCategories)
                text += "\n${tip.icon} ${tip.title}: ${tip.text}"
                FloatingTimer.message("${tip.icon} ${tip.title}")
            }
            if (!autoStarted) actionLabel = "Start break"
        }
        if (note != null) {
            text = "$note\n$text"
            FloatingTimer.message(note)
        }
        val b = NotificationCompat.Builder(ctx, if (s.sound) CH_ALERTS else CH_ALERTS_QUIET)
            .setSmallIcon(R.drawable.ic_stat_lock)
            .setColor(phaseColor(to))
            .setContentTitle(title)
            .setContentText(text.substringBefore('\n'))
            .setStyle(NotificationCompat.BigTextStyle().bigText(text))
            .setAutoCancel(true)
            .setContentIntent(openApp(ctx))
            .setCategory(NotificationCompat.CATEGORY_REMINDER)
            .setTimeoutAfter(if (autoStarted) 60_000L else 10 * 60_000L)
        if (actionLabel != null) b.addAction(R.drawable.ic_play, actionLabel, action(ctx, TimerReceiver.ACTION_START, 20))
        post(ctx, ID_ALERT, b.build())
    }

    fun tip(ctx: Context, tip: Tip) {
        val b = NotificationCompat.Builder(ctx, CH_TIPS)
            .setSmallIcon(R.drawable.ic_stat_lock)
            .setColor(Tips.color(tip.category).toInt())
            .setContentTitle("${tip.icon}  ${tip.title}")
            .setContentText(tip.text)
            .setStyle(NotificationCompat.BigTextStyle().bigText(tip.text))
            .setAutoCancel(true)
            .setTimeoutAfter(25_000L)
            .setCategory(NotificationCompat.CATEGORY_REMINDER)
            .setContentIntent(openApp(ctx))
        post(ctx, ID_TIP, b.build())
        FloatingTimer.message("${tip.icon} ${tip.title}: ${tip.text}")
    }

    private fun post(ctx: Context, id: Int, n: Notification) {
        try {
            NotificationManagerCompat.from(ctx).notify(id, n)
        } catch (e: SecurityException) {
            // Notifications not permitted; the floating bubble still shows messages.
        }
    }
}
