package com.kk.fokkus.ui

import android.content.Context
import android.net.Uri
import android.os.Handler
import android.os.Looper
import android.widget.Toast
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.kk.fokkus.R
import com.kk.fokkus.data.Analytics
import com.kk.fokkus.data.AppSettings
import com.kk.fokkus.data.Backup
import com.kk.fokkus.data.CustomStation
import com.kk.fokkus.data.DistractionLog
import com.kk.fokkus.data.FocusRecord
import com.kk.fokkus.data.Insights
import com.kk.fokkus.data.Planner
import com.kk.fokkus.data.Review
import com.kk.fokkus.data.SessionLog
import com.kk.fokkus.data.SettingsStore
import com.kk.fokkus.data.SubjectColors
import com.kk.fokkus.data.SyncManager
import com.kk.fokkus.timer.FocusMusic
import com.kk.fokkus.timer.Phase
import com.kk.fokkus.timer.RunState
import com.kk.fokkus.timer.Stations
import com.kk.fokkus.timer.TimerEngine
import com.kk.fokkus.timer.TimerState
import java.time.LocalDate
import java.time.format.DateTimeFormatter
import java.util.Locale

private val en = Locale.UK
private fun fmt(p: String) = DateTimeFormatter.ofPattern(p, en)

private fun toast(ctx: Context, text: String) =
    Handler(Looper.getMainLooper()).post { Toast.makeText(ctx, text, Toast.LENGTH_LONG).show() }

/** Writes [text] to a file the user picked, off the main thread. */
private fun writeTo(ctx: Context, uri: Uri, text: String, done: String) = Thread {
    try {
        val cr = ctx.contentResolver
        (try { cr.openOutputStream(uri, "wt") } catch (e: Exception) { null } ?: cr.openOutputStream(uri, "w"))
            ?.use { it.write(text.toByteArray(Charsets.UTF_8)) } ?: throw IllegalStateException("no access")
        toast(ctx, done)
    } catch (e: Exception) {
        toast(ctx, "Couldn't save: ${e.message}")
    }
}.start()

@Composable
private fun WideButton(text: String, icon: Int?, enabled: Boolean = true, accent: Boolean = false, onClick: () -> Unit) {
    Button(
        onClick = onClick, enabled = enabled, shape = RoundedCornerShape(12.dp),
        colors = ButtonDefaults.buttonColors(
            containerColor = if (accent) Palette.Focus else Palette.Surface2,
            contentColor = if (accent) Palette.OnAccent else Palette.Text,
        ),
        modifier = Modifier.fillMaxWidth().padding(top = 8.dp),
    ) {
        if (icon != null) { IconRes(icon, if (accent) Palette.OnAccent else Palette.Text, 18.dp); Spacer(Modifier.width(8.dp)) }
        Text(text)
    }
}

// ---------------------------------------------------------------- Settings: sync

@Composable
fun SyncSection(s: AppSettings) {
    val status by SyncManager.status.collectAsState()
    val busy by SyncManager.busy.collectAsState()
    val create = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("application/json")) { uri -> uri?.let(SyncManager::use) }
    val open = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri -> uri?.let(SyncManager::use) }
    SectionTitle("Sync with your PC")
    Card {
        Column {
            if (s.syncUri == null) {
                Text("Share statistics, notes and the study plan with Fokkus on your PC through one file in Google Drive or OneDrive.",
                    color = Palette.Text, fontSize = 14.sp, lineHeight = 19.sp)
                Text("PC first? On Windows: Sync & data → Choose folder. Then here: “Use an existing sync file” and pick Fokkus-sync.json.",
                    color = Palette.Muted, fontSize = 12.sp, lineHeight = 16.sp, modifier = Modifier.padding(top = 6.dp))
                WideButton("Use an existing sync file…", R.drawable.ic_open, accent = true) { open.launch(arrayOf("application/json", "application/octet-stream", "text/plain", "*/*")) }
                WideButton("Create a new sync file…", R.drawable.ic_add) { create.launch("Fokkus-sync.json") }
            } else {
                val text = status ?: if (s.lastSyncMs > 0) "Last synced " + java.text.DateFormat.getDateTimeInstance(java.text.DateFormat.SHORT, java.text.DateFormat.SHORT).format(java.util.Date(s.lastSyncMs)) else "Not synced yet"
                Text(text, color = if (SyncManager.lastFailed) Palette.Warn else if (s.lastSyncMs > 0) Palette.Focus else Palette.Muted,
                    fontSize = 15.sp, fontWeight = FontWeight.SemiBold)
                Text("Syncs when you open Fokkus and after each focus block. Blocking lists stay on each device.",
                    color = Palette.Muted, fontSize = 12.sp, lineHeight = 16.sp, modifier = Modifier.padding(top = 4.dp))
                WideButton(if (busy) "Syncing…" else "Sync now", R.drawable.ic_timer, enabled = !busy, accent = true) { SyncManager.run() }
                WideButton("Stop syncing", null) { SyncManager.stop() }
            }
        }
    }
}

// ---------------------------------------------------------------- Settings: backup & export

@Composable
fun BackupSection(s: AppSettings, strict: Boolean) {
    val ctx = LocalContext.current
    var pending by remember { mutableStateOf<Backup.Parsed?>(null) }
    val csv = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("text/csv")) { uri ->
        uri?.let { writeTo(ctx, it, "﻿" + Backup.csv(SettingsStore.current, SessionLog.records.value), "Statistics exported") }
    }
    val save = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("application/json")) { uri ->
        uri?.let { writeTo(ctx, it, Backup.create(SettingsStore.current, SessionLog.records.value, DistractionLog.entries.value), "Backup saved") }
    }
    val restore = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri != null) Thread {
            try {
                val text = ctx.contentResolver.openInputStream(uri)?.use { it.readBytes().toString(Charsets.UTF_8) } ?: ""
                val p = Backup.parse(text)
                Handler(Looper.getMainLooper()).post { pending = p }
            } catch (e: Exception) {
                toast(ctx, "This file can't be restored: ${e.message}")
            }
        }.start()
    }
    val today = LocalDate.now().toString()
    SectionTitle("Backup & export")
    Card {
        Column {
            Text("The CSV opens in Excel or Google Sheets (one row per focus block). A backup holds your settings, plan, statistics, notes and distraction log.",
                color = Palette.Muted, fontSize = 12.sp, lineHeight = 16.sp)
            WideButton("Export statistics (CSV)…", R.drawable.ic_chart) { csv.launch("Fokkus-statistics-$today.csv") }
            WideButton("Back up everything…", R.drawable.ic_add) { save.launch("Fokkus-backup-$today.json") }
            WideButton("Restore a backup…", R.drawable.ic_open, enabled = !strict) { restore.launch(arrayOf("application/json", "application/octet-stream", "text/plain", "*/*")) }
        }
    }
    val p = pending
    if (p != null) AlertDialog(
        onDismissRequest = { pending = null },
        containerColor = Palette.Surface,
        title = { Text("Restore backup?", color = Palette.Text) },
        text = {
            Text(
                if (p.settings != null) "Restore your settings and study plan from ${p.created}? Statistics, notes and distractions from the backup are added to the ones you have — nothing is deleted."
                else "This backup is from your PC (${p.created}). Its statistics, notes and distractions will be added — your phone settings stay as they are.",
                color = Palette.Text, fontSize = 14.sp,
            )
        },
        confirmButton = {
            TextButton(onClick = {
                pending = null
                val added = Backup.restore(p)
                TimerEngine.settingsChanged()
                toast(ctx, "Backup restored · $added focus blocks added")
            }) { Text("Restore", color = Palette.Focus) }
        },
        dismissButton = { TextButton(onClick = { pending = null }) { Text("Cancel", color = Palette.Muted) } },
    )
}

// ---------------------------------------------------------------- Timer: focus music

@Composable
fun MusicRow(s: AppSettings, modifier: Modifier = Modifier) {
    var menu by remember { mutableStateOf(false) }
    var adding by remember { mutableStateOf(false) }
    val error by FocusMusic.error.collectAsState()
    val station = Stations.find(s, s.musicStation)
    fun set(f: (AppSettings) -> AppSettings) { SettingsStore.update(f); TimerEngine.settingsChanged() }
    Box(modifier) {
        Row(
            Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(Palette.Surface)
                .clickable { menu = true }.padding(start = 12.dp, end = 8.dp, top = 4.dp, bottom = 4.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            IconRes(R.drawable.ic_music, if (s.musicOn) Palette.Focus else Palette.Muted, 20.dp)
            Column(Modifier.weight(1f).padding(start = 10.dp)) {
                Text(if (error != null && s.musicOn) "Can't play right now" else station.name, color = Palette.Text, fontSize = 15.sp)
                Text(if (s.musicOn) "Plays while you focus · tap to change" else "Focus music · tap to pick a station", color = Palette.Muted, fontSize = 11.sp, maxLines = 1)
            }
            Switch(
                checked = s.musicOn, onCheckedChange = { v -> set { it.copy(musicOn = v) } },
                colors = SwitchDefaults.colors(
                    checkedTrackColor = Palette.Focus, checkedThumbColor = Color.White,
                    uncheckedTrackColor = Palette.Surface2, uncheckedThumbColor = Palette.Muted, uncheckedBorderColor = Palette.Border,
                ),
            )
        }
        DropdownMenu(expanded = menu, onDismissRequest = { menu = false }, containerColor = Palette.Surface) {
            Stations.all(s).forEach { st ->
                DropdownMenuItem(
                    text = {
                        Column {
                            Text((if (st.id == s.musicStation) "✓  " else "     ") + st.name, color = Palette.Text,
                                fontWeight = if (st.id == s.musicStation) FontWeight.SemiBold else FontWeight.Normal)
                            Text("     " + st.credit, color = Palette.Muted, fontSize = 11.sp)
                        }
                    },
                    onClick = { menu = false; set { it.copy(musicStation = st.id, musicOn = true) } },
                )
            }
            HorizontalDivider(color = Palette.Border)
            Row(Modifier.padding(horizontal = 12.dp, vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                Text("Volume", color = Palette.Muted, fontSize = 13.sp, modifier = Modifier.padding(end = 8.dp))
                listOf(20, 40, 60, 80, 100).forEach { v ->
                    val on = s.musicVolume == v
                    Box(
                        Modifier.padding(horizontal = 2.dp).clip(RoundedCornerShape(8.dp)).background(if (on) Palette.Focus else Palette.Surface2)
                            .clickable { set { it.copy(musicVolume = v) } }.padding(horizontal = 8.dp, vertical = 6.dp),
                    ) { Text("$v", color = if (on) Palette.OnAccent else Palette.Text, fontSize = 12.sp) }
                }
            }
            HorizontalDivider(color = Palette.Border)
            DropdownMenuItem(text = { Text("Add a stream URL…", color = Palette.Text) }, onClick = { menu = false; adding = true })
            if (station.id.startsWith("custom:")) DropdownMenuItem(
                text = { Text("Remove “${station.name}”", color = Palette.Warn) },
                onClick = {
                    menu = false
                    set { st -> st.copy(customStations = st.customStations.filter { "custom:" + it.url != station.id }, musicStation = "lofi") }
                },
            )
            Text("Streams by SomaFM — free, listener-supported radio", color = Palette.Muted, fontSize = 11.sp,
                modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp))
        }
    }
    if (adding) {
        var url by remember { mutableStateOf("") }
        var bad by remember { mutableStateOf(false) }
        AlertDialog(
            onDismissRequest = { adding = false },
            containerColor = Palette.Surface,
            title = { Text("Add a stream", color = Palette.Text) },
            text = {
                Column {
                    Text("Link to an internet radio stream (http/https, e.g. ending in .mp3). YouTube links can't be played here.",
                        color = Palette.Muted, fontSize = 13.sp)
                    OutlinedTextField(
                        value = url, onValueChange = { url = it; bad = false }, singleLine = true, isError = bad,
                        placeholder = { Text("https://…") },
                        colors = OutlinedTextFieldDefaults.colors(focusedBorderColor = Palette.Focus, unfocusedBorderColor = Palette.Border),
                        modifier = Modifier.padding(top = 8.dp),
                    )
                }
            },
            confirmButton = {
                TextButton(onClick = {
                    val u = url.trim()
                    if (!Stations.isStreamUrl(u)) { bad = true; return@TextButton }
                    adding = false
                    set { st ->
                        st.copy(
                            customStations = if (st.customStations.any { it.url == u }) st.customStations else st.customStations + CustomStation(Stations.hostOf(u), u),
                            musicStation = "custom:$u", musicOn = true,
                        )
                    }
                }) { Text("Add", color = Palette.Focus) }
            },
            dismissButton = { TextButton(onClick = { adding = false }) { Text("Cancel", color = Palette.Muted) } },
        )
    }
}

// ---------------------------------------------------------------- Timer: during the break

/** "What did you finish?" for the pomodoro that just ended. */
@Composable
fun NoteCard(st: TimerState, s: AppSettings) {
    val records by SessionLog.records.collectAsState()
    var dismissed by rememberSaveable { mutableStateOf<String?>(null) }
    val last = records.lastOrNull { it.pomodoros > 0 } ?: return
    val ended = last.startMs + last.seconds * 1000L
    if (!s.askForNotes || st.phase == Phase.FOCUS || !st.inSession || !last.note.isNullOrEmpty() ||
        dismissed == last.key || System.currentTimeMillis() - ended > 45 * 60_000L) return
    var text by rememberSaveable(last.key) { mutableStateOf("") }
    val subject = s.subjects.firstOrNull { it.id == last.subjectId }?.name
    fun save() {
        if (text.isNotBlank()) { SessionLog.setNote(last.key, text); SyncManager.runSoon() }
        dismissed = last.key
    }
    Card(Modifier.padding(top = 14.dp)) {
        Column {
            Text("✎  What did you finish?", color = Palette.Text, fontSize = 16.sp, fontWeight = FontWeight.SemiBold)
            Text((subject?.let { "$it · " } ?: "") + "${last.seconds / 60} min · " + last.start.format(fmt("HH:mm")),
                color = Palette.Muted, fontSize = 12.sp)
            OutlinedTextField(
                value = text, onValueChange = { text = it.take(200) }, maxLines = 3,
                placeholder = { Text("e.g. Exercise sheet 3, tasks 1–2") },
                keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Sentences, imeAction = ImeAction.Done),
                keyboardActions = KeyboardActions(onDone = { save() }),
                shape = RoundedCornerShape(12.dp),
                colors = OutlinedTextFieldDefaults.colors(focusedBorderColor = Palette.Focus, unfocusedBorderColor = Palette.Border),
                modifier = Modifier.fillMaxWidth().padding(top = 8.dp),
            )
            Row(Modifier.fillMaxWidth().padding(top = 4.dp), horizontalArrangement = Arrangement.End) {
                TextButton(onClick = { dismissed = last.key }) { Text("Skip", color = Palette.Muted) }
                TextButton(onClick = { save() }) { Text("Save", color = Palette.Focus) }
            }
        }
    }
}

/** Smarter breaks: offer a longer break after a long block or skipped breaks. */
@Composable
fun LongerBreakCard(st: TimerState) {
    if (st.suggestMin <= 0 || st.phase == Phase.FOCUS || !st.inSession) return
    val ready = st.run == RunState.READY
    Card(Modifier.padding(top = 14.dp), color = Palette.ShortBreak.copy(alpha = 0.14f)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text("Give your brain a longer break", color = Palette.Text, fontSize = 15.sp, fontWeight = FontWeight.SemiBold)
                Text("${st.suggestMin} minutes instead of ${st.totalMs / 60_000L}", color = Palette.Muted, fontSize = 12.sp)
            }
            Button(
                onClick = { TimerEngine.setPhaseLength(st.suggestMin, start = ready) }, shape = RoundedCornerShape(12.dp),
                colors = ButtonDefaults.buttonColors(containerColor = Palette.ShortBreak, contentColor = Palette.Bg),
            ) { Text(if (ready) "Start ${st.suggestMin} min" else "Make it ${st.suggestMin}") }
        }
    }
}

// ---------------------------------------------------------------- Weekly review

@Composable
fun ReviewDialog(day: LocalDate, onDismiss: () -> Unit) {
    val s by SettingsStore.state.collectAsState()
    val records by SessionLog.records.collectAsState()
    val dist by DistractionLog.entries.collectAsState()
    val w = remember(s, records, dist, day) { Review.compute(s, records, dist, day) }
    val r = w.stats
    val last = r.end.minusDays(1)
    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = Palette.Bg,
        title = { Text("Your week · ${r.start.format(fmt("d MMM"))} – ${last.format(fmt("d MMM"))}", color = Palette.Text) },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState())) {
                val headline = when {
                    r.totalMinutes < 1 -> "No focus time this week. A fresh start is one pomodoro away."
                    r.planned > 0 && r.pomodoros >= r.planned -> "You did everything you planned. Great week!"
                    w.prevMinutes >= 1 && r.totalMinutes > w.prevMinutes * 1.1 -> "More focus than last week. Keep the momentum!"
                    else -> "Here's how your week went."
                }
                Text(headline, color = Palette.Muted, fontSize = 14.sp)
                Spacer(Modifier.height(10.dp))
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    MiniTile("Focused", Analytics.compact(r.totalMinutes),
                        if (w.prevMinutes >= 1) "%+d%% vs last week".format(Math.round(100 * (r.totalMinutes - w.prevMinutes) / w.prevMinutes)) else "first week",
                        Modifier.weight(1f))
                    MiniTile("Pomodoros", "${r.pomodoros}", if (r.planned > 0) "of ${r.planned} planned" else "completed", Modifier.weight(1f))
                    MiniTile("Streak", "${r.streak}d", "in a row", Modifier.weight(1f))
                }
                SectionTitle("By subject")
                if (r.bySubject.isEmpty()) Text("No subjects tracked this week.", color = Palette.Muted, fontSize = 13.sp)
                r.bySubject.forEach { st ->
                    Row(Modifier.padding(vertical = 3.dp), verticalAlignment = Alignment.CenterVertically) {
                        Box(Modifier.width(10.dp).height(10.dp).background(Color(st.series.color)))
                        Text(st.series.name, color = Palette.Text, fontSize = 14.sp, modifier = Modifier.weight(1f).padding(start = 8.dp))
                        Text(Analytics.compact(st.minutes) + if (st.planned > 0) " · ${st.pomodoros}/${st.planned}" else "", color = Palette.Text, fontSize = 13.sp)
                    }
                }
                SectionTitle("Your rhythm")
                Text(
                    (if (w.bestHour >= 0) "You focused most between %02d:00 and %02d:00 — plan your hardest subject there.".format(w.bestHour, w.bestHour + 2)
                    else "Not enough focus time yet to see your best hours.") +
                        (w.bestDay?.let { "\nBest day: ${it.format(fmt("EEEE"))} (${Analytics.compact(w.bestDayMinutes)})." } ?: ""),
                    color = Palette.Text, fontSize = 14.sp, lineHeight = 19.sp,
                )
                SectionTitle("What you finished" + if (w.notes.isNotEmpty()) " (${w.notes.size})" else "")
                if (w.notes.isEmpty()) Text("No notes this week. After a pomodoro, write one line about what you got done.", color = Palette.Muted, fontSize = 13.sp)
                w.notes.take(14).forEach { n -> NoteLine(n, s) }
                SectionTitle("Distractions you resisted" + if (w.distractionCount > 0) " (${w.distractionCount})" else "")
                if (w.distractions.isEmpty()) Text("Nothing was blocked this week — nice.", color = Palette.Muted, fontSize = 13.sp)
                w.distractions.forEach { d ->
                    Text("${d.name}${if (d.kind == "app") " (app)" else ""} — ${Insights.describe(d)}", color = Palette.Text, fontSize = 13.sp,
                        modifier = Modifier.padding(vertical = 2.dp))
                }
                val next = s.subjects.mapNotNull { sub -> Planner.daysLeft(sub)?.let { sub to it } }.minByOrNull { it.second }
                if (next != null) {
                    SectionTitle("Coming up")
                    Text("${next.first.name}: ${Planner.deadlineText(next.first, next.second)}.",
                        color = if (next.second <= 7) Palette.ShortBreak else Palette.Text, fontSize = 14.sp)
                }
            }
        },
        confirmButton = { TextButton(onClick = onDismiss) { Text("Close", color = Palette.Focus) } },
    )
}

@Composable
private fun MiniTile(label: String, value: String, sub: String, modifier: Modifier) {
    Column(modifier.clip(RoundedCornerShape(12.dp)).background(Palette.Surface).padding(10.dp)) {
        Text(label, color = Palette.Muted, fontSize = 11.sp)
        Text(value, color = Palette.Text, fontSize = 18.sp, fontWeight = FontWeight.SemiBold, maxLines = 1)
        Text(sub, color = Palette.Muted, fontSize = 10.sp, maxLines = 2, lineHeight = 12.sp)
    }
}

@Composable
private fun NoteLine(n: FocusRecord, s: AppSettings, onClick: (() -> Unit)? = null) {
    val subject = s.subjects.firstOrNull { it.id == n.subjectId }
    Row(
        Modifier.fillMaxWidth().clip(RoundedCornerShape(8.dp)).then(if (onClick != null) Modifier.clickable(onClick = onClick) else Modifier)
            .padding(vertical = 5.dp),
        verticalAlignment = Alignment.Top,
    ) {
        Box(Modifier.padding(top = 5.dp).width(8.dp).height(8.dp).clip(RoundedCornerShape(4.dp)).background(Color(SubjectColors.of(subject))))
        Column(Modifier.padding(start = 10.dp)) {
            Text(n.note ?: "", color = Palette.Text, fontSize = 14.sp)
            Text(n.start.format(fmt("EEE d MMM · HH:mm")) + (subject?.let { " · ${it.name}" } ?: ""), color = Palette.Muted, fontSize = 11.sp)
        }
    }
}

// ---------------------------------------------------------------- Statistics: notes + distractions

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun NotesAndDistractions(start: LocalDate, end: LocalDate, period: String) {
    val s by SettingsStore.state.collectAsState()
    val records by SessionLog.records.collectAsState()
    val dist by DistractionLog.entries.collectAsState()
    var editing by remember { mutableStateOf<FocusRecord?>(null) }
    val notes = remember(records, start, end) {
        records.filter { !it.note.isNullOrEmpty() && !it.day.isBefore(start) && it.day.isBefore(end) }.sortedByDescending { it.startMs }
    }
    SectionTitle("What you finished" + if (notes.isNotEmpty()) " (${notes.size})" else "")
    if (notes.isEmpty()) Text("No notes $period. They're asked for when a pomodoro ends.", color = Palette.Muted, fontSize = 13.sp)
    notes.take(30).forEach { n -> NoteLine(n, s) { editing = n } }
    if (notes.size > 30) Text("… and ${notes.size - 30} more (export the CSV to see all).", color = Palette.Muted, fontSize = 12.sp)

    val top = remember(dist, start, end) { Insights.top(dist, start, end, 8) }
    val count = remember(dist, start, end) { Insights.count(dist, start, end) }
    SectionTitle("Distractions" + if (count > 0) " ($count)" else "")
    if (top.isEmpty()) Text("Nothing blocked $period.", color = Palette.Muted, fontSize = 13.sp)
    top.forEach { d ->
        Row(Modifier.fillMaxWidth().padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
            Text(d.name + if (d.kind == "app") " (app)" else "", color = Palette.Text, fontSize = 14.sp, modifier = Modifier.weight(1f))
            Text(Insights.describe(d), color = Palette.Muted, fontSize = 12.sp)
        }
    }

    val e = editing
    if (e != null) {
        var text by remember(e.key) { mutableStateOf(e.note ?: "") }
        AlertDialog(
            onDismissRequest = { editing = null },
            containerColor = Palette.Surface,
            title = { Text("Note", color = Palette.Text) },
            text = {
                OutlinedTextField(
                    value = text, onValueChange = { text = it.take(200) }, maxLines = 4, label = { Text("What did you finish?") },
                    colors = OutlinedTextFieldDefaults.colors(focusedBorderColor = Palette.Focus, unfocusedBorderColor = Palette.Border),
                )
            },
            confirmButton = {
                TextButton(onClick = { SessionLog.setNote(e.key, text); SyncManager.runSoon(); editing = null }) { Text("Save", color = Palette.Focus) }
            },
            dismissButton = { TextButton(onClick = { editing = null }) { Text("Cancel", color = Palette.Muted) } },
        )
    }
}
