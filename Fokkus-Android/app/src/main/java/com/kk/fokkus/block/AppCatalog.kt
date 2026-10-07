package com.kk.fokkus.block

import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.drawable.Drawable
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import com.kk.fokkus.data.Presets
import com.kk.fokkus.data.SettingsStore
import java.util.concurrent.ConcurrentHashMap

data class AppInfo(val pkg: String, val label: String)

/** Lists launchable apps and caches their icons for the UI. */
object AppCatalog {
    private val icons = ConcurrentHashMap<String, ImageBitmap>()

    fun launchableApps(ctx: Context): List<AppInfo> {
        val pm = ctx.packageManager
        val intent = Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER)
        return pm.queryIntentActivities(intent, 0)
            .map { AppInfo(it.activityInfo.packageName, it.loadLabel(pm).toString()) }
            .filter { it.pkg != ctx.packageName }
            .distinctBy { it.pkg }
            .sortedBy { it.label.lowercase() }
    }

    fun label(ctx: Context, pkg: String): String = try {
        val pm = ctx.packageManager
        pm.getApplicationLabel(pm.getApplicationInfo(pkg, 0)).toString()
    } catch (e: Exception) {
        pkg
    }

    fun icon(ctx: Context, pkg: String, sizePx: Int = 96): ImageBitmap? {
        icons[pkg]?.let { return it }
        return try {
            val bmp = ctx.packageManager.getApplicationIcon(pkg).toBitmap(sizePx).asImageBitmap()
            icons[pkg] = bmp
            bmp
        } catch (e: Exception) {
            null
        }
    }

    private fun Drawable.toBitmap(size: Int): Bitmap {
        val bmp = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888)
        val c = Canvas(bmp)
        setBounds(0, 0, size, size)
        draw(c)
        return bmp
    }

    fun launch(ctx: Context, pkg: String) {
        ctx.packageManager.getLaunchIntentForPackage(pkg)?.let {
            ctx.startActivity(it.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
        }
    }

    /** First start: pre-allow useful study apps that are installed (Drive, calculator, Moodle, ChatGPT...). */
    fun ensureDefaults(ctx: Context) {
        if (SettingsStore.current.appsInitialized) return
        val installed = launchableApps(ctx).map { it.pkg }.toSet()
        val keywords = listOf("calculator", "calendar", "deskclock", "clock", "documentsui", "files", "myfiles")
        val picked = installed.filter { pkg -> pkg in Presets.recommendedApps || keywords.any { pkg.endsWith(it) || pkg.contains(".$it") } }
        SettingsStore.update { it.copy(allowedApps = it.allowedApps + picked, appsInitialized = true) }
    }
}
