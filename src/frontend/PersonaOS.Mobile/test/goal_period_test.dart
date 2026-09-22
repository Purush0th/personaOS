import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/goal_period.dart';

/// The phone mirrors the server's goal date rules so the sheet can refuse bad dates before
/// saving. These cases are the server's own (GoalPeriodCalculatorTests), so the two agree.
void main() {
  DateTime d(String iso) => DateTime.parse(iso);

  group('default end', () {
    final cases = {
      ('month', '2026-09-01'): '2026-09-30',
      ('month', '2026-09-22'): '2026-10-21',
      ('month', '2026-01-31'): '2026-02-27',
      ('month', '2026-12-15'): '2027-01-14',
      ('quarter', '2026-09-22'): '2026-12-20',
      ('year', '2026-09-22'): '2026-12-31',
      ('year', '2027-03-01'): '2027-12-31',
    };
    cases.forEach((input, expected) {
      test('${input.$1} from ${input.$2} ends $expected', () {
        final end = defaultGoalEnd(input.$1, d(input.$2));
        expect(end, d(expected));
        expect(goalPeriodProblem(input.$1, d(input.$2), end), isNull);
      });
    });
  });

  test('accepts periods within the rules', () {
    expect(goalPeriodProblem('month', d('2026-10-01'), d('2026-10-31')), isNull);
    expect(goalPeriodProblem('month', d('2026-09-22'), d('2026-10-15')), isNull);
    expect(goalPeriodProblem('quarter', d('2026-09-22'), d('2026-12-20')), isNull);
    expect(goalPeriodProblem('year', d('2026-02-10'), d('2026-12-31')), isNull);
  });

  test('refuses periods that break them', () {
    expect(goalPeriodProblem('month', d('2026-10-01'), d('2026-11-01')), contains('31 days'));
    expect(goalPeriodProblem('quarter', d('2026-09-22'), d('2026-12-21')), contains('90 days'));
    expect(goalPeriodProblem('year', d('2026-02-10'), d('2026-12-30')), contains('31 December'));
    expect(goalPeriodProblem('month', d('2026-10-15'), d('2026-10-01')), contains('before'));
  });

  test('formats a range with the year once', () {
    expect(formatGoalRange(d('2026-09-22'), d('2026-10-21')), '22 Sep – 21 Oct 2026');
    expect(formatGoalRange(d('2026-12-15'), d('2027-01-14')), '15 Dec 2026 – 14 Jan 2027');
  });
}
