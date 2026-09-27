/// Where a goal sits on the calendar, mirrored from the server's GoalCalendar so a sheet can show
/// the dates and say what is wrong before saving. The server stays the authority and checks again.
///
/// Every goal follows the calendar and never starts in the past:
/// - a year starts today or on a later day picked, ends 31 December, and needs 90 days;
/// - a quarter is Q1-Q4 of a year, starts on the later of today and its first day, and needs 45;
/// - a month is a calendar month, the same way, and needs 15.
/// A nested goal's slot lies in its parent's year or quarter, and it starts no earlier than the
/// parent. Both ends count.
library;

const minDays = {'year': 90, 'quarter': 45, 'month': 15};

const periodLabels = {'year': 'Year', 'quarter': 'Quarter', 'month': 'Month'};

const monthNames = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/// The type a goal's parent must have; null for a year, which never nests.
String? parentTypeOf(String type) => switch (type) {
      'quarter' => 'year',
      'month' => 'quarter',
      _ => null,
    };

/// The type of goal that nests under this one; null for a month.
String? childTypeOf(String type) => switch (type) {
      'year' => 'quarter',
      'quarter' => 'month',
      _ => null,
    };

DateTime _day(DateTime d) => DateTime(d.year, d.month, d.day);

int quarterOf(DateTime day) => (day.month - 1) ~/ 3 + 1;

/// First and last day of a calendar quarter or month. Calendar arithmetic through the
/// constructor, not Duration, so a daylight-saving change never lands on the wrong date.
(DateTime, DateTime) quarterDates(int year, int quarter) {
  final first = (quarter - 1) * 3 + 1;
  return (DateTime(year, first, 1), DateTime(year, first + 3, 0));
}

(DateTime, DateTime) monthDates(int year, int month) => (DateTime(year, month, 1), DateTime(year, month + 1, 0));

/// Days from start to end, counting both.
int goalDays(DateTime start, DateTime end) => _day(end).difference(_day(start)).inDays + 1;

/// "2026", "Q4 2026" or "Oct 2026" — the same as the server's label.
String slotLabel(String type, DateTime end) => switch (type) {
      'quarter' => 'Q${quarterOf(end)} ${end.year}',
      'month' => '${monthNames[end.month - 1]} ${end.year}',
      _ => '${end.year}',
    };

/// The months of the quarter that ends on [quarterEnd], e.g. [10, 11, 12].
List<int> monthsOfQuarter(DateTime quarterEnd) {
  final first = (quarterOf(quarterEnd) - 1) * 3 + 1;
  return [first, first + 1, first + 2];
}

/// What a nested goal needs of its parent.
class ParentSlot {
  const ParentSlot({required this.type, required this.start, required this.end});

  final String type;
  final DateTime start;
  final DateTime end;
}

/// The dates of a goal of [type] in the slot given, or why there are none.
/// [slot] is the quarter (1-4) or month (1-12); [start] is a year goal's chosen start.
({DateTime? start, DateTime? end, String? problem}) resolveGoalDates(
  String type,
  int year,
  int? slot,
  DateTime? start,
  DateTime today, {
  ParentSlot? parent,
}) {
  today = _day(today);
  ({DateTime? start, DateTime? end, String? problem}) fail(String problem) => (start: null, end: null, problem: problem);

  if (type == 'year') {
    final first = _day(start ?? (year > today.year ? DateTime(year, 1, 1) : today));
    if (first.year != year) return fail('A yearly goal for $year must start in $year.');
    if (first.isBefore(today)) return fail('A goal cannot start in the past.');
    final end = DateTime(year, 12, 31);
    final days = goalDays(first, end);
    if (days < minDays['year']!) {
      return fail('A yearly goal needs at least ${minDays['year']} days; this has $days. Pick next year.');
    }
    return (start: first, end: end, problem: null);
  }

  if (slot == null) return fail(type == 'quarter' ? 'Pick a quarter.' : 'Pick a month.');
  if (year < today.year) return fail('$year has already passed.');
  final (calendarStart, calendarEnd) = type == 'quarter' ? quarterDates(year, slot) : monthDates(year, slot);
  final label = slotLabel(type, calendarEnd);
  if (calendarEnd.isBefore(today)) return fail('$label has already ended.');

  var first = calendarStart.isBefore(today) ? today : calendarStart;
  if (parent != null) {
    final withinStart = parent.type == 'year'
        ? DateTime(parent.end.year, 1, 1)
        : quarterDates(parent.end.year, quarterOf(parent.end)).$1;
    if (calendarStart.isBefore(withinStart) || calendarEnd.isAfter(_day(parent.end))) {
      return fail('$label is not in ${slotLabel(parent.type, parent.end)}.');
    }
    if (_day(parent.start).isAfter(first)) first = _day(parent.start);
  }

  final days = goalDays(first, calendarEnd);
  final min = minDays[type]!;
  if (days < min) {
    return fail('$label has only $days days left; a ${type == 'quarter' ? 'quarterly' : 'monthly'} goal needs at least $min.');
  }
  return (start: first, end: calendarEnd, problem: null);
}

/// "22 Sep – 31 Dec 2026", with the year once when both ends share it.
String formatGoalRange(DateTime start, DateTime end) {
  String day(DateTime d, bool withYear) => '${d.day} ${monthNames[d.month - 1]}${withYear ? ' ${d.year}' : ''}';
  return '${day(start, start.year != end.year)} – ${day(end, true)}';
}
