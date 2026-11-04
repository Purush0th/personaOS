import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/now_next.dart';

PlannerItem _item(int id, String title, String start, String end, {String status = 'planned'}) => PlannerItem(
  id: id,
  title: title,
  date: '2026-11-04',
  scheduledTime: '$start:00',
  endTime: '$end:00',
  status: status,
  goalTitle: null,
);

final _day = [
  _item(1, 'Lunch', '12:45', '13:10'),
  _item(2, 'Transition', '13:10', '13:15'),
  _item(3, 'Walk', '13:15', '13:45'),
];

void main() {
  test('the item under way, how far through it is, and the next', () {
    final at = DayPosition.at(_day, DateTime(2026, 11, 4, 13, 0));
    expect(at.current?.title, 'Lunch');
    expect(at.next?.title, 'Transition');
    expect(at.minutesLeft, 10);
    expect(at.progress, closeTo(0.6, 0.01));
  });

  test('between items there is only a next, and done items are behind you', () {
    final at = DayPosition.at([
      _item(1, 'Lunch', '12:45', '13:10', status: 'done'),
      ..._day.skip(1),
    ], DateTime(2026, 11, 4, 12, 50));
    expect(at.current, isNull);
    expect(at.next?.title, 'Transition');
    expect(at.minutesToNext, 20);
  });

  test('after the last item the card has nothing to show', () {
    expect(DayPosition.at(_day, DateTime(2026, 11, 4, 22, 0)).isEmpty, isTrue);
  });

  testWidgets('Done marks the item under way and the card moves on', (tester) async {
    final done = <int>[];
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: NowNextCard(
            items: _day,
            onDone: (item) async => done.add(item.id),
            onOpen: () {},
            clock: () => DateTime(2026, 11, 4, 13, 0),
          ),
        ),
      ),
    );
    expect(find.text('Lunch'), findsOneWidget);
    expect(find.text('Now · 10 min left'), findsOneWidget);

    await tester.tap(find.byKey(const Key('now-done')));
    await tester.pump();

    expect(done, [1]);
    expect(find.text('Lunch'), findsNothing);
    expect(find.textContaining('Next, in 10 min'), findsOneWidget);
  });
}
