package com.kk.fokkus.data

import org.json.JSONArray
import org.json.JSONObject
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter
import java.util.Locale

/** Export (CSV for Excel / Sheets) and full backups of settings, plan, statistics, notes and distractions. */
object Backup {
    private val day = DateTimeFormatter.ofPattern("yyyy-MM-dd", Locale.ROOT)
    private val hm = DateTimeFormatter.ofPattern("HH:mm", Locale.ROOT)

    private fun cell(v: String?): String {
        val s = v ?: ""
        return if (s.any { it == ',' || it == '"' || it == '\n' || it == '\r' }) "\"" + s.replace("\"", "\"\"") + "\"" else s
    }

    /** One row per focus block: date, start, end, minutes, subject, completed pomodoro, note. */
    fun csv(s: AppSettings, records: List<FocusRecord>): String = buildString {
        append("date,start,end,minutes,subject,completed,note\r\n")
        for (r in records.sortedBy { it.startMs }) {
            val a = r.start
            val z = a.plusSeconds(r.seconds.toLong())
            val subject = s.subjects.firstOrNull { it.id == r.subjectId }?.name
            val min = r.seconds / 60.0
            append(a.format(day)).append(',').append(a.format(hm)).append(',').append(z.format(hm)).append(',')
                .append(if (min == Math.floor(min)) min.toLong().toString() else String.format(Locale.ROOT, "%.1f", min)).append(',')
                .append(cell(subject)).append(',').append(if (r.pomodoros > 0) "yes" else "no").append(',').append(cell(r.note)).append("\r\n")
        }
    }

    fun create(s: AppSettings, records: List<FocusRecord>, distractions: List<DistractionEntry>): String = JSONObject().apply {
        put("app", "Fokkus")
        put("kind", "backup")
        put("platform", "android")
        put("format", 1)
        put("created", LocalDateTime.now().format(DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm", Locale.ROOT)))
        put("settings", SettingsStore.toJson(s))
        put("sessions", JSONArray().apply { records.forEach { put(SessionLog.toJson(it)) } })
        put("distractions", JSONArray().apply { distractions.forEach { put(DistractionLog.toJson(it)) } })
    }.toString()

    class Parsed(
        val created: String,
        val platform: String,
        val settings: AppSettings?,      // null for a backup from Windows
        val sessions: List<FocusRecord>,
        val distractions: List<DistractionEntry>,
    )

    /** Reads a Fokkus backup from the phone or from Windows. Throws with a readable message if it isn't one. */
    fun parse(text: String): Parsed {
        val o = JSONObject(text.trimStart('﻿'))
        if (o.optString("app") != "Fokkus") throw IllegalArgumentException("This isn't a Fokkus backup.")
        val platform = o.optString("platform", "android")
        val settings = if (platform == "android") o.optJSONObject("settings")?.let { SettingsStore.fromJson(it) } else null
        val sessions = mutableListOf<FocusRecord>()
        o.optJSONArray("sessions")?.let { arr ->
            for (i in 0 until arr.length()) {
                val r = arr.optJSONObject(i) ?: continue
                // Windows backups use the long names, phone backups the short sync names.
                val rec = if (r.has("StartMs")) FocusRecord(
                    r.optLong("StartMs"), r.optInt("Seconds"), r.optString("SubjectId").takeIf { !r.isNull("SubjectId") && it.isNotEmpty() },
                    r.optInt("Pomodoros"), r.optString("Note").takeIf { !r.isNull("Note") && it.isNotEmpty() }, r.optLong("NoteMs"),
                ) else runCatching { SessionLog.fromJson(r) }.getOrNull()
                if (rec != null && rec.seconds > 0) sessions += rec
            }
        }
        val distractions = mutableListOf<DistractionEntry>()
        o.optJSONArray("distractions")?.let { arr ->
            for (i in 0 until arr.length()) arr.optJSONObject(i)?.let(DistractionLog::fromJson)?.let(distractions::add)
        }
        return Parsed(o.optString("created", "?"), platform, settings, sessions, distractions)
    }

    /** Restores settings + plan (phone backups) and adds the statistics, notes and distractions you don't have. */
    fun restore(p: Parsed): Int {
        p.settings?.let { restored ->
            SettingsStore.update { cur ->
                // This phone's sync link stays; the restored plan counts as the newest one.
                restored.copy(
                    syncUri = cur.syncUri, deviceId = cur.deviceId, lastSyncMs = cur.lastSyncMs,
                    planUpdatedMs = System.currentTimeMillis(), bubbleX = cur.bubbleX, bubbleY = cur.bubbleY,
                )
            }
        }
        val added = SessionLog.merge(p.sessions)
        DistractionLog.merge(p.distractions)
        return added
    }
}
