import 'dart:convert';
import 'dart:ui';

import 'package:flutter/foundation.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;
import 'package:timezone/timezone.dart' as tz;

/// One reminder, as the phone needs it to ring.
@immutable
class ReminderAlarm {
  const ReminderAlarm({
    required this.reminderId,
    required this.dueAtUtc,
    required this.title,
    required this.message,
  });

  final int reminderId;
  final DateTime dueAtUtc;
  final String title;
  final String message;

  /// Reads a server push. Returns null for anything malformed rather than throwing: a background
  /// isolate that throws simply dies, and the alarm is lost with no one to see why.
  static ReminderAlarm? fromPush(Map<String, dynamic> data) {
    final id = int.tryParse('${data['reminderId']}');
    final due = DateTime.tryParse('${data['dueAtUtc']}');
    if (id == null || due == null) return null;
    return ReminderAlarm(
      reminderId: id,
      dueAtUtc: due.toUtc(),
      title: '${data['title'] ?? 'PersonaOS'}',
      message: '${data['message'] ?? ''}',
    );
  }

  String toPayload() => jsonEncode({
        'reminderId': reminderId,
        'dueAtUtc': dueAtUtc.toIso8601String(),
        'title': title,
        'message': message,
      });

  static ReminderAlarm? fromPayload(String? payload) {
    if (payload == null) return null;
    try {
      return fromPush(jsonDecode(payload) as Map<String, dynamic>);
    } catch (_) {
      return null;
    }
  }
}

/// Push message types. Must match `ReminderPushMessages` on the server.
abstract final class ReminderPushTypes {
  static const scheduled = 'reminder_scheduled';
  static const removed = 'reminder_removed';
  static const due = 'reminder_due';
}

/// Action button ids on a ringing alarm.
abstract final class AlarmActions {
  static const snooze = 'snooze';
  static const dismiss = 'dismiss';
}

/// Rings reminders like an alarm clock.
///
/// A reminder is an exact alarm scheduled on this phone, not a notification that arrives when
/// the server gets round to sending one. The server polls and Android's Doze holds pushes back,
/// so a push at the due time was seconds to minutes late; an alarm-clock alarm fires to the
/// second and needs no network. The server pushes the schedule the moment a reminder changes,
/// and this turns it into an alarm.
///
/// Everything here must also work in a background isolate — a push that wakes the app, or a
/// Snooze tapped on the lock screen, runs with no UI and no signed-in session. So state that
/// has to survive between those runs lives in shared preferences, not in memory.
class ReminderAlarms {
  ReminderAlarms._();

  static final ReminderAlarms instance = ReminderAlarms._();

  static const _channelId = 'alarms';
  static const _channelName = 'Reminder alarms';

  /// Alarm notification ids sit in their own range, so they can never collide with the ids other
  /// notifications derive from a hash.
  static const _alarmIdBase = 1000000;
  static const _snoozeIdBase = 2000000;

  static const snoozeFor = Duration(minutes: 10);

  /// Remembers which reminders have an alarm on this phone, and when they are due.
  static const _scheduledKey = 'reminder_alarms.scheduled';

  final _plugin = FlutterLocalNotificationsPlugin();
  bool _initialised = false;

  /// Called when an alarm is opened — tapped, or launched full screen over the lock screen.
  void Function(ReminderAlarm alarm)? onOpen;

  Future<void> init() async {
    if (_initialised) return;
    _initialised = true;

    tz_data.initializeTimeZones();

    await _plugin.initialize(
      settings: const InitializationSettings(
        android: AndroidInitializationSettings('@mipmap/ic_launcher'),
      ),
      onDidReceiveNotificationResponse: _onResponse,
      onDidReceiveBackgroundNotificationResponse: alarmResponseInBackground,
    );

    await _plugin
        .resolvePlatformSpecificImplementation<AndroidFlutterLocalNotificationsPlugin>()
        ?.createNotificationChannel(const AndroidNotificationChannel(
          _channelId,
          _channelName,
          description: 'Reminders that ring like an alarm clock.',
          importance: Importance.max,
          sound: UriAndroidNotificationSound('content://settings/system/alarm_alert'),
          audioAttributesUsage: AudioAttributesUsage.alarm,
        ));
  }

  /// Asks for the two permissions an alarm needs. Safe to call on every sign-in: Android only
  /// prompts when a permission is not already granted.
  Future<void> requestPermissions() async {
    final android =
        _plugin.resolvePlatformSpecificImplementation<AndroidFlutterLocalNotificationsPlugin>();
    if (android == null) return;

    // Without exact-alarm permission Android silently downgrades the alarm to an inexact one,
    // which can fire many minutes late — the very problem this exists to fix.
    if (await android.canScheduleExactNotifications() != true) {
      await android.requestExactAlarmsPermission();
    }
    // From Android 14 a sideloaded app must be allowed to show full-screen alerts. Without it
    // the alarm still rings, but as a heads-up banner instead of taking over the screen.
    await android.requestFullScreenIntentPermission();
  }

  /// Returns the alarm that launched the app, if one did. Checked once at startup, because an
  /// alarm firing over the lock screen starts the app with no other signal.
  Future<ReminderAlarm?> launchedFrom() async {
    final details = await _plugin.getNotificationAppLaunchDetails();
    if (details?.didNotificationLaunchApp != true) return null;
    return ReminderAlarm.fromPayload(details!.notificationResponse?.payload);
  }

  /// Acts on a data push from the server.
  Future<void> handlePush(Map<String, dynamic> data) async {
    switch (data['type']) {
      case ReminderPushTypes.scheduled:
        final alarm = ReminderAlarm.fromPush(data);
        if (alarm != null) await schedule(alarm);
      case ReminderPushTypes.removed:
        final id = int.tryParse('${data['reminderId']}');
        if (id != null) await cancel(id);
      case ReminderPushTypes.due:
        final alarm = ReminderAlarm.fromPush(data);
        if (alarm != null) await _ringIfNotAlreadyRung(alarm);
    }
  }

  /// Schedules (or moves) the alarm for a reminder. A reminder already past due is not scheduled
  /// here — the server's due-time push covers it.
  Future<void> schedule(ReminderAlarm alarm) async {
    if (!alarm.dueAtUtc.isAfter(DateTime.now().toUtc())) return;

    await _plugin.zonedSchedule(
      id: _alarmIdBase + alarm.reminderId,
      // UTC in, UTC out: the server sends the instant, so no device time zone is involved and a
      // phone travelling across zones still rings at the right moment.
      scheduledDate: tz.TZDateTime.from(alarm.dueAtUtc, tz.UTC),
      title: alarm.title,
      body: alarm.message,
      payload: alarm.toPayload(),
      notificationDetails: _alarmDetails(),
      // alarmClock, not exact: it is the only mode Doze never defers, and it shows the alarm
      // icon in the status bar so the user can see a reminder is set.
      androidScheduleMode: AndroidScheduleMode.alarmClock,
    );

    final scheduled = await _readScheduled();
    scheduled['${alarm.reminderId}'] = alarm.dueAtUtc.toIso8601String();
    await _writeScheduled(scheduled);
  }

  Future<void> cancel(int reminderId) async {
    await _plugin.cancel(id: _alarmIdBase + reminderId);
    await _plugin.cancel(id: _snoozeIdBase + reminderId);

    final scheduled = await _readScheduled();
    if (scheduled.remove('$reminderId') != null) await _writeScheduled(scheduled);
  }

  /// Makes this phone's alarms match the server's pending reminders exactly: schedules what is
  /// missing, moves what changed, and removes alarms for reminders that no longer exist. This is
  /// what recovers every alarm after a reinstall, or after a push that never arrived.
  Future<void> syncWith(List<ReminderAlarm> pending) async {
    final wanted = {for (final alarm in pending) alarm.reminderId: alarm};

    for (final id in (await _readScheduled()).keys.map(int.parse).toList()) {
      if (!wanted.containsKey(id)) await cancel(id);
    }
    for (final alarm in wanted.values) {
      await schedule(alarm);
    }
  }

  /// Turns every alarm off — used when reminders are disabled on the server.
  Future<void> cancelAll() async {
    for (final id in (await _readScheduled()).keys.map(int.parse).toList()) {
      await cancel(id);
    }
  }

  Future<void> snooze(ReminderAlarm alarm) async {
    await _plugin.cancel(id: _alarmIdBase + alarm.reminderId);
    await _plugin.zonedSchedule(
      id: _snoozeIdBase + alarm.reminderId,
      scheduledDate: tz.TZDateTime.now(tz.UTC).add(snoozeFor),
      title: alarm.title,
      body: alarm.message,
      payload: alarm.toPayload(),
      notificationDetails: _alarmDetails(),
      androidScheduleMode: AndroidScheduleMode.alarmClock,
    );
  }

  Future<void> dismiss(ReminderAlarm alarm) async {
    await _plugin.cancel(id: _alarmIdBase + alarm.reminderId);
    await _plugin.cancel(id: _snoozeIdBase + alarm.reminderId);
  }

  /// The server's due-time push is a fallback. If this phone had the alarm scheduled, it has
  /// already rung — raising it again would alert twice for every reminder.
  Future<void> _ringIfNotAlreadyRung(ReminderAlarm alarm) async {
    final scheduled = await _readScheduled();
    final localDue = DateTime.tryParse(scheduled['${alarm.reminderId}'] ?? '');
    if (localDue != null && !localDue.isAfter(DateTime.now().toUtc().add(const Duration(minutes: 1)))) {
      return;
    }
    await _plugin.show(
      id: _alarmIdBase + alarm.reminderId,
      title: alarm.title,
      body: alarm.message,
      payload: alarm.toPayload(),
      notificationDetails: _alarmDetails(),
    );
  }

  NotificationDetails _alarmDetails() => NotificationDetails(
        android: AndroidNotificationDetails(
          _channelId,
          _channelName,
          importance: Importance.max,
          priority: Priority.max,
          category: AndroidNotificationCategory.alarm,
          fullScreenIntent: true,
          audioAttributesUsage: AudioAttributesUsage.alarm,
          sound: const UriAndroidNotificationSound('content://settings/system/alarm_alert'),
          // FLAG_INSISTENT: the sound loops until the alarm is dealt with, like an alarm clock,
          // instead of playing once and going quiet.
          additionalFlags: Int32List.fromList(<int>[4]),
          ongoing: true,
          autoCancel: false,
          visibility: NotificationVisibility.public,
          // A safety stop, so an alarm nobody is near cannot ring for hours.
          timeoutAfter: const Duration(minutes: 10).inMilliseconds,
          actions: const <AndroidNotificationAction>[
            AndroidNotificationAction(AlarmActions.snooze, 'Snooze 10 min'),
            AndroidNotificationAction(AlarmActions.dismiss, 'Dismiss'),
          ],
        ),
      );

  void _onResponse(NotificationResponse response) {
    final alarm = ReminderAlarm.fromPayload(response.payload);
    if (alarm == null) return;

    switch (response.actionId) {
      case AlarmActions.snooze:
        snooze(alarm);
      case AlarmActions.dismiss:
        dismiss(alarm);
      default:
        onOpen?.call(alarm);
    }
  }

  Future<Map<String, String>> _readScheduled() async {
    final prefs = await SharedPreferences.getInstance();
    // Another isolate may have changed it since this one last looked.
    await prefs.reload();
    final raw = prefs.getString(_scheduledKey);
    if (raw == null) return <String, String>{};
    return (jsonDecode(raw) as Map<String, dynamic>).map((k, v) => MapEntry(k, '$v'));
  }

  Future<void> _writeScheduled(Map<String, String> scheduled) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_scheduledKey, jsonEncode(scheduled));
  }
}

/// Snooze or Dismiss tapped while the app is not running. Runs in its own isolate, so it sets up
/// everything it touches from scratch.
@pragma('vm:entry-point')
Future<void> alarmResponseInBackground(NotificationResponse response) async {
  WidgetsFlutterBinding.ensureInitialized();
  DartPluginRegistrant.ensureInitialized();

  final alarms = ReminderAlarms.instance;
  await alarms.init();

  final alarm = ReminderAlarm.fromPayload(response.payload);
  if (alarm == null) return;

  if (response.actionId == AlarmActions.snooze) {
    await alarms.snooze(alarm);
  } else {
    await alarms.dismiss(alarm);
  }
}
