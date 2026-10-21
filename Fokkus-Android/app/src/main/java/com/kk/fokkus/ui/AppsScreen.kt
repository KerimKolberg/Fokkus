package com.kk.fokkus.ui

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CheckboxDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.kk.fokkus.R
import com.kk.fokkus.block.AppCatalog
import com.kk.fokkus.block.AppInfo
import com.kk.fokkus.block.Blocking
import com.kk.fokkus.data.AppSettings
import com.kk.fokkus.data.Preset
import com.kk.fokkus.data.Presets
import com.kk.fokkus.data.SettingsStore
import com.kk.fokkus.timer.TimerEngine
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

@Composable
fun AppsScreen() {
    val ctx = LocalContext.current
    val s by SettingsStore.state.collectAsState()
    val st by TimerEngine.state.collectAsState()
    val strict = s.strictMode && st.lockedIn
    var apps by remember { mutableStateOf<List<AppInfo>?>(null) }
    LaunchedEffect(Unit) { apps = withContext(Dispatchers.IO) { AppCatalog.launchableApps(ctx) } }
    var query by rememberSaveable { mutableStateOf("") }
    var tab by rememberSaveable { mutableIntStateOf(0) }
    val allowedTab = tab == 0
    val ticked = if (allowedTab) s.allowedApps else s.blockedApps
    val installed = remember(apps) { apps?.map { it.pkg }?.toSet() }
    // Sort once per tab (ticked first) so rows don't jump around while ticking boxes.
    val initiallyTicked = remember(apps, tab) { if (tab == 0) SettingsStore.current.allowedApps else SettingsStore.current.blockedApps }

    fun setTicked(set: Set<String>) = SettingsStore.update { if (allowedTab) it.copy(allowedApps = set) else it.copy(blockedApps = set) }
    fun count(set: Set<String>) = if (installed == null) set.size else set.count { it in installed }

    LazyColumn(Modifier.fillMaxSize().padding(horizontal = 20.dp)) {
        item { ScreenTitle("Apps", "Two separate functions — switch on one, both or none.") }
        item { StrictBanner(strict) }
        item {
            FilterCard(
                "Allowed apps", "During focus everything except the apps on your allowed list is locked.", s.allowAppsOn,
                { v -> SettingsStore.update { it.copy(allowAppsOn = v) } },
                "Blocked apps", "Apps on your blocked list are locked during focus — this wins over allowed.", s.blockAppsOn,
                { v -> SettingsStore.update { it.copy(blockAppsOn = v) } },
                strict, appStatus(s, count(s.allowedApps), count(s.blockedApps)),
                "Always available: phone & emergency calls, home screen, keyboard, alarms and Android settings." +
                    if (s.allowAppsOn && s.anySiteFilter) " Browsers stay open but filter websites (see Websites)." else "",
            )
        }
        item {
            ListTabs(
                tab, count(s.allowedApps), count(s.blockedApps), if (allowedTab) s.allowAppsOn else s.blockAppsOn,
                if (allowedTab) "In use — during focus only these apps work." else "In use — these apps are locked during focus.",
                if (allowedTab) "Allowed apps" else "Blocked apps",
            ) { tab = it }
        }
        // Quick add for the blocked list: only the apps of a category that are installed on this phone.
        if (!allowedTab && installed != null) item {
            val presets = Presets.blockApps.map { p -> Preset(p.name, p.items.filter { it in installed }) }.filter { it.items.isNotEmpty() }
            QuickAddChips(presets, { it in s.blockedApps }, true) { p -> setTicked(s.blockedApps + p.items) }
        }
        item {
            OutlinedTextField(
                value = query, onValueChange = { query = it }, singleLine = true,
                placeholder = { Text("Search apps") },
                leadingIcon = { IconRes(R.drawable.ic_search, Palette.Muted, 20.dp) },
                shape = RoundedCornerShape(14.dp),
                colors = OutlinedTextFieldDefaults.colors(
                    focusedBorderColor = Palette.Focus, unfocusedBorderColor = Palette.Border,
                    focusedContainerColor = Palette.Surface, unfocusedContainerColor = Palette.Surface,
                ),
                modifier = Modifier.fillMaxWidth().padding(top = 16.dp, bottom = 4.dp),
            )
        }
        val list = apps
        if (list == null) {
            item {
                Box(Modifier.fillMaxWidth().padding(32.dp), contentAlignment = Alignment.Center) {
                    CircularProgressIndicator(color = Palette.Focus)
                }
            }
        } else {
            val q = query.trim().lowercase()
            val shown = list.filter { q.isEmpty() || it.label.lowercase().contains(q) || it.pkg.contains(q) }
                .sortedWith(compareByDescending<AppInfo> { it.pkg in initiallyTicked }.thenBy { it.label.lowercase() })
            item { SectionTitle("${count(ticked)} ${if (allowedTab) "allowed" else "blocked"} · ${list.size} apps") }
            items(shown, key = { it.pkg }) { app ->
                val checked = app.pkg in ticked
                // Browsers are usable with the allowed list when websites are filtered.
                val browser = allowedTab && s.anySiteFilter && Blocking.isFilterableBrowser(app.pkg)
                AppRow(
                    app = app,
                    checked = checked || browser,
                    note = if (browser) "browser · websites filtered" else null,
                    accent = if (allowedTab) Palette.ShortBreak else Palette.Warn,
                    // Strict mode: you may lock more apps, but not unlock new ones.
                    enabled = !browser && (!strict || (if (allowedTab) checked else !checked)),
                ) { on -> setTicked(if (on) ticked + app.pkg else ticked - app.pkg) }
            }
            item { Spacer(Modifier.padding(bottom = 24.dp)) }
        }
    }
}

private fun appStatus(s: AppSettings, allowed: Int, blocked: Int): String = when {
    s.allowAppsOn && s.blockAppsOn -> "During focus: only your $allowed allowed apps work, and your $blocked blocked apps never do."
    s.allowAppsOn -> "During focus: only your $allowed allowed apps work. Everything else is locked."
    s.blockAppsOn -> "During focus: everything works except your $blocked blocked apps."
    else -> "Both off — every app works during focus."
}

@Composable
private fun AppRow(app: AppInfo, checked: Boolean, note: String?, accent: Color, enabled: Boolean, onToggle: (Boolean) -> Unit) {
    val ctx = LocalContext.current
    val icon by produceState<ImageBitmap?>(null, app.pkg) { value = withContext(Dispatchers.IO) { AppCatalog.icon(ctx, app.pkg) } }
    Row(
        Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp))
            .clickable(enabled = enabled) { onToggle(!checked) }
            .padding(vertical = 8.dp, horizontal = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        val img = icon
        if (img != null) Image(img, null, Modifier.size(40.dp))
        else Box(Modifier.size(40.dp).clip(RoundedCornerShape(10.dp)).background(Palette.Surface2))
        Spacer(Modifier.width(14.dp))
        Column(Modifier.weight(1f)) {
            Text(app.label, color = Palette.Text, fontSize = 16.sp, fontWeight = if (checked) FontWeight.SemiBold else FontWeight.Normal)
            Text(note ?: app.pkg, color = if (note != null) Palette.ShortBreak else Palette.Muted, fontSize = 11.sp, maxLines = 1)
        }
        Checkbox(
            checked = checked, onCheckedChange = { onToggle(it) }, enabled = enabled,
            colors = CheckboxDefaults.colors(checkedColor = accent, uncheckedColor = Palette.Border, checkmarkColor = Palette.Bg),
        )
    }
}
