import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/main.dart';
import 'package:personaos_mobile/screens/board_screen.dart';

Map<String, dynamic> _task(int n, String column, {int? points, String? goalKey, String? goalTitle}) => {
      'id': n,
      'key': 'TASK-$n',
      'title': 'Task $n',
      'points': points,
      'column': column,
      // Everything but the backlog sits in the running sprint.
      'sprintKey': column == 'backlog' ? null : 'SPRINT-2',
      'goalKey': goalKey,
      'goalTitle': goalTitle,
      'carryOverCount': 0,
      'addedMidSprint': false,
    };

Map<String, dynamic> _sprintJson({bool active = true}) => {
      'id': 2,
      'key': 'SPRINT-2',
      'number': 2,
      'name': 'Week two',
      'status': active ? 'active' : 'planned',
      'startsAtUtc': '2026-09-13T14:30:00',
      'endsAtUtc': '2026-09-20T12:30:00',
      'committedPoints': active ? 8 : null,
      'addedPoints': 0,
      'removedPoints': 0,
      'completedPoints': 3,
      'totalPoints': 8,
      'unestimatedCount': 1,
      'scopeLocked': active,
    };

Map<String, dynamic> _boardJson({bool running = true}) => {
      'sprint': running ? _sprintJson() : null,
      'velocity': 7.5,
      'wipLimit': 3,
      'todo': running ? [_task(2, 'todo', points: 5, goalKey: 'GOAL-1', goalTitle: 'Learn Rust')] : [],
      'inProgress': running ? [_task(3, 'in_progress', points: 3)] : [],
      'done': <Map<String, dynamic>>[],
    };

Map<String, dynamic> _planJson() => {
      'sprints': [
        {'sprint': _sprintJson(), 'tasks': <Map<String, dynamic>>[]},
      ],
      'backlog': [_task(1, 'backlog')],
    };

/// A board API that records moves and can insist a move is a scope change.
class FakeBoardApi extends PersonaOsApi {
  FakeBoardApi({this.scopeChange = false, this.running = true}) : super(serverUrl: 'http://fake');

  final bool scopeChange;
  final bool running;
  final moves = <(String, String, bool)>[];

  @override
  Future<BoardView> getBoard() async => BoardView.fromJson(_boardJson(running: running));

  @override
  Future<PlanView> getPlan() async => PlanView.fromJson(_planJson());

  @override
  Future<List<Goal>> getGoals({bool includeDropped = false}) async => [];

  @override
  Future<void> moveTask(String key,
      {required String column, String? sprintKey, int? index, bool acknowledgeScopeChange = false}) async {
    moves.add((key, column, acknowledgeScopeChange));
    if (scopeChange && !acknowledgeScopeChange) {
      throw ApiException('SPRINT-2 is running, so adding it is a scope change.', code: scopeChangeCode);
    }
  }
}

Future<void> _pumpBoard(WidgetTester tester, PersonaOsApi api) async {
  tester.view.physicalSize = const Size(1080, 2340);
  tester.view.devicePixelRatio = 2.75;
  addTearDown(tester.view.reset);
  await tester.pumpWidget(MaterialApp(theme: personaOsTheme, home: BoardScreen(api: api)));
  await tester.pumpAndSettle();
}

/// Long-presses a card and drops it on the named column's drop target.
Future<void> _dragCardToColumn(WidgetTester tester, String cardKey, String column) async {
  final gesture = await tester.startGesture(tester.getCenter(find.byKey(Key('card-$cardKey'))));
  await tester.pump(kLongPressTimeout + const Duration(milliseconds: 100));
  await gesture.moveBy(const Offset(0, -20));
  await tester.pump();
  await gesture.moveTo(tester.getCenter(find.byKey(Key('drop-$column'))));
  await tester.pump();
  await gesture.up();
  await tester.pumpAndSettle();
}

void main() {
  group('board models', () {
    test('parse the running sprint with its columns, keys, points and goals', () {
      final board = BoardView.fromJson(_boardJson());

      expect(board.sprint!.key, 'SPRINT-2');
      expect(board.sprint!.name, 'Week two');
      expect(board.sprint!.isActive, isTrue);
      expect(board.sprint!.endsAtUtc, DateTime.utc(2026, 9, 20, 12, 30));
      expect(board.velocity, 7.5);
      expect(board.columns[BoardColumns.todo]!.single.key, 'TASK-2');
      expect(board.columns[BoardColumns.todo]!.single.goalTitle, 'Learn Rust');
      // The backlog is not a board column any more; it lives on the plan.
      expect(board.visibleColumns, [BoardColumns.todo, BoardColumns.inProgress, BoardColumns.done]);
    });

    test('with no sprint running the board has no sprint at all', () {
      expect(BoardView.fromJson(_boardJson(running: false)).sprint, isNull);
    });

    test('the plan carries the sprints and the backlog', () {
      final plan = PlanView.fromJson(_planJson());

      expect(plan.sprints.single.sprint.key, 'SPRINT-2');
      expect(plan.backlog.single.key, 'TASK-1');
    });

    test('goals carry their key and tasks', () {
      final goal = Goal.fromJson({
        'id': 8,
        'key': 'GOAL-2',
        'title': 'Learn Rust',
        'periodType': 'month',
        'status': 'active',
        'progress': 0,
        'effectiveProgress': 38,
        'taskCount': 2,
        'doneTaskCount': 1,
        'totalPoints': 8,
        'donePoints': 3,
        'tasks': [
          {'id': 1, 'key': 'TASK-1', 'title': 'Ownership', 'points': 3, 'column': 'done', 'sprintNumber': 1},
        ],
      });

      expect(goal.key, 'GOAL-2');
      expect(goal.tasks.single.column, BoardColumns.done);
      expect(goal.effectiveProgress, 38);
    });
  });

  group('board screen', () {
    testWidgets('shows the running sprint and the cards of the first column', (tester) async {
      await _pumpBoard(tester, FakeBoardApi());

      expect(find.text('SPRINT-2 · Week two'), findsOneWidget);
      expect(find.text('8 committed'), findsOneWidget);
      expect(find.byKey(const Key('card-TASK-2')), findsOneWidget); // This week, the first page
      expect(tester.takeException(), isNull);
    });

    testWidgets('with nothing running it says where to plan one', (tester) async {
      await _pumpBoard(tester, FakeBoardApi(running: false));

      expect(find.text('No sprint is running.'), findsOneWidget);
      expect(find.textContaining('Backlog page'), findsOneWidget);
    });

    testWidgets('dragging a card onto a column moves it there', (tester) async {
      final api = FakeBoardApi();
      await _pumpBoard(tester, api);

      await _dragCardToColumn(tester, 'TASK-2', BoardColumns.done);

      expect(api.moves, [('TASK-2', BoardColumns.done, false)]);
    });

    testWidgets('a scope change asks first and retries with the acknowledgement', (tester) async {
      final api = FakeBoardApi(scopeChange: true);
      await _pumpBoard(tester, api);

      await _dragCardToColumn(tester, 'TASK-2', BoardColumns.done);
      expect(find.text('Scope change'), findsOneWidget);
      await tester.tap(find.text('Change scope'));
      await tester.pumpAndSettle();

      expect(api.moves, [('TASK-2', BoardColumns.done, false), ('TASK-2', BoardColumns.done, true)]);
    });

    testWidgets('declining a scope change does not retry', (tester) async {
      final api = FakeBoardApi(scopeChange: true);
      await _pumpBoard(tester, api);

      await _dragCardToColumn(tester, 'TASK-2', BoardColumns.done);
      await tester.tap(find.text('Cancel'));
      await tester.pumpAndSettle();

      expect(api.moves, hasLength(1));
    });

    testWidgets('the task sheet lays out its points, sprint picker and actions', (tester) async {
      await _pumpBoard(tester, FakeBoardApi());

      await tester.tap(find.byKey(const Key('card-TASK-2')));
      await tester.pumpAndSettle();

      expect(tester.takeException(), isNull);
      expect(find.text('TASK-2'), findsWidgets);
      expect(find.widgetWithText(ChoiceChip, '13'), findsOneWidget);
      // The sprint can be changed from the task itself; the picker shows where it sits now.
      expect(find.widgetWithText(DropdownButtonFormField<String?>, 'SPRINT-2 · Week two'), findsOneWidget);
      expect(find.widgetWithText(OutlinedButton, 'In progress'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'Save'), findsOneWidget);
    });
  });
}
