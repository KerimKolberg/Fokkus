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
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.kk.fokkus.Perms
import com.kk.fokkus.data.AppSettings
import com.kk.fokkus.data.SettingsStore
import com.kk.fokkus.data.TipKind
import com.kk.fokkus.data.Tips
import com.kk.fokkus.data.TimerDisplay
import com.kk.fokkus.timer.Notifications
import com.kk.fokkus.timer.Phase
import com.kk.fokkus.timer.TimerEngine

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun SettingsScreen(resumeTick: Int) {
    val ctx = LocalContext.current
    val s by SettingsStore.state.collectAsState()
    val st by TimerEngine.state.collectAsState()
    val strict = s.strictMode && st.lockedIn

    // Every change also refreshes the timer (new durations, notification style, bubble...).
    fun upd(f: (AppSettings) -> AppSettings) {
        SettingsStore.update(f)
        TimerEngine.settingsChanged()
    }

    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 20.dp)) {
        ScreenTitle("Settings", "Tune the timer, reminders and how it's shown.")
        StrictBanner(strict)

        SectionTitle("Timer")
        Card {
            Column {
                StepperRow("Focus", s.focusMin, " min", 5, 180, 5, { v -> upd { it.copy(focusMin = v) } })
                StepperRow("Short break", s.shortBreakMin, " min", 1, 30, 1, { v -> upd { it.copy(shortBreakMin = v) } })
                StepperRow("Long break", s.longBreakMin, " min", 5, 60, 5, { v -> upd { it.copy(longBreakMin = v) } })
                StepperRow("Long break after", s.sessionsBeforeLong, " ×", 2, 8, 1, { v -> upd { it.copy(sessionsBeforeLong = v) } })
            }
        }

        SectionTitle("Flow")
        Card {
            Column {
                SwitchRow("Start breaks automatically", s.autoStartBreaks, { v -> upd { it.copy(autoStartBreaks = v) } })
                SwitchRow("Start focus automatically after breaks", s.autoStartFocus, { v -> upd { it.copy(autoStartFocus = v) } })
                SwitchRow(
                    "Strict mode", s.strictMode, { v -> upd { it.copy(strictMode = v) } },
                    description = "No pause, skip or stop during focus, and lists can only get stricter until the session ends.",
                    enabled = !strict,
                )
            }
        }

        SectionTitle("Timer display")
        Card {
            Column {
                Segmented(
                    listOf("Notification", "Floating", "Both"),
                    when (s.display) { TimerDisplay.NOTIFICATION -> 0; TimerDisplay.FLOATING -> 1; TimerDisplay.BOTH -> 2 },
                    { i -> upd { it.copy(display = TimerDisplay.entries[i]) } },
                )
                Text(
                    when (s.display) {
                        TimerDisplay.NOTIFICATION -> "A big countdown in your notifications and on the lock screen."
                        TimerDisplay.FLOATING -> "A small bubble floats over your apps. Drag it anywhere, tap it for controls."
                        TimerDisplay.BOTH -> "Countdown notification plus the floating bubble over your apps."
                    },
                    color = Palette.Muted, fontSize = 13.sp, modifier = Modifier.padding(top = 10.dp),
                )
                val overlay = remember(resumeTick) { Perms.overlay(ctx) }
                if (s.display != TimerDisplay.NOTIFICATION && !overlay) {
                    Button(
                        onClick = { Perms.openOverlay(ctx) }, shape = RoundedCornerShape(12.dp),
                        colors = ButtonDefaults.buttonColors(containerColor = Palette.Focus, contentColor = Palette.OnAccent),
                        modifier = Modifier.padding(top = 10.dp),
                    ) { Text("Allow “display over other apps”") }
                }
            }
        }

        SectionTitle("Wellbeing tips")
        Card {
            Column {
                SwitchRow(
                    "Reminders during focus", s.tipsEnabled, { v -> upd { it.copy(tipsEnabled = v) } },
                    description = "A gentle nudge to drink, stretch, rest your eyes…",
                )
                StepperRow("Every", s.tipIntervalMin, " min", 5, 90, 5, { v -> upd { it.copy(tipIntervalMin = v) } }, enabled = s.tipsEnabled)
                SwitchRow(
                    "Break suggestions", s.breakSuggestions, { v -> upd { it.copy(breakSuggestions = v) } },
                    description = "When a break starts, get an idea of what to do with it.",
                )
                Spacer(Modifier.height(8.dp))
                FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Tips.categories.forEach { cat ->
                        val on = cat !in s.disabledTipCategories
                        val c = Color(Tips.color(cat))
                        Box(
                            Modifier.clip(RoundedCornerShape(10.dp))
                                .background(if (on) c.copy(alpha = 0.18f) else Palette.Surface2)
                                .clickable {
                                    upd { it.copy(disabledTipCategories = if (on) it.disabledTipCategories + cat else it.disabledTipCategories - cat) }
                                }
                                .padding(horizontal = 12.dp, vertical = 8.dp),
                        ) {
                            Text("${Tips.icon(cat)} $cat", color = if (on) Palette.Text else Palette.Muted, fontSize = 14.sp)
                        }
                    }
                }
                TextButton(
                    onClick = {
                        val kind = if (st.phase == Phase.FOCUS) TipKind.MICRO else TipKind.BREAK
                        Notifications.tip(ctx, Tips.next(kind, s.disabledTipCategories))
                    },
                    modifier = Modifier.padding(top = 6.dp),
                ) { Text("Show me a tip now", color = Palette.Focus) }
            }
        }

        SectionTitle("Sound")
        Card {
            SwitchRow("Sound when a phase ends", s.sound, { v -> upd { it.copy(sound = v) } }, description = "Vibration stays on either way.")
        }

        SectionTitle("Permissions")
        Card {
            Column {
                PermRow("Notifications", remember(resumeTick) { Perms.notifications(ctx) }) { Perms.openNotifications(ctx) }
                HorizontalDivider(color = Palette.Surface2)
                PermRow("App & website blocking (accessibility)", remember(resumeTick) { Perms.accessibility(ctx) }) { Perms.openAccessibility(ctx) }
                HorizontalDivider(color = Palette.Surface2)
                PermRow("Display over other apps", remember(resumeTick) { Perms.overlay(ctx) }) { Perms.openOverlay(ctx) }
                HorizontalDivider(color = Palette.Surface2)
                PermRow("Run in background", remember(resumeTick) { Perms.battery(ctx) }) { Perms.openBattery(ctx) }
            }
        }
        Text(
            "Fokkus works fully on your phone. Nothing you do is recorded or sent anywhere.",
            color = Palette.Muted, fontSize = 12.sp, modifier = Modifier.padding(vertical = 20.dp),
        )
    }
}

@Composable
private fun PermRow(title: String, granted: Boolean, open: () -> Unit) {
    Row(
        Modifier.fillMaxWidth().clickable { open() }.padding(vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(title, color = Palette.Text, fontSize = 15.sp, modifier = Modifier.weight(1f))
        Text(
            if (granted) "✓ On" else "Off →",
            color = if (granted) Palette.ShortBreak else Palette.Warn, fontSize = 14.sp,
        )
    }
}
