package com.kk.fokkus.timer

import android.content.Context
import android.media.AudioAttributes
import android.media.AudioFocusRequest
import android.media.AudioManager
import android.media.MediaPlayer
import android.os.PowerManager
import com.kk.fokkus.data.AppSettings
import com.kk.fokkus.data.SettingsStore
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import java.io.ByteArrayOutputStream
import java.io.File
import java.nio.ByteBuffer
import java.nio.ByteOrder
import kotlin.random.Random

class Station(val id: String, val name: String, val credit: String, val urls: List<String>?) {
    val offline: Boolean get() = urls == null
}

object Stations {
    // SomaFM: listener-supported internet radio; the mirrors are the ones in their official playlists.
    private fun soma(channel: String) = listOf("ice6", "ice2", "ice5").map { "https://$it.somafm.com/$channel-128-mp3" }

    val builtIn = listOf(
        Station("lofi", "Lofi beats", "SomaFM Fluid", soma("fluid")),
        Station("chill", "Chill grooves", "SomaFM Groove Salad", soma("groovesalad")),
        Station("ambient", "Ambient", "SomaFM Drone Zone", soma("dronezone")),
        Station("space", "Deep space", "SomaFM Deep Space One", soma("deepspaceone")),
        Station("brown", "Brown noise", "made by Fokkus, works offline", null),
    )

    fun all(s: AppSettings): List<Station> =
        builtIn + s.customStations.map { Station("custom:" + it.url, it.name.ifBlank { hostOf(it.url) }, "your stream", listOf(it.url)) }

    fun find(s: AppSettings, id: String): Station = all(s).firstOrNull { it.id == id } ?: builtIn[0]

    fun hostOf(url: String): String = runCatching { java.net.URI(url).host }.getOrNull() ?: url

    fun isStreamUrl(url: String): Boolean = runCatching {
        val u = java.net.URI(url.trim())
        (u.scheme == "http" || u.scheme == "https") && !u.host.isNullOrEmpty()
    }.getOrDefault(false)
}

/** Plays the focus music while a focus phase runs; pauses on breaks. Driven by the timer service every second. */
object FocusMusic {
    private var player: MediaPlayer? = null
    private var station: Station? = null
    private var mirror = 0
    private var volume = -1
    private var focus: AudioFocusRequest? = null
    private var heldOffKey: String? = null   // another app took the audio: stay quiet until the next focus phase
    private var ducked = false
    private val _error = MutableStateFlow<String?>(null)
    val error: StateFlow<String?> = _error

    private var lastChoice: String? = null

    fun update(ctx: Context, st: TimerState) {
        val s = SettingsStore.current
        val choice = "${s.musicOn}|${s.musicStation}"
        if (choice != lastChoice) { lastChoice = choice; heldOffKey = null; _error.value = null }
        val phaseKey = "${st.phase}|${st.completedInCycle}|${st.totalMs}"
        val play = s.musicOn && st.phase == Phase.FOCUS && st.run == RunState.RUNNING && heldOffKey != phaseKey
        if (!play) {
            if (heldOffKey != null && heldOffKey != phaseKey) heldOffKey = null
            stop(ctx)
            return
        }
        val want = Stations.find(s, s.musicStation)
        if (player == null || station?.id != want.id) {
            mirror = 0
            start(ctx, want)
        } else if (volume != s.musicVolume) applyVolume(s.musicVolume)
    }

    private fun applyVolume(v: Int) {
        volume = v
        val f = (v / 100f) * (if (ducked) 0.3f else 1f)
        try { player?.setVolume(f, f) } catch (_: Exception) { }
    }

    private fun start(ctx: Context, st: Station) {
        release()
        station = st
        val mp = MediaPlayer()
        player = mp
        try {
            mp.setAudioAttributes(AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_MEDIA).setContentType(AudioAttributes.CONTENT_TYPE_MUSIC).build())
            mp.setWakeMode(ctx.applicationContext, PowerManager.PARTIAL_WAKE_LOCK)
            if (st.offline) {
                mp.setDataSource(noiseFile(ctx).path)
                mp.isLooping = true
            } else mp.setDataSource(st.urls!![mirror % st.urls.size])
            mp.setOnPreparedListener {
                if (player !== it) return@setOnPreparedListener
                if (!requestFocus(ctx)) { stop(ctx); return@setOnPreparedListener }
                applyVolume(SettingsStore.current.musicVolume)
                it.start()
                _error.value = null
            }
            mp.setOnErrorListener { p, _, _ -> if (player === p) retry(ctx); true }
            mp.setOnCompletionListener { p -> if (player === p && !st.offline) retry(ctx) }   // a live stream shouldn't end
            mp.prepareAsync()
        } catch (e: Exception) {
            retry(ctx)
        }
    }

    // Next mirror; after two rounds give up and say so.
    private fun retry(ctx: Context) {
        val st = station ?: return
        mirror++
        if (st.offline || mirror >= (st.urls?.size ?: 1) * 2) {
            _error.value = "Can't reach “${st.name}” right now. Try another station or Brown noise."
            release()
            heldOffKey = TimerEngine.current.let { "${it.phase}|${it.completedInCycle}|${it.totalMs}" }
            return
        }
        start(ctx, st)
    }

    private fun requestFocus(ctx: Context): Boolean {
        val am = ctx.getSystemService(AudioManager::class.java) ?: return true
        val req = focus ?: AudioFocusRequest.Builder(AudioManager.AUDIOFOCUS_GAIN)
            .setAudioAttributes(AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_MEDIA).setContentType(AudioAttributes.CONTENT_TYPE_MUSIC).build())
            .setOnAudioFocusChangeListener { change ->
                when (change) {
                    AudioManager.AUDIOFOCUS_LOSS -> {
                        // e.g. you started a video: stay quiet for the rest of this focus phase
                        heldOffKey = TimerEngine.current.let { "${it.phase}|${it.completedInCycle}|${it.totalMs}" }
                        stop(ctx)
                    }
                    AudioManager.AUDIOFOCUS_LOSS_TRANSIENT -> try { player?.pause() } catch (_: Exception) { }
                    AudioManager.AUDIOFOCUS_LOSS_TRANSIENT_CAN_DUCK -> { ducked = true; applyVolume(volume) }
                    AudioManager.AUDIOFOCUS_GAIN -> {
                        ducked = false
                        applyVolume(volume)
                        try { player?.let { if (!it.isPlaying) it.start() } } catch (_: Exception) { }
                    }
                }
            }.build().also { focus = it }
        return am.requestAudioFocus(req) == AudioManager.AUDIOFOCUS_REQUEST_GRANTED
    }

    fun stop(ctx: Context) {
        if (player == null) return
        release()
        focus?.let { ctx.getSystemService(AudioManager::class.java)?.abandonAudioFocusRequest(it) }
    }

    private fun release() {
        val p = player
        player = null
        station = null
        volume = -1
        ducked = false
        try { p?.release() } catch (_: Exception) { }
    }

    // ---- Brown noise, generated once: 30 s that loop without a click (the end fades into the start).

    private fun noiseFile(ctx: Context): File {
        val f = File(ctx.filesDir, "brown-noise.wav")
        if (!f.exists()) f.writeBytes(brownNoiseWav())
        return f
    }

    fun brownNoiseWav(seconds: Int = 30, rate: Int = 22050): ByteArray {
        val n = seconds * rate
        val fade = rate / 2
        val x = DoubleArray(n + fade)
        val rnd = Random(42)
        var b = 0.0
        for (i in x.indices) {
            b = (b + 0.02 * (rnd.nextDouble() * 2 - 1)) / 1.02
            x[i] = b * 3.5
        }
        val pcm = ByteBuffer.allocate(n * 2).order(ByteOrder.LITTLE_ENDIAN)
        for (i in 0 until n) {
            var v = x[i + fade]
            val j = i - (n - fade)
            if (j >= 0) { val t = j / fade.toDouble(); v = v * (1 - t) + x[j] * t }
            pcm.putShort((v * 0.9 * Short.MAX_VALUE).coerceIn(Short.MIN_VALUE.toDouble(), Short.MAX_VALUE.toDouble()).toInt().toShort())
        }
        val out = ByteArrayOutputStream()
        val h = ByteBuffer.allocate(44).order(ByteOrder.LITTLE_ENDIAN)
        h.put("RIFF".toByteArray()); h.putInt(36 + n * 2); h.put("WAVE".toByteArray())
        h.put("fmt ".toByteArray()); h.putInt(16); h.putShort(1); h.putShort(1); h.putInt(rate); h.putInt(rate * 2); h.putShort(2); h.putShort(16)
        h.put("data".toByteArray()); h.putInt(n * 2)
        out.write(h.array())
        out.write(pcm.array())
        return out.toByteArray()
    }
}
