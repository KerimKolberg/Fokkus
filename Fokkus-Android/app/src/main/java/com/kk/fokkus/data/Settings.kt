package com.kk.fokkus.data

import android.content.Context
import android.content.SharedPreferences
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import org.json.JSONArray
import org.json.JSONObject
import java.time.LocalDate

enum class TimerDisplay { NOTIFICATION, FLOATING, BOTH }

data class AppSettings(
    val focusMin: Int = 25,
    val shortBreakMin: Int = 5,
    val longBreakMin: Int = 15,
    val sessionsBeforeLong: Int = 4,
    val autoStartBreaks: Boolean = true,
    val autoStartFocus: Boolean = false,
    val strictMode: Boolean = false,
    val blockApps: Boolean = true,
    val allowedApps: Set<String> = emptySet(),
    val webFilter: Boolean = true,
    val webAllowMode: Boolean = true,
    val allowedSites: List<String> = Presets.defaultAllowedSites,
    val blockedSites: List<String> = Presets.defaultBlockedSites,
    val display: TimerDisplay = TimerDisplay.BOTH,
    val tipsEnabled: Boolean = true,
    val tipIntervalMin: Int = 20,
    val breakSuggestions: Boolean = true,
    val disabledTipCategories: Set<String> = emptySet(),
    val sound: Boolean = true,
    val bubbleX: Int = -1,
    val bubbleY: Int = -1,
    val appsInitialized: Boolean = false,
    // study plan
    val subjects: List<Subject> = emptyList(),
    val activeSubjectId: String? = null,
    val autoNextSubject: Boolean = true,
    val planSince: String? = null, // yyyy-MM-dd: plan targets count from this day on
) {
    val currentSites: List<String> get() = if (webAllowMode) allowedSites else blockedSites
    val planStart: LocalDate get() = planSince?.let { runCatching { LocalDate.parse(it) }.getOrNull() } ?: LocalDate.MAX
}

object SettingsStore {
    private lateinit var prefs: SharedPreferences
    private val _state = MutableStateFlow(AppSettings())
    val state: StateFlow<AppSettings> = _state
    val current: AppSettings get() = _state.value

    fun init(context: Context) {
        prefs = context.getSharedPreferences("settings", Context.MODE_PRIVATE)
        _state.value = load()
    }

    fun update(transform: (AppSettings) -> AppSettings) {
        val next = transform(_state.value)
        if (next == _state.value) return
        _state.value = next
        prefs.edit().putString("json", toJson(next).toString()).apply()
    }

    private fun load(): AppSettings {
        val json = prefs.getString("json", null) ?: return AppSettings()
        return try { fromJson(JSONObject(json)) } catch (e: Exception) { AppSettings() }
    }

    private fun toJson(s: AppSettings) = JSONObject().apply {
        put("focusMin", s.focusMin); put("shortBreakMin", s.shortBreakMin); put("longBreakMin", s.longBreakMin)
        put("sessionsBeforeLong", s.sessionsBeforeLong); put("autoStartBreaks", s.autoStartBreaks)
        put("autoStartFocus", s.autoStartFocus); put("strictMode", s.strictMode); put("blockApps", s.blockApps)
        put("allowedApps", JSONArray(s.allowedApps.toList())); put("webFilter", s.webFilter); put("webAllowMode", s.webAllowMode)
        put("allowedSites", JSONArray(s.allowedSites)); put("blockedSites", JSONArray(s.blockedSites))
        put("display", s.display.name); put("tipsEnabled", s.tipsEnabled); put("tipIntervalMin", s.tipIntervalMin)
        put("breakSuggestions", s.breakSuggestions); put("disabledTipCategories", JSONArray(s.disabledTipCategories.toList()))
        put("sound", s.sound); put("bubbleX", s.bubbleX); put("bubbleY", s.bubbleY); put("appsInitialized", s.appsInitialized)
        put("subjects", JSONArray().apply {
            s.subjects.forEach { sub ->
                put(JSONObject().put("id", sub.id).put("name", sub.name).put("color", sub.colorIndex).put("targets", JSONArray(sub.targets)))
            }
        })
        put("activeSubjectId", s.activeSubjectId ?: JSONObject.NULL)
        put("autoNextSubject", s.autoNextSubject)
        put("planSince", s.planSince ?: JSONObject.NULL)
    }

    private fun JSONObject.subjects(): List<Subject> {
        val arr = optJSONArray("subjects") ?: return emptyList()
        return (0 until arr.length()).mapNotNull { i ->
            val o = arr.optJSONObject(i) ?: return@mapNotNull null
            val t = o.optJSONArray("targets")
            val targets = List(7) { d -> (t?.optInt(d, 0) ?: 0).coerceIn(0, 16) }
            Subject(o.optString("id"), o.optString("name", "Subject"), o.optInt("color", 8), targets)
        }.filter { it.id.isNotEmpty() }
    }

    private fun JSONObject.strings(key: String, fallback: List<String>): List<String> {
        val arr = optJSONArray(key) ?: return fallback
        return (0 until arr.length()).map { arr.getString(it) }
    }

    private fun fromJson(o: JSONObject): AppSettings {
        val d = AppSettings()
        return AppSettings(
            focusMin = o.optInt("focusMin", d.focusMin).coerceIn(1, 240),
            shortBreakMin = o.optInt("shortBreakMin", d.shortBreakMin).coerceIn(1, 60),
            longBreakMin = o.optInt("longBreakMin", d.longBreakMin).coerceIn(1, 120),
            sessionsBeforeLong = o.optInt("sessionsBeforeLong", d.sessionsBeforeLong).coerceIn(1, 12),
            autoStartBreaks = o.optBoolean("autoStartBreaks", d.autoStartBreaks),
            autoStartFocus = o.optBoolean("autoStartFocus", d.autoStartFocus),
            strictMode = o.optBoolean("strictMode", d.strictMode),
            blockApps = o.optBoolean("blockApps", d.blockApps),
            allowedApps = o.strings("allowedApps", emptyList()).toSet(),
            webFilter = o.optBoolean("webFilter", d.webFilter),
            webAllowMode = o.optBoolean("webAllowMode", d.webAllowMode),
            allowedSites = o.strings("allowedSites", d.allowedSites),
            blockedSites = o.strings("blockedSites", d.blockedSites),
            display = runCatching { TimerDisplay.valueOf(o.optString("display")) }.getOrDefault(d.display),
            tipsEnabled = o.optBoolean("tipsEnabled", d.tipsEnabled),
            tipIntervalMin = o.optInt("tipIntervalMin", d.tipIntervalMin).coerceIn(5, 120),
            breakSuggestions = o.optBoolean("breakSuggestions", d.breakSuggestions),
            disabledTipCategories = o.strings("disabledTipCategories", emptyList()).toSet(),
            sound = o.optBoolean("sound", d.sound),
            bubbleX = o.optInt("bubbleX", -1),
            bubbleY = o.optInt("bubbleY", -1),
            appsInitialized = o.optBoolean("appsInitialized", false),
            subjects = o.subjects(),
            activeSubjectId = if (o.isNull("activeSubjectId")) null else o.optString("activeSubjectId").ifEmpty { null },
            autoNextSubject = o.optBoolean("autoNextSubject", true),
            planSince = if (o.isNull("planSince")) null else o.optString("planSince").ifEmpty { null },
        )
    }
}

