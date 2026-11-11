package com.kk.fokkus.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.kk.fokkus.R
import com.kk.fokkus.data.AppSettings
import com.kk.fokkus.data.Profile
import com.kk.fokkus.data.SettingsStore
import com.kk.fokkus.data.currentProfile
import com.kk.fokkus.timer.TimerEngine

private enum class NameAction { NEW, RENAME }

/** "Profile: Study ▾" — one tap sets all four blocking functions (+ strict mode). */
@Composable
fun ProfilePicker(s: AppSettings, strictLocked: Boolean, modifier: Modifier = Modifier) {
    var open by remember { mutableStateOf(false) }
    var naming by remember { mutableStateOf<NameAction?>(null) }
    val cur = s.currentProfile()
    val last = s.profiles.firstOrNull { it.id == s.activeProfileId }

    fun update(f: (AppSettings) -> AppSettings) {
        SettingsStore.update(f)
        TimerEngine.settingsChanged()
    }

    Box(modifier) {
        Row(
            Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(Palette.Surface)
                .clickable { open = true }.padding(horizontal = 12.dp, vertical = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            IconRes(R.drawable.ic_shield, Palette.Focus, 20.dp)
            Spacer(Modifier.width(8.dp))
            Column(Modifier.weight(1f)) {
                Text("Profile: ${cur?.name ?: "Custom"}", color = Palette.Text, fontSize = 15.sp, fontWeight = FontWeight.SemiBold)
                Text(cur?.describe() ?: "Your own mix of the switches below", color = Palette.Muted, fontSize = 11.sp, maxLines = 1)
            }
            IconRes(R.drawable.ic_arrow_down, Palette.Muted, 18.dp)
        }
        DropdownMenu(expanded = open, onDismissRequest = { open = false }, containerColor = Palette.Surface) {
            s.profiles.forEach { p ->
                // Strict mode: a profile that loosens the lock can't be picked until focus ends.
                val blocked = strictLocked && p.loosens(s)
                DropdownMenuItem(
                    text = {
                        Column {
                            Text((if (p == cur) "✓  " else "     ") + p.name, color = if (blocked) Palette.Muted else Palette.Text,
                                fontWeight = if (p == cur) FontWeight.SemiBold else FontWeight.Normal)
                            Text("     " + p.describe(), color = Palette.Muted, fontSize = 11.sp)
                        }
                    },
                    enabled = !blocked,
                    onClick = { open = false; update { p.applyTo(it) } },
                )
            }
            HorizontalDivider(color = Palette.Border)
            DropdownMenuItem(text = { Text("Save current switches as a new profile…", color = Palette.Text) },
                onClick = { open = false; naming = NameAction.NEW })
            if (last != null) {
                DropdownMenuItem(
                    text = { Text("Update “${last.name}” to the current switches", color = if (last.matches(s)) Palette.Muted else Palette.Text) },
                    enabled = !last.matches(s),
                    onClick = {
                        open = false
                        update { st -> st.copy(profiles = st.profiles.map { if (it.id == last.id) Profile.capture(it.name, st, it.id) else it }) }
                    },
                )
                DropdownMenuItem(text = { Text("Rename “${last.name}”…", color = Palette.Text) }, onClick = { open = false; naming = NameAction.RENAME })
                DropdownMenuItem(
                    text = { Text("Delete “${last.name}”", color = if (s.profiles.size > 1) Palette.Warn else Palette.Muted) },
                    enabled = s.profiles.size > 1,
                    onClick = {
                        open = false
                        update { st ->
                            st.copy(
                                profiles = st.profiles.filter { it.id != last.id },
                                activeProfileId = null,
                                subjects = st.subjects.map { if (it.profileId == last.id) it.copy(profileId = null) else it },
                            )
                        }
                    },
                )
            }
        }
    }

    val action = naming
    if (action != null) NameDialog(
        title = if (action == NameAction.NEW) "New profile" else "Rename profile",
        initial = if (action == NameAction.RENAME) last?.name.orEmpty() else "",
        onDismiss = { naming = null },
        onSave = { name ->
            naming = null
            if (action == NameAction.NEW) {
                update { st ->
                    val p = Profile.capture(name, st)
                    st.copy(profiles = st.profiles + p, activeProfileId = p.id)
                }
            } else if (last != null) {
                update { st -> st.copy(profiles = st.profiles.map { if (it.id == last.id) it.copy(name = name) else it }) }
            }
        },
    )
}

@Composable
private fun NameDialog(title: String, initial: String, onDismiss: () -> Unit, onSave: (String) -> Unit) {
    var name by remember { mutableStateOf(initial) }
    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = Palette.Surface,
        title = { Text(title, color = Palette.Text) },
        text = {
            OutlinedTextField(
                value = name, onValueChange = { name = it.take(30) }, singleLine = true, label = { Text("Name") },
                colors = OutlinedTextFieldDefaults.colors(focusedBorderColor = Palette.Focus, unfocusedBorderColor = Palette.Border),
            )
        },
        confirmButton = {
            TextButton(onClick = { if (name.isNotBlank()) onSave(name.trim()) }) { Text("Save", color = Palette.Focus) }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel", color = Palette.Muted) } },
    )
}
