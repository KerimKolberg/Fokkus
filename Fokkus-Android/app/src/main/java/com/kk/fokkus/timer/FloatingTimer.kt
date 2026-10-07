package com.kk.fokkus.timer

import android.annotation.SuppressLint
import android.content.Context
import android.content.Intent
import android.graphics.PixelFormat
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.provider.Settings
import android.text.TextUtils
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.view.ViewConfiguration
import android.view.WindowManager
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
import com.kk.fokkus.MainActivity
import com.kk.fokkus.R
import com.kk.fokkus.data.Motivation
import com.kk.fokkus.data.Planner
import com.kk.fokkus.data.SettingsStore
import kotlin.math.abs

/** Small draggable timer that floats above other apps. Tap to show controls, drag to move. */
class FloatingTimer(private val ctx: Context) {
    private val wm = ctx.getSystemService(WindowManager::class.java)
    private val dp = ctx.resources.displayMetrics.density
    private var root: LinearLayout? = null
    private lateinit var dot: View
    private lateinit var dotBg: GradientDrawable
    private lateinit var time: TextView
    private lateinit var sub: TextView
    private lateinit var controls: LinearLayout
    private lateinit var btnToggle: ImageView
    private lateinit var btnSkip: ImageView
    private var expanded = false
    private var message: String? = null
    private var messageUntil = 0L
    private var last: TimerState? = null

    private val lp = WindowManager.LayoutParams(
        WindowManager.LayoutParams.WRAP_CONTENT,
        WindowManager.LayoutParams.WRAP_CONTENT,
        WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
        WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
        PixelFormat.TRANSLUCENT,
    ).apply {
        gravity = Gravity.TOP or Gravity.START
        val s = SettingsStore.current
        x = if (s.bubbleX >= 0) s.bubbleX else px(12)
        y = if (s.bubbleY >= 0) s.bubbleY else px(140)
    }

    private fun px(v: Int) = (v * dp).toInt()

    fun show() {
        if (root != null || !Settings.canDrawOverlays(ctx)) return
        build()
        try {
            wm.addView(root, lp)
            instance = this
        } catch (e: Exception) {
            root = null
        }
    }

    fun hide() {
        root?.let { try { wm.removeView(it) } catch (_: Exception) { } }
        root = null
        if (instance === this) instance = null
    }

    @SuppressLint("ClickableViewAccessibility")
    private fun build() {
        val r = LinearLayout(ctx).apply {
            orientation = LinearLayout.VERTICAL
            background = GradientDrawable().apply {
                cornerRadius = 20 * dp
                setColor(0xF2102038.toInt())
                setStroke(px(1), 0xFF2F4B72.toInt())
            }
            setPadding(px(14), px(8), px(14), px(8))
            elevation = 8 * dp
        }
        val row = LinearLayout(ctx).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        dotBg = GradientDrawable().apply { shape = GradientDrawable.OVAL; setColor(Notifications.FOCUS_COLOR) }
        dot = View(ctx).apply {
            background = dotBg
            layoutParams = LinearLayout.LayoutParams(px(10), px(10)).apply { marginEnd = px(10) }
        }
        val texts = LinearLayout(ctx).apply { orientation = LinearLayout.VERTICAL }
        time = TextView(ctx).apply {
            textSize = 20f
            setTextColor(0xFFEAF1F6.toInt())
            typeface = Typeface.create("sans-serif-medium", Typeface.NORMAL)
            fontFeatureSettings = "tnum"
            includeFontPadding = false
        }
        sub = TextView(ctx).apply {
            textSize = 10.5f
            setTextColor(0xFF8FA3BD.toInt())
            maxWidth = px(210)
            maxLines = 3
            ellipsize = TextUtils.TruncateAt.END
        }
        texts.addView(time)
        texts.addView(sub)
        row.addView(dot)
        row.addView(texts)

        controls = LinearLayout(ctx).apply {
            orientation = LinearLayout.HORIZONTAL
            visibility = View.GONE
            setPadding(0, px(6), 0, 0)
        }
        btnToggle = iconButton(R.drawable.ic_pause) { TimerEngine.toggle() }
        btnSkip = iconButton(R.drawable.ic_skip) { TimerEngine.skip() }
        controls.addView(btnToggle)
        controls.addView(btnSkip)
        controls.addView(iconButton(R.drawable.ic_open) {
            ctx.startActivity(Intent(ctx, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP))
        })
        controls.addView(iconButton(R.drawable.ic_close) {
            TimerService.bubbleDismissed = true
            hide()
        })

        r.addView(row)
        r.addView(controls)

        val slop = ViewConfiguration.get(ctx).scaledTouchSlop
        var downX = 0f
        var downY = 0f
        var startX = 0
        var startY = 0
        var dragging = false
        r.setOnTouchListener { v, e ->
            when (e.actionMasked) {
                MotionEvent.ACTION_DOWN -> {
                    downX = e.rawX; downY = e.rawY; startX = lp.x; startY = lp.y; dragging = false
                }
                MotionEvent.ACTION_MOVE -> {
                    val dx = e.rawX - downX
                    val dy = e.rawY - downY
                    if (!dragging && (abs(dx) > slop || abs(dy) > slop)) dragging = true
                    if (dragging) {
                        lp.x = (startX + dx).toInt().coerceAtLeast(0)
                        lp.y = (startY + dy).toInt().coerceAtLeast(0)
                        try { wm.updateViewLayout(v, lp) } catch (_: Exception) { }
                    }
                }
                MotionEvent.ACTION_UP -> {
                    if (dragging) {
                        SettingsStore.update { it.copy(bubbleX = lp.x, bubbleY = lp.y) }
                    } else {
                        expanded = !expanded
                        controls.visibility = if (expanded) View.VISIBLE else View.GONE
                        v.performClick()
                    }
                }
            }
            true
        }
        root = r
        last?.let { update(it) }
    }

    private fun iconButton(icon: Int, onClick: () -> Unit) = ImageView(ctx).apply {
        setImageResource(icon)
        setColorFilter(0xFFEAF1F6.toInt())
        val p = px(7)
        setPadding(p, p, p, p)
        layoutParams = LinearLayout.LayoutParams(px(36), px(36)).apply { marginEnd = px(4) }
        background = GradientDrawable().apply { cornerRadius = 10 * dp; setColor(0xFF1E3556.toInt()) }
        setOnClickListener { onClick() }
    }

    fun update(st: TimerState) {
        last = st
        if (root == null) return
        val now = System.currentTimeMillis()
        time.text = formatTime(st.remaining(now))
        val color = when {
            st.run == RunState.IDLE -> 0xFF6F84A0.toInt()
            st.run == RunState.PAUSED || st.run == RunState.READY -> blend(Notifications.phaseColor(st.phase), 0xFF6F84A0.toInt())
            else -> Notifications.phaseColor(st.phase)
        }
        dotBg.setColor(color)
        val msg = message
        if (msg != null && now < messageUntil) {
            sub.text = msg
            sub.setTextColor(0xFF7FD6D2.toInt())
        } else if (st.run == RunState.RUNNING && (now / 1000L) % 40 >= 30) {
            // For 10 of every 40 seconds: a short motivation line.
            message = null
            val (done, target) = Planner.totals(SettingsStore.current)
            sub.text = Motivation.current(st, done, target, now)
            sub.setTextColor(0xFF7FD6D2.toInt())
        } else {
            message = null
            sub.text = subtitle(st)
            sub.setTextColor(0xFF8FA3BD.toInt())
        }
        val strict = TimerEngine.strictLocked
        btnToggle.setImageResource(if (st.run == RunState.RUNNING) R.drawable.ic_pause else R.drawable.ic_play)
        btnToggle.alpha = if (strict && st.run == RunState.RUNNING) 0.3f else 1f
        btnSkip.alpha = if (strict || st.run == RunState.IDLE) 0.3f else 1f
    }

    private fun subtitle(st: TimerState): String {
        val settings = SettingsStore.current
        val sessions = settings.sessionsBeforeLong
        val subject = Planner.active(settings)?.name?.uppercase()
        return when (st.run) {
            RunState.IDLE -> if (subject != null) "READY · $subject" else "READY"
            RunState.PAUSED -> "${Notifications.phaseName(st.phase).uppercase()} · PAUSED"
            RunState.READY -> "NEXT: ${Notifications.phaseName(st.phase).uppercase()}"
            RunState.RUNNING ->
                if (st.phase == Phase.FOCUS) "🔒 ${subject ?: "FOCUS"} ${(st.completedInCycle + 1).coerceAtMost(sessions)}/$sessions"
                else Notifications.phaseName(st.phase).uppercase()
        }
    }

    fun showMessage(text: String) {
        message = text
        messageUntil = System.currentTimeMillis() + 10_000L
        last?.let { update(it) }
    }

    companion object {
        @Volatile
        private var instance: FloatingTimer? = null

        fun message(text: String) {
            instance?.showMessage(text)
        }

        fun formatTime(ms: Long): String {
            val total = ((ms + 999) / 1000).coerceAtLeast(0)
            val h = total / 3600
            val m = (total % 3600) / 60
            val s = total % 60
            return if (h > 0) "%d:%02d:%02d".format(h, m, s) else "%02d:%02d".format(m, s)
        }

        private fun blend(a: Int, b: Int): Int {
            fun ch(c: Int, shift: Int) = (c shr shift) and 0xFF
            val r = (ch(a, 16) + ch(b, 16)) / 2
            val g = (ch(a, 8) + ch(b, 8)) / 2
            val bl = (ch(a, 0) + ch(b, 0)) / 2
            return (0xFF shl 24) or (r shl 16) or (g shl 8) or bl
        }
    }
}
