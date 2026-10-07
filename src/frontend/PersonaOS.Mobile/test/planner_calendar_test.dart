import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/planner_calendar.dart';

PlannerItem _item(int id, String? start, String? end) => PlannerItem(
  id: id,
  title: 'Event $id',
  date: '2026-10-03',
  scheduledTime: start,
  endTime: end,
  status: 'planned',
  goalTitle: null,
);

void main() {
  // 3 October 2026 is a Saturday.
  final sat = DateTime(2026, 10, 3);

  group('views', () {
    test('weeks run Monday to Sunday', () {
      expect(startOfWeek(sat), DateTime(2026, 9, 28));
      expect(startOfWeek(DateTime(2026, 10, 4)), DateTime(2026, 9, 28));
      final week = viewRange(CalendarView.week, sat);
      expect([week.from, week.to], [DateTime(2026, 9, 28), DateTime(2026, 10, 4)]);
    });

    test('a month shows whole weeks from the week of the 1st to the week of the last day', () {
      final grid = monthGrid(sat);
      expect(grid.first.first, DateTime(2026, 9, 28));
      expect(grid.last.last, DateTime(2026, 11, 1));
      expect(grid.length, 5);
    });

    test('a year loads the weeks spilling over its edges', () {
      final year = viewRange(CalendarView.year, sat);
      expect([year.from, year.to], [DateTime(2025, 12, 29), DateTime(2027, 1, 3)]);
    });

    test('stepping moves by the size of the view, a month from the 31st included', () {
      expect(stepAnchor(CalendarView.day, sat, 1), DateTime(2026, 10, 4));
      expect(stepAnchor(CalendarView.fourDay, sat, -1), DateTime(2026, 9, 29));
      expect(stepAnchor(CalendarView.month, DateTime(2026, 1, 31), 1), DateTime(2026, 2));
      expect(stepAnchor(CalendarView.year, sat, -1), DateTime(2025));
    });

    test('titles read like Google Calendar', () {
      expect(viewTitle(CalendarView.day, sat), '3 October 2026');
      expect(viewTitle(CalendarView.week, sat), 'Sept – Oct 2026');
      expect(viewTitle(CalendarView.week, DateTime(2026, 10, 14)), 'October 2026');
      expect(viewTitle(CalendarView.week, DateTime(2026, 12, 31)), 'Dec 2026 – Jan 2027');
      expect(viewTitle(CalendarView.month, sat), 'October 2026');
      expect(viewTitle(CalendarView.year, sat), '2026');
    });
  });

  group('times', () {
    test('are written the way Google Calendar writes them', () {
      expect(formatTime('08:30:00'), '8:30am');
      expect(formatTime('12:00'), '12pm');
      expect(formatTime('00:15'), '12:15am');
      expect(formatRange('08:30:00', '09:30:00'), '8:30 – 9:30am');
      expect(formatRange('11:00', '13:00'), '11am – 1pm');
      expect(hourLabel(0), '12 AM');
      expect(hourLabel(13), '1 PM');
    });
  });

  group('laying out a day', () {
    test('puts overlapping events side by side and reuses a column once it is free', () {
      final placed = layoutDay([_item(1, '09:00', '11:00'), _item(2, '09:30', '10:00'), _item(3, '10:15', '10:45')]);
      final byId = {for (final p in placed) p.item.id: p};
      expect(byId[1]!.column, 0);
      expect(byId[2]!.column, 1);
      expect(byId[3]!.column, 1);
      expect(placed.every((p) => p.columns == 2), isTrue);
    });

    test('gives events that do not overlap the whole width', () {
      final placed = layoutDay([_item(1, '08:30', '09:30'), _item(2, '12:00', '15:30')]);
      expect(placed.map((p) => p.columns), [1, 1]);
    });

    test('leaves all-day events out and draws a tiny event tall enough to read', () {
      final placed = layoutDay([_item(1, null, null), _item(2, '09:00', '09:05')]);
      expect(placed, hasLength(1));
      expect(placed.single.endMinutes - placed.single.startMinutes, minEventMinutes);
    });
  });

  group('repeat', () {
    test('offers the choices Google Calendar offers for the day', () {
      expect(repeatOptions(sat).map((o) => o.label), [
        'Does not repeat',
        'Daily',
        'Weekly on Saturday',
        'Monthly on the first Saturday',
        'Annually on October 3',
        'Every weekday (Monday to Friday)',
        'Custom…',
      ]);
    });

    test('offers "the last" for a fifth weekday, never "fifth"', () {
      final labels = repeatOptions(DateTime(2026, 10, 31)).map((o) => o.label).toList();
      expect(labels, contains('Monthly on the last Saturday'));
      expect(labels.any((l) => l.contains('fifth') || l.contains('fourth')), isFalse);
    });

    test('builds a custom rule and reads it back', () {
      final repeat = CustomRepeat.forDay(sat)
        ..interval = 2
        ..days = {'SA', 'MO'}
        ..end = RepeatEnd.on
        ..until = DateTime(2026, 12, 31);
      final rule = repeat.rule(sat);
      expect(rule, 'FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,SA;UNTIL=20261231');

      final back = CustomRepeat.parse(rule, sat);
      expect(back.interval, 2);
      expect(back.days, {'MO', 'SA'});
      expect(back.end, RepeatEnd.on);
      expect(back.until, DateTime(2026, 12, 31));
    });

    test('builds a monthly rule on the day or on the weekday', () {
      final monthly = CustomRepeat.forDay(sat)
        ..unit = RepeatUnit.monthly
        ..end = RepeatEnd.after
        ..count = 6;
      expect(monthly.rule(sat), 'FREQ=MONTHLY;BYMONTHDAY=3;COUNT=6');
      monthly.byWeekday = true;
      expect(monthly.rule(DateTime(2026, 10, 31)), 'FREQ=MONTHLY;BYDAY=-1SA;COUNT=6');
    });
  });

  group('notification', () {
    test('says the lead in the largest whole unit', () {
      expect(leadText(25), '25 minutes');
      expect(leadText(60), '1 hour');
      expect(leadText(90), '90 minutes');
      expect(leadText(2880), '2 days');
      expect(leadText(10080), '1 week');
      expect(notifyLabel(null), 'No notification');
    });

    test('works out when the phone is notified, all day counting from 9 am', () {
      expect(notifyAt(sat, '10:00:00', 30), DateTime(2026, 10, 3, 9, 30));
      expect(notifyAt(sat, null, 60), DateTime(2026, 10, 3, 8));
      expect(notifyAt(sat, '10:00', 1440), DateTime(2026, 10, 2, 10));
      expect(formatMoment(DateTime(2026, 10, 2, 10)), 'Fri 2 Oct, 10:00 am');
    });
  });
}
