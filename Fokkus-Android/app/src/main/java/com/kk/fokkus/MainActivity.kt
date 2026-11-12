package com.kk.fokkus

import android.Manifest
import android.graphics.Color
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.ui.unit.dp
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.NavigationBarItemDefaults
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.runtime.mutableStateOf
import com.kk.fokkus.block.AppCatalog
import com.kk.fokkus.block.Blocking
import com.kk.fokkus.data.Analytics
import com.kk.fokkus.data.Review
import com.kk.fokkus.data.SessionLog
import com.kk.fokkus.data.SettingsStore
import com.kk.fokkus.data.StatRange
import com.kk.fokkus.data.SyncManager
import com.kk.fokkus.timer.TimerEngine
import com.kk.fokkus.ui.ReviewDialog
import java.time.LocalDate
import com.kk.fokkus.ui.AppsScreen
import com.kk.fokkus.ui.IconRes
import com.kk.fokkus.ui.FokkusTheme
import com.kk.fokkus.ui.Palette
import com.kk.fokkus.ui.PlanScreen
import com.kk.fokkus.ui.Segmented
import com.kk.fokkus.ui.SettingsScreen
import com.kk.fokkus.ui.StatsScreen
import com.kk.fokkus.ui.TimerScreen
import com.kk.fokkus.ui.WebsitesScreen

class MainActivity : ComponentActivity() {
    private val resumeTick = mutableIntStateOf(0)
    private val reviewWeek = mutableStateOf<LocalDate?>(null)
    private val notificationPermission = registerForActivityResult(ActivityResultContracts.RequestPermission()) {
        resumeTick.intValue++
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        enableEdgeToEdge(
            statusBarStyle = SystemBarStyle.dark(Color.TRANSPARENT),
            navigationBarStyle = SystemBarStyle.dark(Color.TRANSPARENT),
        )
        super.onCreate(savedInstanceState)
        Thread { AppCatalog.ensureDefaults(applicationContext) }.start()
        val requestNotifications = {
            if (Build.VERSION.SDK_INT >= 33) notificationPermission.launch(Manifest.permission.POST_NOTIFICATIONS)
        }
        setContent {
            FokkusTheme {
                MainUi(resumeTick.intValue, requestNotifications)
                reviewWeek.value?.let { week -> ReviewDialog(week) { reviewWeek.value = null } }
            }
        }
    }

    override fun onResume() {
        super.onResume()
        Blocking.invalidate()
        resumeTick.intValue++
        SyncManager.run(minGapMs = 60_000L)
        offerWeeklyReview()
    }

    /** Once a week (Sunday evening, or the next time you open Fokkus) the review of the week opens by itself. */
    private fun offerWeeklyReview() {
        if (reviewWeek.value != null || TimerEngine.current.lockedIn) return
        val due = Review.dueWeek()
        val key = due.toString()
        val s = SettingsStore.current
        if ((s.lastReviewedWeek ?: "") >= key) return
        SettingsStore.update { it.copy(lastReviewedWeek = key) }
        // Nothing to look back on: don't show an empty review.
        if (Analytics.compute(s, SessionLog.records.value, StatRange.WEEK, due).totalMinutes >= 1) reviewWeek.value = due
    }
}

private data class Tab(val label: String, val icon: Int)

private val tabs = listOf(
    Tab("Timer", R.drawable.ic_timer),
    Tab("Plan", R.drawable.ic_calendar),
    Tab("Stats", R.drawable.ic_chart),
    Tab("Blocking", R.drawable.ic_shield),
    Tab("Settings", R.drawable.ic_settings),
)

@Composable
private fun BlockingScreen() {
    var part by rememberSaveable { mutableIntStateOf(0) }
    Column(Modifier.fillMaxSize()) {
        Segmented(listOf("Apps", "Websites"), part, { part = it }, modifier = Modifier.padding(start = 20.dp, end = 20.dp, top = 12.dp))
        Box(Modifier.weight(1f)) {
            if (part == 0) AppsScreen() else WebsitesScreen()
        }
    }
}

@Composable
private fun MainUi(resumeTick: Int, requestNotifications: () -> Unit) {
    var tab by rememberSaveable { mutableIntStateOf(0) }
    Scaffold(
        containerColor = Palette.Bg,
        bottomBar = {
            NavigationBar(containerColor = Palette.Surface) {
                tabs.forEachIndexed { i, t ->
                    NavigationBarItem(
                        selected = tab == i,
                        onClick = { tab = i },
                        icon = { IconRes(t.icon, tint = if (tab == i) Palette.Focus else Palette.Muted) },
                        label = { Text(t.label) },
                        colors = NavigationBarItemDefaults.colors(
                            indicatorColor = Palette.Hover,
                            selectedTextColor = Palette.Text,
                            unselectedTextColor = Palette.Muted,
                        ),
                    )
                }
            }
        },
    ) { padding ->
        Box(Modifier.fillMaxSize().padding(padding)) {
            when (tab) {
                0 -> TimerScreen(resumeTick, requestNotifications, onOpenPlan = { tab = 1 })
                1 -> PlanScreen()
                2 -> StatsScreen()
                3 -> BlockingScreen()
                else -> SettingsScreen(resumeTick)
            }
        }
    }
}
