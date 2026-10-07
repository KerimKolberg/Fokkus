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
    // In strict mode you may tighten (block more) but not loosen.
    val canAdd = !(strict && s.webAllowMode)
    val canRemove = !(strict && !s.webAllowMode)
    var input by rememberSaveable { mutableStateOf("") }
    var error by rememberSaveable { mutableStateOf(false) }

    fun setSites(list: List<String>) = SettingsStore.update {
        val clean = list.distinct().sorted()
        if (it.webAllowMode) it.copy(allowedSites = clean) else it.copy(blockedSites = clean)
    }

    fun add() {
        val d = Domains.normalize(input)
        if (d == null) { error = true; return }
        setSites(s.currentSites + d)
        input = ""
        error = false
    }

    LazyColumn(Modifier.fillMaxSize().padding(horizontal = 20.dp)) {
        item { ScreenTitle("Websites", "What your browser may open during focus. Breaks are always free.") }
        item { StrictBanner(strict) }
        item {
            Card(Modifier.padding(top = 8.dp)) {
                SwitchRow(
                    "Filter websites in browsers", s.webFilter, { v -> SettingsStore.update { it.copy(webFilter = v) } },
                    description = "Chrome, Edge, Brave, Firefox, Samsung Internet, Opera, DuckDuckGo, Vivaldi. A blocked page is swapped for an allowed one.",
                    enabled = !strict,
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
                if (s.webAllowMode) "Strict: every site except these (and their subdomains) is blocked."
                else "Relaxed: only these sites (and their subdomains) are blocked.",
                color = Palette.Muted, fontSize = 13.sp, modifier = Modifier.padding(top = 8.dp),
            )
        }
        item {
            Row(Modifier.fillMaxWidth().padding(top = 14.dp), verticalAlignment = Alignment.CenterVertically) {
                OutlinedTextField(
                    value = input, onValueChange = { input = it; error = false }, singleLine = true, enabled = canAdd,
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
                    onClick = { add() }, enabled = canAdd, shape = RoundedCornerShape(14.dp), modifier = Modifier.height(56.dp),
                    colors = ButtonDefaults.buttonColors(containerColor = Palette.Focus, contentColor = Palette.OnAccent),
                ) { Text("Add") }
            }
        }
        item {
            SectionTitle("Quick add")
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                (if (s.webAllowMode) Presets.allowSites else Presets.blockSites).forEach { p ->
                    val all = p.items.all { it in s.currentSites }
                    Box(
                        Modifier.clip(RoundedCornerShape(10.dp))
                            .background(if (all) Palette.Hover else Palette.Surface2)
                            .clickable(enabled = canAdd && !all) { setSites(s.currentSites + p.items) }
                            .padding(horizontal = 12.dp, vertical = 8.dp),
                    ) {
                        Text((if (all) "✓ " else "+ ") + p.name, color = if (all) Palette.Muted else Palette.Text, fontSize = 14.sp)
                    }
                }
            }
        }
        item { SectionTitle((if (s.webAllowMode) "Allowed sites" else "Blocked sites") + " (${s.currentSites.size})") }
        items(s.currentSites, key = { it }) { site ->
            Row(
                Modifier.fillMaxWidth().padding(vertical = 2.dp).clip(RoundedCornerShape(12.dp)).background(Palette.Surface)
                    .padding(start = 16.dp, end = 4.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(site, color = Palette.Text, fontSize = 15.sp, modifier = Modifier.weight(1f).padding(vertical = 14.dp))
                if (canRemove) Box(
                    Modifier.size(44.dp).clip(RoundedCornerShape(10.dp)).clickable { setSites(s.currentSites - site) },
                    contentAlignment = Alignment.Center,
                ) { IconRes(R.drawable.ic_delete, Palette.Muted, 20.dp, "Remove") }
            }
        }
        item { Spacer(Modifier.height(24.dp)) }
    }
}
