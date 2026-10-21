package com.kk.fokkus.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
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
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.kk.fokkus.R
import com.kk.fokkus.data.Domains
import com.kk.fokkus.data.Presets
import com.kk.fokkus.data.SettingsStore
import com.kk.fokkus.timer.TimerEngine

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun WebsitesScreen() {
    val s by SettingsStore.state.collectAsState()
    val st by TimerEngine.state.collectAsState()
    val strict = s.strictMode && st.lockedIn
    // Strict mode: lists may only get stricter. Allow list: switching ON loosens; block list: switching OFF loosens.
    fun loosens(turningOn: Boolean) = if (s.webAllowMode) turningOn else !turningOn
    fun allowed(turningOn: Boolean) = !(strict && loosens(turningOn))
    var input by rememberSaveable { mutableStateOf("") }
    var error by rememberSaveable { mutableStateOf(false) }

    fun setSites(list: List<String>, off: Set<String> = s.currentDisabled) = SettingsStore.update { it.withCurrent(list, off) }

    fun add() {
        val d = Domains.normalize(input)
        if (d == null || !allowed(true)) { error = true; return }
        setSites(s.currentSites + d, s.currentDisabled - d)
        input = ""
        error = false
    }

    val presets = if (s.webAllowMode) Presets.allowSites else Presets.blockSites
    val onCount = s.currentSites.count { it !in s.currentDisabled }

    LazyColumn(Modifier.fillMaxSize().padding(horizontal = 20.dp)) {
        item { ScreenTitle("Websites", "What your browser may open during focus. Breaks are always free.") }
        item { StrictBanner(strict) }
        item {
            Card(Modifier.padding(top = 8.dp)) {
                SwitchRow(
                    "Filter websites in browsers", s.webFilter, { v -> if (v || !strict) SettingsStore.update { it.copy(webFilter = v) } },
                    description = "Chrome, Edge, Brave, Firefox, Samsung Internet, Opera, DuckDuckGo, Vivaldi. A blocked page is swapped for an allowed one.",
                    enabled = !strict || !s.webFilter,
                )
            }
        }
        item {
            Spacer(Modifier.height(14.dp))
            Segmented(
                listOf("Allow only my list", "Block my list"), if (s.webAllowMode) 0 else 1,
                { i -> SettingsStore.update { it.copy(webAllowMode = i == 0) } }, enabled = !strict,
            )
            Text(
                if (s.webAllowMode) "Strict: every site except the ones switched on (and their subdomains) is blocked."
                else "Relaxed: only the sites switched on (and their subdomains) are blocked.",
                color = Palette.Muted, fontSize = 13.sp, modifier = Modifier.padding(top = 8.dp),
            )
        }
        item {
            SectionTitle("Categories \u00B7 tap to turn on/off")
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                presets.forEach { p ->
                    val on = Presets.isOn(p, s.currentSites, s.currentDisabled)
                    val enabled = allowed(!on)
                    Box(
                        Modifier.clip(RoundedCornerShape(10.dp))
                            .background(if (on) lerp(Palette.Focus, Palette.Bg, 0.55f) else Palette.Surface2)
                            .clickable(enabled = enabled) {
                                val (list, off) = Presets.toggle(p, presets, s.currentSites, s.currentDisabled)
                                setSites(list, off)
                            }
                            .padding(horizontal = 12.dp, vertical = 8.dp),
                    ) {
                        Text((if (on) "\u2713  " else "+  ") + p.name, fontSize = 14.sp,
                            color = if (enabled) Palette.Text else Palette.Muted, fontWeight = if (on) FontWeight.SemiBold else FontWeight.Normal)
                    }
                }
            }
        }
        item {
            Row(Modifier.fillMaxWidth().padding(top = 16.dp), verticalAlignment = Alignment.CenterVertically) {
                OutlinedTextField(
                    value = input, onValueChange = { input = it; error = false }, singleLine = true, enabled = allowed(true),
                    placeholder = { Text("e.g. moodle.tum.de") }, isError = error,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri, imeAction = ImeAction.Done),
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
                    onClick = { add() }, enabled = allowed(true), shape = RoundedCornerShape(14.dp), modifier = Modifier.height(56.dp),
                    colors = ButtonDefaults.buttonColors(containerColor = Palette.Focus, contentColor = Palette.OnAccent),
                ) { Text("Add") }
            }
        }
        item {
            SectionTitle((if (s.webAllowMode) "Allowed sites" else "Blocked sites") + " ($onCount of ${s.currentSites.size} on)")
            Text("Use the switch to turn a site on or off without deleting it.", color = Palette.Muted, fontSize = 12.sp,
                modifier = Modifier.padding(bottom = 6.dp))
        }
        items(s.currentSites, key = { it }) { site ->
            val on = site !in s.currentDisabled
            Row(
                Modifier.fillMaxWidth().padding(vertical = 2.dp).clip(RoundedCornerShape(12.dp)).background(Palette.Surface)
                    .padding(start = 16.dp, end = 4.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(site, color = if (on) Palette.Text else Palette.Muted, fontSize = 15.sp, modifier = Modifier.weight(1f).padding(vertical = 10.dp))
                Switch(
                    checked = on, enabled = allowed(!on),
                    onCheckedChange = { v -> setSites(s.currentSites, if (v) s.currentDisabled - site else s.currentDisabled + site) },
                    colors = SwitchDefaults.colors(
                        checkedTrackColor = Palette.Focus, checkedThumbColor = Color.White,
                        uncheckedTrackColor = Palette.Surface2, uncheckedThumbColor = Palette.Muted, uncheckedBorderColor = Palette.Border,
                    ),
                )
                if (allowed(!s.webAllowMode)) Box(
                    Modifier.size(44.dp).clip(RoundedCornerShape(10.dp)).clickable { setSites(s.currentSites - site, s.currentDisabled - site) },
                    contentAlignment = Alignment.Center,
                ) { IconRes(R.drawable.ic_delete, Palette.Muted, 20.dp, "Remove") }
            }
        }
        item { Spacer(Modifier.height(24.dp)) }
    }
}