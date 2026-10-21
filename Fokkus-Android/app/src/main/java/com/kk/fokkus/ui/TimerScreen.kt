package com.kk.fokkus.ui

import android.os.Build
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.FilledTonalIconButton
import androidx.compose.material3.IconButtonDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.kk.fokkus.Perms
import com.kk.fokkus.R
import com.kk.fokkus.data.AppSettings
import com.kk.fokkus.data.Motivation
import com.kk.fokkus.data.PlanRow
import com.kk.fokkus.data.Planner
import com.kk.fokkus.data.SessionLog
import com.kk.fokkus.data.SettingsStore
import com.kk.fokkus.data.Subject
import com.kk.fokkus.data.SubjectColors
import com.kk.fokkus.data.TimerDisplay
import com.kk.fokkus.timer.Phase
import com.kk.fokkus.timer.RunState
import com.kk.fokkus.timer.TimerEngine
import kotlinx.coroutines.delay
import java.time.LocalDate

@Composable
fun TimerScreen(resumeTick: Int, requestNotifications: () -> Unit, onOpenPlan: () -> Unit) {
    val ctx = LocalContext.current
    val st by TimerEngine.state.collectAsState()
    val s by SettingsStore.state.collectAsState()
    val records by SessionLog.records.collectAsState()
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    LaunchedEffect(st.run, st.endAt) {
        while (true) {
            now = System.currentTimeMillis()
            if (st.run == RunState.RUNNING) TimerEngine.tick()
            delay(250)
        }
    }
    val strict = s.strictMode && st.lockedIn
    val color = if (st.run == RunState.IDLE) Palette.Focus else Palette.phase(st.phase)
    val today = LocalDate.now()
    val todayPomos = SessionLog.pomodorosOn(today, null, records)
    val todayMinutes = SessionLog.minutesOn(today, records)
    val planRows = Planner.today(s, records)
    val (done, target) = Planner.totals(s, records)
    val active = Planner.active(s)
    val select: (String?) -> Unit = { id ->
        SettingsStore.update { it.copy(activeSubjectId = id) }
        TimerEngine.settingsChanged()
    }

    Column(
        Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 20.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Row(Modifier.fillMaxWidth().padding(top = 16.dp, bottom = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            Image(painterResource(R.drawable.logo_round), "Fokkus logo", Modifier.size(46.dp))
            Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f)) {
                Text("Fokkus", color = Palette.Text, fontSize = 24.sp, fontWeight = FontWeight.SemiBold)
                Text("stay locked in", color = Palette.Muted, fontSize = 13.sp)
            }
            Column(
                Modifier.clip(RoundedCornerShape(12.dp)).background(Palette.Surface).padding(horizontal = 12.dp, vertical = 8.dp),
                horizontalAlignment = Alignment.End,
            ) {
                Text("Today", color = Palette.Muted, fontSize = 11.sp)
                Text("$todayPomos ${if (todayPomos == 1) "pomodoro" else "pomodoros"} · ${formatMinutes(todayMinutes)}",
                    color = Palette.Text, fontSize = 13.sp, fontWeight = FontWeight.SemiBold)
            }
        }

        SetupCard(resumeTick, requestNotifications)

        SubjectChips(s.subjects, active, planRows, enabled = true, onSelect = select, onOpenPlan = onOpenPlan)

        Spacer(Modifier.height(8.dp))
        TimerRing(
            remaining = if (st.run == RunState.IDLE) 1f else 1f - st.progress(now),
            color = color,
            dim = st.run != RunState.RUNNING,
            phaseText = phaseLabel(st.phase).uppercase(),
            timeText = formatTime(st.remaining(now)),
            subText = when (st.run) {
                RunState.IDLE -> active?.name ?: "ready when you are"
                RunState.PAUSED -> "paused"
                RunState.READY -> "press start"
                RunState.RUNNING -> if (st.phase == Phase.FOCUS) active?.name ?: "stay locked in" else "relax & recharge"
            },
            dots = s.sessionsBeforeLong,
            dotsFilled = st.completedInCycle,
            size = 290.dp,
        )

        // Motivation, right under the timer.
        Text(
            "“${Motivation.current(st, done, target, now)}”",
            color = Palette.Focus, fontSize = 16.sp, fontStyle = FontStyle.Italic, fontWeight = FontWeight.Medium,
            textAlign = TextAlign.Center, modifier = Modifier.padding(top = 4.dp, bottom = 16.dp),
        )

        Row(horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
            val (label, icon) = when (st.run) {
                RunState.RUNNING -> "Pause" to R.drawable.ic_pause
                RunState.PAUSED -> "Resume" to R.drawable.ic_play
                RunState.READY -> (if (st.phase == Phase.FOCUS) "Start focus" else "Start break") to R.drawable.ic_play
                RunState.IDLE -> "Lock in" to R.drawable.ic_play
            }
            Button(
                onClick = {
                    if (st.run == RunState.IDLE && Build.VERSION.SDK_INT >= 33 && !Perms.notifications(ctx)) requestNotifications()
                    TimerEngine.toggle()
                },
                enabled = !(strict && st.run == RunState.RUNNING),
                colors = ButtonDefaults.buttonColors(containerColor = color, contentColor = Palette.OnAccent),
                shape = RoundedCornerShape(16.dp),
                modifier = Modifier.height(56.dp).width(180.dp),
            ) {
                IconRes(icon, tint = Palette.OnAccent)
                Spacer(Modifier.width(8.dp))
                Text(label, fontSize = 17.sp, fontWeight = FontWeight.SemiBold)
            }
            val iconColors = IconButtonDefaults.filledTonalIconButtonColors(containerColor = Palette.Surface2, contentColor = Palette.Text)
            FilledTonalIconButton(
                onClick = { TimerEngine.skip() }, enabled = st.run != RunState.IDLE && !strict,
                colors = iconColors, modifier = Modifier.size(56.dp), shape = RoundedCornerShape(16.dp),
            ) { IconRes(R.drawable.ic_skip, tint = if (st.run != RunState.IDLE && !strict) Palette.Text else Palette.Muted) }
            FilledTonalIconButton(
                onClick = { TimerEngine.stop() }, enabled = st.run != RunState.IDLE && !strict,
                colors = iconColors, modifier = Modifier.size(56.dp), shape = RoundedCornerShape(16.dp),
            ) { IconRes(R.drawable.ic_stop, tint = if (st.run != RunState.IDLE && !strict) Palette.Text else Palette.Muted) }
        }

        Spacer(Modifier.height(16.dp))
        val status = when {
            st.lockedIn -> buildString {
                append("🔒 Locked in")
                val parts = mutableListOf<String>()
                if (s.allowAppsOn) parts += "allowed apps only" else if (s.blockAppsOn) parts += "blocked apps locked"
                if (s.allowSitesOn) parts += "allowed sites only" else if (s.blockSitesOn) parts += "blocked sites stopped"
                if (parts.isNotEmpty()) append(" — ").append(parts.joinToString(", "))
                if (strict) append(" · strict")
            }
            st.run == RunState.IDLE -> "Press Lock in to start. During focus Fokkus uses the switches below."
            st.phase == Phase.FOCUS -> "Next up: focus. Locking starts when you press start."
            else -> "Break time — everything is unlocked. Move, drink, breathe."
        }
        Text(
            status, color = if (st.lockedIn) Palette.Focus else Palette.Muted, fontSize = 14.sp,
            textAlign = TextAlign.Center, fontWeight = if (st.lockedIn) FontWeight.SemiBold else FontWeight.Normal,
        )

        // Quick switches for the four functions: decide per session what gets locked
        // (e.g. only "Blocked websites" for a programming session).
        Row(Modifier.fillMaxWidth().padding(top = 14.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            FilterQuick("Allowed", "websites", R.drawable.ic_globe, s.allowSitesOn, strict, Modifier.weight(1f)) { a, v -> a.copy(allowSitesOn = v) }
            FilterQuick("Blocked", "websites", R.drawable.ic_shield, s.blockSitesOn, strict, Modifier.weight(1f)) { a, v -> a.copy(blockSitesOn = v) }
        }
        Row(Modifier.fillMaxWidth().padding(top = 10.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            FilterQuick("Allowed", "apps", R.drawable.ic_apps, s.allowAppsOn, strict, Modifier.weight(1f)) { a, v -> a.copy(allowAppsOn = v) }
            FilterQuick("Blocked", "apps", R.drawable.ic_close, s.blockAppsOn, strict, Modifier.weight(1f)) { a, v -> a.copy(blockAppsOn = v) }
        }

        Spacer(Modifier.height(16.dp))
        TodayPlanCard(planRows, active, done, target, onSelect = select, onOpenPlan = onOpenPlan)

        Text(
            "${s.focusMin} min focus · ${s.shortBreakMin} min break · ${s.longBreakMin} min long break after ${s.sessionsBeforeLong}",
            color = Palette.Muted.copy(alpha = 0.7f), fontSize = 12.sp, modifier = Modifier.padding(top = 12.dp, bottom = 24.dp),
        )
    }
}

@Composable
private fun SubjectChips(
    subjects: List<Subject>,
    active: Subject?,
    rows: List<PlanRow>,
    enabled: Boolean,
    onSelect: (String?) -> Unit,
    onOpenPlan: () -> Unit,
) {
    if (subjects.isEmpty()) {
        TextButton(onClick = onOpenPlan, modifier = Modifier.padding(top = 4.dp)) {
            IconRes(R.drawable.ic_calendar, Palette.Focus, 18.dp)
            Spacer(Modifier.width(8.dp))
            Text("Plan your week: add your subjects", color = Palette.Focus)
        }
        return
    }
    val progress = rows.associateBy { it.subject.id }
    LazyRow(Modifier.fillMaxWidth().padding(top = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        items(subjects, key = { it.id }) { sub ->
            val sel = sub.id == active?.id
            val row = progress[sub.id]
            Row(
                Modifier.clip(RoundedCornerShape(20.dp))
                    .background(if (sel) Palette.Hover else Palette.Surface)
                    .then(if (sel) Modifier.border(1.5.dp, Palette.Focus, RoundedCornerShape(20.dp)) else Modifier)
                    .clickable(enabled = enabled) { onSelect(sub.id) }
                    .padding(horizontal = 14.dp, vertical = 9.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Box(Modifier.size(9.dp).clip(CircleShape).background(Color(SubjectColors.of(sub))))
                Spacer(Modifier.width(8.dp))
                Text(sub.name, color = Palette.Text, fontSize = 14.sp, fontWeight = if (sel) FontWeight.SemiBold else FontWeight.Normal)
                if (row != null && row.target > 0) {
                    Spacer(Modifier.width(6.dp))
                    Text(if (row.done >= row.target) "✓" else "${row.done}/${row.target}", color = Palette.Muted, fontSize = 12.sp)
                }
            }
        }
        item {
            Box(
                Modifier.clip(RoundedCornerShape(20.dp)).background(if (active == null) Palette.Hover else Palette.Surface)
                    .clickable(enabled = enabled) { onSelect(null) }.padding(horizontal = 14.dp, vertical = 9.dp),
            ) { Text("No subject", color = Palette.Muted, fontSize = 14.sp) }
        }
    }
}

@Composable
private fun TodayPlanCard(rows: List<PlanRow>, active: Subject?, done: Int, target: Int, onSelect: (String?) -> Unit, onOpenPlan: () -> Unit) {
    Card {
        Column {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text("Today's plan", color = Palette.Text, fontSize = 16.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
                if (target > 0) Text("$done / $target", color = Palette.Muted, fontSize = 14.sp)
            }
            if (rows.isEmpty()) {
                Text("Nothing planned for today.", color = Palette.Muted, fontSize = 13.sp, modifier = Modifier.padding(top = 6.dp))
                TextButton(onClick = onOpenPlan) { Text("Open the study plan", color = Palette.Focus) }
                return@Column
            }
            Spacer(Modifier.height(6.dp))
            rows.forEach { r ->
                val sel = r.subject.id == active?.id
                val c = Color(SubjectColors.of(r.subject))
                Column(
                    Modifier.fillMaxWidth().clip(RoundedCornerShape(10.dp))
                        .background(if (sel) Palette.Surface2 else Color.Transparent)
                        .clickable { onSelect(r.subject.id) }
                        .padding(horizontal = 10.dp, vertical = 8.dp),
                ) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Box(Modifier.size(9.dp).clip(CircleShape).background(c))
                        Spacer(Modifier.width(10.dp))
                        Text(r.subject.name, color = Palette.Text, fontSize = 15.sp, maxLines = 1, overflow = TextOverflow.Ellipsis,
                            fontWeight = if (sel) FontWeight.SemiBold else FontWeight.Normal, modifier = Modifier.weight(1f))
                        val finished = r.target > 0 && r.done >= r.target
                        Text(if (finished) "✓ done" else if (r.target > 0) "${r.done}/${r.target}" else "${r.done}",
                            color = if (finished) Palette.Focus else Palette.Text, fontSize = 14.sp)
                    }
                    Spacer(Modifier.height(6.dp))
                    val frac = if (r.target > 0) (r.done.toFloat() / r.target).coerceIn(0f, 1f) else if (r.done > 0) 1f else 0f
                    Box(Modifier.fillMaxWidth().padding(start = 19.dp).height(5.dp).clip(RoundedCornerShape(3.dp)).background(Palette.GridLine)) {
                        if (frac > 0f) Box(Modifier.fillMaxWidth(frac).fillMaxHeight().clip(RoundedCornerShape(3.dp)).background(c))
                    }
                }
            }
        }
    }
}

fun phaseLabel(p: Phase) = when (p) {
    Phase.FOCUS -> "Focus"
    Phase.SHORT_BREAK -> "Short break"
    Phase.LONG_BREAK -> "Long break"
}

/** One of the four function switches. Strict mode: a function can be switched on, but not off. */
@Composable
private fun FilterQuick(
    label: String, sub: String, icon: Int, on: Boolean, strict: Boolean, modifier: Modifier, set: (AppSettings, Boolean) -> AppSettings,
) {
    QuickToggle(label, icon, on, enabled = !(strict && on), modifier = modifier, sub = sub) { v ->
        SettingsStore.update { set(it, v) }
        TimerEngine.settingsChanged()
    }
}

@Composable
fun SetupCard(resumeTick: Int, requestNotifications: () -> Unit) {
    val ctx = LocalContext.current
    val s by SettingsStore.state.collectAsState()
    val notif = remember(resumeTick) { Perms.notifications(ctx) }
    val acc = remember(resumeTick) { Perms.accessibility(ctx) }
    val overlay = remember(resumeTick) { Perms.overlay(ctx) }
    val battery = remember(resumeTick) { Perms.battery(ctx) }
    val needOverlay = s.display != TimerDisplay.NOTIFICATION
    val needAcc = s.allowAppsOn || s.blockAppsOn || s.anySiteFilter
    if (notif && (acc || !needAcc) && (overlay || !needOverlay)) {
        if (!battery) {
            TextButton(onClick = { Perms.openBattery(ctx) }) {
                Text("Tip: let Fokkus run in the background for a reliable timer", color = Palette.Muted, fontSize = 12.sp)
            }
        }
        return
    }
    Card(Modifier.padding(top = 8.dp)) {
        Column {
            Text("Finish setup", color = Palette.Text, fontSize = 17.sp, fontWeight = FontWeight.SemiBold)
            Text("A few permissions so Fokkus can lock apps and show the timer.", color = Palette.Muted, fontSize = 13.sp)
            Spacer(Modifier.height(8.dp))
            if (!notif) SetupRow("Notifications", "Timer, break alerts and tips.", "Allow") {
                if (Build.VERSION.SDK_INT >= 33) requestNotifications() else Perms.openNotifications(ctx)
            }
            if (needAcc && !acc) {
                SetupRow("App & website blocking", "Settings → Accessibility → Fokkus → turn on.", "Open") { Perms.openAccessibility(ctx) }
                Text(
                    "Greyed out (“restricted setting”)? Open App info → ⋮ menu → “Allow restricted settings”, then try again.",
                    color = Palette.Muted, fontSize = 12.sp, lineHeight = 16.sp,
                )
                TextButton(onClick = { Perms.openAppInfo(ctx) }) { Text("Open App info", color = Palette.Focus) }
            }
            if (needOverlay && !overlay) SetupRow("Floating timer", "Allow “display over other apps”.", "Allow") { Perms.openOverlay(ctx) }
            if (!battery) SetupRow("Run in background", "Recommended so the timer is never stopped.", "Allow") { Perms.openBattery(ctx) }
        }
    }
}

@Composable
private fun SetupRow(title: String, desc: String, button: String, onClick: () -> Unit) {
    Row(Modifier.fillMaxWidth().padding(vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Text(title, color = Palette.Text, fontSize = 15.sp)
            Text(desc, color = Palette.Muted, fontSize = 12.sp, lineHeight = 16.sp)
        }
        Button(
            onClick = onClick, shape = RoundedCornerShape(12.dp),
            colors = ButtonDefaults.buttonColors(containerColor = Palette.Focus, contentColor = Palette.OnAccent),
        ) { Text(button) }
    }
}
