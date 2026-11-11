package com.kk.fokkus.data

import android.content.Context
import android.content.SharedPreferences
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import org.json.JSONArray
import org.json.JSONObject
import java.time.LocalDate

enum class TimerDisplay { NOTIFICATION, FLOATING, BOTH }

/** An internet-radio stream you added yourself. */
data class CustomStation(val name: String, val url: String)

/** A saved combination of the four blocking functions + strict mode ("Programming", "Exam prep"...). */
data class Profile(
    val id: String,
    val name: String,
    val allowSitesOn: Boolean,
    val blockSitesOn: Boolean,
    val allowAppsOn: Boolean,
    val blockAppsOn: Boolean,
    val strict: Boolean,
) {
    fun matches(s: AppSettings) = s.allowSitesOn == allowSitesOn && s.blockSitesOn == blockSitesOn &&
        s.allowAppsOn == allowAppsOn && s.blockAppsOn == blockAppsOn && s.strictMode == strict

    /** Switching from the current settings to this profile would loosen the lock. */
    fun loosens(s: AppSettings) = (s.allowSitesOn && !allowSitesOn) || (s.blockSitesOn && !blockSitesOn) ||
        (s.allowAppsOn && !allowAppsOn) || (s.blockAppsOn && !blockAppsOn) || (s.strictMode && !strict)

    fun applyTo(s: AppSettings) = s.copy(
        allowSitesOn = allowSitesOn, blockSitesOn = blockSitesOn, allowAppsOn = allowAppsOn, blockAppsOn = blockAppsOn,
        strictMode = strict, activeProfileId = id,
    )

    fun describe(): String {
        val on = listOfNotNull(
            "allowed websites".takeIf { allowSitesOn }, "blocked websites".takeIf { blockSitesOn },
            "allowed apps".takeIf { allowAppsOn }, "blocked apps".takeIf { blockAppsOn },
        )
        return (if (on.isEmpty()) "No blocking" else on.joinToString(", ")) + if (strict) " · strict" else ""
    }

    companion object {
        fun capture(name: String, s: AppSettings, id: String = java.util.UUID.randomUUID().toString().take(10)) =
            Profile(id, name, s.allowSitesOn, s.blockSitesOn, s.allowAppsOn, s.blockAppsOn, s.strictMode)

        // On the phone "Allowed apps" is the usual lock (everything else is locked).
        val defaults = listOf(
            Profile("study", "Study", allowSitesOn = true, blockSitesOn = false, allowAppsOn = true, blockAppsOn = false, strict = false),
            Profile("coding", "Programming", allowSitesOn = false, blockSitesOn = true, allowAppsOn = false, blockAppsOn = true, strict = false),
            Profile("exam", "Exam prep", allowSitesOn = true, blockSitesOn = true, allowAppsOn = true, blockAppsOn = true, strict = true),
            Profile("timer", "Just the timer", allowSitesOn = false, blockSitesOn = false, allowAppsOn = false, blockAppsOn = false, strict = false),
        )
    }
}

/** The profile describing the current switches (the last applied one first), or null = custom. */
fun AppSettings.currentProfile(): Profile? =
    profiles.firstOrNull { it.id == activeProfileId && it.matches(this) } ?: profiles.firstOrNull { it.matches(this) }

/**
 * Picks the subject you study and switches on its profile, if it has one
 * (unless that would loosen the lock during a strict session).
 */
fun AppSettings.withSubject(id: String?, strictLocked: Boolean): AppSettings {
    val next = copy(activeSubjectId = id)
    val p = subjects.firstOrNull { it.id == id }?.profileId?.let { pid -> profiles.firstOrNull { it.id == pid } } ?: return next
    if (p.matches(next) || (strictLocked && p.loosens(next))) return next
    return p.applyTo(next)
}

/** Ready-made timer rhythms (shortcuts in Settings). */
data class Rhythm(val name: String, val focus: Int, val shortBreak: Int, val longBreak: Int, val sessions: Int) {
    fun applyTo(s: AppSettings) = s.copy(focusMin = focus, shortBreakMin = shortBreak, longBreakMin = longBreak, sessionsBeforeLong = sessions)
    fun matches(s: AppSettings) =
        s.focusMin == focus && s.shortBreakMin == shortBreak && s.longBreakMin == longBreak && s.sessionsBeforeLong == sessions

    companion object {
        val all = listOf(
            Rhythm("Pomodoro 25", 25, 5, 15, 4),
            Rhythm("Pomodoro 45", 45, 10, 20, 3),
            Rhythm("Ultradian 90", 90, 20, 30, 2),
        )
    }
}

data class AppSettings(
    val focusMin: Int = 25,
    val shortBreakMin: Int = 5,
    val longBreakMin: Int = 15,
    val sessionsBeforeLong: Int = 4,
    val autoStartBreaks: Boolean = true,
    val autoStartFocus: Boolean = false,
    val strictMode: Boolean = false,
    // Apps: two independent functions.
    //   allowAppsOn -> during focus everything except the allowed apps is locked
    //   blockAppsOn -> during focus the blocked apps are locked (also wins over the allowed list)
    val allowAppsOn: Boolean = true,
    val blockAppsOn: Boolean = false,
    val allowedApps: Set<String> = emptySet(),
    val blockedApps: Set<String> = Presets.defaultBlockedApps,
    // Websites: the same two functions.
    val allowSitesOn: Boolean = true,
    val blockSitesOn: Boolean = false,
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
    // session profiles
    val profiles: List<Profile> = Profile.defaults,
    val activeProfileId: String? = null,
    // notes + smarter breaks
    val askForNotes: Boolean = true,
    val smartBreaks: Boolean = true,
    // focus music (plays only while a focus phase runs)
    val musicOn: Boolean = false,
    val musicStation: String = "lofi",
    val musicVolume: Int = 40,
    val customStations: List<CustomStation> = emptyList(),
    // weekly review: Monday (yyyy-MM-dd) of the last week that was offered
    val lastReviewedWeek: String? = null,
    // sync with the PC through a file in Google Drive / OneDrive
    val syncUri: String? = null,
    val deviceId: String = "",
    val lastSyncMs: Long = 0L,
    val planUpdatedMs: Long = 0L,   // when the study plan was last changed (the newest plan wins)
    val settingsVersion: Int = CURRENT_VERSION,
) {
    val anySiteFilter: Boolean get() = allowSitesOn || blockSitesOn

    companion object {
        /** Bumped when defaults change, so saved settings get the new defaults once. */
        const val CURRENT_VERSION = 4
    }
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

    @Synchronized
    fun update(transform: (AppSettings) -> AppSettings) {
        val old = _state.value
        var next = transform(old)
        if (next == old) return
        // Any change to the plan gets a timestamp, so the newest plan wins when syncing (sync sets its own).
        if (next.planUpdatedMs == old.planUpdatedMs && planChanged(old, next)) next = next.copy(planUpdatedMs = System.currentTimeMillis())
        _state.value = next
        prefs.edit().putString("json", toJson(next).toString()).apply()
    }

    private fun planChanged(a: AppSettings, b: AppSettings) =
        a.subjects != b.subjects || a.planSince != b.planSince || a.autoNextSubject != b.autoNextSubject

    private fun load(): AppSettings {
        val s = loadRaw()
        if (s.deviceId.isNotEmpty()) return s
        val withId = s.copy(deviceId = "and-" + java.util.UUID.randomUUID().toString().replace("-", "").take(8))
        prefs.edit().putString("json", toJson(withId).toString()).apply()
        return withId
    }

    private fun loadRaw(): AppSettings {
        val json = prefs.getString("json", null) ?: return AppSettings()
        val s = try { fromJson(JSONObject(json)) } catch (e: Exception) { return AppSettings() }
        if (s.settingsVersion >= AppSettings.CURRENT_VERSION) return s
        // v2: GitHub & coding sites (GitHub sign-in, VS Code / github.dev, registries) are allowed by default.
        // v3: separate on/off functions - the old keys are converted in fromJson().
        var upgraded = s.copy(settingsVersion = AppSettings.CURRENT_VERSION)
        if (s.settingsVersion < 2) upgraded = upgraded.copy(allowedSites = (upgraded.allowedSites + Presets.codingSites).distinct().sorted())
        // v4: YouTube is for study music - off the blocked lists, onto the allowed lists.
        if (s.settingsVersion < 4) upgraded = upgraded.copy(
            blockedSites = upgraded.blockedSites.filterNot { site -> Presets.youTubeHosts.any { site == it || site.endsWith(".$it") } },
            allowedSites = (upgraded.allowedSites + Presets.youTubeSites).distinct().sorted(),
            blockedApps = upgraded.blockedApps - Presets.youTubeApps,
            allowedApps = upgraded.allowedApps + Presets.youTubeApps,
        )
        prefs.edit().putString("json", toJson(upgraded).toString()).apply()
        return upgraded
    }

    internal fun toJson(s: AppSettings) = JSONObject().apply {
        put("askForNotes", s.askForNotes); put("smartBreaks", s.smartBreaks)
        put("musicOn", s.musicOn); put("musicStation", s.musicStation); put("musicVolume", s.musicVolume)
        put("customStations", JSONArray().apply { s.customStations.forEach { put(JSONObject().put("name", it.name).put("url", it.url)) } })
        put("lastReviewedWeek", s.lastReviewedWeek ?: JSONObject.NULL)
        put("syncUri", s.syncUri ?: JSONObject.NULL); put("deviceId", s.deviceId)
        put("lastSyncMs", s.lastSyncMs); put("planUpdatedMs", s.planUpdatedMs)
        put("focusMin", s.focusMin); put("shortBreakMin", s.shortBreakMin); put("longBreakMin", s.longBreakMin)
        put("sessionsBeforeLong", s.sessionsBeforeLong); put("autoStartBreaks", s.autoStartBreaks)
        put("autoStartFocus", s.autoStartFocus); put("strictMode", s.strictMode)
        put("allowAppsOn", s.allowAppsOn); put("blockAppsOn", s.blockAppsOn)
        put("allowedApps", JSONArray(s.allowedApps.toList())); put("blockedApps", JSONArray(s.blockedApps.toList()))
        put("allowSitesOn", s.allowSitesOn); put("blockSitesOn", s.blockSitesOn)
        put("allowedSites", JSONArray(s.allowedSites)); put("blockedSites", JSONArray(s.blockedSites))
        put("display", s.display.name); put("tipsEnabled", s.tipsEnabled); put("tipIntervalMin", s.tipIntervalMin)
        put("breakSuggestions", s.breakSuggestions); put("disabledTipCategories", JSONArray(s.disabledTipCategories.toList()))
        put("sound", s.sound); put("bubbleX", s.bubbleX); put("bubbleY", s.bubbleY); put("appsInitialized", s.appsInitialized)
        put("subjects", JSONArray().apply {
            s.subjects.forEach { sub ->
                put(JSONObject().put("id", sub.id).put("name", sub.name).put("color", sub.colorIndex).put("targets", JSONArray(sub.targets))
                    .put("deadline", sub.deadline ?: JSONObject.NULL).put("deadlineLabel", sub.deadlineLabel ?: JSONObject.NULL)
                    .put("profileId", sub.profileId ?: JSONObject.NULL))
            }
        })
        put("profiles", JSONArray().apply {
            s.profiles.forEach { p ->
                put(JSONObject().put("id", p.id).put("name", p.name).put("allowSites", p.allowSitesOn).put("blockSites", p.blockSitesOn)
                    .put("allowApps", p.allowAppsOn).put("blockApps", p.blockAppsOn).put("strict", p.strict))
            }
        })
        put("activeProfileId", s.activeProfileId ?: JSONObject.NULL)
        put("settingsVersion", s.settingsVersion)
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
            Subject(
                o.optString("id"), o.optString("name", "Subject"), o.optInt("color", 8), targets,
                deadline = o.optNullable("deadline")?.takeIf { d -> runCatching { java.time.LocalDate.parse(d) }.isSuccess },
                deadlineLabel = o.optNullable("deadlineLabel"),
                profileId = o.optNullable("profileId"),
            )
        }.filter { it.id.isNotEmpty() }
    }

    private fun JSONObject.optNullable(key: String): String? = if (isNull(key)) null else optString(key).ifEmpty { null }

    private fun JSONObject.profiles(): List<Profile> {
        val arr = optJSONArray("profiles") ?: return Profile.defaults
        return (0 until arr.length()).mapNotNull { i ->
            val o = arr.optJSONObject(i) ?: return@mapNotNull null
            Profile(
                o.optString("id"), o.optString("name", "Profile").ifBlank { "Profile" },
                o.optBoolean("allowSites"), o.optBoolean("blockSites"), o.optBoolean("allowApps"), o.optBoolean("blockApps"), o.optBoolean("strict"),
            )
        }.filter { it.id.isNotEmpty() }
    }

    private fun JSONObject.strings(key: String, fallback: List<String>): List<String> {
        val arr = optJSONArray(key) ?: return fallback
        return (0 until arr.length()).map { arr.getString(it) }
    }

    internal fun fromJson(o: JSONObject): AppSettings {
        val d = AppSettings()
        val stations = o.optJSONArray("customStations")
        val custom = if (stations == null) emptyList() else (0 until stations.length()).mapNotNull { i ->
            stations.optJSONObject(i)?.let { CustomStation(it.optString("name"), it.optString("url")) }?.takeIf { it.url.isNotBlank() }
        }
        // Saved by v1.0/1.1: one filter switch + mode, and per-site switches (switched-off sites are dropped).
        val webFilter = o.optBoolean("webFilter", true)
        val webAllowMode = o.optBoolean("webAllowMode", true)
        val offAllowed = o.strings("disabledAllowedSites", emptyList()).toSet()
        val offBlocked = o.strings("disabledBlockedSites", emptyList()).toSet()
        return AppSettings(
            focusMin = o.optInt("focusMin", d.focusMin).coerceIn(1, 240),
            shortBreakMin = o.optInt("shortBreakMin", d.shortBreakMin).coerceIn(1, 60),
            longBreakMin = o.optInt("longBreakMin", d.longBreakMin).coerceIn(1, 120),
            sessionsBeforeLong = o.optInt("sessionsBeforeLong", d.sessionsBeforeLong).coerceIn(1, 12),
            autoStartBreaks = o.optBoolean("autoStartBreaks", d.autoStartBreaks),
            autoStartFocus = o.optBoolean("autoStartFocus", d.autoStartFocus),
            strictMode = o.optBoolean("strictMode", d.strictMode),
            allowAppsOn = o.optBoolean("allowAppsOn", o.optBoolean("blockApps", d.allowAppsOn)),
            blockAppsOn = o.optBoolean("blockAppsOn", d.blockAppsOn),
            allowedApps = o.strings("allowedApps", emptyList()).toSet(),
            blockedApps = o.strings("blockedApps", d.blockedApps.toList()).toSet(),
            allowSitesOn = o.optBoolean("allowSitesOn", webFilter && webAllowMode),
            blockSitesOn = o.optBoolean("blockSitesOn", webFilter && !webAllowMode),
            allowedSites = o.strings("allowedSites", d.allowedSites).filter { it !in offAllowed },
            blockedSites = o.strings("blockedSites", d.blockedSites).filter { it !in offBlocked },
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
            profiles = o.profiles(),
            activeProfileId = o.optNullable("activeProfileId"),
            autoNextSubject = o.optBoolean("autoNextSubject", true),
            planSince = if (o.isNull("planSince")) null else o.optString("planSince").ifEmpty { null },
            askForNotes = o.optBoolean("askForNotes", d.askForNotes),
            smartBreaks = o.optBoolean("smartBreaks", d.smartBreaks),
            musicOn = o.optBoolean("musicOn", d.musicOn),
            musicStation = o.optString("musicStation", d.musicStation).ifEmpty { d.musicStation },
            musicVolume = o.optInt("musicVolume", d.musicVolume).coerceIn(0, 100),
            customStations = custom,
            lastReviewedWeek = o.optNullable("lastReviewedWeek"),
            syncUri = o.optNullable("syncUri"),
            deviceId = o.optString("deviceId", ""),
            lastSyncMs = o.optLong("lastSyncMs", 0L),
            planUpdatedMs = o.optLong("planUpdatedMs", 0L),
            settingsVersion = o.optInt("settingsVersion", 1),
        )
    }
}

