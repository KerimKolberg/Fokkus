package com.kk.fokkus.timer

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.SharedPreferences
import android.os.Build
import com.kk.fokkus.data.FocusRecord
import com.kk.fokkus.data.Planner
import com.kk.fokkus.data.SessionLog
import com.kk.fokkus.data.SettingsStore
import java.time.LocalDate
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow

enum class Phase { FOCUS, SHORT_BREAK, LONG_BREAK }

/** IDLE = no session. READY = session on, the next phase waits for the user to press start. */
enum class RunState { IDLE, RUNNING, PAUSED, READY }

data class TimerState(
    val phase: Phase = Phase.FOCUS,
    val run: RunState = RunState.IDLE,
    val endAt: Long = 0L,
    val remainingMs: Long = 25 * 60_000L,
    val totalMs: Long = 25 * 60_000L,
    val completedInCycle: Int = 0,
) {
    fun remaining(now: Long = System.currentTimeMillis()): Long =
        if (run == RunState.RUNNING) (endAt - now).coerceAtLeast(0L) else remainingMs

    fun progress(now: Long = System.currentTimeMillis()): Float =
        if (totalMs <= 0) 0f else (1f - remaining(now).toFloat() / totalMs).coerceIn(0f, 1f)

    /** Blocking is active for a started focus phase (also while paused, so pausing is no loophole). */
    val lockedIn: Boolean get() = phase == Phase.FOCUS && (run == RunState.RUNNING || run == RunState.PAUSED)
    val inSession: Boolean get() = run != RunState.IDLE
}

object TimerEngine {
    private lateinit var app: Context
    private lateinit var prefs: SharedPreferences
    private val _state = MutableStateFlow(TimerState())
    val state: StateFlow<TimerState> = _state
    val current: TimerState get() = _state.value

    fun init(context: Context) {
        app = context.applicationContext
        prefs = app.getSharedPreferences("timer", Context.MODE_PRIVATE)
        _state.value = load()
        if (current.run == RunState.IDLE) {
            val t = durationOf(Phase.FOCUS)
            _state.value = TimerState(remainingMs = t, totalMs = t)
        }
    }

    val strictLocked: Boolean get() = SettingsStore.current.strictMode && current.lockedIn

    fun durationOf(p: Phase): Long {
        val s = SettingsStore.current
        val min = when (p) {
            Phase.FOCUS -> s.focusMin
            Phase.SHORT_BREAK -> s.shortBreakMin
            Phase.LONG_BREAK -> s.longBreakMin
        }
        return min * 60_000L
    }

    @Synchronized
    fun start() {
        val s = current
        val now = System.currentTimeMillis()
        // Nothing picked yet: study whatever today's plan says is next.
        if ((s.run == RunState.IDLE || (s.run == RunState.READY && s.phase == Phase.FOCUS)) && Planner.active(SettingsStore.current) == null) {
            Planner.nextPending(SettingsStore.current, null)?.let { next -> SettingsStore.update { it.copy(activeSubjectId = next.id) } }
        }
        when (s.run) {
            RunState.RUNNING -> return
            RunState.PAUSED -> resume()
            RunState.READY -> set(s.copy(run = RunState.RUNNING, endAt = now + s.remainingMs))
            RunState.IDLE -> {
                val t = durationOf(Phase.FOCUS)
                set(TimerState(Phase.FOCUS, RunState.RUNNING, now + t, t, t, 0))
            }
        }
    }

    @Synchronized
    fun pause() {
        val s = current
        if (s.run != RunState.RUNNING || strictLocked) return
        set(s.copy(run = RunState.PAUSED, remainingMs = s.remaining()))
    }

    @Synchronized
    fun resume() {
        val s = current
        if (s.run != RunState.PAUSED) return
        set(s.copy(run = RunState.RUNNING, endAt = System.currentTimeMillis() + s.remainingMs))
    }

    fun toggle() {
        if (current.run == RunState.RUNNING) pause() else start()
    }

    @Synchronized
    fun skip() {
        if (current.run == RunState.IDLE || strictLocked) return
        advance(natural = false, forceStart = current.run == RunState.RUNNING)
    }

    @Synchronized
    fun stop() {
        if (strictLocked) return
        recordFocus(current, completed = false)
        val t = durationOf(Phase.FOCUS)
        set(TimerState(remainingMs = t, totalMs = t))
    }

    /** Durations changed: refresh a phase that has not started yet. */
    @Synchronized
    fun settingsChanged() {
        val s = current
        if (s.run == RunState.IDLE || s.run == RunState.READY) {
            val t = durationOf(s.phase)
            set(s.copy(remainingMs = t, totalMs = t))
        } else onChanged()
    }

    /** Called every second by the service and by the alarm; moves on when a phase is over. */
    @Synchronized
    fun tick() {
        val s = current
        if (s.run == RunState.RUNNING && System.currentTimeMillis() >= s.endAt) advance(natural = true, forceStart = null)
    }

    /**
     * Logs the focused time of a focus phase that just ended. After a full pomodoro, moves on to the next
     * subject of today's plan when this one's target is reached; returns a note for the notification.
     */
    private fun recordFocus(s: TimerState, completed: Boolean): String? {
        if (s.phase != Phase.FOCUS || !(s.run == RunState.RUNNING || s.run == RunState.PAUSED)) return null
        val now = System.currentTimeMillis()
        val elapsed = if (completed) s.totalMs else s.totalMs - s.remaining(now)
        if (!completed && elapsed < 60_000L) return null
        val settings = SettingsStore.current
        val subject = Planner.active(settings)
        SessionLog.add(FocusRecord(now - elapsed, (elapsed / 1000L).toInt(), subject?.id, if (completed) 1 else 0))
        if (!completed || subject == null) return null
        val today = LocalDate.now()
        val target = Planner.target(subject, today)
        if (target <= 0 || SessionLog.pomodorosOn(today, subject.id) < target) return null
        val next = Planner.nextPending(settings, null)
        return when {
            next == null -> "✅ ${subject.name} done — today's whole plan is complete!"
            settings.autoNextSubject -> {
                SettingsStore.update { it.copy(activeSubjectId = next.id) }
                "✅ ${subject.name} done for today. Next up: ${next.name}."
            }
            else -> "✅ ${subject.name} done for today. ${next.name} is still open."
        }
    }

    private fun advance(natural: Boolean, forceStart: Boolean?) {
        val s = current
        val note = recordFocus(s, completed = natural)
        val settings = SettingsStore.current
        var completed = s.completedInCycle
        val next = if (s.phase == Phase.FOCUS) {
            completed++
            if (completed >= settings.sessionsBeforeLong) Phase.LONG_BREAK else Phase.SHORT_BREAK
        } else {
            if (s.phase == Phase.LONG_BREAK) completed = 0
            Phase.FOCUS
        }
        val auto = forceStart ?: if (next == Phase.FOCUS) settings.autoStartFocus else settings.autoStartBreaks
        val total = durationOf(next)
        val now = System.currentTimeMillis()
        set(TimerState(next, if (auto) RunState.RUNNING else RunState.READY, if (auto) now + total else 0L, total, total, completed))
        if (natural) Notifications.phaseCompleted(app, s.phase, next, auto, note)
    }

    private fun set(n: TimerState) {
        _state.value = n
        prefs.edit()
            .putString("phase", n.phase.name).putString("run", n.run.name).putLong("endAt", n.endAt)
            .putLong("remainingMs", n.remainingMs).putLong("totalMs", n.totalMs).putInt("completed", n.completedInCycle)
            .apply()
        onChanged()
    }

    private fun onChanged() {
        TimerService.sync(app)
        scheduleAlarm()
    }

    private fun load(): TimerState = try {
        TimerState(
            Phase.valueOf(prefs.getString("phase", Phase.FOCUS.name)!!),
            RunState.valueOf(prefs.getString("run", RunState.IDLE.name)!!),
            prefs.getLong("endAt", 0L),
            prefs.getLong("remainingMs", 25 * 60_000L),
            prefs.getLong("totalMs", 25 * 60_000L),
            prefs.getInt("completed", 0),
        )
    } catch (e: Exception) {
        TimerState()
    }

    /** Exact alarm so the phase switches on time even when the phone sleeps. */
    private fun scheduleAlarm() {
        val am = app.getSystemService(AlarmManager::class.java) ?: return
        val pi = PendingIntent.getBroadcast(
            app, 1, Intent(app, TimerReceiver::class.java).setAction(TimerReceiver.ACTION_PHASE_END),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        am.cancel(pi)
        val s = current
        if (s.run != RunState.RUNNING) return
        try {
            if (Build.VERSION.SDK_INT < 31 || am.canScheduleExactAlarms())
                am.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, s.endAt, pi)
            else
                am.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, s.endAt, pi)
        } catch (e: SecurityException) {
            am.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, s.endAt, pi)
        }
    }
}
