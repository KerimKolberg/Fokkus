package com.kk.fokkus.data

import android.content.Context
import com.kk.fokkus.timer.Phase
import com.kk.fokkus.timer.RunState
import com.kk.fokkus.timer.TimerState
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.time.Instant
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.ZoneId
import java.util.UUID

/** A subject in the weekly study plan. targets = pomodoros per weekday, Monday first. */
data class Subject(val id: String, val name: String, val colorIndex: Int, val targets: List<Int>) {
    companion object {
        fun create(name: String, colorIndex: Int) =
            Subject(UUID.randomUUID().toString().take(10), name, colorIndex, List(7) { 0 })
    }
}

/** Subject colours: validated categorical palette (dark steps), fixed order, never cycled. */
object SubjectColors {
    val series = longArrayOf(0xFF3987E5, 0xFFD95926, 0xFF199E70, 0xFFC98500, 0xFFD55181, 0xFF008300, 0xFF9085E9, 0xFFE66767)
    const val OTHER = 0xFF6F84A0
    fun of(s: Subject?): Long = if (s == null || s.colorIndex !in series.indices) OTHER else series[s.colorIndex]
    fun nextIndex(subjects: List<Subject>): Int = series.indices.firstOrNull { i -> subjects.none { it.colorIndex == i } } ?: series.size
}

/** One focus phase: a completed pomodoro (pomodoros = 1) or the focused part of a skipped/stopped one. */
data class FocusRecord(val startMs: Long, val seconds: Int, val subjectId: String?, val pomodoros: Int) {
    val start: LocalDateTime get() = LocalDateTime.ofInstant(Instant.ofEpochMilli(startMs), ZoneId.systemDefault())
    val day: LocalDate get() = start.toLocalDate()
}

object SessionLog {
    private lateinit var file: File
    private val _records = MutableStateFlow<List<FocusRecord>>(emptyList())
    val records: StateFlow<List<FocusRecord>> = _records
    private val lock = Any()

    fun init(context: Context) {
        file = File(context.filesDir, "sessions.json")
        _records.value = if (file.exists()) load() else migrateOldStats(context)
    }

    fun add(r: FocusRecord) {
        _records.value = _records.value + r
        val snapshot = _records.value
        Thread { save(snapshot) }.start()
    }

    private fun load(): List<FocusRecord> = try {
        val arr = JSONArray(file.readText())
        (0 until arr.length()).map {
            val o = arr.getJSONObject(it)
            FocusRecord(o.getLong("s"), o.getInt("sec"), if (o.isNull("sub")) null else o.optString("sub"), o.optInt("p", 0))
        }
    } catch (e: Exception) {
        emptyList()
    }

    private fun save(list: List<FocusRecord>) = synchronized(lock) {
        val arr = JSONArray()
        list.forEach { r -> arr.put(JSONObject().put("s", r.startMs).put("sec", r.seconds).put("sub", r.subjectId ?: JSONObject.NULL).put("p", r.pomodoros)) }
        val tmp = File(file.parentFile, "sessions.json.tmp")
        tmp.writeText(arr.toString())
        tmp.renameTo(file)
    }

    /** Older versions only kept per-day totals. */
    private fun migrateOldStats(context: Context): List<FocusRecord> {
        val prefs = context.getSharedPreferences("stats", Context.MODE_PRIVATE)
        val out = mutableListOf<FocusRecord>()
        prefs.all.keys.filter { it.startsWith("m_") }.forEach { key ->
            val date = runCatching { LocalDate.parse(key.removePrefix("m_")) }.getOrNull() ?: return@forEach
            val minutes = prefs.getInt(key, 0)
            if (minutes <= 0) return@forEach
            val ms = date.atTime(12, 0).atZone(ZoneId.systemDefault()).toInstant().toEpochMilli()
            out += FocusRecord(ms, minutes * 60, null, prefs.getInt("s_" + key.removePrefix("m_"), 0))
        }
        if (out.isNotEmpty()) save(out)
        return out
    }

    fun pomodorosOn(day: LocalDate, subjectId: String? = null, list: List<FocusRecord> = records.value) =
        list.filter { it.day == day && (subjectId == null || it.subjectId == subjectId) }.sumOf { it.pomodoros }

    fun minutesOn(day: LocalDate, list: List<FocusRecord> = records.value) =
        Math.round(list.filter { it.day == day }.sumOf { it.seconds } / 60.0).toInt()

    /** Consecutive days with a completed pomodoro, ending today (or yesterday if today is still empty). */
    fun streak(list: List<FocusRecord> = records.value): Int {
        val days = list.filter { it.pomodoros > 0 }.map { it.day }.toHashSet()
        var d = LocalDate.now()
        if (d !in days) d = d.minusDays(1)
        var n = 0
        while (d in days) { n++; d = d.minusDays(1) }
        return n
    }
}

data class PlanRow(val subject: Subject, val target: Int, val done: Int)

object Planner {
    val dayShort = listOf("Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun")
    val dayLetter = listOf("M", "T", "W", "T", "F", "S", "S")

    fun dayIndex(d: LocalDate) = d.dayOfWeek.value - 1

    fun target(s: Subject, d: LocalDate) = s.targets.getOrElse(dayIndex(d)) { 0 }

    fun active(s: AppSettings) = s.subjects.firstOrNull { it.id == s.activeSubjectId }

    fun today(s: AppSettings, list: List<FocusRecord> = SessionLog.records.value): List<PlanRow> {
        val today = LocalDate.now()
        return s.subjects.map { PlanRow(it, target(it, today), SessionLog.pomodorosOn(today, it.id, list)) }
            .filter { it.target > 0 || it.done > 0 }
    }

    fun nextPending(s: AppSettings, preferId: String?, list: List<FocusRecord> = SessionLog.records.value): Subject? {
        val pending = today(s, list).filter { it.done < it.target }
        return pending.firstOrNull { it.subject.id == preferId }?.subject ?: pending.firstOrNull()?.subject
    }

    /** (done, target) for today, done capped per subject. */
    fun totals(s: AppSettings, list: List<FocusRecord> = SessionLog.records.value): Pair<Int, Int> {
        val rows = today(s, list)
        return rows.sumOf { minOf(it.done, it.target) } to rows.sumOf { it.target }
    }
}

object Motivation {
    private val focus = listOf(
        "One pomodoro at a time.", "Deep work, deep results.", "Stay locked in.", "Small steps, big results.",
        "Discipline beats motivation.", "Future you says thanks.", "Progress, not perfection.", "You've got this.",
        "Focus is a superpower.", "Brick by brick.", "Hard now, easy later.", "Make this session count.",
        "Consistency wins.", "Your goals are waiting.", "Just this one task.", "Breathe. Focus. Go.",
        "Done is better than perfect.", "Every pomodoro counts.",
    )
    private val rest = listOf(
        "Rest is part of the work.", "Recharge, then conquer.", "Breathe. Stretch. Smile.",
        "Drink some water.", "Look away from the screen.", "Move a little, feel better.",
    )
    private val idle = listOf(
        "Ready when you are.", "Start small. Start now.", "The hardest part is starting.",
        "Let's make today count.", "One session changes the day.",
    )
    private val seed = (0..999).random()

    /** Changes once a minute; reacts to the moment (last minutes, plan done...). */
    fun current(st: TimerState, done: Int, target: Int, now: Long = System.currentTimeMillis()): String {
        if (st.lockedIn && st.run == RunState.RUNNING && st.remaining(now) < 180_000L) return "Final stretch — finish strong!"
        if (target > 0 && done >= target) return "Today's plan is done. Legend!"
        val list = when {
            st.run == RunState.IDLE -> idle
            st.phase == Phase.FOCUS -> focus
            else -> rest
        }
        return list[((now / 60_000L + seed) % list.size).toInt()]
    }
}
