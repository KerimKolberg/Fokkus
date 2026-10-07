package com.kk.fokkus.timer

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent

/** Handles the phase-end alarm and the buttons in the timer notification. */
class TimerReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        when (intent.action) {
            ACTION_PHASE_END -> TimerEngine.tick()
            ACTION_TOGGLE -> TimerEngine.toggle()
            ACTION_START -> TimerEngine.start()
            ACTION_SKIP -> TimerEngine.skip()
            ACTION_STOP -> TimerEngine.stop()
        }
    }

    companion object {
        const val ACTION_PHASE_END = "com.kk.fokkus.PHASE_END"
        const val ACTION_TOGGLE = "com.kk.fokkus.TOGGLE"
        const val ACTION_START = "com.kk.fokkus.START"
        const val ACTION_SKIP = "com.kk.fokkus.SKIP"
        const val ACTION_STOP = "com.kk.fokkus.STOP"
    }
}
