import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/reminder_alarms.dart';

void main() {
  group('ReminderAlarm.fromPush', () {
    test('reads the payload the server sends', () {
      // Mirrors ReminderPushMessages on the server. FCM delivers every data value as a string.
      final alarm = ReminderAlarm.fromPush({
        'type': 'reminder_scheduled',
        'reminderId': '42',
        'dueAtUtc': '2030-03-04T05:06:07Z',
        'title': 'Friday',
        'message': 'Call mum',
      });

      expect(alarm, isNotNull);
      expect(alarm!.reminderId, 42);
      expect(alarm.dueAtUtc, DateTime.utc(2030, 3, 4, 5, 6, 7));
      expect(alarm.dueAtUtc.isUtc, isTrue);
      expect(alarm.title, 'Friday');
      expect(alarm.message, 'Call mum');
    });

    test('rejects a malformed payload instead of throwing', () {
      // This runs in a background isolate woken by a push. An exception there kills the isolate
      // silently and the reminder never rings — so bad input must come back as null.
      expect(ReminderAlarm.fromPush({'reminderId': 'not-a-number', 'dueAtUtc': '2030-01-01T00:00:00Z'}), isNull);
      expect(ReminderAlarm.fromPush({'reminderId': '1', 'dueAtUtc': 'yesterday'}), isNull);
      expect(ReminderAlarm.fromPush(const {}), isNull);
    });

    test('falls back to the brand name when the push has no title', () {
      final alarm = ReminderAlarm.fromPush({'reminderId': '1', 'dueAtUtc': '2030-01-01T00:00:00Z'});
      expect(alarm!.title, 'PersonaOS');
    });
  });

  group('ReminderAlarm payload', () {
    test('survives the round trip through a notification', () {
      // The payload is all a Snooze tapped on the lock screen has to go on: it rebuilds the
      // alarm from this string in a fresh isolate with no other state.
      final original = ReminderAlarm(
        reminderId: 7,
        dueAtUtc: DateTime.utc(2031, 1, 1, 9),
        title: 'Friday',
        message: 'Stretch',
      );

      final restored = ReminderAlarm.fromPayload(original.toPayload());

      expect(restored!.reminderId, 7);
      expect(restored.dueAtUtc, DateTime.utc(2031, 1, 1, 9));
      expect(restored.message, 'Stretch');
    });

    test('an unreadable payload is ignored', () {
      expect(ReminderAlarm.fromPayload(null), isNull);
      expect(ReminderAlarm.fromPayload('not json'), isNull);
    });
  });
}
