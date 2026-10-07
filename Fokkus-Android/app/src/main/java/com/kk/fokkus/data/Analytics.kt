package com.kk.fokkus.data

import java.time.Duration
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter
import java.time.temporal.ChronoUnit
import java.util.Locale

enum class StatRange { DAY, WEEK, MONTH, YEAR }

data class SeriesInfo(val id: String?, val name: String, val color: Long)

class ChartBucket(
    val label: String,       // axis label ("" = skip)
    val longLabel: String,   // detail title
    val start: LocalDateTime,
    val end: LocalDateTime,
    val minutes: DoubleArray,
) {
    var total = 0.0
    var target = -1.0        // planned minutes, -1 = no plan
}

class SubjectStat(val series: SeriesInfo) {
    var minutes = 0.0
    var pomodoros = 0
    var planned = 0
}

class StatsResult(
    val range: StatRange,
    val title: String,
    val start: LocalDate,
    val end: LocalDate,
    val series: List<SeriesInfo>,
    val buckets: List<ChartBucket>,
    val bySubject: List<SubjectStat>,
    val totalMinutes: Double,
    val pomodoros: Int,
    val planned: Int,
    val dailyAverage: Double,
    val dailyAverageLabel: String,
    val streak: Int,
)

object Analytics {
    private val en = Locale.UK
    private fun fmt(p: String) = DateTimeFormatter.ofPattern(p, en)

    fun weekStart(d: LocalDate): LocalDate = d.minusDays(Planner.dayIndex(d).toLong())

    fun shift(range: StatRange, anchor: LocalDate, dir: Int): LocalDate = when (range) {
        StatRange.DAY -> anchor.plusDays(dir.toLong())
        StatRange.WEEK -> anchor.plusWeeks(dir.toLong())
        StatRange.MONTH -> anchor.plusMonths(dir.toLong())
        StatRange.YEAR -> anchor.plusYears(dir.toLong())
    }

    fun compute(settings: AppSettings, records: List<FocusRecord>, range: StatRange, anchor: LocalDate): StatsResult {
        val today = LocalDate.now()

        // Series: subjects with their own colour (plan order), then one neutral "Other" bucket.
        val series = mutableListOf<SeriesInfo>()
        val seriesIndex = HashMap<String, Int>()
        settings.subjects.filter { it.colorIndex in SubjectColors.series.indices }.forEach {
            seriesIndex[it.id] = series.size
            series += SeriesInfo(it.id, it.name, SubjectColors.of(it))
        }
        val other = series.size
        series += SeriesInfo(null, if (settings.subjects.isNotEmpty()) "Other / no subject" else "Focus", SubjectColors.OTHER)
        val n = series.size

        val buckets = mutableListOf<ChartBucket>()
        val start: LocalDate
        val end: LocalDate
        val title: String
        when (range) {
            StatRange.DAY -> {
                start = anchor; end = anchor.plusDays(1)
                title = when (anchor) {
                    today -> "Today"
                    today.minusDays(1) -> "Yesterday"
                    else -> anchor.format(fmt("EEE, d MMM yyyy"))
                }
                for (h in 0 until 24) buckets += ChartBucket(
                    if (h % 6 == 0) "%02d".format(h) else "", "%02d:00 – %02d:00".format(h, h + 1),
                    anchor.atTime(h, 0), anchor.atStartOfDay().plusHours(h + 1L), DoubleArray(n),
                )
            }
            StatRange.WEEK -> {
                start = weekStart(anchor); end = start.plusDays(7)
                val last = end.minusDays(1)
                var span = if (start.month == last.month) "${start.dayOfMonth}–${last.format(fmt("d MMM"))}"
                else "${start.format(fmt("d MMM"))} – ${last.format(fmt("d MMM"))}"
                if (last.year != today.year) span += " ${last.year}"
                title = (if (start == weekStart(today)) "This week · " else "") + span
                for (i in 0 until 7) {
                    val d = start.plusDays(i.toLong())
                    buckets += ChartBucket(Planner.dayLetter[i], d.format(fmt("EEEE, d MMM")), d.atStartOfDay(), d.plusDays(1).atStartOfDay(), DoubleArray(n))
                }
            }
            StatRange.MONTH -> {
                start = anchor.withDayOfMonth(1); end = start.plusMonths(1)
                title = start.format(fmt("MMMM yyyy"))
                var d = start
                while (d < end) {
                    buckets += ChartBucket(if (d.dayOfMonth == 1 || d.dayOfMonth % 7 == 0) "${d.dayOfMonth}" else "",
                        d.format(fmt("EEE, d MMM")), d.atStartOfDay(), d.plusDays(1).atStartOfDay(), DoubleArray(n))
                    d = d.plusDays(1)
                }
            }
            StatRange.YEAR -> {
                start = anchor.withDayOfYear(1); end = start.plusYears(1)
                title = "${anchor.year}"
                for (m in 0 until 12) {
                    val d = start.plusMonths(m.toLong())
                    buckets += ChartBucket(d.format(fmt("MMMMM")),
                        d.format(fmt("MMMM yyyy")), d.atStartOfDay(), d.plusMonths(1).atStartOfDay(), DoubleArray(n))
                }
            }
        }

        // Distribute every record's minutes over the buckets it overlaps.
        val stats = Array(n) { SubjectStat(series[it]) }
        val rangeStart = start.atStartOfDay()
        val rangeEnd = end.atStartOfDay()
        var pomodoros = 0
        for (r in records) {
            val s0 = r.start
            val s1 = s0.plusSeconds(r.seconds.toLong())
            if (s1 <= rangeStart || s0 >= rangeEnd) continue
            val si = r.subjectId?.let { seriesIndex[it] } ?: other
            var inRange = 0.0
            for (b in buckets) {
                val a = if (s0 > b.start) s0 else b.start
                val z = if (s1 < b.end) s1 else b.end
                if (z <= a) continue
                val min = Duration.between(a, z).seconds / 60.0
                b.minutes[si] += min
                inRange += min
            }
            stats[si].minutes += inRange
            if (s0 >= rangeStart && s0 < rangeEnd) { stats[si].pomodoros += r.pomodoros; pomodoros += r.pomodoros }
        }
        buckets.forEach { b -> b.total = b.minutes.sum() }
        val total = buckets.sumOf { it.total }

        // Plan targets per bucket - only from the day the plan exists.
        val planStart = settings.planStart
        val hasPlan = settings.subjects.any { s -> s.targets.sum() > 0 }
        if (hasPlan && range != StatRange.DAY) for (b in buckets) {
            var planned = 0
            var d = b.start.toLocalDate()
            val bEnd = b.end.toLocalDate()
            while (d < bEnd) {
                if (d >= planStart) planned += settings.subjects.sumOf { Planner.target(it, d) }
                d = d.plusDays(1)
            }
            if (b.end.toLocalDate() > planStart) b.target = planned * settings.focusMin.toDouble()
        }

        // Planned pomodoros per subject for the elapsed part of the range.
        val planEnd = minOf(end, today.plusDays(1))
        val planFrom = maxOf(start, planStart)
        var plannedTotal = 0
        for (s in settings.subjects) {
            var planned = 0
            var d = planFrom
            while (d < planEnd) { planned += Planner.target(s, d); d = d.plusDays(1) }
            stats[seriesIndex[s.id] ?: other].planned += planned
            plannedTotal += planned
        }

        val avg: Double
        val avgLabel: String
        if (range == StatRange.DAY) {
            val from = today.minusDays(29).atStartOfDay()
            avg = records.filter { it.start >= from }.sumOf { it.seconds } / 60.0 / 30.0
            avgLabel = "30-day avg"
        } else {
            var from = start
            records.minOfOrNull { it.day }?.let { first -> if (first > from) from = first }
            val days = maxOf(1L, ChronoUnit.DAYS.between(from, planEnd))
            avg = total / days
            avgLabel = "Daily average"
        }

        return StatsResult(
            range, title, start, end, series, buckets,
            stats.filter { it.minutes > 0.5 || it.planned > 0 || it.pomodoros > 0 }.sortedByDescending { it.minutes },
            total, pomodoros, plannedTotal, avg, avgLabel, SessionLog.streak(records),
        )
    }

    fun compact(minutes: Double): String {
        val m = Math.round(minutes).toInt()
        return when {
            m < 60 -> "${m}m"
            m < 6000 -> "${m / 60}h" + if (m % 60 > 0) " ${m % 60}m" else ""
            else -> "${m / 60}h"
        }
    }
}
