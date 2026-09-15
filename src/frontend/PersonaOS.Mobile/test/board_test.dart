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
      'goalKey': goalKey,
      'goalTitle': goalTitle,
      'carryOverCount': 0,
      'addedMidSprint': false,
    };

Map<String, dynamic> _boardJson({bool active = true, bool scopeLocked = true}) => {
      'view': 'current',
      'sprint': {
        'id': 2,
        'number': 2,
        'status': active ? 'active' : 'planned',
        'startsAtUtc': '2026-09-13T14:30:00',
        'endsAtUtc': '2026-09-20T12:30:00',
        'committedPoints': active ? 8 : null,
        'addedPoints': 0,
        'removedPoints': 0,
        'completedPoints': 3,
        'totalPoints': 8,
        'unestimatedCount': 1,
        'scopeLocked': scopeLocked,
      },
      'inPlanningWindow': false,
      'canStartSprint': !active,
      'velocity': 7.5,
      'wipLimit': 3,
      'backlog': [_task(1, 'backlog')],
      'todo': [_task(2, 'todo', points: 5, goalKey: 'GOAL-1', goalTitle: 'Learn Rust')],
      'inProgress': [_task(3, 'in_progress', points: 3)],
      'done': <Map<String, dynamic>>[],
    };

/// A board API that records moves and can insist a move is a scope change.
class FakeBoardApi extends PersonaOsApi {
  FakeBoardApi({this.scopeChange = false}) : super(serverUrl: 'http://fake');

  final bool scopeChange;
  final moves = <(int, String, bool)>[];

  @override
  Future<BoardView> getBoard({String sprint = 'current'}) async => BoardView.fromJson(_boardJson());

  @override
  Future<List<Goal>> getGoals({bool includeDropped = false}) async => [];

  @override
  Future<void> moveTask(int id,
      {required String column, String? sprint, int? index, bool acknowledgeScopeChange = false}) async {
    moves.add((id, column, acknowledgeScopeChange));
    if (scopeChange && !acknowledgeScopeChange) {
      throw ApiException('Sprint 2 has already started, so adding it is a scope change.', code: scopeChangeCode);
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
    test('parse the board with columns, keys, points and goals', () {
      final board = BoardView.fromJson(_boardJson());

      expect(board.sprint.number, 2);
      expect(board.sprint.isActive, isTrue);
      expect(board.sprint.endsAtUtc, DateTime.utc(2026, 9, 20, 12, 30));
      expect(board.velocity, 7.5);
      expect(board.columns[BoardColumns.todo]!.single.key, 'TASK-2');
      expect(board.columns[BoardColumns.todo]!.single.goalTitle, 'Learn Rust');
      expect(board.columns[BoardColumns.backlog]!.single.points, isNull);
    });

    test('a sprint being planned shows only Backlog and This week', () {
      final json = _boardJson(active: false)..['inProgress'] = <Map<String, dynamic>>[];

      expect(BoardView.fromJson(json).visibleColumns, [BoardColumns.backlog, BoardColumns.todo]);
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
    testWidgets('shows the sprint and the cards of the first column', (tester) async {
      await _pumpBoard(tester, FakeBoardApi());

      expect(find.text('Sprint 2'), findsOneWidget);
      expect(find.text('8 committed'), findsOneWidget);
      expect(find.byKey(const Key('card-TASK-1')), findsOneWidget); // Backlog page
      expect(tester.takeException(), isNull);
    });

    testWidgets('dragging a card onto a column moves it there', (tester) async {
      final api = FakeBoardApi();
      await _pumpBoard(tester, api);

      await _dragCardToColumn(tester, 'TASK-1', BoardColumns.inProgress);

      expect(api.moves, [(1, BoardColumns.inProgress, false)]);
    });

    testWidgets('a scope change asks first and retries with the acknowledgement', (tester) async {
      final api = FakeBoardApi(scopeChange: true);
      await _pumpBoard(tester, api);

      await _dragCardToColumn(tester, 'TASK-1', BoardColumns.todo);
      expect(find.text('Scope change'), findsOneWidget);
      await tester.tap(find.text('Change scope'));
      await tester.pumpAndSettle();

      expect(api.moves, [(1, BoardColumns.todo, false), (1, BoardColumns.todo, true)]);
    });

    testWidgets('declining a scope change does not retry', (tester) async {
      final api = FakeBoardApi(scopeChange: true);
      await _pumpBoard(tester, api);

      await _dragCardToColumn(tester, 'TASK-1', BoardColumns.todo);
      await tester.tap(find.text('Cancel'));
      await tester.pumpAndSettle();

      expect(api.moves, hasLength(1));
    });

    testWidgets('the task sheet lays out its point chips and actions', (tester) async {
      await _pumpBoard(tester, FakeBoardApi());

      await tester.tap(find.byKey(const Key('card-TASK-1')));
      await tester.pumpAndSettle();

      expect(tester.takeException(), isNull);
      expect(find.text('TASK-1'), findsWidgets);
      expect(find.widgetWithText(ChoiceChip, '13'), findsOneWidget);
      expect(find.widgetWithText(OutlinedButton, 'In progress'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'Save'), findsOneWidget);
    });
  });
}
