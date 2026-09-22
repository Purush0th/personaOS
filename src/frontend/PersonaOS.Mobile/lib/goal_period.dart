/// The rules a goal's dates keep, mirrored from the server's GoalPeriodCalculator so the sheet can
/// say what is wrong before saving. The server stays the authority and checks again.
///
/// A month goal runs at most 31 days, a quarter at most 90, and a year ends on 31 December of
/// the year it starts. Both ends count.
library;

const maxMonthDays = 31;
const maxQuarterDays = 90;

DateTime _day(DateTime d) => DateTime(d.year, d.month, d.day);

/// The end a goal gets when none is picked.
DateTime defaultGoalEnd(String period, DateTime start) {
  final s = _day(start);
  switch (period) {
    case 'year':
      return DateTime(s.year, 12, 31);
    // Calendar arithmetic through the constructor, not Duration: adding 24-hour days across a
    // daylight-saving change lands an hour off midnight and on the wrong date.
    case 'quarter':
      return DateTime(s.year, s.month, s.day + maxQuarterDays - 1);
    default:
      // A month on, less a day — clamped the way the server's AddMonths clamps, so 31 Jan runs
      // to 27 Feb on both sides rather than Dart rolling into March.
      final lastOfNext = DateTime(s.year, s.month + 2, 0).day;
      final day = s.day < lastOfNext ? s.day : lastOfNext;
      return DateTime(s.year, s.month + 1, day - 1);
  }
}

/// Days from start to end, counting both.
int goalDays(DateTime start, DateTime end) => _day(end).difference(_day(start)).inDays + 1;

/// Why these dates are not a valid goal of this period, or null when they are.
String? goalPeriodProblem(String period, DateTime start, DateTime end) {
  if (_day(end).isBefore(_day(start))) return 'The end date is before the start date.';
  final days = goalDays(start, end);
  if (period == 'month' && days > maxMonthDays) {
    return 'A monthly goal runs at most $maxMonthDays days — this is $days.';
  }
  if (period == 'quarter' && days > maxQuarterDays) {
    return 'A quarterly goal runs at most $maxQuarterDays days — this is $days.';
  }
  if (period == 'year' && _day(end) != DateTime(start.year, 12, 31)) {
    return 'A yearly goal ends on 31 December of the year it starts.';
  }
  return null;
}

const _months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/// "22 Sep – 21 Oct 2026", with the year once when both ends share it.
String formatGoalRange(DateTime start, DateTime end) {
  String day(DateTime d, bool withYear) => '${d.day} ${_months[d.month - 1]}${withYear ? ' ${d.year}' : ''}';
  return '${day(start, start.year != end.year)} – ${day(end, true)}';
}
