package com.personaos.personaos_mobile

import android.content.Intent
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.core.content.FileProvider
import io.flutter.embedding.android.FlutterFragmentActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel
import java.io.File

// FlutterFragmentActivity, not FlutterActivity: local_auth shows the biometric
// prompt as a fragment, and throws "no_fragment_activity" on a plain Activity.
class MainActivity : FlutterFragmentActivity() {
    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        // Updating the app from inside it (lib/app_update.dart): Dart downloads the release's APK
        // into the cache, this hands it to Android's installer, which asks the user to confirm.
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "personaos/installer").setMethodCallHandler { call, result ->
            when (call.method) {
                // Android 8+ installs from an app only once the user allows it, per app.
                "canInstall" -> result.success(
                    Build.VERSION.SDK_INT < Build.VERSION_CODES.O || packageManager.canRequestPackageInstalls()
                )
                "openInstallSettings" -> {
                    startActivity(
                        Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:$packageName"))
                    )
                    result.success(null)
                }
                "install" -> {
                    val path = call.argument<String>("path")
                    val apk = path?.let { File(it) }
                    // Only an APK this app downloaded into its own update folder.
                    if (apk == null || !apk.exists() || apk.parentFile?.canonicalPath != File(cacheDir, "updates").canonicalPath) {
                        result.error("bad_path", "No downloaded update at $path", null)
                    } else {
                        val uri = FileProvider.getUriForFile(this, "$packageName.updates", apk)
                        startActivity(
                            Intent(Intent.ACTION_VIEW)
                                .setDataAndType(uri, "application/vnd.android.package-archive")
                                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
                        )
                        result.success(null)
                    }
                }
                else -> result.notImplemented()
            }
        }
    }
}
