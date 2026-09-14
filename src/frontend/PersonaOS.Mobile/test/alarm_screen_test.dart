import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/reminder_alarms.dart';
import 'package:personaos_mobile/screens/alarm_screen.dart';

/// The alarm screen blocks Back so a ringing alarm cannot be walked away from. That same block
/// once stopped Dismiss and Snooze from closing it: they called maybePop(), which honours the
/// block, so the sound stopped but the screen stayed up. These open it the way the app does — as
/// a route over another screen — and require both buttons to actually close it.
void main() {
  final alarm = ReminderAlarm(
    reminderId: 1,
    dueAtUtc: DateTime.utc(2030, 1, 1, 9),
    title: 'Friday',
    message: 'Take your medicine',
  );

  Future<List<String>> openAlarm(
    WidgetTester tester, {
    bool actionsFail = false,
  }) async {
    final calls = <String>[];
    Future<void> record(String name) async {
      calls.add(name);
      if (actionsFail) throw StateError('notification plugin unavailable');
    }

    await tester.pumpWidget(MaterialApp(
      home: Builder(
        builder: (context) => Scaffold(
          body: TextButton(
            onPressed: () => Navigator.of(context).push(MaterialPageRoute<void>(
              fullscreenDialog: true,
              builder: (_) => AlarmScreen(
                alarm: alarm,
                onDismiss: (_) => record('dismiss'),
                onSnooze: (_) => record('snooze'),
              ),
            )),
            child: const Text('home'),
          ),
        ),
      ),
    ));
    await tester.tap(find.text('home'));
    await tester.pumpAndSettle();
    expect(find.text('Take your medicine'), findsOneWidget);
    return calls;
  }

  for (final (button, action) in [('Dismiss', 'dismiss'), ('Snooze 10 min', 'snooze')]) {
    testWidgets('$button runs its action and closes the alarm screen', (tester) async {
      final calls = await openAlarm(tester);

      await tester.tap(find.text(button));
      await tester.pumpAndSettle();

      expect(calls, [action]);
      expect(find.byType(AlarmScreen), findsNothing);
      expect(find.text('home'), findsOneWidget);
    });

    testWidgets('$button still closes the screen when its action fails', (tester) async {
      // A stuck alarm screen is worse than a missed cancel, so a failing action must not strand
      // the user on it.
      await openAlarm(tester, actionsFail: true);

      await tester.tap(find.text(button));
      await tester.pumpAndSettle();

      expect(find.byType(AlarmScreen), findsNothing);
    });
  }

  testWidgets('Back does not close a ringing alarm', (tester) async {
    await openAlarm(tester);

    await tester.binding.handlePopRoute();
    await tester.pumpAndSettle();

    expect(find.byType(AlarmScreen), findsOneWidget);
  });
}
