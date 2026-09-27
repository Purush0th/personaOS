import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/goal_calendar.dart';

/// The phone mirrors the server's goal calendar (GoalCalendarTests) so a sheet can refuse a slot
/// before saving. The cases are the server's own, on 27 September 2026, so the two agree.
void main() {
  final today = DateTime(2026, 9, 27);

  test('a year runs from today, or a later start, to 31 December, with at least 90 days', () {
    final year = resolveGoalDates('year', 2026, null, null, today);
    expect((year.start, year.end), (today, DateTime(2026, 12, 31)));
    expect(resolveGoalDates('year', 2027, null, null, today).start, DateTime(2027, 1, 1));
    expect(resolveGoalDates('year', 2026, null, DateTime(2026, 10, 3), today).problem, isNull);
    expect(resolveGoalDates('year', 2026, null, DateTime(2026, 10, 4), today).problem, contains('at least 90'));
    expect(resolveGoalDates('year', 2026, null, DateTime(2026, 9, 1), today).problem, contains('past'));
  });

  test('a quarter or month with too few days left is refused; one under way starts today', () {
    expect(resolveGoalDates('quarter', 2026, 3, null, today).problem, contains('at least 45'));
    expect(resolveGoalDates('month', 2026, 9, null, today).problem, contains('at least 15'));
    final q4 = resolveGoalDates('quarter', 2026, 4, null, today);
    expect((q4.start, q4.end), (DateTime(2026, 10, 1), DateTime(2026, 12, 31)));
    final october = resolveGoalDates('month', 2026, 10, null, DateTime(2026, 10, 17));
    expect((october.start, october.end), (DateTime(2026, 10, 17), DateTime(2026, 10, 31)));
  });

  test('a child sits in its parent and starts no earlier', () {
    final parent = ParentSlot(type: 'year', start: DateTime(2026, 10, 10), end: DateTime(2026, 12, 31));

    expect(resolveGoalDates('quarter', 2026, 4, null, today, parent: parent).start, DateTime(2026, 10, 10));
    expect(resolveGoalDates('quarter', 2027, 1, null, today, parent: parent).problem, contains('not in 2026'));
  });

  test('slots read as the server labels them', () {
    expect(slotLabel('year', DateTime(2026, 12, 31)), '2026');
    expect(slotLabel('quarter', DateTime(2026, 12, 31)), 'Q4 2026');
    expect(slotLabel('month', DateTime(2026, 10, 31)), 'Oct 2026');
    expect(monthsOfQuarter(DateTime(2026, 12, 31)), [10, 11, 12]);
  });
}
