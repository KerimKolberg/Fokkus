package com.kk.fokkus

import android.app.Application
import com.kk.fokkus.data.SettingsStore
import com.kk.fokkus.data.SessionLog
import com.kk.fokkus.timer.Notifications
import com.kk.fokkus.timer.TimerEngine
import com.kk.fokkus.timer.TimerService

class FokkusApp : Application() {
    override fun onCreate() {
        super.onCreate()
        SettingsStore.init(this)
        SessionLog.init(this)
        Notifications.createChannels(this)
        TimerEngine.init(this)
        // The process may have been restarted mid-session (e.g. by the phase alarm): catch up and resume.
        if (TimerEngine.current.inSession) {
            TimerEngine.tick()
            TimerService.sync(this)
        }
    }
}
