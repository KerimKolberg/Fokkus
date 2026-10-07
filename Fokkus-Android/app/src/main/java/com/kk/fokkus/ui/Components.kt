package com.kk.fokkus.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.kk.fokkus.R

@Composable
fun TimerRing(
    remaining: Float,
    color: Color,
    dim: Boolean,
    phaseText: String,
    timeText: String,
    subText: String,
    dots: Int,
    dotsFilled: Int,
    modifier: Modifier = Modifier,
    size: Dp = 280.dp,
) {
    val ringColor = if (dim) color.copy(alpha = 0.45f) else color
    Box(modifier.size(size), contentAlignment = Alignment.Center) {
        Canvas(Modifier.size(size)) {
            val stroke = 12.dp.toPx()
            val inset = stroke / 2 + 10.dp.toPx()
            val arcSize = Size(this.size.width - 2 * inset, this.size.height - 2 * inset)
            val topLeft = Offset(inset, inset)
            drawArc(Palette.Surface2, 0f, 360f, false, topLeft, arcSize, style = Stroke(stroke))
            if (remaining > 0.001f) {
                val sweep = 360f * remaining.coerceAtMost(1f)
                drawArc(color.copy(alpha = if (dim) 0.07f else 0.16f), -90f, sweep, false, topLeft, arcSize,
                    style = Stroke(stroke + 12.dp.toPx(), cap = StrokeCap.Round))
                drawArc(ringColor, -90f, sweep, false, topLeft, arcSize, style = Stroke(stroke, cap = StrokeCap.Round))
            }
        }
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Text(phaseText, color = ringColor, fontWeight = FontWeight.SemiBold, fontSize = 14.sp, letterSpacing = 1.5.sp)
            Text(timeText, color = Palette.Text, fontSize = 60.sp, fontWeight = FontWeight.Light)
            Text(subText, color = Palette.Muted, fontSize = 14.sp)
            Spacer(Modifier.height(14.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                repeat(dots) { i ->
                    Box(Modifier.size(8.dp).clip(CircleShape).background(if (i < dotsFilled) ringColor else Palette.Surface2))
                }
            }
        }
    }
}

@Composable
fun SectionTitle(text: String, modifier: Modifier = Modifier) {
    Text(
        text.uppercase(), color = Palette.Muted, fontSize = 12.sp, fontWeight = FontWeight.SemiBold, letterSpacing = 1.sp,
        modifier = modifier.padding(top = 18.dp, bottom = 8.dp),
    )
}

@Composable
fun Card(modifier: Modifier = Modifier, color: Color = Palette.Surface, content: @Composable () -> Unit) {
    Box(modifier.fillMaxWidth().clip(RoundedCornerShape(18.dp)).background(color).padding(16.dp)) { content() }
}

@Composable
fun SwitchRow(
    title: String,
    checked: Boolean,
    onChange: (Boolean) -> Unit,
    description: String? = null,
    enabled: Boolean = true,
    accent: Color = Palette.Focus,
) {
    Row(
        Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp))
            .clickable(enabled = enabled) { onChange(!checked) }
            .padding(vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(Modifier.weight(1f).padding(end = 12.dp)) {
            Text(title, color = if (enabled) Palette.Text else Palette.Muted, fontSize = 16.sp)
            if (description != null) Text(description, color = Palette.Muted, fontSize = 13.sp, lineHeight = 17.sp)
        }
        Switch(
            checked = checked, onCheckedChange = onChange, enabled = enabled,
            colors = SwitchDefaults.colors(
                checkedTrackColor = accent, checkedThumbColor = Color.White,
                uncheckedTrackColor = Palette.Surface2, uncheckedThumbColor = Palette.Muted, uncheckedBorderColor = Palette.Border,
            ),
        )
    }
}

@Composable
fun StepperRow(title: String, value: Int, suffix: String, min: Int, max: Int, step: Int, onChange: (Int) -> Unit, enabled: Boolean = true) {
    Row(Modifier.fillMaxWidth().padding(vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(title, color = Palette.Text, fontSize = 16.sp, modifier = Modifier.weight(1f))
        Row(
            Modifier.clip(RoundedCornerShape(12.dp)).background(Palette.Surface2),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            StepButton("−", enabled && value > min) { onChange((value - step).coerceAtLeast(min)) }
            Text("$value$suffix", color = Palette.Text, fontWeight = FontWeight.SemiBold, fontSize = 15.sp,
                textAlign = TextAlign.Center, modifier = Modifier.width(92.dp))
            StepButton("+", enabled && value < max) { onChange((value + step).coerceAtMost(max)) }
        }
    }
}

@Composable
private fun StepButton(label: String, enabled: Boolean, onClick: () -> Unit) {
    Box(
        Modifier.size(44.dp).clickable(enabled = enabled, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Text(label, color = if (enabled) Palette.Text else Palette.Muted.copy(alpha = 0.5f), fontSize = 22.sp)
    }
}

@Composable
fun Segmented(options: List<String>, selected: Int, onSelect: (Int) -> Unit, enabled: Boolean = true, modifier: Modifier = Modifier) {
    Row(
        modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(Palette.Surface2).padding(4.dp),
    ) {
        options.forEachIndexed { i, label ->
            val sel = i == selected
            Box(
                Modifier.weight(1f).clip(RoundedCornerShape(10.dp))
                    .background(if (sel) Palette.Hover else Color.Transparent)
                    .clickable(enabled = enabled) { onSelect(i) }
                    .padding(vertical = 10.dp),
                contentAlignment = Alignment.Center,
            ) {
                Text(label, color = if (sel) Palette.Text else Palette.Muted, fontSize = 14.sp,
                    fontWeight = if (sel) FontWeight.SemiBold else FontWeight.Normal, textAlign = TextAlign.Center)
            }
        }
    }
}

@Composable
fun IconRes(id: Int, tint: Color = Palette.Text, size: Dp = 22.dp, contentDescription: String? = null) {
    Icon(painterResource(id), contentDescription, tint = tint, modifier = Modifier.size(size))
}

@Composable
fun StrictBanner(visible: Boolean) {
    if (!visible) return
    Row(
        Modifier.fillMaxWidth().padding(bottom = 8.dp).clip(RoundedCornerShape(12.dp))
            .background(Palette.Warn.copy(alpha = 0.12f)).padding(12.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        IconRes(R.drawable.ic_lock, Palette.Warn, 18.dp)
        Spacer(Modifier.width(10.dp))
        Text("Strict mode: these settings are locked until this focus session ends.", color = Palette.Warn, fontSize = 13.sp)
    }
}

@Composable
fun ScreenTitle(title: String, subtitle: String) {
    Column(Modifier.padding(top = 12.dp, bottom = 6.dp)) {
        Text(title, style = MaterialTheme.typography.headlineMedium, color = Palette.Text, fontWeight = FontWeight.SemiBold)
        Text(subtitle, color = Palette.Muted, fontSize = 14.sp)
    }
}

fun formatTime(ms: Long): String {
    val total = ((ms + 999) / 1000).coerceAtLeast(0)
    val h = total / 3600
    val m = (total % 3600) / 60
    val s = total % 60
    return if (h > 0) "%d:%02d:%02d".format(h, m, s) else "%02d:%02d".format(m, s)
}

fun formatMinutes(min: Int): String = if (min < 60) "$min min" else "${min / 60} h ${"%02d".format(min % 60)} min"
