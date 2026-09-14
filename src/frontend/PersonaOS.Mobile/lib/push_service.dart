import 'dart:async';
import 'dart:io';

import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';

import 'api/personaos_api.dart';
import 'reminder_alarms.dart';

/// Registers this phone for push notifications and shows them.
///
/// Reminders and proactive briefs are sent by the server through FCM. The server knows where to
/// send them only because the app registers its token after sign-in — the server, not the
/// phone, holds the list, so a reinstall recovers the moment the user signs in again.
///
/// Bring-your-own Firebase: the APK contains no Firebase project. After sign-in the app asks
/// its own server for the client options the admin uploaded, and starts Firebase with those.
/// An instance where nobody has set up push answers with nothing, and push simply stays off.
/// The rest of the app must never depend on this succeeding.
class PushService {
  PushService._();

  static final PushService instance = PushService._();

  /// Must match the `ChannelId` the server puts on Android notifications (FcmPushSender). A
  /// message naming a channel that does not exist on the device falls back to a default
  /// channel with default importance — no heads-up banner, easy to miss.
  static const channelId = 'reminders';

  final _local = FlutterLocalNotificationsPlugin();

  bool _started = false;
  String? _registeredToken;
  StreamSubscription<String>? _tokenRefresh;
  StreamSubscription<RemoteMessage>? _foreground;

  /// Starts push for a signed-in session. Safe to call on every sign-in: the listeners are
  /// attached once, but the token is re-registered each time, because the server may have
  /// pruned it or the database may have been reset since the last launch.
  Future<void> start(PersonaOsApi api, {required String assistantNickname}) async {
    if (!Platform.isAndroid) return;

    // Alarms first, and independently of push. The phone schedules its own alarms from the
    // server's pending reminders, so they ring even on an instance where nobody has set up
    // Firebase — push only makes changes arrive instantly instead of at the next sign-in.
    final alarms = ReminderAlarms.instance;
    await alarms.requestPermissions();
    await _syncAlarms(api, assistantNickname);

    final FcmClientOptions? config;
    try {
      config = await api.getPushConfig();
    } catch (e) {
      debugPrint('Push unavailable: could not fetch push config ($e).');
      return;
    }
    if (config == null) {
      // The admin has not uploaded Firebase files. Not an error — push is just not set up.
      debugPrint('Push unavailable: not configured on this server.');
      return;
    }

    try {
      if (Firebase.apps.isEmpty) {
        await Firebase.initializeApp(
          options: FirebaseOptions(
            apiKey: config.apiKey,
            appId: config.appId,
            messagingSenderId: config.messagingSenderId,
            projectId: config.projectId,
          ),
        );
      } else if (Firebase.app().options.projectId != config.projectId) {
        // Firebase cannot re-initialise its default app with different options in a running
        // process. The admin switched projects; the new one takes effect on the next launch.
        debugPrint('Push project changed on the server; restart the app to use it.');
        return;
      }
    } catch (e) {
      debugPrint('Push unavailable: Firebase failed to start ($e).');
      return;
    }

    final messaging = FirebaseMessaging.instance;

    if (!_started) {
      _started = true;
      await _createChannel();

      // Android 13+ shows nothing without this, and FCM does not report the failure.
      await messaging.requestPermission();

      // FCM rotates tokens. Without re-registering, the server keeps sending to the old one,
      // FCM rejects it as unregistered, the server prunes it — and the phone silently stops
      // getting anything.
      _tokenRefresh = messaging.onTokenRefresh.listen((token) => _register(api, token));

      // Reminder schedule changes arrive as data-only messages, which wake the app even when it
      // is not running. The handler has to be a top-level function: it runs in its own isolate.
      FirebaseMessaging.onBackgroundMessage(handleBackgroundPush);

      // In the foreground FCM hands every message to the app and displays nothing, so both kinds
      // are handled here: reminder data goes to the alarms, notifications are shown directly.
      _foreground = FirebaseMessaging.onMessage.listen((message) {
        if (_isReminderPush(message)) {
          ReminderAlarms.instance.handlePush(message.data);
        } else {
          _showInForeground(message);
        }
      });
    }

    final token = await messaging.getToken();
    if (token != null) await _register(api, token);
  }

  /// Schedules this phone's alarms to match the server's pending reminders. A server with
  /// reminders switched off answers with an error, and then no alarm should ring at all.
  Future<void> _syncAlarms(PersonaOsApi api, String assistantNickname) async {
    try {
      final pending = await api.getReminders();
      await ReminderAlarms.instance.syncWith([
        for (final reminder in pending.where((r) => r.status == 'pending'))
          ReminderAlarm(
            reminderId: reminder.id,
            dueAtUtc: reminder.dueAtUtc,
            title: assistantNickname,
            message: reminder.message,
          ),
      ]);
    } on ApiException catch (e) {
      debugPrint('Reminders unavailable, clearing alarms: $e');
      await ReminderAlarms.instance.cancelAll();
    } catch (e) {
      // Offline or unreachable: keep whatever alarms are already set rather than wiping them.
      debugPrint('Could not sync reminder alarms: $e');
    }
  }

  Future<void> _register(PersonaOsApi api, String token) async {
    // The first launch produces the same token twice at once — from getToken() and from FCM's
    // refresh event — and the two registrations used to race each other into a server error.
    if (token == _registeredToken) return;
    _registeredToken = token;
    try {
      await api.registerDevice(token, platform: 'android');
    } catch (e) {
      // Retried on the next sign-in or token refresh. Losing one registration attempt must not
      // surface as an error in the middle of whatever the user is doing.
      _registeredToken = null;
      debugPrint('Push registration failed: $e');
    }
  }

  Future<void> _createChannel() async {
    // Deliberately no initialize() here. The plugin is initialised exactly once, at startup, by
    // ReminderAlarms — and a second initialize() replaces the response callbacks it registered,
    // which would silently break Snooze, Dismiss and opening a ringing alarm.
    await _local
        .resolvePlatformSpecificImplementation<AndroidFlutterLocalNotificationsPlugin>()
        ?.createNotificationChannel(const AndroidNotificationChannel(
          channelId,
          'Reminders and briefs',
          description: 'Reminders you set, and scheduled briefs from your assistant.',
          // High, so a reminder shows as a heads-up banner rather than sitting silently in the
          // shade — a reminder that is not noticed has not been delivered.
          importance: Importance.high,
        ));
  }

  Future<void> _showInForeground(RemoteMessage message) async {
    final notification = message.notification;
    if (notification == null) return;

    await _local.show(
      id: notification.hashCode,
      title: notification.title,
      body: notification.body,
      notificationDetails: const NotificationDetails(
        android: AndroidNotificationDetails(
          channelId,
          'Reminders and briefs',
          importance: Importance.high,
          priority: Priority.high,
        ),
      ),
    );
  }

  @visibleForTesting
  Future<void> dispose() async {
    await _tokenRefresh?.cancel();
    await _foreground?.cancel();
    _started = false;
  }
}

bool _isReminderPush(RemoteMessage message) =>
    '${message.data['type']}'.startsWith('reminder_');

/// A data push that arrived while the app was not in the foreground. Runs in its own isolate with
/// no UI and no signed-in session, which is why reminder messages carry everything needed to act.
@pragma('vm:entry-point')
Future<void> handleBackgroundPush(RemoteMessage message) async {
  if (!_isReminderPush(message)) return;

  WidgetsFlutterBinding.ensureInitialized();
  final alarms = ReminderAlarms.instance;
  await alarms.init();
  await alarms.handlePush(message.data);
}
