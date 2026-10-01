import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/main.dart';
import 'package:personaos_mobile/screens/board_screen.dart';
import 'package:personaos_mobile/screens/sprint_page.dart';

Map<String, dynamic> _task(int n, String column, String? sprintKey, {int? points}) => {
      'id': n,
      'key': 'TASK-$n',
      'title': 'Task $n',
      'points': points,
      'column': column,
      'sprintKey': sprintKey,
      'carryOverCount': 0,
      'addedMidSprint': false,
    };

Map<String, dynamic> _sprint(int n, String status, {int tasks = 0, int done = 0}) => {
      'id': n,
      'key': 'SPRINT-$n',
      'number': n,
      'name': status == 'active' ? 'Week two' : null,
      'status': status,
      'startsAtUtc': '2026-09-${10 + n}T14:30:00',
      'endsAtUtc': '2026-09-${16 + n}T12:30:00',
      'committedPoints': status == 'planned' ? null : 8,
      'addedPoints': 2,
      'removedPoints': 0,
      'completedPoints': 3,
      'totalPoints': 8,
      'taskCount': tasks,
      'doneTaskCount': done,
    };

/// The plan has a running sprint, a planned one and a backlog; sprint calls are recorded.
class TabsApi extends PersonaOsApi {
  TabsApi() : super(serverUrl: 'http://fake');

  final calls = <String>[];

  @override
  Future<BoardView> getBoard() async => BoardView.fromJson({
        'sprint': _sprint(2, 'active', tasks: 2, done: 1),
        'velocity': 7.5,
        'wipLimit': 3,
        'todo': [_task(2, 'todo', 'SPRINT-2', points: 5)],
        'inProgress': <Map<String, dynamic>>[],
        'done': [_task(3, 'done', 'SPRINT-2', points: 3)],
      });

  @override
  Future<PlanView> getPlan() async => PlanView.fromJson({
        'sprints': [
          {'sprint': _sprint(2, 'active', tasks: 2, done: 1), 'tasks': [_task(2, 'todo', 'SPRINT-2', points: 5)]},
          {'sprint': _sprint(3, 'planned'), 'tasks': [_task(4, 'todo', 'SPRINT-3', points: 2)]},
        ],
        'backlog': [_task(1, 'backlog', null)],
      });

  @override
  Future<List<Goal>> getGoals({bool includeDropped = false}) async => [];

  @override
  Future<SprintReport> getSprintReport({int count = 104}) async => SprintReport.fromJson({
        'velocity': 7.5,
        'sprints': [_sprint(2, 'active', tasks: 2, done: 1), _sprint(1, 'closed', tasks: 3, done: 3)],
      });

  @override
  Future<SprintDetail> getSprintDetail(String key) async {
    calls.add('detail $key');
    return SprintDetail.fromJson({
      'sprint': _sprint(int.parse(key.split('-')[1]), key == 'SPRINT-2' ? 'active' : 'closed', tasks: 2, done: 1),
      'tasks': [_task(2, 'todo', key, points: 5), _task(3, 'done', key, points: 3)],
      'burndown': [
        {'date': '2026-09-12', 'remainingPoints': 8, 'completedPoints': 0},
        {'date': '2026-09-13', 'remainingPoints': 5, 'completedPoints': 3},
      ],
      'velocity': 7.5,
    });
  }

  @override
  Future<void> startSprint(String key) async => calls.add('start $key');

  @override
  Future<void> completeSprint(String key, {String? moveUnfinishedToSprintKey, bool toBacklog = false}) async =>
      calls.add('complete $key${toBacklog ? ' to backlog' : ''}');

  @override
  Future<void> createSprint({String? name, String? startsOn}) async => calls.add('create $name');

  @override
  Future<void> deleteSprint(String key) async => calls.add('delete $key');

  @override
  Future<void> moveTask(String key,
          {required String column, String? sprintKey, int? index, bool acknowledgeScopeChange = false}) async =>
      calls.add('move $key $column ${sprintKey ?? '-'}');
}

void main() {
  Future<TabsApi> open(WidgetTester tester, String tab, {Size size = const Size(400, 900)}) async {
    tester.view.physicalSize = size;
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final api = TabsApi();
    await tester.pumpWidget(MaterialApp(theme: personaOsTheme, home: BoardScreen(api: api)));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(Tab, tab));
    await tester.pumpAndSettle();
    return api;
  }

  testWidgets('the board has Sprint, Backlog and Reports tabs, as on the web', (tester) async {
    await open(tester, 'Sprint');

    expect(find.widgetWithText(Tab, 'Sprint'), findsOneWidget);
    expect(find.widgetWithText(Tab, 'Backlog'), findsOneWidget);
    expect(find.widgetWithText(Tab, 'Reports'), findsOneWidget);
    expect(find.text('Task'), findsOneWidget); // the add button belongs to the Sprint tab
  });

  testWidgets('the Sprint tab completes the running sprint from its menu', (tester) async {
    final api = await open(tester, 'Sprint');

    await tester.tap(find.byKey(const Key('sprint-menu')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Complete sprint'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('To SPRINT-3'));
    await tester.pumpAndSettle();

    expect(api.calls, contains('complete SPRINT-2'));
  });

  testWidgets('the sprint menu belongs to the Sprint tab only', (tester) async {
    await open(tester, 'Backlog');

    expect(find.byKey(const Key('sprint-menu')), findsNothing);
  });

  group('Backlog', () {
    testWidgets('lists each sprint and the backlog with their tasks', (tester) async {
      await open(tester, 'Backlog');

      expect(find.byKey(const Key('section-SPRINT-2')), findsOneWidget);
      expect(find.byKey(const Key('section-SPRINT-3')), findsOneWidget);
      expect(find.byKey(const Key('section-backlog')), findsOneWidget);
      expect(find.byKey(const Key('row-TASK-1')), findsOneWidget);
      expect(find.byKey(const Key('row-TASK-4')), findsOneWidget);
      expect(find.text('Create sprint'), findsOneWidget);
    });

    testWidgets('a task moves to another sprint from its menu', (tester) async {
      final api = await open(tester, 'Backlog');

      await tester.tap(find.descendant(of: find.byKey(const Key('row-TASK-1')), matching: find.byTooltip('Move to')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Move to SPRINT-3').last);
      await tester.pumpAndSettle();

      expect(api.calls, contains('move TASK-1 todo SPRINT-3'));
    });

    testWidgets('the running sprint completes, unfinished work to the next sprint', (tester) async {
      final api = await open(tester, 'Backlog');

      await tester.tap(find.descendant(of: find.byKey(const Key('section-SPRINT-2')), matching: find.byTooltip('Sprint actions')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Complete sprint'));
      await tester.pumpAndSettle();
      expect(find.textContaining('1 unfinished task will move on'), findsOneWidget);
      await tester.tap(find.text('To SPRINT-3'));
      await tester.pumpAndSettle();

      expect(api.calls, contains('complete SPRINT-2'));
    });

    testWidgets('a planned sprint cannot start while another runs, and can be deleted', (tester) async {
      final api = await open(tester, 'Backlog');

      await tester.tap(find.descendant(of: find.byKey(const Key('section-SPRINT-3')), matching: find.byTooltip('Sprint actions')));
      await tester.pumpAndSettle();
      expect(find.text('Start sprint'), findsNothing);
      await tester.tap(find.text('Delete'));
      await tester.pumpAndSettle();
      await tester.tap(find.widgetWithText(FilledButton, 'Delete'));
      await tester.pumpAndSettle();

      expect(api.calls, contains('delete SPRINT-3'));
    });

    testWidgets('a sprint opens on its own page, which can complete it', (tester) async {
      final api = await open(tester, 'Backlog');

      await tester.tap(find.descendant(of: find.byKey(const Key('section-SPRINT-2')), matching: find.byTooltip('Sprint actions')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Open sprint'));
      await tester.pumpAndSettle();

      expect(find.byType(SprintPage), findsOneWidget);
      expect(find.byKey(const Key('burndown')), findsOneWidget);
      await tester.tap(find.widgetWithText(FilledButton, 'Complete sprint'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('To SPRINT-3'));
      await tester.pumpAndSettle();

      expect(api.calls, contains('complete SPRINT-2'));
    });

    testWidgets('a sprint is created with a name', (tester) async {
      final api = await open(tester, 'Backlog');

      await tester.tap(find.text('Create sprint'));
      await tester.pumpAndSettle();
      await tester.enterText(find.byType(TextField), 'Paperwork week');
      await tester.tap(find.widgetWithText(FilledButton, 'Create'));
      await tester.pumpAndSettle();

      expect(api.calls, contains('create Paperwork week'));
    });
  });

  group('Reports', () {
    testWidgets('opens on the running sprint with its numbers, burndown and tasks', (tester) async {
      final api = await open(tester, 'Reports');

      expect(api.calls, contains('detail SPRINT-2'));
      expect(find.text('Committed'), findsOneWidget);
      expect(find.text('Completed'), findsOneWidget);
      expect(find.byKey(const Key('burndown')), findsOneWidget);
      expect(find.text('Task 2'), findsOneWidget);
      // The report's own list, not the search field's scrollable inside it.
      await tester.scrollUntilVisible(find.textContaining('Velocity 7.5'), 300,
          scrollable: find.ancestor(of: find.text('Committed'), matching: find.byType(Scrollable)).first);
      expect(find.textContaining('Velocity 7.5'), findsOneWidget);
    });

    testWidgets('the tasks can be searched, and broken down by status and priority', (tester) async {
      await open(tester, 'Reports');

      expect(find.byKey(const Key('by-status')), findsOneWidget);
      expect(find.byKey(const Key('by-priority')), findsOneWidget);
      expect(find.text('Task 2'), findsOneWidget);
      expect(find.text('Task 3'), findsOneWidget);

      await tester.enterText(find.byKey(const Key('report-search')), 'task 3');
      await tester.pumpAndSettle();

      expect(find.text('Task 2'), findsNothing);
      expect(find.text('Task 3'), findsOneWidget);
    });

    testWidgets('the sprint opens on its own page from the report', (tester) async {
      await open(tester, 'Reports');

      await tester.tap(find.byKey(const Key('open-sprint')));
      await tester.pumpAndSettle();

      expect(find.byType(SprintPage), findsOneWidget);
    });

    testWidgets('another sprint can be picked', (tester) async {
      final api = await open(tester, 'Reports');

      await tester.tap(find.byKey(const Key('report-sprint')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('SPRINT-1').last);
      await tester.pumpAndSettle();

      expect(api.calls, contains('detail SPRINT-1'));
    });
  });

  for (final (name, size) in [('tablet landscape', const Size(1280, 800)), ('phone landscape', const Size(844, 390))]) {
    testWidgets('Backlog and Reports lay out on a $name', (tester) async {
      await open(tester, 'Backlog', size: size);
      expect(tester.takeException(), isNull);
      await tester.tap(find.widgetWithText(Tab, 'Reports'));
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
    });
  }
}
