import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/date_utils.dart';
import 'package:personaos_mobile/planner_calendar.dart';
import 'package:personaos_mobile/screens/planner_event.dart';
import 'package:personaos_mobile/screens/planner_screen.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// A planner server in memory: records the ranges asked for and the changes sent.
class _Api extends PersonaOsApi {
  _Api(this.events) : super(serverUrl: 'http://test');

  final List<PlannerItem> events;
  final ranges = <String>[];
  final calls = <String>[];

  @override
  Future<List<PlannerItem>> getPlannerRange(String from, String to) async {
    ranges.add('$from..$to');
    return events.where((e) => e.date.compareTo(from) >= 0 && e.date.compareTo(to) <= 0).toList();
  }

  @override
  Future<List<Goal>> getGoals() async => [];

  @override
  Future<void> createEvent({
    required String title,
    required String date,
    String? notes,
    String? scheduledTime,
    String? endTime,
    int? notifyBeforeMinutes,
    String? recurrence,
    int? goalId,
    int? taskId,
  }) async =>
      calls.add('create $title $date $scheduledTime-$endTime notify=$notifyBeforeMinutes repeat=$recurrence');

  @override
  Future<void> updateEvent(int id, Map<String, Object?> change, {EditScope? scope, String? occurrence}) async =>
      calls.add('update $id $change scope=${scope?.wire} occurrence=$occurrence');

  @override
  Future<void> setPlannerStatus(int id, String status, {String? occurrence}) async =>
      calls.add('status $id $status occurrence=$occurrence');

  @override
  Future<void> deletePlannerItem(int id, {EditScope? scope, String? occurrence}) async =>
      calls.add('delete $id scope=${scope?.wire} occurrence=$occurrence');
}

PlannerItem _event(int id, String title, String date, {String? start, String? end, String? rule, String status = 'planned'}) =>
    PlannerItem(
      id: id,
      title: title,
      date: date,
      scheduledTime: start,
      endTime: end,
      status: status,
      goalTitle: null,
      notifyBeforeMinutes: 30,
      recurrence: rule,
      recurrenceText: rule == null ? null : 'Weekly on Saturday',
    );

void main() {
  final today = localYmd(DateTime.now());

  setUp(() => SharedPreferences.setMockInitialValues({}));

  void tall(WidgetTester tester) {
    tester.view.physicalSize = const Size(430, 932);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
  }

  Future<_Api> open(WidgetTester tester, List<PlannerItem> events) async {
    tall(tester);
    final api = _Api(events);
    await tester.pumpWidget(MaterialApp(home: PlannerScreen(api: api)));
    await tester.pumpAndSettle();
    return api;
  }

  testWidgets('opens on today in the day view with the current time marked', (tester) async {
    final api = await open(tester, [_event(1, 'Orders Collection', today, start: '08:30:00', end: '09:30:00')]);

    expect(api.ranges.single, '$today..$today');
    expect(find.text('Orders Collection'), findsOneWidget);
    expect(find.byKey(const Key('now-line')), findsOneWidget);
  });

  testWidgets('every view fits a small phone with short, overlapping and all-day events', (tester) async {
    tester.view.physicalSize = const Size(360, 740);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final monday = localYmd(startOfWeek(DateTime.now()));
    final api = _Api([
      _event(1, 'Stand-up with the whole team', today, start: '09:45:00', end: '10:00:00'),
      _event(2, 'Send Mail', today, start: '12:00:00', end: '15:30:00'),
      _event(3, 'Review PR', today, start: '12:30:00', end: '13:30:00'),
      _event(4, 'Durga Puja', today),
      _event(5, 'Gym', monday, start: '07:00:00', end: '08:00:00'),
    ]);
    await tester.pumpWidget(MaterialApp(home: PlannerScreen(api: api)));
    await tester.pumpAndSettle();

    for (final view in ['day', 'fourDay', 'week', 'month', 'year', 'schedule']) {
      await tester.tap(find.byKey(const Key('planner-view-menu')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(Key('view-$view')));
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull, reason: view);
    }
  });

  testWidgets('switches views from the menu and remembers the choice', (tester) async {
    final api = await open(tester, []);

    await tester.tap(find.byKey(const Key('planner-view-menu')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('view-month')));
    await tester.pumpAndSettle();

    final month = viewRange(CalendarView.month, DateTime.now());
    expect(api.ranges.last, '${localYmd(month.from)}..${localYmd(month.to)}');
    final prefs = await SharedPreferences.getInstance();
    expect(prefs.getString('planner.view'), 'month');
  });

  testWidgets('hides completed events from the view menu toggle', (tester) async {
    await open(tester, [_event(1, 'Send Mail', today, start: '12:00:00', end: '15:30:00', status: 'done')]);
    expect(find.text('Send Mail'), findsOneWidget);

    await tester.tap(find.byKey(const Key('planner-view-menu')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('opt-completed')));
    await tester.pumpAndSettle();

    expect(find.text('Send Mail'), findsNothing);
  });

  testWidgets('marking one day of a repeating event done sends that day', (tester) async {
    final api = await open(tester, [_event(4, 'Orders Collection', today, start: '08:30:00', end: '09:30:00', rule: 'FREQ=WEEKLY')]);

    await tester.tap(find.text('Orders Collection'));
    await tester.pumpAndSettle();
    expect(find.text('Weekly on Saturday'), findsOneWidget);
    await tester.tap(find.byKey(const Key('event-done')));
    await tester.pumpAndSettle();

    expect(api.calls.single, 'status 4 done occurrence=$today');
  });

  testWidgets('deleting a repeating event asks which events', (tester) async {
    final api = await open(tester, [_event(4, 'Orders Collection', today, start: '08:30:00', end: '09:30:00', rule: 'FREQ=WEEKLY')]);

    await tester.tap(find.text('Orders Collection'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('event-delete')));
    await tester.pumpAndSettle();
    expect(find.text('Delete recurring event'), findsOneWidget);
    await tester.tap(find.text('This and following events'));
    await tester.tap(find.byKey(const Key('scope-ok')));
    await tester.pumpAndSettle();

    expect(api.calls.single, 'delete 4 scope=following occurrence=$today');
  });

  testWidgets('a new timed event starts with a 30-minute notification and saves it', (tester) async {
    final api = await open(tester, []);

    await tester.tap(find.byKey(const Key('planner-create')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('event-title')), 'Dentist');
    await tester.pumpAndSettle();
    expect(find.text('30 minutes before'), findsOneWidget);
    await tester.tap(find.byKey(const Key('event-save')));
    await tester.pumpAndSettle();

    expect(api.calls.single, 'create Dentist $today 09:00-10:00 notify=30 repeat=null');
  });

  testWidgets('the custom repeat editor fits a small phone and builds the rule', (tester) async {
    tester.view.physicalSize = const Size(360, 740);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final api = _Api([]);
    await tester.pumpWidget(MaterialApp(home: EventEditorPage(api: api, date: DateTime(2026, 10, 3), start: '08:30')));
    await tester.pumpAndSettle();

    await tester.enterText(find.byKey(const Key('event-title')), 'Orders');
    await tester.tap(find.byKey(const Key('event-repeat')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Custom…'));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('custom-repeat')), findsOneWidget);
    expect(tester.takeException(), isNull);
    await tester.tap(find.text('After '));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('custom-repeat-done')));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);

    await tester.tap(find.byKey(const Key('event-save')));
    await tester.pumpAndSettle();
    expect(api.calls.single, 'create Orders 2026-10-03 08:30-09:30 notify=30 repeat=FREQ=WEEKLY;BYDAY=SA;COUNT=10');
  });

  testWidgets('a custom notification past four weeks says when it would be and cannot be saved', (tester) async {
    tall(tester);
    await tester.pumpWidget(MaterialApp(
      home: Scaffold(
        body: Builder(
          builder: (context) => TextButton(
            onPressed: () => showDialog<int>(
              context: context,
              builder: (_) => CustomNotifyDialog(day: DateTime(2026, 10, 6), start: '09:00', initialMinutes: 4 * 10080),
            ),
            child: const Text('open'),
          ),
        ),
      ),
    ));
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();

    expect(find.textContaining('You will be notified Tue 8 Sep, 9:00 am'), findsOneWidget);
    await tester.tap(find.byKey(const Key('notify-plus')));
    await tester.pumpAndSettle();

    expect(find.textContaining('Too far ahead: 5 weeks before would be Tue 1 Sep, 9:00 am'), findsOneWidget);
    expect(tester.widget<FilledButton>(find.byKey(const Key('notify-done'))).onPressed, isNull);

    await tester.tap(find.byKey(const Key('notify-minus')));
    await tester.pumpAndSettle();
    expect(tester.widget<FilledButton>(find.byKey(const Key('notify-done'))).onPressed, isNotNull);
  });
}
