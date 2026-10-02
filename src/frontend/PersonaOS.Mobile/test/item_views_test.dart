import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/screens/goals_screen.dart';
import 'package:personaos_mobile/screens/task_views.dart';

BoardTask _task({String column = 'todo'}) => BoardTask.fromJson({
      'id': 7,
      'key': 'TASK-7',
      'title': 'File taxes',
      'description': 'Need the P60 first.',
      'points': 3,
      'column': column,
      'sprintKey': 'SPRINT-2',
      'commentCount': 1,
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
    return _task(column: column);
  }

  @override
  Future<void> updateTask(String key,
          {String? title, int? points, int? goalId, String? description, String? priority}) async =>
      calls.add('update $key description=${description ?? '(unchanged)'}');

  @override
  Future<List<Goal>> getGoals() async => [Goal.fromJson(_goalJson())];

  @override
  Future<PlanView> getPlan() async => PlanView.fromJson({'sprints': <Object>[], 'backlog': <Object>[]});

  @override
  Future<List<WorkItemAttachment>> getAttachments(String itemType, String key) async => [];

  String column = 'todo';

  @override
  Future<void> moveTask(String key,
      {required String column, String? sprintKey, int? index, bool acknowledgeScopeChange = false}) async {
    calls.add('move $key $column ${sprintKey ?? '-'}');
    this.column = column;
  }

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

  /// A button that opens TASK-7's quick view, as a list would.
  Future<ItemsApi> openQuickView(WidgetTester tester, {Size size = const Size(400, 900)}) async {
    tester.view.physicalSize = size;
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final api = ItemsApi();
    await tester.pumpWidget(MaterialApp(
      home: Builder(
        builder: (context) => Scaffold(
          body: Center(
            child: TextButton(onPressed: () => showTaskQuickView(context, api, 'TASK-7'), child: const Text('open')),
          ),
        ),
      ),
    ));
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();
    return api;
  }

  Future<ItemsApi> openPage(WidgetTester tester, {Size size = const Size(800, 1400)}) async {
    tester.view.physicalSize = size;
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final api = ItemsApi();
    await tester.pumpWidget(MaterialApp(home: TaskPage(api: api, taskKey: 'TASK-7')));
    await tester.pumpAndSettle();
    return api;
  }

  group('task quick view', () {
    testWidgets('is a short look: facts, the start of the text, counts and moves, no form', (tester) async {
      await openQuickView(tester);

      expect(find.byKey(const Key('task-quick-view-TASK-7')), findsOneWidget);
      expect(find.text('File taxes'), findsOneWidget);
      expect(find.text('Need the P60 first.'), findsOneWidget);
      expect(find.textContaining('1 comment'), findsOneWidget);
      expect(find.byKey(const Key('quick-move-in_progress')), findsOneWidget);
      expect(find.byKey(const Key('quick-move-done')), findsOneWidget);
      expect(find.byKey(const Key('comment-draft')), findsNothing);
      expect(find.widgetWithText(FilledButton, 'Save'), findsNothing);
      // A phone gets a sheet from the bottom edge.
      expect(find.byType(BottomSheet), findsOneWidget);
    });

    testWidgets('moves the task and shows where it went', (tester) async {
      final api = await openQuickView(tester);

      await tester.tap(find.byKey(const Key('quick-move-done')));
      await tester.pumpAndSettle();

      expect(api.calls, contains('move TASK-7 done SPRINT-2'));
      expect(find.byKey(const Key('quick-move-done')), findsNothing);
      expect(find.byKey(const Key('quick-move-todo')), findsOneWidget);
    });

    testWidgets('floats as a dialog on a tablet', (tester) async {
      await openQuickView(tester, size: const Size(1280, 800));

      expect(find.byType(Dialog), findsOneWidget);
      expect(find.byType(BottomSheet), findsNothing);
      expect(tester.getSize(find.byKey(const Key('task-quick-view-TASK-7'))).width, lessThanOrEqualTo(560));
    });

    testWidgets('opens the full page in its place', (tester) async {
      await openQuickView(tester);

      await tester.tap(find.byKey(const Key('open-full-page')));
      await tester.pumpAndSettle();

      expect(find.byType(TaskPage), findsOneWidget);
      expect(find.byKey(const Key('task-quick-view-TASK-7')), findsNothing);
    });
  });

  group('task full page', () {
    testWidgets('has the form, the files and the discussion, and takes a comment', (tester) async {
      final api = await openPage(tester);

      expect(find.widgetWithText(AppBar, 'TASK-7'), findsOneWidget);
      expect(find.byKey(const Key('task-description')), findsOneWidget);
      expect(find.byKey(const Key('attachments')), findsOneWidget);
      expect(find.text('Reminder set for Friday.'), findsOneWidget);

      await tester.scrollUntilVisible(find.byKey(const Key('comment-draft')), 300,
          scrollable: find.byType(Scrollable).first);
      await tester.enterText(find.byKey(const Key('comment-draft')), 'Asked HR for it.');
      await tester.pump();
      await tester.tap(find.widgetWithText(TextButton, 'Comment'));
      await tester.pumpAndSettle();

      expect(api.calls, contains('comment task TASK-7 Asked HR for it.'));
    });

    testWidgets('saves only what changed and stays on the page', (tester) async {
      final api = await openPage(tester);

      await tester.tap(find.widgetWithText(FilledButton, 'Save'));
      await tester.pumpAndSettle();

      expect(api.calls, contains('update TASK-7 description=(unchanged)'));
      expect(find.byType(TaskPage), findsOneWidget);
      expect(find.text('Saved.'), findsOneWidget);
    });

    testWidgets('puts the details beside the text on a tablet, below it on a phone', (tester) async {
      await openPage(tester, size: const Size(1280, 900));
      expect(find.byKey(const Key('task-details-panel')), findsOneWidget);
      final panel = tester.getRect(find.byKey(const Key('task-details-panel')));
      final description = tester.getRect(find.byKey(const Key('task-description')));
      expect(panel.left, greaterThan(description.right));

      await openPage(tester, size: const Size(400, 900));
      expect(find.byKey(const Key('task-details-panel')), findsNothing);
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

    testWidgets("a goal's key opens a short look: progress and what drives it, no discussion", (tester) async {
      final api = await openGoals(tester);

      await tester.tap(find.byKey(const Key('open-GOAL-4')));
      await tester.pumpAndSettle();

      expect(api.calls, contains('goal GOAL-4'));
      expect(find.byKey(const Key('goal-quick-view-GOAL-4')), findsOneWidget);
      expect(find.text('Get the paperwork done.'), findsOneWidget);
      expect(
          find.descendant(
              of: find.byKey(const Key('goal-quick-view-GOAL-4')), matching: find.textContaining('50% · 0 of 1 task done')),
          findsOneWidget);
      expect(find.byKey(const Key('comments')), findsNothing);
      expect(find.byKey(const Key('goal-view-GOAL-4')), findsNothing);
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

      await tester.tap(find.byKey(const Key('open-full-page')));
      await tester.pumpAndSettle();

      expect(find.widgetWithText(AppBar, 'GOAL-4'), findsOneWidget);
      expect(find.byKey(const Key('goal-view-GOAL-4')), findsOneWidget);
      expect(find.byKey(const Key('goal-task-TASK-7')), findsOneWidget);
      expect(find.byKey(const Key('comments')), findsOneWidget);
      // Every action the goals list has, from the page's menu.
      await tester.tap(find.byKey(const Key('goal-page-menu')));
      await tester.pumpAndSettle();
      expect(find.text('Move'), findsOneWidget);
      expect(find.text('Delete'), findsOneWidget);
    });
  });
}
