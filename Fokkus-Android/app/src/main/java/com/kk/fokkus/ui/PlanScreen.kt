package com.kk.fokkus.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
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
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.kk.fokkus.R
import com.kk.fokkus.data.AppSettings
import com.kk.fokkus.data.Analytics
import com.kk.fokkus.data.Planner
import com.kk.fokkus.data.SettingsStore
import com.kk.fokkus.data.Subject
import com.kk.fokkus.data.SubjectColors
import com.kk.fokkus.timer.TimerEngine
import java.time.LocalDate

private const val MAX_SUBJECTS = 10

@Composable
fun PlanScreen() {
    val s by SettingsStore.state.collectAsState()
    var input by rememberSaveable { mutableStateOf("") }
    var error by rememberSaveable { mutableStateOf(false) }
    var editingId by remember { mutableStateOf<String?>(null) }
    val todayIdx = Planner.dayIndex(LocalDate.now())

    fun save(f: (AppSettings) -> AppSettings) {
        SettingsStore.update(f)
        TimerEngine.settingsChanged()
    }

    fun add() {
        val name = input.trim().take(40)
        if (name.isEmpty() || s.subjects.size >= MAX_SUBJECTS || s.subjects.any { it.name.equals(name, true) }) { error = true; return }
        val sub = Subject.create(name, SubjectColors.nextIndex(s.subjects))
        save {
            it.copy(
                subjects = it.subjects + sub,
                activeSubjectId = it.activeSubjectId ?: sub.id,
                planSince = it.planSince ?: LocalDate.now().toString(),
            )
        }
        input = ""
        error = false
    }

    fun bump(sub: Subject, day: Int, d: Int) = save { st ->
        st.copy(subjects = st.subjects.map {
            if (it.id != sub.id) it else it.copy(targets = it.targets.mapIndexed { i, v -> if (i == day) (v + d).coerceIn(0, 16) else v })
        })
    }

    LazyColumn(Modifier.fillMaxSize().padding(horizontal = 20.dp)) {
        item { ScreenTitle("Study plan", "How many pomodoros per subject on each weekday.") }
        item {
            Row(Modifier.fillMaxWidth().padding(top = 10.dp), verticalAlignment = Alignment.CenterVertically) {
                OutlinedTextField(
                    value = input, onValueChange = { input = it; error = false }, singleLine = true, isError = error,
                    placeholder = { Text("New subject, e.g. Linear Algebra") },
                    keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Words, imeAction = ImeAction.Done),
                    keyboardActions = KeyboardActions(onDone = { add() }),
                    shape = RoundedCornerShape(14.dp),
                    colors = OutlinedTextFieldDefaults.colors(
                        focusedBorderColor = Palette.Focus, unfocusedBorderColor = Palette.Border,
                        focusedContainerColor = Palette.Surface, unfocusedContainerColor = Palette.Surface,
                    ),
                    modifier = Modifier.weight(1f),
                )
                Spacer(Modifier.width(10.dp))
                Button(
                    onClick = { add() }, shape = RoundedCornerShape(14.dp), modifier = Modifier.height(56.dp),
                    colors = ButtonDefaults.buttonColors(containerColor = Palette.Focus, contentColor = Palette.OnAccent),
                ) { Text("Add") }
            }
            Text(
                "Tap a day to add a pomodoro, long-press to remove one. 1 pomodoro = ${s.focusMin} min of focus.",
                color = Palette.Muted, fontSize = 12.sp, lineHeight = 16.sp, modifier = Modifier.padding(top = 8.dp, bottom = 4.dp),
            )
        }
        if (s.subjects.isEmpty()) item {
            Card(Modifier.padding(top = 8.dp)) {
                Text("No subjects yet. Add the courses you study (e.g. Analysis, Physics, Programming) and plan how many pomodoros each gets per day.",
                    color = Palette.Muted, fontSize = 14.sp, lineHeight = 19.sp)
            }
        }
        items(s.subjects, key = { it.id }) { sub ->
            SubjectPlanCard(sub, s.focusMin, todayIdx, onBump = { day, d -> bump(sub, day, d) }, onEdit = { editingId = sub.id })
        }
        if (s.subjects.isNotEmpty()) item { WeekTotals(s, todayIdx) }
        item {
            Card(Modifier.padding(top = 10.dp)) {
                SwitchRow(
                    "Move on automatically", s.autoNextSubject, { v -> save { it.copy(autoNextSubject = v) } },
                    description = "When today's target for a subject is reached, the timer switches to the next subject in your plan.",
                )
            }
        }
        item { Spacer(Modifier.height(24.dp)) }
    }

    val editing = s.subjects.firstOrNull { it.id == editingId }
    if (editing != null) EditSubjectDialog(
        subject = editing,
        onDismiss = { editingId = null },
        onRename = { name -> save { st -> st.copy(subjects = st.subjects.map { if (it.id == editing.id) it.copy(name = name) else it }) } },
        onMove = { dir ->
            save { st ->
                val list = st.subjects.toMutableList()
                val i = list.indexOfFirst { it.id == editing.id }
                val j = i + dir
                if (i >= 0 && j in list.indices) { list[i] = list[j]; list[j] = editing }
                st.copy(subjects = list)
            }
        },
        onDelete = {
            save { st -> st.copy(subjects = st.subjects.filter { it.id != editing.id }, activeSubjectId = st.activeSubjectId.takeIf { it != editing.id }) }
            editingId = null
        },
    )
}

@Composable
private fun SubjectPlanCard(sub: Subject, focusMin: Int, todayIdx: Int, onBump: (Int, Int) -> Unit, onEdit: () -> Unit) {
    val c = Color(SubjectColors.of(sub))
    val week = sub.targets.sum()
    Card(Modifier.padding(top = 10.dp)) {
        Column {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.size(10.dp).clip(CircleShape).background(c))
                Spacer(Modifier.width(10.dp))
                Text(sub.name, color = Palette.Text, fontSize = 16.sp, fontWeight = FontWeight.SemiBold, maxLines = 1,
                    overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
                Text(if (week > 0) "$week · ${Analytics.compact(week * focusMin.toDouble())}/week" else "not planned",
                    color = Palette.Muted, fontSize = 12.sp)
                Box(Modifier.size(40.dp).clip(RoundedCornerShape(10.dp)).clickable { onEdit() }, contentAlignment = Alignment.Center) {
                    IconRes(R.drawable.ic_edit, Palette.Muted, 18.dp, "Edit")
                }
            }
            Spacer(Modifier.height(6.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(5.dp)) {
                for (d in 0 until 7) {
                    val v = sub.targets[d]
                    Column(Modifier.weight(1f), horizontalAlignment = Alignment.CenterHorizontally) {
                        Text(Planner.dayLetter[d], color = if (d == todayIdx) Palette.Focus else Palette.Muted, fontSize = 11.sp,
                            fontWeight = if (d == todayIdx) FontWeight.Bold else FontWeight.Normal)
                        Spacer(Modifier.height(4.dp))
                        Box(
                            Modifier.fillMaxWidth().height(40.dp).clip(RoundedCornerShape(10.dp))
                                .background(if (v > 0) lerp(c, Palette.Bg, 0.55f) else Palette.Surface2)
                                .then(if (d == todayIdx) Modifier.border(1.dp, Palette.Focus.copy(alpha = 0.6f), RoundedCornerShape(10.dp)) else Modifier)
                                .pointerInput(sub.id, d) {
                                    detectTapGestures(onTap = { onBump(d, 1) }, onLongPress = { onBump(d, -1) })
                                },
                            contentAlignment = Alignment.Center,
                        ) {
                            Text(if (v > 0) "$v" else "·", color = if (v > 0) Palette.Text else Palette.Muted,
                                fontSize = 16.sp, fontWeight = FontWeight.SemiBold, textAlign = TextAlign.Center)
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun WeekTotals(s: AppSettings, todayIdx: Int) {
    val perDay = (0 until 7).map { d -> s.subjects.sumOf { it.targets[d] } }
    val week = perDay.sum()
    Card(Modifier.padding(top = 10.dp), color = Palette.Bg.copy(alpha = 0f)) {
        Column {
            Row {
                Text("Per day", color = Palette.Muted, fontSize = 13.sp, modifier = Modifier.weight(1f))
                Text("$week pomodoros ≈ ${formatMinutes(week * s.focusMin)} / week", color = Palette.Text, fontSize = 13.sp, fontWeight = FontWeight.SemiBold)
            }
            Spacer(Modifier.height(6.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(5.dp)) {
                for (d in 0 until 7) Column(Modifier.weight(1f), horizontalAlignment = Alignment.CenterHorizontally) {
                    Text(Planner.dayLetter[d], color = if (d == todayIdx) Palette.Focus else Palette.Muted, fontSize = 11.sp)
                    Text("${perDay[d]}", color = Palette.Text, fontSize = 15.sp, fontWeight = FontWeight.SemiBold)
                    Text(if (perDay[d] > 0) Analytics.compact(perDay[d] * s.focusMin.toDouble()) else "", color = Palette.Muted, fontSize = 10.sp)
                }
            }
        }
    }
}

@Composable
private fun EditSubjectDialog(subject: Subject, onDismiss: () -> Unit, onRename: (String) -> Unit, onMove: (Int) -> Unit, onDelete: () -> Unit) {
    var name by remember(subject.id) { mutableStateOf(subject.name) }
    var confirmDelete by remember(subject.id) { mutableStateOf(false) }
    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = Palette.Surface,
        title = { Text("Edit subject", color = Palette.Text) },
        text = {
            Column {
                OutlinedTextField(
                    value = name, onValueChange = { name = it.take(40) }, singleLine = true, label = { Text("Name") },
                    colors = OutlinedTextFieldDefaults.colors(focusedBorderColor = Palette.Focus, unfocusedBorderColor = Palette.Border),
                )
                Row(Modifier.padding(top = 8.dp)) {
                    TextButton(onClick = { onMove(-1) }) {
                        IconRes(R.drawable.ic_arrow_up, Palette.Text, 18.dp); Spacer(Modifier.width(4.dp)); Text("Move up", color = Palette.Text)
                    }
                    TextButton(onClick = { onMove(1) }) {
                        IconRes(R.drawable.ic_arrow_down, Palette.Text, 18.dp); Spacer(Modifier.width(4.dp)); Text("Move down", color = Palette.Text)
                    }
                }
                if (confirmDelete) Text(
                    "Tap Delete again to remove “${subject.name}”. Time you already focused on it stays in your statistics (as “Other”).",
                    color = Palette.Warn, fontSize = 13.sp, modifier = Modifier.padding(top = 6.dp),
                )
            }
        },
        confirmButton = {
            TextButton(onClick = {
                if (name.isNotBlank()) onRename(name.trim())
                onDismiss()
            }) { Text("Save", color = Palette.Focus) }
        },
        dismissButton = {
            TextButton(onClick = { if (confirmDelete) onDelete() else confirmDelete = true }) { Text("Delete", color = Palette.Warn) }
        },
    )
}
