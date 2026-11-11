package com.kk.fokkus.data

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Handler
import android.os.Looper
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import org.json.JSONArray
import org.json.JSONObject
import java.text.DateFormat
import java.util.Date
import java.util.concurrent.Executors

// ---- The shared sync file "Fokkus-sync.json" (same format as Fokkus for Windows).

class SyncPlan(val updated: Long, val planSince: String?, val autoNext: Boolean, val subjects: List<Subject>)

class SyncDoc(
    var plan: SyncPlan? = null,
    val sessions: MutableList<FocusRecord> = mutableListOf(),
    val devices: MutableMap<String, Pair<String, Long>> = mutableMapOf(),
) {
    fun toJson(): JSONObject = JSONObject().apply {
        put("app", "Fokkus")
        put("format", 1)
        plan?.let { p ->
            put("plan", JSONObject().put("updated", p.updated).put("planSince", p.planSince ?: JSONObject.NULL).put("autoNext", p.autoNext)
                .put("subjects", JSONArray().apply { p.subjects.forEach { put(subjectToJson(it)) } }))
        }
        put("sessions", JSONArray().apply { sessions.forEach { put(SessionLog.toJson(it)) } })
        put("devices", JSONObject().apply { devices.forEach { (id, d) -> put(id, JSONObject().put("name", d.first).put("seen", d.second)) } })
    }

    companion object {
        fun subjectToJson(s: Subject): JSONObject = JSONObject().put("id", s.id).put("name", s.name).put("color", s.colorIndex)
            .put("targets", JSONArray(s.targets)).put("deadline", s.deadline ?: JSONObject.NULL)
            .put("deadlineLabel", s.deadlineLabel ?: JSONObject.NULL).put("profileId", s.profileId ?: JSONObject.NULL)

        private fun JSONObject.str(key: String): String? = if (isNull(key)) null else optString(key).ifEmpty { null }

        fun subjectFromJson(o: JSONObject): Subject {
            val t = o.optJSONArray("targets")
            return Subject(
                o.str("id") ?: java.util.UUID.randomUUID().toString().take(10), o.str("name") ?: "Subject", o.optInt("color", 8),
                List(7) { d -> (t?.optInt(d, 0) ?: 0).coerceIn(0, 16) },
                deadline = o.str("deadline"), deadlineLabel = o.str("deadlineLabel"), profileId = o.str("profileId"),
            )
        }

        /** Throws if the text isn't a Fokkus sync file (so we never overwrite someone else's file). */
        fun parse(text: String): SyncDoc {
            val clean = text.trimStart('﻿').trim()
            if (clean.isEmpty()) return SyncDoc()
            val o = JSONObject(clean)
            if (o.optString("app") != "Fokkus") throw IllegalArgumentException("This isn't a Fokkus sync file.")
            val doc = SyncDoc()
            o.optJSONObject("plan")?.let { p ->
                val arr = p.optJSONArray("subjects")
                doc.plan = SyncPlan(
                    p.optLong("updated"), p.str("planSince"), p.optBoolean("autoNext", true),
                    if (arr == null) emptyList() else (0 until arr.length()).mapNotNull { i -> arr.optJSONObject(i)?.let(::subjectFromJson) },
                )
            }
            o.optJSONArray("sessions")?.let { arr ->
                for (i in 0 until arr.length()) arr.optJSONObject(i)?.let { r -> runCatching { SessionLog.fromJson(r) }.getOrNull()?.takeIf { it.seconds > 0 }?.let(doc.sessions::add) }
            }
            o.optJSONObject("devices")?.let { d ->
                d.keys().forEach { id -> d.optJSONObject(id)?.let { doc.devices[id] = it.optString("name") to it.optLong("seen") } }
            }
            return doc
        }
    }
}

class SyncOutcome(var settings: AppSettings) {
    var remap: Map<String, String> = emptyMap()
    var sent = 0
    var planReceived = false
    var planSent = false
    var plansJoined = false
    var fileChanged = false
}

/**
 * Merges this phone's data with the shared file. Statistics and notes are joined (nothing is ever deleted);
 * for the study plan the newest version wins - the very first time, two separate plans are joined by subject name.
 * The records from the file are added by the caller (SessionLog.merge).
 */
object SyncMerge {
    fun apply(s0: AppSettings, local: List<FocusRecord>, doc: SyncDoc, deviceName: String, now: Long): SyncOutcome {
        var s = s0
        val o = SyncOutcome(s)
        val first = s.lastSyncMs == 0L
        val remote = doc.plan
        val remoteHasPlan = remote != null && remote.subjects.isNotEmpty()
        val remoteUpdated = remote?.updated ?: 0L
        var mine = local

        if (first && remoteHasPlan && s.subjects.isNotEmpty()) {
            val joined = remote!!.subjects.toMutableList()
            val map = HashMap<String, String>()
            for (sub in s.subjects) {
                val i = joined.indexOfFirst { it.name.trim().equals(sub.name.trim(), ignoreCase = true) }
                if (i >= 0) {
                    val twin = joined[i]
                    if (twin.id != sub.id) map[sub.id] = twin.id
                    joined[i] = twin.copy(
                        deadline = twin.deadline ?: sub.deadline,
                        deadlineLabel = if (twin.deadline == null) sub.deadlineLabel else twin.deadlineLabel,
                        profileId = twin.profileId ?: sub.profileId,
                    )
                } else {
                    val color = if (joined.any { it.colorIndex == sub.colorIndex }) SubjectColors.nextIndex(joined) else sub.colorIndex
                    joined += sub.copy(colorIndex = color)
                }
            }
            val since = listOfNotNull(remote.planSince, s.planSince).minOrNull()
            s = s.copy(
                subjects = joined, planSince = since, activeSubjectId = s.activeSubjectId?.let { map[it] ?: it },
                planUpdatedMs = maxOf(now, remoteUpdated + 1),
            )
            o.remap = map
            mine = local.map { r -> r.subjectId?.let { map[it] }?.let { r.copy(subjectId = it) } ?: r }
            o.plansJoined = true
        } else if (first && !remoteHasPlan && s.subjects.isNotEmpty()) {
            // An empty plan from another device never replaces yours.
            s = s.copy(planUpdatedMs = maxOf(s.planUpdatedMs, remoteUpdated + 1))
        } else if (remote != null && remote.updated > s.planUpdatedMs) {
            s = s.copy(
                subjects = remote.subjects, planSince = remote.planSince, autoNextSubject = remote.autoNext, planUpdatedMs = remote.updated,
                activeSubjectId = s.activeSubjectId?.takeIf { id -> remote.subjects.any { it.id == id } },
            )
            o.planReceived = true
        }
        if (remote == null || s.planUpdatedMs > remote.updated) {
            doc.plan = SyncPlan(s.planUpdatedMs, s.planSince, s.autoNextSubject, s.subjects)
            o.planSent = true
            o.fileChanged = true
        }

        // Statistics + notes into the file.
        val inFile = HashMap<String, Int>()
        doc.sessions.forEachIndexed { i, r -> inFile[r.key] = i }
        for (r in mine) {
            val i = inFile[r.key]
            if (i == null) { inFile[r.key] = doc.sessions.size; doc.sessions += r; o.sent++ }
            else if (r.noteMs > doc.sessions[i].noteMs) { doc.sessions[i] = doc.sessions[i].copy(note = r.note, noteMs = r.noteMs); o.sent++ }
        }
        if (o.sent > 0) {
            doc.sessions.sortBy { it.startMs }
            o.fileChanged = true
        }
        val me = doc.devices[s.deviceId]
        if (me == null || o.fileChanged || now - me.second > 6 * 3600_000L) {
            doc.devices[s.deviceId] = deviceName to now
            o.fileChanged = true
        }
        o.settings = s.copy(lastSyncMs = now)
        return o
    }
}

/** Reads and writes the sync file you picked (in Google Drive, OneDrive...) through Android's file picker permission. */
object SyncManager {
    private lateinit var app: Context
    private val io = Executors.newSingleThreadExecutor()
    private val main = Handler(Looper.getMainLooper())
    private val _status = MutableStateFlow<String?>(null)
    val status: StateFlow<String?> = _status
    private val _busy = MutableStateFlow(false)
    val busy: StateFlow<Boolean> = _busy
    var lastFailed = false
        private set
    private var lastRun = 0L

    fun init(ctx: Context) { app = ctx.applicationContext }

    /** Keeps access to the picked file after restarts. */
    fun use(uri: Uri) {
        try {
            app.contentResolver.takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION)
        } catch (_: Exception) {
            // Some storage apps don't grant lasting access; it still works until the phone restarts.
        }
        SettingsStore.update { it.copy(syncUri = uri.toString()) }
        run()
    }

    fun stop() {
        val uri = SettingsStore.current.syncUri
        if (uri != null) try {
            app.contentResolver.releasePersistableUriPermission(Uri.parse(uri), Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION)
        } catch (_: Exception) { }
        SettingsStore.update { it.copy(syncUri = null) }
        _status.value = null
    }

    /** Sync now, unless one ran less than [minGapMs] ago. */
    fun run(minGapMs: Long = 0L) {
        val uriStr = SettingsStore.current.syncUri ?: return
        val now = System.currentTimeMillis()
        if (_busy.value || now - lastRun < minGapMs) return
        lastRun = now
        _busy.value = true
        _status.value = "Syncing…"
        io.execute {
            val uri = Uri.parse(uriStr)
            val doc: SyncDoc = try {
                val text = app.contentResolver.openInputStream(uri)?.use { it.readBytes().toString(Charsets.UTF_8) } ?: ""
                SyncDoc.parse(text)
            } catch (e: Exception) {
                main.post { finish(false, "Sync problem: " + (e.message ?: e.javaClass.simpleName)) }
                return@execute
            }
            main.post { merge(uri, doc) }
        }
    }

    fun runSoon() = main.postDelayed({ run() }, 4000L)

    private fun merge(uri: Uri, doc: SyncDoc) {
        val o = SyncMerge.apply(SettingsStore.current, SessionLog.records.value, doc, "Android · " + Build.MODEL, System.currentTimeMillis())
        SessionLog.remapSubjects(o.remap)
        val received = SessionLog.merge(doc.sessions)
        SettingsStore.update { o.settings }
        val summary = summary(o, received)
        if (!o.fileChanged) { finish(true, summary); return }
        val text = doc.toJson().toString()
        io.execute {
            val err = try { write(uri, text); null } catch (e: Exception) { e.message ?: e.javaClass.simpleName }
            main.post { finish(err == null, err?.let { "Couldn't write the sync file: $it" } ?: summary) }
        }
    }

    private fun write(uri: Uri, text: String) {
        val cr = app.contentResolver
        val out = try { cr.openOutputStream(uri, "wt") } catch (e: Exception) { null } ?: cr.openOutputStream(uri, "w")
        out?.use { it.write(text.toByteArray(Charsets.UTF_8)) } ?: throw IllegalStateException("no access")
    }

    private fun summary(o: SyncOutcome, received: Int): String {
        val parts = mutableListOf<String>()
        if (received > 0) parts += "$received from your other devices"
        if (o.sent > 0) parts += "${o.sent} shared"
        when {
            o.plansJoined -> parts += "plans joined"
            o.planReceived -> parts += "plan updated"
            o.planSent -> parts += "plan shared"
        }
        val time = DateFormat.getTimeInstance(DateFormat.SHORT).format(Date())
        return "Synced $time · " + if (parts.isEmpty()) "up to date" else parts.joinToString(", ")
    }

    private fun finish(ok: Boolean, status: String) {
        lastFailed = !ok
        _busy.value = false
        _status.value = status
    }
}
