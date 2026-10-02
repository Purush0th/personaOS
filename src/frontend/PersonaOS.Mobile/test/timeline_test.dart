import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/screens/timeline_screen.dart';

Map<String, dynamic> _goal(int id, String type, String slot, String start, String end, {int? parentId}) => {
      'id': id,
      'key': 'GOAL-$id',
      'title': 'Goal $id',
      'periodType': type,
      'slot': slot,
      'periodStart': start,
      'periodEnd': end,
      'parentId': parentId,
      'status': 'active',
      'progress': 0,
      'effectiveProgress': 40,
      'taskCount': 0,
      'doneTaskCount': 0,
      'tasks': const <Map<String, dynamic>>[],
    };

class _Api extends PersonaOsApi {
  _Api() : super(serverUrl: 'http://fake');

  @override
  Future<List<Goal>> getGoals() async => [
        _goal(1, 'year', '2026', '2026-01-01', '2026-12-31'),
        _goal(2, 'quarter', 'Q4 2026', '2026-10-01', '2026-12-31', parentId: 1),
        _goal(3, 'month', 'Oct 2026', '2026-10-01', '2026-10-31', parentId: 2),
        _goal(4, 'month', 'Nov 2026', '2026-11-01', '2026-11-30'),
        _goal(5, 'year', '2027', '2027-01-01', '2027-12-31'),
      ].map(Goal.fromJson).toList();
}

void main() {
  Future<void> open(WidgetTester tester) async {
    tester.view.physicalSize = const Size(1080, 2340);
    tester.view.devicePixelRatio = 2.7;
    addTearDown(tester.view.reset);
    await tester.pumpWidget(MaterialApp(
      home: Scaffold(body: TimelineView(goals: _Api().getGoals(), today: DateTime(2026, 9, 27))),
    ));
    await tester.pumpAndSettle();
  }

  List<String> barKeys(WidgetTester tester) => tester
      .widgetList<GestureDetector>(find.byWidgetPredicate((w) => w is GestureDetector && '${w.key}'.contains('bar-')))
      .map((w) => '${w.key}'.replaceAll(RegExp(r"[\[\]<>']"), '').replaceFirst('bar-', ''))
      .toList();

  testWidgets('shows the current year, parents before their children', (tester) async {
    await open(tester);

    expect(barKeys(tester), ['GOAL-1', 'GOAL-2', 'GOAL-3', 'GOAL-4']);
  });

  testWidgets('places each bar on the month axis', (tester) async {
    await open(tester);

    final scroll = tester.getTopLeft(find.byKey(const Key('timeline-scroll')));
    final offset = tester.widget<SingleChildScrollView>(find.byKey(const Key('timeline-scroll'))).controller!.offset;
    double left(String key) => tester.getTopLeft(find.byKey(Key('bar-$key'))).dx - scroll.dx + offset;
    double width(String key) => tester.getSize(find.byKey(Key('bar-$key'))).width;

    expect(left('GOAL-1'), closeTo(0, 0.01));
    expect(width('GOAL-1'), closeTo(monthWidth * 12, 0.01));
    expect(left('GOAL-2'), closeTo(monthWidth * 9, 0.01)); // 1 October
    expect(width('GOAL-3'), closeTo(monthWidth, 0.01));
  });

  testWidgets('a standalone goal starts its own lane, apart from the one above', (tester) async {
    await open(tester);

    final month = tester.getRect(find.byKey(const Key('bar-GOAL-3'))); // last row of GOAL-1's lane
    final standalone = tester.getRect(find.byKey(const Key('bar-GOAL-4')));
    // One row further down, plus the gap between lanes and each lane's padding.
    expect(standalone.center.dy - month.center.dy, greaterThan(40 + 10));
  });

  testWidgets('opens a month before today, scrolled sideways', (tester) async {
    await open(tester);

    final offset = tester.widget<SingleChildScrollView>(find.byKey(const Key('timeline-scroll'))).controller!.offset;
    expect(offset, greaterThan(0));
  });

  testWidgets('folds a goal\'s children away and back', (tester) async {
    await open(tester);

    await tester.tap(find.byKey(const Key('fold-GOAL-1')));
    await tester.pumpAndSettle();
    expect(barKeys(tester), ['GOAL-1', 'GOAL-4']);

    await tester.tap(find.byKey(const Key('fold-GOAL-1')));
    await tester.pumpAndSettle();
    expect(barKeys(tester), hasLength(4));
  });

  testWidgets('switches year, and a bar opens its goal', (tester) async {
    await open(tester);

    await tester.tap(find.byKey(const Key('timeline-year')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('2027').last);
    await tester.pumpAndSettle();
    expect(barKeys(tester), ['GOAL-5']);

    // The year-long bar is wider than the screen; tap its visible start.
    await tester.tapAt(tester.getTopLeft(find.byKey(const Key('bar-GOAL-5'))) + const Offset(12, 6));
    await tester.pumpAndSettle();
    expect(find.text('GOAL-5 · Yearly · 2027'), findsOneWidget);
  });

  // Reported from a tablet: the year stopped at a fixed width and left the rest of the screen empty.
  for (final (name, size) in [('tablet landscape', const Size(1280, 800)), ('tablet portrait', const Size(800, 1280))]) {
    testWidgets('on a $name the year fills the width beside the labels', (tester) async {
      tester.view.physicalSize = size;
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
      await tester.pumpWidget(MaterialApp(
        home: Scaffold(body: TimelineView(goals: _Api().getGoals(), today: DateTime(2026, 9, 27))),
      ));
      await tester.pumpAndSettle();

      final track = tester.getRect(find.byKey(const Key('timeline-scroll')));
      final year = tester.getSize(find.byKey(const Key('bar-GOAL-1')));
      expect(track.right, closeTo(size.width - 8, 1));
      expect(year.width, closeTo(track.width, 1)); // GOAL-1 runs the whole year: no sideways scroll
      expect(tester.takeException(), isNull);
    });
  }
}
