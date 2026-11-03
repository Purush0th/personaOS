package com.personaos.personaos_mobile

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import androidx.core.app.NotificationCompat

/**
 * Keeps hands-free going with the app in the background or the screen off, as ChatGPT's voice
 * mode does (owner, 2026-11-03). Android lets an app use the microphone out of sight only while it
 * runs a foreground service of the microphone type, which shows this notification for as long as
 * it runs. The conversation itself stays in Dart; this only keeps the process allowed to listen
 * and play. The notification's End button ends hands-free in the chat as well.
 */
class HandsFreeService : Service() {
    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_END) {
            onEnded?.invoke()
            stopSelf()
            return START_NOT_STICKY
        }

        val manager = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            manager.createNotificationChannel(
                NotificationChannel(CHANNEL, "Hands-free", NotificationManager.IMPORTANCE_LOW).apply {
                    description = "Shown while the assistant listens with the app in the background."
                    setShowBadge(false)
                }
            )
        }
        val open = PendingIntent.getActivity(
            this, 0,
            Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val end = PendingIntent.getService(
            this, 1,
            Intent(this, HandsFreeService::class.java).setAction(ACTION_END),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val notification = NotificationCompat.Builder(this, CHANNEL)
            .setSmallIcon(R.drawable.ic_stat_hands_free)
            .setContentTitle("Hands-free is on")
            .setContentText(intent?.getStringExtra(EXTRA_TEXT) ?: "Talk to your assistant any time.")
            .setContentIntent(open)
            .addAction(0, "End", end)
            .setOngoing(true)
            .setSilent(true)
            .setCategory(NotificationCompat.CATEGORY_SERVICE)
            .build()

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            startForeground(
                NOTIFICATION_ID, notification,
                ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE or ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PLAYBACK,
            )
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }
        return START_NOT_STICKY
    }

    companion object {
        private const val CHANNEL = "hands_free"
        private const val NOTIFICATION_ID = 7301
        const val ACTION_END = "com.personaos.HANDS_FREE_END"
        const val EXTRA_TEXT = "text"

        /** Set by MainActivity: tells the chat that hands-free was ended from the notification. */
        var onEnded: (() -> Unit)? = null
    }
}
