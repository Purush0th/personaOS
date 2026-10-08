/// Calendar arithmetic for the planner, kept free of Flutter so it can be tested on its own; the
/// web app's `planner/calendar.ts` does the same, so both apps agree on what a view shows.
///
/// Days are local calendar days (midnight `DateTime`s, `yyyy-MM-dd` on the wire). Weeks start on
/// Monday, as the planner's repeat rules do.
library;

import 'dart:math' as math;

import 'api/personaos_api.dart';
import 'date_utils.dart';

enum CalendarView { day, fourDay, week, month, year, schedule }

class ViewOption {
  const ViewOption(this.view, this.label, this.icon);

  final CalendarView view;
  final String label;

  /// A Material icon code point name, resolved by the screen.
  final String icon;
}

/// The views in the order Google Calendar lists them.
const viewOptions = [
  ViewOption(CalendarView.schedule, 'Schedule', 'schedule'),
  ViewOption(CalendarView.day, 'Day', 'day'),
  ViewOption(CalendarView.fourDay, '4 days', 'four'),
  ViewOption(CalendarView.week, 'Week', 'week'),
  ViewOption(CalendarView.month, 'Month', 'month'),
  ViewOption(CalendarView.year, 'Year', 'year'),
];

/// How many days the Schedule view lists at a time.
const scheduleDays = 30;

const monthNames = [
  'January', 'February', 'March', 'April', 'May', 'June', //
  'July', 'August', 'September', 'October', 'November', 'December',
];
const weekdayNames = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

// --- Days --------------------------------------------------------------------------------

DateTime dayOnly(DateTime d) => DateTime(d.year, d.month, d.day);

DateTime parseDay(String ymd) {
  final parts = ymd.split('-').map(int.parse).toList();
  return DateTime(parts[0], parts[1], parts[2]);
}

/// Adds days on the calendar (not 24-hour steps, which slip across a daylight-saving change).
DateTime addDays(DateTime d, int days) => DateTime(d.year, d.month, d.day + days);

DateTime startOfWeek(DateTime d) => addDays(dayOnly(d), -(d.weekday - DateTime.monday));

DateTime startOfMonth(DateTime d) => DateTime(d.year, d.month);

String weekdayName(DateTime d) => weekdayNames[d.weekday - 1];

String shortWeekday(DateTime d) => weekdayName(d).substring(0, 3).toUpperCase();

bool isWeekend(DateTime d) => d.weekday == DateTime.saturday || d.weekday == DateTime.sunday;

bool sameDay(DateTime a, DateTime b) => a.year == b.year && a.month == b.month && a.day == b.day;

List<DateTime> daysBetween(DateTime from, DateTime to) => [
  for (var d = dayOnly(from); !d.isAfter(to); d = addDays(d, 1)) d,
];

// --- Views -------------------------------------------------------------------------------

/// The days a view shows around [anchor], which is also the range to load for it.
({DateTime from, DateTime to}) viewRange(CalendarView view, DateTime anchor) {
  final a = dayOnly(anchor);
  switch (view) {
    case CalendarView.day:
      return (from: a, to: a);
    case CalendarView.fourDay:
      return (from: a, to: addDays(a, 3));
    case CalendarView.week:
      final from = startOfWeek(a);
      return (from: from, to: addDays(from, 6));
    case CalendarView.month:
      final grid = monthGrid(a);
      return (from: grid.first.first, to: grid.last.last);
    case CalendarView.year:
      return (from: startOfWeek(DateTime(a.year)), to: addDays(startOfWeek(DateTime(a.year, 12, 31)), 6));
    case CalendarView.schedule:
      return (from: a, to: addDays(a, scheduleDays - 1));
  }
}

/// The anchor one step before or after: a day, 4 days, a week, a month, a year or a schedule page.
DateTime stepAnchor(CalendarView view, DateTime anchor, int direction) {
  final a = dayOnly(anchor);
  return switch (view) {
    CalendarView.day => addDays(a, direction),
    CalendarView.fourDay => addDays(a, 4 * direction),
    CalendarView.week => addDays(a, 7 * direction),
    CalendarView.month => DateTime(a.year, a.month + direction),
    CalendarView.year => DateTime(a.year + direction),
    CalendarView.schedule => addDays(a, scheduleDays * direction),
  };
}

/// The heading: "3 October 2026", "Sept – Oct 2026", "October 2026", "2026".
String viewTitle(CalendarView view, DateTime anchor) {
  final a = dayOnly(anchor);
  switch (view) {
    case CalendarView.year:
      return '${a.year}';
    case CalendarView.month:
      return '${monthNames[a.month - 1]} ${a.year}';
    case CalendarView.day:
      return '${a.day} ${monthNames[a.month - 1]} ${a.year}';
    case CalendarView.fourDay:
    case CalendarView.week:
    case CalendarView.schedule:
      final r = viewRange(view, a);
      if (r.from.month == r.to.month && r.from.year == r.to.year) {
        return '${monthNames[r.from.month - 1]} ${r.from.year}';
      }
      String short(DateTime d) => d.month == 9 ? 'Sept' : monthNames[d.month - 1].substring(0, 3);
      return r.from.year == r.to.year
          ? '${short(r.from)} – ${short(r.to)} ${r.to.year}'
          : '${short(r.from)} ${r.from.year} – ${short(r.to)} ${r.to.year}';
  }
}

/// The weeks shown for the month of [anchor]: Monday to Sunday, from the week of the 1st to the
/// week of the last day.
List<List<DateTime>> monthGrid(DateTime anchor) {
  final first = startOfMonth(anchor);
  final last = DateTime(anchor.year, anchor.month + 1, 0);
  return [
    for (var start = startOfWeek(first); !start.isAfter(last); start = addDays(start, 7))
      [for (var i = 0; i < 7; i++) addDays(start, i)],
  ];
}

// --- Times -------------------------------------------------------------------------------

/// Minutes after midnight of `HH:mm` or `HH:mm:ss`.
int minutesOf(String time) {
  final parts = time.split(':');
  return int.parse(parts[0]) * 60 + int.parse(parts[1]);
}

/// `HH:mm` for minutes after midnight, kept within the day.
String timeOf(int minutes) {
  final m = minutes.clamp(0, 23 * 60 + 59);
  return '${(m ~/ 60).toString().padLeft(2, '0')}:${(m % 60).toString().padLeft(2, '0')}';
}

/// "8:30am", "12pm", as Google Calendar writes times.
String formatTime(String time) {
  final m = minutesOf(time);
  final h = m ~/ 60;
  final minute = m % 60;
  final suffix = h < 12 ? 'am' : 'pm';
  final hour = h % 12 == 0 ? 12 : h % 12;
  return minute == 0 ? '$hour$suffix' : '$hour:${minute.toString().padLeft(2, '0')}$suffix';
}

/// "8:30 – 9:30am", "11am – 1pm": the suffix once when both halves share it.
String formatRange(String start, String end) {
  final a = formatTime(start);
  final b = formatTime(end);
  final sameHalf = (minutesOf(start) < 720) == (minutesOf(end) < 720);
  return sameHalf ? '${a.replaceAll(RegExp('am|pm'), '')} – $b' : '$a – $b';
}

/// The label of an hour row: "7 AM", "12 PM".
String hourLabel(int hour) => '${hour % 12 == 0 ? 12 : hour % 12} ${hour < 12 ? 'AM' : 'PM'}';

/// "Saturday, 3 October · 8:30 – 9:30am", or "… · All day"; over several days "Friday, 9 October –
/// Sunday, 11 October", or with times "Friday, 9 October, 6pm – Saturday, 10 October, 9am".
String describeWhen(PlannerItem item) {
  String dayName(String ymd) {
    final d = parseDay(ymd);
    return '${weekdayName(d)}, ${d.day} ${monthNames[d.month - 1]}';
  }

  final start = item.scheduledTime;
  final end = item.endTime;
  if (item.isMultiDay) {
    return start != null && end != null
        ? '${dayName(item.date)}, ${formatTime(start)} – ${dayName(item.lastDay)}, ${formatTime(end)}'
        : '${dayName(item.date)} – ${dayName(item.lastDay)}';
  }
  return '${dayName(item.date)} · ${start != null && end != null ? formatRange(start, end) : 'All day'}';
}

// --- Several days ------------------------------------------------------------------------

/// Drawn as a bar across the top of the time grid: all day, or over several days (timed or not).
bool isBar(PlannerItem item) => item.isAllDay || item.isMultiDay;

/// Days from [a] to [b] (negative when [b] is earlier).
int dayDiff(DateTime a, DateTime b) => DateTime.utc(b.year, b.month, b.day).difference(DateTime.utc(a.year, a.month, a.day)).inDays;

/// Which of its days [day] is, and how many it has: "(Day 2/3)" as Google Calendar titles it.
({int n, int of}) dayOfSpan(PlannerItem item, DateTime day) {
  final first = parseDay(item.date);
  return (n: dayDiff(first, day) + 1, of: dayDiff(first, parseDay(item.lastDay)) + 1);
}

/// Each event under every day it covers (yyyy-MM-dd), so a day's list, count or dot includes an
/// event that began earlier. Days outside [from]…[to] are left out.
Map<String, List<PlannerItem>> eventsByDay(Iterable<PlannerItem> items, DateTime from, DateTime to) {
  final byDay = <String, List<PlannerItem>>{};
  for (final item in items) {
    final first = parseDay(item.date);
    final last = parseDay(item.lastDay);
    for (var d = first.isBefore(from) ? dayOnly(from) : first; !d.isAfter(last) && !d.isAfter(to); d = addDays(d, 1)) {
      (byDay[localYmd(d)] ??= []).add(item);
    }
  }
  return byDay;
}

/// An event drawn as a bar over a row of days: its first column, how many it spans, and its lane.
class PlacedBar {
  PlacedBar(this.item, this.start, this.span, {required this.startsBefore, required this.endsAfter});

  final PlannerItem item;
  final int start;
  final int span;
  int lane = 0;

  /// It began before the first day shown, or goes on after the last: that end is cut square.
  final bool startsBefore;
  final bool endsAfter;
}

/// Places events on a row of days in lanes, as Google Calendar stacks them: the earliest first,
/// the longest first among those, all-day before timed, each in the first lane free over all its
/// days. [days] may skip some (weekends hidden).
List<PlacedBar> layoutBars(List<DateTime> days, Iterable<PlannerItem> items) {
  final ymd = [for (final d in days) localYmd(d)];
  final placed = <PlacedBar>[];
  for (final item in items) {
    final last = item.lastDay;
    final start = ymd.indexWhere((d) => d.compareTo(item.date) >= 0 && d.compareTo(last) <= 0);
    if (start < 0) continue;
    var end = start;
    while (end + 1 < ymd.length && ymd[end + 1].compareTo(last) <= 0) {
      end++;
    }
    placed.add(
      PlacedBar(
        item,
        start,
        end - start + 1,
        startsBefore: item.date.compareTo(ymd[start]) < 0,
        endsAfter: last.compareTo(ymd[end]) > 0,
      ),
    );
  }

  placed.sort((a, b) {
    if (a.start != b.start) return a.start.compareTo(b.start);
    if (a.span != b.span) return b.span.compareTo(a.span);
    if (a.item.isAllDay != b.item.isAllDay) return a.item.isAllDay ? -1 : 1;
    return (a.item.scheduledTime ?? '').compareTo(b.item.scheduledTime ?? '');
  });

  final taken = <List<bool>>[];
  for (final bar in placed) {
    var lane = 0;
    while (lane < taken.length && taken[lane].sublist(bar.start, bar.start + bar.span).any((t) => t)) {
      lane++;
    }
    if (lane == taken.length) taken.add(List.filled(days.length, false));
    for (var i = bar.start; i < bar.start + bar.span; i++) {
      taken[lane][i] = true;
    }
    bar.lane = lane;
  }
  return placed;
}

// --- Dragging ----------------------------------------------------------------------------

/// Dragged events snap to quarter hours, as in Google Calendar.
const snapMinutes = 15;

int snap(num minutes) => (minutes / snapMinutes).round() * snapMinutes;

/// The change that moves a timed event to [date] at [start] (minutes), keeping its length (cut at
/// midnight), in the server's field names.
Map<String, Object?> movedTo(PlannerItem item, DateTime date, int start) {
  final length = minutesOf(item.endTime ?? item.scheduledTime!) - minutesOf(item.scheduledTime!);
  return {
    if (localYmd(date) != item.date) 'date': localYmd(date),
    'scheduledTime': timeOf(start),
    'endTime': timeOf(start + (length > 0 ? length : 60)),
  };
}

// --- Laying out a day --------------------------------------------------------------------

/// A timed event placed in a day column: where it starts and ends, and its share of the width.
class PlacedEvent {
  PlacedEvent(this.item, this.startMinutes, this.endMinutes);

  final PlannerItem item;
  final int startMinutes;
  final int endMinutes;
  int column = 0;
  int columns = 1;
}

/// The shortest an event is drawn, so a 5-minute event can still be read and tapped.
const minEventMinutes = 20;

/// Places a day's timed events side by side where they overlap, as Google Calendar does: each run
/// of overlapping events shares the width, and an event takes the first column free at its start.
/// One over several days is not here: it is a bar across the top.
List<PlacedEvent> layoutDay(List<PlannerItem> items) {
  final timed =
      [
        for (final item in items)
          if (!isBar(item))
            () {
              final start = minutesOf(item.scheduledTime!);
              final end = math.max(
                item.endTime != null ? minutesOf(item.endTime!) : start + 60,
                start + minEventMinutes,
              );
              return PlacedEvent(item, start, math.min(end, 24 * 60));
            }(),
      ]..sort((a, b) {
        final byStart = a.startMinutes.compareTo(b.startMinutes);
        return byStart != 0 ? byStart : b.endMinutes.compareTo(a.endMinutes);
      });

  final placed = <PlacedEvent>[];
  var cluster = <PlacedEvent>[];
  var clusterEnd = -1;
  void close() {
    final columns = cluster.fold(1, (most, e) => math.max(most, e.column + 1));
    for (final e in cluster) {
      e.columns = columns;
    }
    placed.addAll(cluster);
    cluster = [];
  }

  for (final event in timed) {
    if (event.startMinutes >= clusterEnd) close();
    final taken = {
      for (final e in cluster)
        if (e.endMinutes > event.startMinutes) e.column,
    };
    var column = 0;
    while (taken.contains(column)) {
      column++;
    }
    event.column = column;
    cluster.add(event);
    clusterEnd = math.max(clusterEnd, event.endMinutes);
  }
  close();
  return placed;
}

// --- Repeat ------------------------------------------------------------------------------

class RepeatOption {
  const RepeatOption(this.label, this.rule, {this.custom = false});

  final String label;

  /// The rule text; null for "Does not repeat".
  final String? rule;

  /// "Custom…": opens the custom editor.
  final bool custom;
}

const _ordinals = ['first', 'second', 'third', 'fourth'];
const _codes = ['MO', 'TU', 'WE', 'TH', 'FR', 'SA', 'SU'];

/// The day code of a date: MO … SU.
String dayCode(DateTime d) => _codes[d.weekday - 1];

/// Which weekday of its month a day is (1 to 5), and whether it is also the last one.
({int n, bool last}) nthWeekday(DateTime d) => (n: (d.day - 1) ~/ 7 + 1, last: addDays(d, 7).month != d.month);

/// The repeat menu for an event on [day], as Google Calendar offers it. A fifth weekday is not in
/// every month, so it is only offered as "the last".
List<RepeatOption> repeatOptions(DateTime day) {
  final name = weekdayName(day);
  final code = dayCode(day);
  final nth = nthWeekday(day);
  return [
    const RepeatOption('Does not repeat', null),
    const RepeatOption('Daily', 'FREQ=DAILY'),
    RepeatOption('Weekly on $name', 'FREQ=WEEKLY'),
    if (nth.n <= 4) RepeatOption('Monthly on the ${_ordinals[nth.n - 1]} $name', 'FREQ=MONTHLY;BYDAY=${nth.n}$code'),
    if (nth.last) RepeatOption('Monthly on the last $name', 'FREQ=MONTHLY;BYDAY=-1$code'),
    RepeatOption('Annually on ${monthNames[day.month - 1]} ${day.day}', 'FREQ=YEARLY'),
    const RepeatOption('Every weekday (Monday to Friday)', 'FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR'),
    const RepeatOption('Custom…', null, custom: true),
  ];
}

enum RepeatUnit { daily, weekly, monthly, yearly }

enum RepeatEnd { never, on, after }

/// The custom repeat editor's state.
class CustomRepeat {
  CustomRepeat({
    required this.interval,
    required this.unit,
    required this.days,
    required this.byWeekday,
    required this.end,
    required this.until,
    required this.count,
  });

  factory CustomRepeat.forDay(DateTime day) => CustomRepeat(
    interval: 1,
    unit: RepeatUnit.weekly,
    days: {dayCode(day)},
    byWeekday: false,
    end: RepeatEnd.never,
    until: addDays(day, 90),
    count: 10,
  );

  int interval;
  RepeatUnit unit;

  /// Weekly: day codes, MO … SU.
  Set<String> days;

  /// Monthly: on the same weekday of the month (true) or the same day of it (false).
  bool byWeekday;
  RepeatEnd end;
  DateTime until;
  int count;

  /// The rule text for an event first on [day].
  String rule(DateTime day) {
    final parts = ['FREQ=${unit.name.toUpperCase()}'];
    if (interval > 1) parts.add('INTERVAL=$interval');
    if (unit == RepeatUnit.weekly && days.isNotEmpty) {
      parts.add('BYDAY=${_codes.where(days.contains).join(',')}');
    }
    if (unit == RepeatUnit.monthly) {
      final n = nthWeekday(day).n;
      parts.add(byWeekday ? 'BYDAY=${n > 4 ? -1 : n}${dayCode(day)}' : 'BYMONTHDAY=${day.day}');
    }
    if (end == RepeatEnd.on) parts.add('UNTIL=${localYmd(until).replaceAll('-', '')}');
    if (end == RepeatEnd.after) parts.add('COUNT=$count');
    return parts.join(';');
  }

  /// Reads a rule back, for editing an event that already repeats.
  static CustomRepeat parse(String rule, DateTime day) {
    final repeat = CustomRepeat.forDay(day);
    for (final part in rule.replaceFirst(RegExp('^RRULE:', caseSensitive: false), '').split(';')) {
      final kv = part.split('=');
      if (kv.length != 2) continue;
      final value = kv[1];
      switch (kv[0].toUpperCase()) {
        case 'FREQ':
          repeat.unit = RepeatUnit.values.firstWhere(
            (u) => u.name.toUpperCase() == value.toUpperCase(),
            orElse: () => RepeatUnit.weekly,
          );
        case 'INTERVAL':
          repeat.interval = int.tryParse(value) ?? 1;
        case 'BYDAY':
          if (RegExp(r'^-?\d').hasMatch(value)) {
            repeat.byWeekday = true;
          } else {
            repeat.days = value.split(',').toSet();
          }
        case 'UNTIL':
          repeat
            ..end = RepeatEnd.on
            ..until = DateTime(
              int.parse(value.substring(0, 4)),
              int.parse(value.substring(4, 6)),
              int.parse(value.substring(6, 8)),
            );
        case 'COUNT':
          repeat
            ..end = RepeatEnd.after
            ..count = int.tryParse(value) ?? 1;
      }
    }
    return repeat;
  }
}

// --- Notification ------------------------------------------------------------------------

enum NotifyUnit {
  minutes(1),
  hours(60),
  days(1440),
  weeks(10080);

  const NotifyUnit(this.size);

  /// Minutes in one of this unit.
  final int size;
}

/// The longest lead the server accepts: four weeks, as in Google Calendar.
const maxNotifyMinutes = 4 * 10080;

/// The lead a new timed event starts with.
const defaultNotifyMinutes = 30;

/// The time of day an all-day event's notification counts from.
const allDayAlertReference = '09:00';

/// The notification choices, the owner's list. Null is no notification; "Custom…" follows them.
const notifyPresets = <({int? minutes, String label})>[
  (minutes: null, label: 'No notification'),
  (minutes: 5, label: '5 minutes before'),
  (minutes: 10, label: '10 minutes before'),
  (minutes: 15, label: '15 minutes before'),
  (minutes: 25, label: '25 minutes before'),
  (minutes: 30, label: '30 minutes before'),
  (minutes: 60, label: '1 hour before'),
  (minutes: 120, label: '2 hours before'),
  (minutes: 240, label: '4 hours before'),
  (minutes: 480, label: '8 hours before'),
  (minutes: 1440, label: '1 day before'),
];

NotifyUnit largestUnit(int minutes) {
  if (minutes > 0 && minutes % 10080 == 0) return NotifyUnit.weeks;
  if (minutes > 0 && minutes % 1440 == 0) return NotifyUnit.days;
  if (minutes > 0 && minutes % 60 == 0) return NotifyUnit.hours;
  return NotifyUnit.minutes;
}

/// "30 minutes", "1 hour", "2 days", "1 week".
String leadText(int minutes) {
  final unit = largestUnit(minutes);
  final count = minutes ~/ unit.size;
  final name = unit.name.substring(0, unit.name.length - 1);
  return '$count ${count == 1 ? name : unit.name}';
}

String notifyLabel(int? minutes) => switch (minutes) {
  null => 'No notification',
  0 => 'At the start',
  final m => '${leadText(m)} before',
};

/// When the phone would be notified for an event on [day] at [start] (all day counts from 9 am).
DateTime notifyAt(DateTime day, String? start, int minutes) {
  final at = minutesOf(start ?? allDayAlertReference);
  return DateTime(day.year, day.month, day.day, at ~/ 60, at % 60 - minutes);
}

/// "Fri 2 Oct, 10:00 am".
String formatMoment(DateTime at) {
  final hour = at.hour % 12 == 0 ? 12 : at.hour % 12;
  final minute = at.minute.toString().padLeft(2, '0');
  return '${weekdayName(at).substring(0, 3)} ${at.day} ${monthNames[at.month - 1].substring(0, 3)}, '
      '$hour:$minute ${at.hour < 12 ? 'am' : 'pm'}';
}
