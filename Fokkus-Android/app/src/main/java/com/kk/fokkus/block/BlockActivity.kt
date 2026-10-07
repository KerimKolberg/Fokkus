package com.kk.fokkus.block

import android.content.Context
import android.content.Intent
import android.graphics.Color as AColor
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.addCallback
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.systemBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.kk.fokkus.R
import com.kk.fokkus.data.SettingsStore
import com.kk.fokkus.data.TipKind
import com.kk.fokkus.data.Tips
import com.kk.fokkus.timer.TimerEngine
import com.kk.fokkus.ui.Card
import com.kk.fokkus.ui.IconRes
import com.kk.fokkus.ui.FokkusTheme
import com.kk.fokkus.ui.Palette
import com.kk.fokkus.ui.formatTime
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext

/** Full-screen "locked in" page shown on top of a blocked app or website. */
class BlockActivity : ComponentActivity() {
    private val pkg = mutableStateOf<String?>(null)
    private val site = mutableStateOf<String?>(null)

    override fun onCreate(savedInstanceState: Bundle?) {
        enableEdgeToEdge(SystemBarStyle.dark(AColor.TRANSPARENT), SystemBarStyle.dark(AColor.TRANSPARENT))
        super.onCreate(savedInstanceState)
        read(intent)
        onBackPressedDispatcher.addCallback(this) { goHome() }
        setContent {
            FokkusTheme {
                BlockScreen(pkg.value, site.value, onHome = ::goHome, onContinue = ::finish)
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        read(intent)
    }

    private fun read(i: Intent?) {
        pkg.value = i?.getStringExtra(EXTRA_PKG)
        site.value = i?.getStringExtra(EXTRA_SITE)
    }

    private fun goHome() {
        try {
            startActivity(Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_HOME).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
        } catch (_: Exception) { }
        finish()
    }

    companion object {
        private const val EXTRA_PKG = "pkg"
        private const val EXTRA_SITE = "site"

        fun show(ctx: Context, pkg: String, site: String?) {
            ctx.startActivity(
                Intent(ctx, BlockActivity::class.java)
                    .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_NO_ANIMATION)
                    .putExtra(EXTRA_PKG, pkg)
                    .putExtra(EXTRA_SITE, site)
            )
        }
    }
}

@Composable
private fun BlockScreen(pkg: String?, site: String?, onHome: () -> Unit, onContinue: () -> Unit) {
    val ctx = LocalContext.current
    val st by TimerEngine.state.collectAsState()
    val s by SettingsStore.state.collectAsState()
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    LaunchedEffect(Unit) {
        while (true) {
            now = System.currentTimeMillis()
            TimerEngine.tick()
            delay(500)
        }
    }
    // Focus is over: nothing to block any more.
    LaunchedEffect(st.lockedIn) { if (!st.lockedIn) onContinue() }

    val name = remember(pkg, site) { site ?: pkg?.let { AppCatalog.label(ctx, it) } ?: "This app" }
    val tip = remember(pkg, site) { Tips.next(TipKind.MICRO, s.disabledTipCategories) }
    val allowed by produceState(emptyList<Pair<String, ImageBitmap?>>(), s.allowedApps) {
        value = withContext(Dispatchers.IO) {
            val installed = AppCatalog.launchableApps(ctx).associateBy { it.pkg }
            s.allowedApps.filter { it in installed }.sortedBy { installed[it]?.label?.lowercase() }
                .take(12).map { it to AppCatalog.icon(ctx, it) }
        }
    }

    Box(
        Modifier.fillMaxSize().background(Brush.verticalGradient(listOf(Color(0xFF103446), Palette.Bg, Palette.Bg))),
    ) {
        Column(
            Modifier.fillMaxSize().systemBarsPadding().padding(24.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center,
        ) {
            Box(Modifier.size(96.dp).clip(CircleShape).background(Palette.Focus.copy(alpha = 0.16f)), contentAlignment = Alignment.Center) {
                IconRes(R.drawable.ic_lock, Palette.Focus, 44.dp)
            }
            Spacer(Modifier.height(20.dp))
            Text("Locked in", color = Palette.Text, fontSize = 32.sp, fontWeight = FontWeight.SemiBold)
            Spacer(Modifier.height(6.dp))
            Text(
                if (site != null) "$name is blocked during focus." else "$name is locked during focus.",
                color = Palette.Muted, fontSize = 16.sp, textAlign = TextAlign.Center,
            )
            Spacer(Modifier.height(14.dp))
            Text("${formatTime(st.remaining(now))} left", color = Palette.Focus, fontSize = 26.sp, fontWeight = FontWeight.SemiBold)
            Spacer(Modifier.height(24.dp))
            Card(color = Palette.Surface) {
                Column {
                    Text("${tip.icon}  ${tip.title}", color = Palette.Text, fontSize = 15.sp, fontWeight = FontWeight.SemiBold)
                    Text(tip.text, color = Palette.Muted, fontSize = 14.sp)
                }
            }
            if (allowed.isNotEmpty()) {
                Spacer(Modifier.height(20.dp))
                Text("Your allowed apps", color = Palette.Muted, fontSize = 12.sp, modifier = Modifier.fillMaxWidth())
                Spacer(Modifier.height(8.dp))
                LazyRow(horizontalArrangement = Arrangement.spacedBy(12.dp), modifier = Modifier.fillMaxWidth()) {
                    items(allowed) { (p, icon) ->
                        Box(
                            Modifier.size(52.dp).clip(RoundedCornerShape(14.dp)).background(Palette.Surface)
                                .clickable { AppCatalog.launch(ctx, p); onContinue() },
                            contentAlignment = Alignment.Center,
                        ) {
                            if (icon != null) Image(icon, null, Modifier.size(40.dp))
                        }
                    }
                }
            }
            Spacer(Modifier.height(28.dp))
            Button(
                onClick = if (site != null) onContinue else onHome,
                shape = RoundedCornerShape(16.dp),
                colors = ButtonDefaults.buttonColors(containerColor = Palette.Focus, contentColor = Palette.OnAccent),
                modifier = Modifier.fillMaxWidth().height(54.dp),
            ) {
                Text(if (site != null) "Back to an allowed page" else "Back to focus", fontSize = 16.sp, fontWeight = FontWeight.SemiBold)
            }
            if (site != null) {
                TextButton(onClick = onHome) { Text("Go to home screen", color = Palette.Muted) }
            }
            Spacer(Modifier.width(1.dp))
        }
    }
}
