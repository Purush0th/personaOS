import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/screens/goal_forms.dart';
import 'package:personaos_mobile/screens/goals_screen.dart';

Map<String, dynamic> _goal(int id, String type, String slot,
        {int? parentId, List<Map<String, dynamic>> tasks = const [], List<Map<String, dynamic>> children = const [],
        String? completeProblem, String start = '2026-10-01', String end = '2026-12-31'}) =>
    {
      'id': id,
      'key': 'GOAL-$id',
      'title': 'Goal $id',
      'periodType': type,
      'slot': slot,
      'periodStart': start,
      'periodEnd': end,
      'parentId': parentId,
      'parentKey': parentId == null ? null : 'GOAL-$parentId',
      'status': 'active',
      'progress': 0,
      'effectiveProgress': 0,
      'taskCount': tasks.length,
      'doneTaskCount': 0,
      'childCount': children.length,
      'completedChildCount': 0,
      'completeProblem': completeProblem,
      'tasks': tasks,
      'children': children,
    };

Map<String, dynamic> _child(Map<String, dynamic> g) => {
      'id': g['id'],
      'key': g['key'],
      'title': g['title'],
      'periodType': g['periodType'],
      'slot': g['slot'],
      'status': g['status'],
      'effectiveProgress': 0,
    };

/// A year > quarter > month, and a standalone month listed first.
final _month = _goal(3, 'month', 'Oct 2026', parentId: 2, end: '2026-10-31', tasks: [
  {'id': 9, 'key': 'TASK-9', 'title': 'Run', 'points': null, 'column': 'backlog', 'sprintNumber': null},
], completeProblem: 'GOAL-3 has open tasks: TASK-9.');
final _quarter = _goal(2, 'quarter', 'Q4 2026', parentId: 1, children: [_child(_month)]);
final _year = _goal(1, 'year', '2026', start: '2026-09-27', children: [_child(_quarter)]);
final _standalone = _goal(4, 'month', 'Nov 2026', start: '2026-11-01', end: '2026-11-30');

class _FakeApi extends PersonaOsApi {
  _FakeApi() : super(serverUrl: 'http://fake');

  final deleted = <({int id, String action, Map<String, String?>? reassign})>[];

  @override
  Future<List<Goal>> getGoals() async => [_standalone, _year, _quarter, _month].map(Goal.fromJson).toList();

  @override
  Future<void> deleteGoal(int id, {String taskAction = 'keep', Map<String, String?>? reassign}) async =>
      deleted.add((id: id, action: taskAction, reassign: reassign));
}

void main() {
  final today = DateTime(2026, 9, 27);

  Future<_FakeApi> open(WidgetTester tester) async {
    tester.view.physicalSize = const Size(1080, 2400);
    tester.view.devicePixelRatio = 2.5;
    addTearDown(tester.view.reset);
    final api = _FakeApi();
    await tester.pumpWidget(MaterialApp(home: GoalsScreen(api: api, today: today)));
    await tester.pumpAndSettle();
    return api;
  }

  testWidgets('shows each child goal under its parent, one step in per level', (tester) async {
    await open(tester);

    // The card itself moves in; the tile around it spans the list.
    Finder card(String key) => find.descendant(of: find.byKey(Key('goal-$key')), matching: find.byType(Card));
    final x = [for (final key in ['GOAL-4', 'GOAL-1', 'GOAL-2', 'GOAL-3']) tester.getTopLeft(card(key)).dx];
    final y = [for (final key in ['GOAL-4', 'GOAL-1', 'GOAL-2', 'GOAL-3']) tester.getTopLeft(find.byKey(Key('goal-$key'))).dy];

    expect(y, orderedEquals([...y]..sort()));
    expect(x[0], x[1]);
    expect(x[2], greaterThan(x[1]));
    expect(x[3], greaterThan(x[2]));
  });

  testWidgets('offers tasks only on monthly goals and the next level on the others', (tester) async {
    await open(tester);

    Finder inCard(String key, String text) => find.descendant(of: find.byKey(Key('goal-$key')), matching: find.text(text));
    expect(inCard('GOAL-3', 'Create task'), findsOneWidget);
    expect(inCard('GOAL-1', 'Add quarterly goal'), findsOneWidget);
    expect(inCard('GOAL-1', 'Create task'), findsNothing);
    expect(inCard('GOAL-2', 'Create task'), findsNothing);
  });

  testWidgets('says why a goal cannot be completed or deleted yet', (tester) async {
    await open(tester);

    await tester.tap(find.byKey(const Key('goal-menu-GOAL-3')));
    await tester.pumpAndSettle();
    expect(find.text('GOAL-3 has open tasks: TASK-9.'), findsOneWidget);
    await tester.tapAt(const Offset(10, 10));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('goal-menu-GOAL-1')));
    await tester.pumpAndSettle();
    expect(find.text('Delete its quarterly goals first.'), findsOneWidget);
    expect(find.text('Update progress'), findsNothing);
  });

  testWidgets('delete moves each task to the goal picked for it', (tester) async {
    final api = await open(tester);

    await tester.tap(find.byKey(const Key('goal-menu-GOAL-3')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Delete'));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('delete-task-action')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Move to other goals').last);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('reassign-TASK-9')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('GOAL-4 Goal 4 · Nov 2026').last);
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'Delete'));
    await tester.pumpAndSettle();

    expect(api.deleted, hasLength(1));
    expect(api.deleted.single.id, 3);
    expect(api.deleted.single.action, 'reassign');
    expect(api.deleted.single.reassign, {'TASK-9': 'GOAL-4'});
  });

  testWidgets('gives each top-level goal and everything under it its own swim lane', (tester) async {
    await open(tester);

    List<String> inLane(String key) => [
          for (final g in ['GOAL-1', 'GOAL-2', 'GOAL-3', 'GOAL-4'])
            if (find.descendant(of: find.byKey(Key('lane-$key')), matching: find.byKey(Key('goal-$g'))).evaluate().isNotEmpty) g,
        ];
    expect(inLane('GOAL-4'), ['GOAL-4']);
    expect(inLane('GOAL-1'), ['GOAL-1', 'GOAL-2', 'GOAL-3']);
  });

  testWidgets('folds child goals away and back, one goal or all', (tester) async {
    await open(tester);

    await tester.tap(find.byKey(const Key('fold-GOAL-2')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('goal-GOAL-3')), findsNothing);

    await tester.tap(find.byKey(const Key('goals-fold-all'))); // Expand all
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('goal-GOAL-3')), findsOneWidget);
    expect(find.byKey(const Key('fold-GOAL-4')), findsNothing); // nothing under it, nothing to fold
  });

  testWidgets('has a Timeline tab beside Goals', (tester) async {
    await open(tester);

    await tester.tap(find.widgetWithText(Tab, 'Timeline'));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('timeline-year')), findsOneWidget);
  });

  test('slots that are over, too short or taken cannot be picked', () {
    final quarter = Goal.fromJson(_quarter);

    String describe(List<SlotOption> options) => options.map((o) => '${o.label}:${o.blocked ?? ''}').join(' ');
    expect(describe(slotOptions('quarter', 2026, today)), 'Q1:over Q2:over Q3:too short Q4:');
    expect(describe(slotOptions('month', 2026, today, parent: quarter)), 'Oct:taken Nov: Dec:');
  });
}
