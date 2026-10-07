package com.kk.fokkus.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.RoundRect
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.drawText
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.kk.fokkus.R
import com.kk.fokkus.data.Analytics
import com.kk.fokkus.data.ChartBucket
import com.kk.fokkus.data.SessionLog
import com.kk.fokkus.data.SettingsStore
import com.kk.fokkus.data.StatRange
import com.kk.fokkus.data.StatsResult
import java.time.LocalDate
import kotlin.math.ceil
import kotlin.math.min

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun StatsScreen() {
    val s by SettingsStore.state.collectAsState()
    val records by SessionLog.records.collectAsState()
    var range by rememberSaveable { mutableStateOf(StatRange.WEEK) }
    var anchor by remember { mutableStateOf(LocalDate.now()) }
    var selected by remember { mutableIntStateOf(-1) }
    val r = remember(s, records, range, anchor) { Analytics.compute(s, records, range, anchor) }
    val today = LocalDate.now()

    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 20.dp)) {
        ScreenTitle("Statistics", "Your focused time. Tap a bar for details.")
        Spacer(Modifier.height(8.dp))
        Segmented(listOf("Day", "Week", "Month", "Year"), range.ordinal, { i ->
            range = StatRange.entries[i]; anchor = today; selected = -1
        })
        Row(Modifier.fillMaxWidth().padding(top = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            NavButton(R.drawable.ic_chevron_left, true) { anchor = Analytics.shift(range, anchor, -1); selected = -1 }
            Text(r.title, color = Palette.Text, fontSize = 16.sp, fontWeight = FontWeight.SemiBold,
                modifier = Modifier.weight(1f).padding(horizontal = 8.dp))
            NavButton(R.drawable.ic_chevron_right, r.end <= today) { anchor = Analytics.shift(range, anchor, 1); selected = -1 }
            Spacer(Modifier.width(6.dp))
            Box(
                Modifier.clip(RoundedCornerShape(12.dp)).background(Palette.Surface2).clickable { anchor = today; selected = -1 }
                    .padding(horizontal = 14.dp, vertical = 10.dp),
            ) { Text("Today", color = Palette.Text, fontSize = 14.sp) }
        }

        val period = when (range) { StatRange.DAY -> "on this day"; StatRange.WEEK -> "this week"; StatRange.MONTH -> "this month"; StatRange.YEAR -> "this year" }
        Spacer(Modifier.height(12.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Tile("Focused", Analytics.compact(r.totalMinutes), period, Modifier.weight(1f))
            Tile("Pomodoros", "${r.pomodoros}", if (r.planned > 0) "of ${r.planned} planned so far" else "completed", Modifier.weight(1f))
        }
        Spacer(Modifier.height(10.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Tile(r.dailyAverageLabel, Analytics.compact(r.dailyAverage), "per day", Modifier.weight(1f))
            Tile("Streak", "${r.streak} ${if (r.streak == 1) "day" else "days"}", "in a row with a pomodoro", Modifier.weight(1f))
        }

        // Legend: always for 2+ series, plus the plan marker key.
        val present = r.series.filterIndexed { i, _ -> r.buckets.any { it.minutes[i] > 0.01 } }
        val showPlan = r.buckets.any { it.target > 0 }
        Spacer(Modifier.height(14.dp))
        FlowRow(horizontalArrangement = Arrangement.spacedBy(14.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            if (present.size >= 2) present.forEach { ser ->
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Box(Modifier.size(10.dp).background(Color(ser.color)))
                    Spacer(Modifier.width(6.dp))
                    Text(ser.name, color = Palette.Text, fontSize = 12.sp)
                }
            }
            if (showPlan) Row(verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.width(14.dp).height(2.dp).background(Palette.Text))
                Spacer(Modifier.width(6.dp))
                Text("Plan", color = Palette.Text, fontSize = 12.sp)
            }
        }
        Spacer(Modifier.height(6.dp))
        StackedBars(r, selected) { selected = if (it == selected) -1 else it }

        val sel = r.buckets.getOrNull(selected)
        if (sel != null) BucketDetail(r, sel)
        else Text("Tap a bar to see the details.", color = Palette.Muted, fontSize = 12.sp, modifier = Modifier.padding(top = 4.dp))

        SectionTitle("By subject")
        if (r.bySubject.isEmpty()) Text("Pick a subject on the Timer screen before you focus to see your time per subject here.",
            color = Palette.Muted, fontSize = 13.sp)
        val maxM = r.bySubject.maxOfOrNull { it.minutes }?.coerceAtLeast(1.0) ?: 1.0
        r.bySubject.forEach { st ->
            Column(Modifier.padding(vertical = 6.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Box(Modifier.size(10.dp).background(Color(st.series.color)))
                    Spacer(Modifier.width(8.dp))
                    Text(st.series.name, color = Palette.Text, fontSize = 14.sp, modifier = Modifier.weight(1f))
                    Text("${Analytics.compact(st.minutes)} · ${Math.round(100 * st.minutes / r.totalMinutes.coerceAtLeast(1.0))}%",
                        color = Palette.Text, fontSize = 13.sp)
                }
                Spacer(Modifier.height(5.dp))
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Box(Modifier.weight(1f).padding(start = 18.dp).height(6.dp).clip(RoundedCornerShape(3.dp)).background(Palette.GridLine)) {
                        if (st.minutes > 0) Box(Modifier.fillMaxWidth((st.minutes / maxM).toFloat().coerceIn(0.02f, 1f)).fillMaxHeight()
                            .clip(RoundedCornerShape(3.dp)).background(Color(st.series.color)))
                    }
                    Spacer(Modifier.width(10.dp))
                    Text(if (st.planned > 0) "${st.pomodoros}/${st.planned} planned" else "${st.pomodoros} pomodoros",
                        color = Palette.Muted, fontSize = 12.sp)
                }
            }
        }
        Spacer(Modifier.height(28.dp))
    }
}

@Composable
private fun NavButton(icon: Int, enabled: Boolean, onClick: () -> Unit) {
    Box(
        Modifier.size(40.dp).clip(RoundedCornerShape(12.dp)).background(Palette.Surface2).clickable(enabled = enabled, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) { IconRes(icon, if (enabled) Palette.Text else Palette.Muted.copy(alpha = 0.4f), 22.dp) }
}

@Composable
private fun Tile(label: String, value: String, sub: String, modifier: Modifier) {
    Column(modifier.clip(RoundedCornerShape(14.dp)).background(Palette.Surface).padding(14.dp)) {
        Text(label, color = Palette.Muted, fontSize = 12.sp)
        Text(value, color = Palette.Text, fontSize = 22.sp, fontWeight = FontWeight.SemiBold)
        Text(sub, color = Palette.Muted, fontSize = 11.sp, maxLines = 1)
    }
}

@Composable
private fun BucketDetail(r: StatsResult, b: ChartBucket) {
    Card(Modifier.padding(top = 6.dp)) {
        Column {
            Text(b.longLabel, color = Palette.Text, fontSize = 15.sp, fontWeight = FontWeight.SemiBold)
            Text("Total ${Analytics.compact(b.total)}" + if (b.target > 0) " · plan ${Analytics.compact(b.target)}" else "",
                color = Palette.Muted, fontSize = 13.sp)
            r.series.forEachIndexed { i, ser ->
                if (b.minutes[i] >= 0.5) Row(Modifier.padding(top = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                    Box(Modifier.size(10.dp).background(Color(ser.color)))
                    Spacer(Modifier.width(8.dp))
                    Text(ser.name, color = Palette.Text, fontSize = 13.sp, modifier = Modifier.weight(1f))
                    Text(Analytics.compact(b.minutes[i]), color = Palette.Text, fontSize = 13.sp)
                }
            }
        }
    }
}

private fun niceStep(max: Double): Double {
    val steps = doubleArrayOf(5.0, 10.0, 15.0, 30.0, 60.0, 120.0, 180.0, 240.0, 360.0, 480.0, 600.0, 900.0, 1200.0, 1800.0, 2400.0, 3000.0, 3600.0, 6000.0, 9000.0, 12000.0, 18000.0, 24000.0)
    return steps.firstOrNull { max / it <= 4 } ?: (ceil(max / 4 / 6000) * 6000)
}

/** Stacked columns, one colour per subject; thin bars, rounded data-end, 2dp gaps; white marks = plan. */
@Composable
private fun StackedBars(r: StatsResult, selected: Int, onSelect: (Int) -> Unit) {
    val measurer = rememberTextMeasurer()
    val labelStyle = TextStyle(color = Palette.Muted, fontSize = 10.sp)
    val n = r.buckets.size
    Canvas(
        Modifier.fillMaxWidth().height(230.dp).pointerInput(r) {
            detectTapGestures { pos ->
                val left = 40.dp.toPx()
                val slot = (size.width - left) / n
                val i = ((pos.x - left) / slot).toInt()
                if (pos.x >= left && i in 0 until n) onSelect(i)
            }
        },
    ) {
        val left = 40.dp.toPx()
        val bottomPad = 22.dp.toPx()
        val top = 8.dp.toPx()
        val plotH = size.height - bottomPad - top
        val plotW = size.width - left
        val maxVal = r.buckets.maxOf { maxOf(it.total, it.target) }
        val empty = r.totalMinutes < 0.5 && maxVal <= 0
        val step = niceStep(if (maxVal > 0) maxVal else 60.0)
        val topVal = ceil((if (maxVal > 0) maxVal else 60.0) / step) * step
        fun y(v: Double) = (top + plotH - (v / topVal) * plotH).toFloat()

        // hairline grid + y labels
        var v = 0.0
        while (v <= topVal + 0.01) {
            val yy = y(v)
            drawLine(if (v == 0.0) Palette.Baseline else Palette.GridLine, Offset(left, yy), Offset(size.width, yy), strokeWidth = 1f)
            val txt = if (v == 0.0) "0" else Analytics.compact(v)
            val m = measurer.measure(txt, labelStyle)
            drawText(m, topLeft = Offset(left - m.size.width - 6.dp.toPx(), yy - m.size.height / 2f))
            v += step
        }

        val slot = plotW / n
        val barW = min(20.dp.toPx(), slot * 0.62f)
        val gap = 2.dp.toPx()
        val radius = min(4.dp.toPx(), barW / 2)
        if (selected in 0 until n) drawRect(Palette.Surface2.copy(alpha = 0.55f), Offset(left + selected * slot, top), Size(slot, plotH))

        r.buckets.forEachIndexed { i, b ->
            val x = left + i * slot + (slot - barW) / 2
            val topIdx = b.minutes.indices.lastOrNull { b.minutes[it] > 0.01 } ?: -1
            var cum = 0.0
            var first = true
            for (si in b.minutes.indices) {
                val m = b.minutes[si]
                if (m <= 0.01) continue
                var y0 = y(cum)
                val y1 = y(cum + m)
                cum += m
                if (!first) y0 -= gap
                first = false
                if (y0 - y1 < 0.75f) continue
                val color = Color(r.series[si].color)
                if (si == topIdx) {
                    val path = Path().apply {
                        addRoundRect(RoundRect(x, y1, x + barW, y0, topLeftCornerRadius = CornerRadius(radius), topRightCornerRadius = CornerRadius(radius)))
                    }
                    drawPath(path, color)
                } else drawRect(color, Offset(x, y1), Size(barW, y0 - y1))
            }
            if (b.target > 0) {
                val ty = y(b.target)
                val w = min(slot * 0.86f, barW + 10.dp.toPx())
                drawLine(Palette.Text, Offset(left + i * slot + (slot - w) / 2, ty), Offset(left + i * slot + (slot + w) / 2, ty),
                    strokeWidth = 2.dp.toPx(), cap = StrokeCap.Round)
            }
            if (b.label.isNotEmpty()) {
                val m = measurer.measure(b.label, if (i == selected) labelStyle.copy(color = Palette.Text) else labelStyle)
                drawText(m, topLeft = Offset(left + i * slot + slot / 2 - m.size.width / 2f, size.height - bottomPad + 6.dp.toPx()))
            }
        }

        if (empty) {
            val m = measurer.measure("No focus time in this period yet.", TextStyle(color = Palette.Muted, fontSize = 13.sp))
            drawText(m, topLeft = Offset(left + (plotW - m.size.width) / 2, top + plotH / 2 - m.size.height))
        }
    }
}
