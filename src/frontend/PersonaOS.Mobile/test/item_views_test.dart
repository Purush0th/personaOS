import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/screens/board_screen.dart';
import 'package:personaos_mobile/screens/goals_screen.dart';

BoardTask _task() => BoardTask.fromJson({
      'id': 7,
      'key': 'TASK-7',
      'title': 'File taxes',
      'description': 'Need the P60 first.',
      'points': 3,
      'column': 'todo',
      'sprintKey': 'SPRINT-2',
      'goalId': 4,
      'goalKey': 'GOAL-4',
      'goalTitle': 'October',
    });

Map<String, dynamic> _goalJson() => {
      'id': 4,
      'key': 'GOAL-4',
      'title': 'October',
      'periodType': 'month',
      'slot': 'Oct 2026',
      'periodStart': '2026-10-01',
      'periodEnd': '2026-10-31',
      'status': 'active',
      'progress': 0,
      'effectiveProgress': 50,
      'taskCount': 1,
      'doneTaskCount': 0,
      'description': 'Get the paperwork done.',
      'tasks': [
        {'id': 7, 'key': 'TASK-7', 'title': 'File taxes', 'points': 3, 'column': 'todo'},
      ],
    };

/// Comments are kept per item; every call is recorded.
class ItemsApi extends PersonaOsApi {
  ItemsApi() : super(serverUrl: 'http://fake');

  final calls = <String>[];
  final comments = <String, List<WorkItemComment>>{
    'task TASK-7': [
      WorkItemComment(
        id: 1,
        author: 'assistant',
        body: 'Reminder set for Friday.',
        createdAtUtc: DateTime.utc(2026, 9, 29),
        updatedAtUtc: DateTime.utc(2026, 9, 29),
      ),
    ],
  };

  @override
  Future<List<WorkItemComment>> getComments(String itemType, String key) async =>
      List.of(comments['$itemType $key'] ?? const []);

  @override
  Future<void> addComment(String itemType, String key, String body) async {
    calls.add('comment $itemType $key $body');
    (comments['$itemType $key'] ??= []).add(WorkItemComment(
      id: 2,
      author: 'user',
      body: body,
      createdAtUtc: DateTime.utc(2026, 9, 30),
      updatedAtUtc: DateTime.utc(2026, 9, 30),
    ));
  }

  @override
  Future<BoardTask> getTask(String key) async {
    calls.add('task $key');
    return _task();
  }

  @override
  Future<void> updateTask(String key, {String? title, int? points, int? goalId, String? description}) async =>
      calls.add('update $key description=${description ?? '(unchanged)'}');

  @override
  Future<List<Goal>> getGoals() async => [Goal.fromJson(_goalJson())];

  @override
  Future<Goal> getGoal(String key) async {
    calls.add('goal $key');
    return Goal.fromJson(_goalJson());
  }
}

void main() {
  void tall(WidgetTester tester) {
    tester.view.physicalSize = const Size(800, 1400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
  }

  Future<ItemsApi> openSheet(WidgetTester tester) async {
    tall(tester);
    final api = ItemsApi();
    await tester.pumpWidget(MaterialApp(
      home: Scaffold(body: SingleChildScrollView(child: TaskSheet(api: api, goals: const [], task: _task()))),
    ));
    await tester.pumpAndSettle();
    return api;
  }

  group('task quick view', () {
    testWidgets('shows the description and the comments, and takes a new one', (tester) async {
      final api = await openSheet(tester);

      expect(find.text('Need the P60 first.'), findsOneWidget);
      expect(find.text('Reminder set for Friday.'), findsOneWidget);

      await tester.enterText(find.byKey(const Key('comment-draft')), 'Asked HR for it.');
      await tester.pump();
      await tester.tap(find.widgetWithText(TextButton, 'Comment'));
      await tester.pumpAndSettle();

      expect(api.calls, contains('comment task TASK-7 Asked HR for it.'));
      expect(find.text('Asked HR for it.'), findsOneWidget);
    });

    testWidgets('sends the description only when it changed', (tester) async {
      final api = await openSheet(tester);

      await tester.tap(find.text('Save'));
      await tester.pumpAndSettle();
      expect(api.calls, contains('update TASK-7 description=(unchanged)'));
    });

    testWidgets('opens the task on its own page', (tester) async {
      tall(tester);
      final api = ItemsApi();
      await tester.pumpWidget(MaterialApp(
        home: Builder(
          builder: (context) => Scaffold(
            body: Center(
              child: TextButton(
                onPressed: () => showModalBottomSheet<bool>(
                  context: context,
                  isScrollControlled: true,
                  builder: (_) => TaskSheet(api: api, goals: const [], task: _task()),
                ),
                child: const Text('open'),
              ),
            ),
          ),
        ),
      ));
      await tester.tap(find.text('open'));
      await tester.pumpAndSettle();

      await tester.tap(find.byTooltip('Open full page'));
      await tester.pumpAndSettle();

      expect(api.calls, contains('task TASK-7'));
      expect(find.widgetWithText(AppBar, 'TASK-7'), findsOneWidget);
      expect(find.text('Reminder set for Friday.'), findsOneWidget);
    });
  });

  group('goal quick view', () {
    Future<ItemsApi> openGoals(WidgetTester tester) async {
      tall(tester);
      final api = ItemsApi();
      await tester.pumpWidget(MaterialApp(home: GoalsScreen(api: api, today: DateTime(2026, 10, 1))));
      await tester.pumpAndSettle();
      return api;
    }

    testWidgets("a goal's key opens its quick view with its tasks and comments", (tester) async {
      final api = await openGoals(tester);

      await tester.tap(find.byKey(const Key('open-GOAL-4')));
      await tester.pumpAndSettle();

      expect(api.calls, contains('goal GOAL-4'));
      expect(find.byKey(const Key('goal-view-GOAL-4')), findsOneWidget);
      expect(find.text('Get the paperwork done.'), findsOneWidget);
      expect(find.byKey(const Key('goal-task-TASK-7')), findsOneWidget);
      expect(find.byKey(const Key('comments')), findsOneWidget);
    });

    testWidgets('a task under a goal opens its quick view', (tester) async {
      final api = await openGoals(tester);

      await tester.tap(find.byKey(const Key('open-TASK-7')));
      await tester.pumpAndSettle();

      expect(api.calls, contains('task TASK-7'));
      expect(find.text('Need the P60 first.'), findsOneWidget);
    });

    testWidgets('the goal quick view opens the goal on its own page', (tester) async {
      await openGoals(tester);
      await tester.tap(find.byKey(const Key('open-GOAL-4')));
      await tester.pumpAndSettle();

      await tester.tap(find.byTooltip('Open full page'));
      await tester.pumpAndSettle();

      expect(find.widgetWithText(AppBar, 'GOAL-4'), findsOneWidget);
      expect(find.byKey(const Key('goal-view-GOAL-4')), findsOneWidget);
    });
  });
}
