package com.kk.fokkus.data

import android.content.Context
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.time.DayOfWeek
import java.time.Instant
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.ZoneId
import java.util.concurrent.Executors

/** One blocked attempt: a distracting website ("site") or a locked app ("app"). */
data class DistractionEntry(val t: Long, val k: String, val n: String) {
    val time: LocalDateTime get() = LocalDateTime.ofInstant(Instant.ofEpochMilli(t), ZoneId.systemDefault())
}

/** Remembers which distractions you tried to open during focus. */
object DistractionLog {
    private const val MAX = 20000
    private lateinit var file: File
    private val _entries = MutableStateFlow<List<DistractionEntry>>(emptyList())
    val entries: StateFlow<List<DistractionEntry>> = _entries
    private val lastSeen = HashMap<String, Long>()
    private val io = Executors.newSingleThreadExecutor()

    fun init(context: Context) {
        file = File(context.filesDir, "distractions.json")
        _entries.value = try {
            if (!file.exists()) emptyList() else {
                val arr = JSONArray(file.readText())
                (0 until arr.length()).mapNotNull { i -> arr.optJSONObject(i)?.let(::fromJson) }
            }
        } catch (e: Exception) { emptyList() }
    }

    fun toJson(e: DistractionEntry): JSONObject = JSONObject().put("T", e.t).put("K", e.k).put("N", e.n)
    fun fromJson(o: JSONObject): DistractionEntry? =
        DistractionEntry(o.optLong("T"), o.optString("K", "site"), o.optString("N")).takeIf { it.n.isNotEmpty() }

    /** One attempt per thing per minute: a single page load or app launch fires many events. */
    @Synchronized
    fun add(kind: String, name: String, now: Long = System.currentTimeMillis()): Boolean {
        if (name.isEmpty()) return false
        val key = kind + ":" + name.lowercase()
        val last = lastSeen[key]
        if (last != null && now - last in 0 until 60_000L) return false
        lastSeen[key] = now
        set((_entries.value + DistractionEntry(now, kind, name)).takeLast(MAX))
        return true
    }

    @Synchronized
    fun merge(incoming: List<DistractionEntry>) {
        val have = _entries.value.map { "${it.t}${it.k}${it.n}" }.toHashSet()
        val add = incoming.filter { have.add("${it.t}${it.k}${it.n}") }
        if (add.isNotEmpty()) set((_entries.value + add).sortedBy { it.t }.takeLast(MAX))
    }

    private fun set(list: List<DistractionEntry>) {
        _entries.value = list
        io.execute {
            try {
                val arr = JSONArray()
                list.forEach { arr.put(toJson(it)) }
                val tmp = File(file.parentFile, "distractions.json.tmp")
                tmp.writeText(arr.toString())
                tmp.renameTo(file)
            } catch (_: Exception) { }
        }
    }
}

data class DistractionStat(val kind: String, val name: String, val count: Int, val peakHour: Int)

object Insights {
    private fun inRange(e: DistractionEntry, start: LocalDate, end: LocalDate): Boolean {
        val d = e.time.toLocalDate()
        return !d.isBefore(start) && d.isBefore(end)
    }

    fun count(list: List<DistractionEntry>, start: LocalDate, end: LocalDate) = list.count { inRange(it, start, end) }

    /** Most tried distractions in a period, with the hour they usually happen. */
    fun top(list: List<DistractionEntry>, start: LocalDate, end: LocalDate, max: Int): List<DistractionStat> =
        list.filter { inRange(it, start, end) }
            .groupBy { it.k + "\n" + it.n.lowercase() }
            .map { (_, g) ->
                val peak = g.groupingBy { it.time.hour }.eachCount().entries.sortedWith(compareBy({ -it.value }, { it.key })).first().key
                DistractionStat(g.first().k, g.first().n, g.size, peak)
            }
            .sortedWith(compareBy({ -it.count }, { it.name }))
            .take(max)

    fun describe(d: DistractionStat) = "${d.count}× · mostly around %02d:00".format(d.peakHour)
}

/** Smarter breaks: a longer break after a long block, or after skipping breaks. */
object Breaks {
    /** Suggested break length in minutes, or 0 to keep the planned one. */
    fun suggest(focusMin: Int, breakMin: Int, skippedInRow: Int): Int {
        var s = breakMin
        if (focusMin >= 50) s = maxOf(s, (Math.ceil(focusMin / 5.0 / 5.0) * 5).toInt())
        if (skippedInRow >= 2) s = maxOf(s, breakMin + 5)
        return if (s > breakMin) s else 0
    }

    fun reason(focusMin: Int, skippedInRow: Int) =
        if (focusMin >= 50) "That was a long block ($focusMin min)." else "You skipped your last $skippedInRow breaks."
}

class WeekReview(
    val stats: StatsResult,
    val prevMinutes: Double,
    val bestHour: Int,          // start of the best 2-hour window, -1 = not enough data
    val bestDay: LocalDate?,
    val bestDayMinutes: Double,
    val notes: List<FocusRecord>,
    val distractions: List<DistractionStat>,
    val distractionCount: Int,
)

object Review {
    fun compute(s: AppSettings, records: List<FocusRecord>, distractions: List<DistractionEntry>, anyDay: LocalDate): WeekReview {
        val r = Analytics.compute(s, records, StatRange.WEEK, anyDay)
        val prev = Analytics.compute(s, records, StatRange.WEEK, r.start.minusDays(7)).totalMinutes
        val from = r.start.atStartOfDay()
        val to = r.end.atStartOfDay()
        // Minutes per hour of the day, then the best 2-hour window.
        val hours = DoubleArray(24)
        for (rec in records) {
            val s0 = rec.start
            val s1 = s0.plusSeconds(rec.seconds.toLong())
            if (!s1.isAfter(from) || !s0.isBefore(to)) continue
            var h = s0.withMinute(0).withSecond(0).withNano(0)
            while (h.isBefore(s1)) {
                val a = if (s0.isAfter(h)) s0 else h
                val z = if (s1.isBefore(h.plusHours(1))) s1 else h.plusHours(1)
                if (z.isAfter(a) && !a.isBefore(from) && a.isBefore(to))
                    hours[h.hour] += java.time.Duration.between(a, z).seconds / 60.0
                h = h.plusHours(1)
            }
        }
        var bestHour = -1
        if (hours.sum() >= 30) {
            var best = -1.0
            for (h in 0 until 23) if (hours[h] + hours[h + 1] > best) { best = hours[h] + hours[h + 1]; bestHour = h }
        }
        val day = r.buckets.maxByOrNull { it.total }?.takeIf { it.total > 0 }
        val notes = records.filter { !it.note.isNullOrEmpty() && !it.day.isBefore(r.start) && it.day.isBefore(r.end) }.sortedBy { it.startMs }
        return WeekReview(
            r, prev, bestHour, day?.start?.toLocalDate(), day?.total ?: 0.0, notes,
            Insights.top(distractions, r.start, r.end, 4), Insights.count(distractions, r.start, r.end),
        )
    }

    /** The week to offer: on Sunday evening this week, on any other day the week before. */
    fun dueWeek(now: LocalDateTime = LocalDateTime.now()): LocalDate {
        val week = Analytics.weekStart(now.toLocalDate())
        return if (now.dayOfWeek == DayOfWeek.SUNDAY && now.hour >= 18) week else week.minusDays(7)
    }
}
