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
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.kk.fokkus.R
import com.kk.fokkus.block.AppCatalog
import com.kk.fokkus.block.AppInfo
import com.kk.fokkus.block.Blocking
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
    // Sort once (allowed first) so rows don't jump around while ticking boxes.
    val initiallyAllowed = remember(apps) { SettingsStore.current.allowedApps }

    LazyColumn(Modifier.fillMaxSize().padding(horizontal = 20.dp)) {
        item { ScreenTitle("Apps", "During focus only the apps you tick work. Everything else is locked.") }
        item { StrictBanner(strict) }
        item {
            Card(Modifier.padding(top = 8.dp)) {
                Column {
                    SwitchRow(
                        "Lock apps during focus", s.blockApps, { v -> SettingsStore.update { it.copy(blockApps = v) } },
                        description = "Opening anything else shows the lock screen until the focus session ends.",
                        enabled = !strict,
                    )
                    Text(
                        "Always available: phone & emergency calls, home screen, keyboard, alarms and Android settings." +
                            if (s.webFilter) " Browsers stay open but only show allowed websites (see Websites)." else "",
                        color = Palette.Muted, fontSize = 12.sp, lineHeight = 16.sp, modifier = Modifier.padding(top = 4.dp),
                    )
                }
            }
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
                .sortedWith(compareByDescending<AppInfo> { it.pkg in initiallyAllowed }.thenBy { it.label.lowercase() })
            item { SectionTitle("${s.allowedApps.count { a -> list.any { it.pkg == a } }} allowed · ${list.size} apps") }
            items(shown, key = { it.pkg }) { app ->
                val checked = app.pkg in s.allowedApps
                val browser = s.webFilter && Blocking.isFilterableBrowser(app.pkg)
                AppRow(
                    app = app,
                    checked = checked || browser,
                    note = if (browser) "browser · websites filtered" else null,
                    // Strict mode: you may lock more apps, but not unlock new ones.
                    enabled = !browser && (!strict || checked),
                ) { on ->
                    SettingsStore.update { it.copy(allowedApps = if (on) it.allowedApps + app.pkg else it.allowedApps - app.pkg) }
                }
            }
            item { Spacer(Modifier.padding(bottom = 24.dp)) }
        }
    }
}

@Composable
private fun AppRow(app: AppInfo, checked: Boolean, note: String?, enabled: Boolean, onToggle: (Boolean) -> Unit) {
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
            colors = CheckboxDefaults.colors(checkedColor = Palette.ShortBreak, uncheckedColor = Palette.Border, checkmarkColor = Palette.Bg),
        )
    }
}
