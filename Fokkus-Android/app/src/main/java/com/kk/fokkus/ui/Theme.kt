package com.kk.fokkus.ui

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import com.kk.fokkus.timer.Phase

/** Navy + teal from the Fokkus logo. */
object Palette {
    val Bg = Color(0xFF0E1B30)
    val Surface = Color(0xFF162A47)
    val Surface2 = Color(0xFF1E3556)
    val Hover = Color(0xFF284366)
    val Border = Color(0xFF2F4B72)
    val Text = Color(0xFFEAF1F6)
    val Muted = Color(0xFF8FA3BD)
    val Focus = Color(0xFF3CBCB9)
    val ShortBreak = Color(0xFFF2B84B)
    val LongBreak = Color(0xFFA78BFA)
    val Warn = Color(0xFFFF8A80)
    val Idle = Color(0xFF6F84A0)
    val OnAccent = Color(0xFF0B1628)
    val GridLine = Color(0xFF1D2E4A)
    val Baseline = Color(0xFF344E73)

    fun phase(p: Phase) = when (p) {
        Phase.FOCUS -> Focus
        Phase.SHORT_BREAK -> ShortBreak
        Phase.LONG_BREAK -> LongBreak
    }
}

private val scheme = darkColorScheme(
    primary = Palette.Focus,
    onPrimary = Palette.OnAccent,
    primaryContainer = Color(0xFF173F4A),
    onPrimaryContainer = Palette.Text,
    secondary = Palette.ShortBreak,
    onSecondary = Palette.OnAccent,
    secondaryContainer = Palette.Hover,
    onSecondaryContainer = Palette.Text,
    tertiary = Palette.LongBreak,
    background = Palette.Bg,
    onBackground = Palette.Text,
    surface = Palette.Bg,
    onSurface = Palette.Text,
    surfaceVariant = Palette.Surface2,
    onSurfaceVariant = Palette.Muted,
    surfaceContainerLowest = Palette.Bg,
    surfaceContainerLow = Palette.Surface,
    surfaceContainer = Palette.Surface,
    surfaceContainerHigh = Palette.Surface2,
    surfaceContainerHighest = Palette.Hover,
    outline = Palette.Border,
    outlineVariant = Palette.Surface2,
)

@Composable
fun FokkusTheme(content: @Composable () -> Unit) {
    MaterialTheme(colorScheme = scheme, typography = Typography(), content = content)
}
