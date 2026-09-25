import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/screens/goals_screen.dart';

/// One goal kept by hand (no tasks), and a record of the progress the screen saves.
class _FakeGoalsApi extends PersonaOsApi {
  _FakeGoalsApi() : super(serverUrl: 'http://fake');

  final saved = <int>[];

  @override
  Future<List<Goal>> getGoals({bool includeDropped = false}) async => [
        Goal.fromJson({
          'id': 2,
          'key': 'GOAL-2',
          'title': 'Create a nutrition plan',
          'periodType': 'month',
          'status': 'active',
          'progress': 40,
          'effectiveProgress': 40,
          'taskCount': 0,
        }),
      ];

  @override
  Future<void> setGoalProgress(int id, int progress) async => saved.add(progress);
}

void main() {
  Future<_FakeGoalsApi> openProgress(WidgetTester tester) async {
    final api = _FakeGoalsApi();
    await tester.pumpWidget(MaterialApp(home: GoalsScreen(api: api)));
    await tester.pumpAndSettle();
    await tester.tap(find.byType(PopupMenuButton<String>).first);
    await tester.pumpAndSettle();
    await tester.tap(find.text('Set progress…'));
    await tester.pumpAndSettle();
    return api;
  }

  testWidgets('progress is set with a slider, not by typing a number', (tester) async {
    final api = await openProgress(tester);

    expect(find.byType(Slider), findsOneWidget);
    expect(find.byType(TextField), findsNothing);
    Finder inDialog(String text) => find.descendant(of: find.byType(AlertDialog), matching: find.text(text));
    expect(inDialog('40%'), findsOneWidget);

    await tester.drag(find.byType(Slider), const Offset(1000, 0));
    await tester.pumpAndSettle();
    expect(inDialog('100%'), findsOneWidget);

    await tester.tap(find.text('Save'));
    await tester.pumpAndSettle();
    expect(api.saved, [100]);
  });

  testWidgets('saving the same value sends nothing', (tester) async {
    final api = await openProgress(tester);

    await tester.tap(find.text('Save'));
    await tester.pumpAndSettle();
    expect(api.saved, isEmpty);
  });
}
