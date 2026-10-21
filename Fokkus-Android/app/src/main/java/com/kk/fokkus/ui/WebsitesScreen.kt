package com.kk.fokkus.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
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
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.kk.fokkus.R
import com.kk.fokkus.data.AppSettings
import com.kk.fokkus.data.Domains
import com.kk.fokkus.data.Presets
import com.kk.fokkus.data.SettingsStore
import com.kk.fokkus.timer.TimerEngine

@Composable
fun WebsitesScreen() {
    val s by SettingsStore.state.collectAsState()
    val st by TimerEngine.state.collectAsState()
    val strict = s.strictMode && st.lockedIn
    var tab by rememberSaveable { mutableIntStateOf(0) }
    val allowedTab = tab == 0
    val list = if (allowedTab) s.allowedSites else s.blockedSites
    // Strict mode: lists may only get stricter. Adding to the allowed list / removing from the blocked list loosens.
    val canAdd = !(strict && allowedTab)
    val canRemove = !(strict && !allowedTab)
    var input by rememberSaveable { mutableStateOf("") }
    var error by rememberSaveable { mutableStateOf(false) }

    fun setList(newList: List<String>) {
        val clean = newList.distinct().sorted()
        SettingsStore.update { if (allowedTab) it.copy(allowedSites = clean) else it.copy(blockedSites = clean) }
    }

    fun add() {
        val d = Domains.normalize(input)
        if (d == null || !canAdd) { error = true; return }
        setList(list + d)
        input = ""
        error = false
    }

    LazyColumn(Modifier.fillMaxSize().padding(horizontal = 20.dp)) {
        item { ScreenTitle("Websites", "Two separate functions — switch on one, both or none. Breaks are always free.") }
        item { StrictBanner(strict) }
        item {
            FilterCard(
                "Allowed websites", "Only the sites on your allowed list (and their subdomains) work during focus.", s.allowSitesOn,
                { v -> SettingsStore.update { it.copy(allowSitesOn = v) } },
                "Blocked websites", "Sites on your blocked list never work during focus — this wins over allowed.", s.blockSitesOn,
                { v -> SettingsStore.update { it.copy(blockSitesOn = v) } },
                strict, siteStatus(s),
                "Chrome, Edge, Brave, Firefox, Samsung Internet, Opera, DuckDuckGo, Vivaldi. A blocked page is swapped for an allowed one.",
            )
        }
        item {
            ListTabs(
                tab, s.allowedSites.size, s.blockedSites.size, if (allowedTab) s.allowSitesOn else s.blockSitesOn,
                if (allowedTab) "In use — during focus only these sites work." else "In use — during focus these sites never work.",
                if (allowedTab) "Allowed websites" else "Blocked websites",
            ) { tab = it; error = false }
        }
        item {
            QuickAddChips(if (allowedTab) Presets.allowSites else Presets.blockSites, { it in list }, canAdd) { p -> setList(list + p.items) }
        }
        item {
            Row(Modifier.fillMaxWidth().padding(top = 16.dp), verticalAlignment = Alignment.CenterVertically) {
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
        item { SectionTitle((if (allowedTab) "Allowed websites" else "Blocked websites") + " (${list.size})") }
        items(list, key = { it }) { site ->
            Row(
                Modifier.fillMaxWidth().padding(vertical = 2.dp).clip(RoundedCornerShape(12.dp)).background(Palette.Surface)
                    .padding(start = 16.dp, end = 4.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(site, color = Palette.Text, fontSize = 15.sp, modifier = Modifier.weight(1f).padding(vertical = 10.dp))
                if (canRemove) Box(
                    Modifier.size(44.dp).clip(RoundedCornerShape(10.dp)).clickable { setList(list - site) },
                    contentAlignment = Alignment.Center,
                ) { IconRes(R.drawable.ic_delete, Palette.Muted, 20.dp, "Remove") }
                else Spacer(Modifier.size(44.dp))
            }
        }
        item { Spacer(Modifier.height(24.dp)) }
    }
}

private fun siteStatus(s: AppSettings): String {
    val a = s.allowedSites.size
    val b = s.blockedSites.size
    return when {
        s.allowSitesOn && s.blockSitesOn -> "During focus: only your $a allowed sites work, and your $b blocked sites never do."
        s.allowSitesOn -> "During focus: only your $a allowed sites work. Everything else is blocked."
        s.blockSitesOn -> "During focus: everything works except your $b blocked sites."
        else -> "Both off — every website works during focus."
    }
}
