package com.kk.fokkus.timer

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import androidx.core.app.NotificationManagerCompat
import androidx.core.app.RemoteInput
import com.kk.fokkus.data.SessionLog
import com.kk.fokkus.data.SyncManager

/** Handles the phase-end alarm and the buttons in the timer notifications. */
class TimerReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        when (intent.action) {
            ACTION_PHASE_END -> TimerEngine.tick()
            ACTION_TOGGLE -> TimerEngine.toggle()
            ACTION_START -> TimerEngine.start()
            ACTION_SKIP -> TimerEngine.skip()
            ACTION_STOP -> TimerEngine.stop()
            ACTION_LONGER -> {
                TimerEngine.setPhaseLength(intent.getIntExtra(EXTRA_MINUTES, 0), start = intent.getBooleanExtra(EXTRA_START, false))
                NotificationManagerCompat.from(context).cancel(Notifications.ID_ALERT)
            }
            ACTION_NOTE -> {
                val text = RemoteInput.getResultsFromIntent(intent)?.getCharSequence(Notifications.KEY_NOTE)?.toString()
                val key = intent.getStringExtra(EXTRA_KEY)
                if (key != null && !text.isNullOrBlank()) {
                    SessionLog.setNote(key, text)
                    SyncManager.runSoon()
                }
                Notifications.noteSaved(context)
            }
        }
    }

    companion object {
        const val ACTION_PHASE_END = "com.kk.fokkus.PHASE_END"
        const val ACTION_TOGGLE = "com.kk.fokkus.TOGGLE"
        const val ACTION_START = "com.kk.fokkus.START"
        const val ACTION_SKIP = "com.kk.fokkus.SKIP"
        const val ACTION_STOP = "com.kk.fokkus.STOP"
        const val ACTION_LONGER = "com.kk.fokkus.LONGER"
        const val ACTION_NOTE = "com.kk.fokkus.NOTE"
        const val EXTRA_MINUTES = "minutes"
        const val EXTRA_START = "start"
        const val EXTRA_KEY = "key"
    }
}
