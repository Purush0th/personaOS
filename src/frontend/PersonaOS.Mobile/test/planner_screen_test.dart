import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/date_utils.dart';
import 'package:personaos_mobile/planner_calendar.dart';
import 'package:personaos_mobile/screens/planner_event.dart';
import 'package:personaos_mobile/screens/planner_screen.dart';
import 'package:personaos_mobile/screens/planner_views.dart';
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
    return events.where((e) => e.lastDay.compareTo(from) >= 0 && e.date.compareTo(to) <= 0).toList();
  }

  @override
  Future<List<Goal>> getGoals() async => [];

  @override
  Future<void> createEvent({
    required String title,
    required String date,
    String? endDate,
    String? notes,
    String? scheduledTime,
    String? endTime,
    int? notifyBeforeMinutes,
    String? recurrence,
    int? goalId,
    int? taskId,
  }) async =>
      calls.add('create $title $date${endDate == null ? '' : '..$endDate'} $scheduledTime-$endTime notify=$notifyBeforeMinutes repeat=$recurrence');

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

PlannerItem _event(
  int id,
  String title,
  String date, {
  String? start,
  String? end,
  String? rule,
  String status = 'planned',
  String? endDate,
}) =>
    PlannerItem(
      id: id,
      title: title,
      date: date,
      endDate: endDate,
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
  Future<void> pickView(WidgetTester tester, String view) async {
    await tester.tap(find.byKey(const Key('planner-view-menu')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(Key('view-$view')));
    await tester.pumpAndSettle();
  }

  testWidgets('a trip over several days is one bar on top, and each of its days in the schedule', (tester) async {
    final last = localYmd(addDays(DateTime.now(), 2));
    await open(tester, [_event(1, 'Goa trip', today, endDate: last)]);

    await pickView(tester, 'fourDay');
    expect(find.text('Goa trip'), findsOneWidget);
    expect(tester.takeException(), isNull);

    await pickView(tester, 'schedule');
    expect(find.text('Goa trip (Day 1/3)'), findsOneWidget);
    expect(find.text('Goa trip (Day 2/3)'), findsOneWidget);
    expect(find.text('Goa trip (Day 3/3)'), findsOneWidget);
  });

  testWidgets('a long press picks an event up and drops it an hour later', (tester) async {
    final api = await open(tester, [_event(1, 'Call', today, start: '09:00:00', end: '10:00:00')]);

    final finger = await tester.startGesture(tester.getCenter(find.byKey(Key('event-1-$today'))));
    await tester.pump(kLongPressTimeout + const Duration(milliseconds: 50));
    await finger.moveBy(const Offset(0, hourHeight));
    await tester.pump();
    expect(find.byKey(const Key('drag-ghost')), findsOneWidget);
    expect(find.text('10 – 11am'), findsOneWidget);
    await finger.up();
    await tester.pumpAndSettle();

    expect(api.calls.single, 'update 1 {scheduledTime: 10:00, endTime: 11:00} scope=null occurrence=null');
  });

  testWidgets('held by its bottom edge, an event gets a later end', (tester) async {
    final api = await open(tester, [_event(1, 'Call', today, start: '09:00:00', end: '10:00:00')]);

    final block = tester.getRect(find.byKey(Key('event-1-$today')));
    final finger = await tester.startGesture(Offset(block.center.dx, block.bottom - 4));
    await tester.pump(kLongPressTimeout + const Duration(milliseconds: 50));
    await finger.moveBy(const Offset(0, hourHeight / 2));
    await tester.pump();
    await finger.up();
    await tester.pumpAndSettle();

    expect(api.calls.single, 'update 1 {endTime: 10:30} scope=null occurrence=null');
  });

  testWidgets('dragging one of a repeating event asks which events, and cancelling puts it back', (tester) async {
    final api = await open(tester, [_event(4, 'Orders', today, start: '09:00:00', end: '10:00:00', rule: 'FREQ=WEEKLY')]);

    Future<void> drag() async {
      final finger = await tester.startGesture(tester.getCenter(find.byKey(Key('event-4-$today'))));
      await tester.pump(kLongPressTimeout + const Duration(milliseconds: 50));
      await finger.moveBy(const Offset(0, hourHeight));
      await tester.pump();
      await finger.up();
      await tester.pumpAndSettle();
    }

    await drag();
    expect(find.text('Edit recurring event'), findsOneWidget);
    await tester.tap(find.text('Cancel'));
    await tester.pumpAndSettle();
    expect(api.calls, isEmpty);
    expect(find.text('9 – 10am'), findsOneWidget);

    await drag();
    await tester.tap(find.byKey(const Key('scope-ok')));
    await tester.pumpAndSettle();
    expect(api.calls.single, 'update 4 {scheduledTime: 10:00, endTime: 11:00} scope=this occurrence=$today');
  });

  testWidgets('a bar on top moves along the days', (tester) async {
    final api = await open(tester, [_event(1, 'Trip', today)]);
    await pickView(tester, 'fourDay');

    final columns = tester.getSize(find.byKey(Key('day-head-$today'))).width;
    final finger = await tester.startGesture(tester.getCenter(find.byKey(Key('event-1-$today'))));
    await tester.pump(kLongPressTimeout + const Duration(milliseconds: 50));
    await finger.moveBy(Offset(columns, 0));
    await tester.pump();
    await finger.up();
    await tester.pumpAndSettle();

    expect(api.calls.single, 'update 1 {date: ${localYmd(addDays(DateTime.now(), 1))}} scope=null occurrence=null');
  });

  testWidgets('the editor shows the first and the last day of an event over several days', (tester) async {
    tall(tester);
    final api = _Api([]);
    final trip = _event(1, 'Goa trip', '2026-10-09', endDate: '2026-10-11');
    await tester.pumpWidget(MaterialApp(home: EventEditorPage(api: api, item: trip)));
    await tester.pumpAndSettle();

    expect(find.text('Fri, 9 Oct 2026'), findsOneWidget);
    expect(find.text('Sun, 11 Oct 2026'), findsOneWidget);
  });
}
