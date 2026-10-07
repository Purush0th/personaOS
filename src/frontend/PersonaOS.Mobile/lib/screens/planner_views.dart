import 'dart:async';

import 'package:flutter/material.dart';

import '../api/personaos_api.dart';
import '../date_utils.dart';
import '../planner_calendar.dart';
import 'planner_event.dart';

/// The events of the loaded range by day (`yyyy-MM-dd`).
typedef EventsByDay = Map<String, List<PlannerItem>>;

/// The height of one hour in the time grid.
const hourHeight = 52.0;

/// The Day, 4 days and Week views: a column per day, an all-day strip, the hours below with the
/// events placed by time and side by side where they overlap, and a red line at the current
/// time. Tapping an empty slot makes an event at that half hour.
class TimeGridView extends StatefulWidget {
  const TimeGridView({
    super.key,
    required this.days,
    required this.events,
    required this.onOpen,
    required this.onCreate,
    required this.onOpenDay,
  });

  final List<DateTime> days;
  final EventsByDay events;
  final ValueChanged<PlannerItem> onOpen;
  final void Function(DateTime day, String? start) onCreate;
  final ValueChanged<DateTime> onOpenDay;

  @override
  State<TimeGridView> createState() => _TimeGridViewState();
}

class _TimeGridViewState extends State<TimeGridView> {
  late final ScrollController _scroll;
  Timer? _tick;
  DateTime _now = DateTime.now();

  static const _gutter = 44.0;

  @override
  void initState() {
    super.initState();
    // Open on the working morning, or an hour before now when today is shown and it is earlier.
    final showsToday = widget.days.any((d) => sameDay(d, _now));
    final hour = showsToday ? (_now.hour - 1).clamp(0, 7) : 7;
    _scroll = ScrollController(initialScrollOffset: (hour * hourHeight - 8).clamp(0, double.infinity));
    _tick = Timer.periodic(const Duration(minutes: 1), (_) => setState(() => _now = DateTime.now()));
  }

  @override
  void dispose() {
    _tick?.cancel();
    _scroll.dispose();
    super.dispose();
  }

  List<PlannerItem> _on(DateTime day) => widget.events[localYmd(day)] ?? const [];

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final allDay = [for (final d in widget.days) _on(d).where((i) => i.isAllDay).toList()];
    final hasAllDay = allDay.any((l) => l.isNotEmpty);

    return Column(
      children: [
        // Day headings.
        Row(
          children: [
            const SizedBox(width: _gutter),
            for (final day in widget.days)
              Expanded(
                child: InkWell(
                  key: Key('day-head-${localYmd(day)}'),
                  onTap: () => widget.onOpenDay(day),
                  child: Padding(
                    padding: const EdgeInsets.symmetric(vertical: 6),
                    child: Column(
                      children: [
                        Text(
                          shortWeekday(day),
                          style: theme.textTheme.labelSmall?.copyWith(
                            color: sameDay(day, _now) ? scheme.primary : scheme.onSurfaceVariant,
                          ),
                        ),
                        const SizedBox(height: 2),
                        Container(
                          width: 34,
                          height: 34,
                          alignment: Alignment.center,
                          decoration: BoxDecoration(
                            shape: BoxShape.circle,
                            color: sameDay(day, _now) ? scheme.primary : null,
                          ),
                          child: Text(
                            '${day.day}',
                            style: theme.textTheme.titleMedium?.copyWith(
                              color: sameDay(day, _now) ? scheme.onPrimary : null,
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
          ],
        ),
        if (hasAllDay)
          Container(
            decoration: BoxDecoration(
              border: Border(bottom: BorderSide(color: scheme.outlineVariant)),
            ),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const SizedBox(width: _gutter),
                for (var i = 0; i < widget.days.length; i++)
                  Expanded(
                    child: Padding(
                      padding: const EdgeInsets.fromLTRB(1, 0, 1, 4),
                      child: Column(
                        children: [
                          for (final item in allDay[i])
                            _EventChip(
                              item: item,
                              past: dayOnly(widget.days[i]).isBefore(dayOnly(_now)),
                              onTap: () => widget.onOpen(item),
                            ),
                        ],
                      ),
                    ),
                  ),
              ],
            ),
          ),
        Expanded(
          child: SingleChildScrollView(
            controller: _scroll,
            child: SizedBox(
              height: 24 * hourHeight,
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  SizedBox(
                    width: _gutter,
                    child: Stack(
                      children: [
                        for (var h = 1; h < 24; h++)
                          Positioned(
                            top: h * hourHeight - 7,
                            right: 6,
                            child: Text(
                              hourLabel(h),
                              style: theme.textTheme.labelSmall?.copyWith(fontSize: 10, color: scheme.onSurfaceVariant),
                            ),
                          ),
                      ],
                    ),
                  ),
                  for (final day in widget.days) Expanded(child: _dayColumn(context, day)),
                ],
              ),
            ),
          ),
        ),
      ],
    );
  }

  Widget _dayColumn(BuildContext context, DateTime day) {
    final scheme = Theme.of(context).colorScheme;
    final placed = layoutDay(_on(day));
    final isToday = sameDay(day, _now);
    final nowMinutes = _now.hour * 60 + _now.minute;

    return LayoutBuilder(
      builder: (context, constraints) {
        final width = constraints.maxWidth;
        return GestureDetector(
          key: Key('day-column-${localYmd(day)}'),
          behavior: HitTestBehavior.opaque,
          onTapUp: (details) {
            final minutes = (details.localPosition.dy / hourHeight * 2).floor() * 30;
            widget.onCreate(day, timeOf(minutes.clamp(0, 23 * 60)));
          },
          child: Container(
            decoration: BoxDecoration(
              border: Border(left: BorderSide(color: scheme.outlineVariant)),
            ),
            child: Stack(
              children: [
                for (var h = 0; h < 24; h++)
                  Positioned(
                    top: h * hourHeight,
                    left: 0,
                    right: 0,
                    child: Divider(height: 1, thickness: 1, color: scheme.outlineVariant),
                  ),
                for (final e in placed)
                  Positioned(
                    top: e.startMinutes / 60 * hourHeight,
                    height: ((e.endMinutes - e.startMinutes) / 60 * hourHeight - 2).clamp(16, double.infinity),
                    left: width * e.column / e.columns + 1,
                    width: width / e.columns - 3,
                    child: _EventBlock(
                      event: e,
                      narrow: width / e.columns < 72,
                      past: dayOnly(day).isBefore(dayOnly(_now)) || (isToday && e.endMinutes <= nowMinutes),
                      onTap: () => widget.onOpen(e.item),
                    ),
                  ),
                if (isToday)
                  Positioned(
                    top: nowMinutes / 60 * hourHeight - 1,
                    left: 0,
                    right: 0,
                    child: IgnorePointer(
                      child: Row(
                        key: const Key('now-line'),
                        children: [
                          Container(
                            width: 8,
                            height: 8,
                            decoration: const BoxDecoration(color: Color(0xFFEA4335), shape: BoxShape.circle),
                          ),
                          Expanded(child: Container(height: 2, color: const Color(0xFFEA4335))),
                        ],
                      ),
                    ),
                  ),
              ],
            ),
          ),
        );
      },
    );
  }
}

class _EventBlock extends StatelessWidget {
  const _EventBlock({required this.event, required this.past, required this.onTap, this.narrow = false});

  final PlacedEvent event;
  final bool past;

  /// Too narrow for a word to fit: titles stay on one line.
  final bool narrow;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final item = event.item;
    final fg = eventTextColor(context, item);
    final compact = event.endMinutes - event.startMinutes < 45;
    final done = item.status != 'planned';
    // A narrow column shows one line, cut at the edge: wrapping there would break words mid-way.
    final title = Text(
      item.title,
      maxLines: compact || narrow ? 1 : 3,
      softWrap: !narrow,
      overflow: narrow ? TextOverflow.clip : TextOverflow.ellipsis,
      style: TextStyle(
        fontSize: 11,
        fontWeight: FontWeight.w600,
        color: fg,
        decoration: done ? TextDecoration.lineThrough : null,
      ),
    );
    final time = Text(
      compact ? formatTime(item.scheduledTime!) : formatRange(item.scheduledTime!, item.endTime ?? item.scheduledTime!),
      maxLines: 1,
      overflow: TextOverflow.clip,
      style: TextStyle(fontSize: 10, color: fg),
    );
    return Opacity(
      opacity: past ? 0.6 : 1,
      child: Material(
        key: Key('event-${item.id}-${item.occurrenceDate}'),
        color: eventColor(context, item),
        borderRadius: BorderRadius.circular(6),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: onTap,
          child: Padding(
            padding: EdgeInsets.symmetric(horizontal: 4, vertical: compact ? 1 : 3),
            // What does not fit the block is cut at its edge, as on Google Calendar, instead of
            // overflowing: a narrow column or a short event has room for less.
            child: ClipRect(
              child: OverflowBox(
                alignment: Alignment.topLeft,
                maxHeight: double.infinity,
                // A one-line event runs on past the edge and is cut there; a taller one wraps.
                maxWidth: compact ? double.infinity : null,
                child: compact
                    ? Row(mainAxisSize: MainAxisSize.min, children: [title, const SizedBox(width: 4), time])
                    : Column(
                        mainAxisSize: MainAxisSize.min,
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [title, time],
                      ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _EventChip extends StatelessWidget {
  const _EventChip({required this.item, required this.past, required this.onTap});

  final PlannerItem item;
  final bool past;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Opacity(
    opacity: past ? 0.6 : 1,
    child: Padding(
      padding: const EdgeInsets.only(bottom: 2),
      child: Material(
        key: Key('event-${item.id}-${item.occurrenceDate}'),
        color: eventColor(context, item),
        borderRadius: BorderRadius.circular(4),
        child: InkWell(
          onTap: onTap,
          child: Container(
            width: double.infinity,
            padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 2),
            child: Text(
              item.title,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(
                fontSize: 11,
                color: eventTextColor(context, item),
                decoration: item.status != 'planned' ? TextDecoration.lineThrough : null,
              ),
            ),
          ),
        ),
      ),
    ),
  );
}

/// The Month view: whole weeks from the week of the 1st, all-day events as bars, timed ones as a
/// dot and the title, "+N" when a day is full. Tapping a day opens it.
class MonthGridView extends StatelessWidget {
  const MonthGridView({
    super.key,
    required this.anchor,
    required this.events,
    required this.showWeekends,
    required this.onOpen,
    required this.onOpenDay,
  });

  final DateTime anchor;
  final EventsByDay events;
  final bool showWeekends;
  final ValueChanged<PlannerItem> onOpen;
  final ValueChanged<DateTime> onOpenDay;

  static const _maxInCell = 3;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final today = dayOnly(DateTime.now());
    final weeks = [
      for (final week in monthGrid(anchor))
        [
          for (final d in week)
            if (showWeekends || !isWeekend(d)) d,
        ],
    ];

    return Column(
      children: [
        Row(
          children: [
            for (final d in weeks.first)
              Expanded(
                child: Padding(
                  padding: const EdgeInsets.symmetric(vertical: 4),
                  child: Text(
                    shortWeekday(d).substring(0, 1),
                    textAlign: TextAlign.center,
                    style: theme.textTheme.labelSmall?.copyWith(color: scheme.onSurfaceVariant),
                  ),
                ),
              ),
          ],
        ),
        for (final week in weeks)
          Expanded(
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                for (final day in week)
                  Expanded(
                    child: InkWell(
                      key: Key('month-day-${localYmd(day)}'),
                      onTap: () => onOpenDay(day),
                      child: Container(
                        decoration: BoxDecoration(
                          border: Border(
                            top: BorderSide(color: scheme.outlineVariant),
                            left: BorderSide(color: scheme.outlineVariant, width: 0.5),
                          ),
                        ),
                        padding: const EdgeInsets.all(1),
                        child: ClipRect(
                          child: OverflowBox(
                            alignment: Alignment.topCenter,
                            maxHeight: double.infinity,
                            child: _cell(context, day, today),
                          ),
                        ),
                      ),
                    ),
                  ),
              ],
            ),
          ),
      ],
    );
  }

  Widget _cell(BuildContext context, DateTime day, DateTime today) {
    final scheme = Theme.of(context).colorScheme;
    final items = events[localYmd(day)] ?? const <PlannerItem>[];
    final sorted = [...items.where((i) => i.isAllDay), ...items.where((i) => !i.isAllDay)];
    final fits = sorted.length > _maxInCell + 1 ? _maxInCell : sorted.length;
    final isToday = day == today;
    final inMonth = day.month == anchor.month;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Center(
          child: Container(
            width: 22,
            height: 22,
            alignment: Alignment.center,
            decoration: BoxDecoration(shape: BoxShape.circle, color: isToday ? scheme.primary : null),
            child: Text(
              '${day.day}',
              style: TextStyle(
                fontSize: 11,
                color: isToday ? scheme.onPrimary : (inMonth ? scheme.onSurface : scheme.outline),
              ),
            ),
          ),
        ),
        for (final item in sorted.take(fits))
          GestureDetector(
            onTap: () => onOpen(item),
            child: Container(
              key: Key('event-${item.id}-${item.occurrenceDate}'),
              margin: const EdgeInsets.only(top: 1),
              padding: const EdgeInsets.symmetric(horizontal: 2),
              decoration: item.isAllDay
                  ? BoxDecoration(color: eventColor(context, item), borderRadius: BorderRadius.circular(3))
                  : null,
              child: Row(
                children: [
                  if (!item.isAllDay)
                    Container(
                      width: 5,
                      height: 5,
                      margin: const EdgeInsets.only(right: 2),
                      decoration: BoxDecoration(
                        color: item.goalKey == null ? scheme.primary : eventColor(context, item),
                        shape: BoxShape.circle,
                      ),
                    ),
                  Expanded(
                    child: Text(
                      item.title,
                      maxLines: 1,
                      overflow: TextOverflow.clip,
                      softWrap: false,
                      style: TextStyle(
                        fontSize: 9.5,
                        color: item.isAllDay ? eventTextColor(context, item) : scheme.onSurface,
                        decoration: item.status != 'planned' ? TextDecoration.lineThrough : null,
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
        if (sorted.length > fits)
          Text('+${sorted.length - fits}', style: TextStyle(fontSize: 9.5, color: scheme.onSurfaceVariant)),
      ],
    );
  }
}

/// The Year view: twelve small months; a dot under a day with events. Tapping a day opens it.
class YearGridView extends StatelessWidget {
  const YearGridView({super.key, required this.anchor, required this.events, required this.onOpenDay});

  final DateTime anchor;
  final EventsByDay events;
  final ValueChanged<DateTime> onOpenDay;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final today = dayOnly(DateTime.now());
    return LayoutBuilder(
      builder: (context, constraints) {
        final columns = (constraints.maxWidth / 170).floor().clamp(2, 4);
        return GridView.builder(
          padding: const EdgeInsets.all(8),
          gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(crossAxisCount: columns, childAspectRatio: 0.92),
          itemCount: 12,
          itemBuilder: (context, month) {
            final first = DateTime(anchor.year, month + 1);
            return Padding(
              padding: const EdgeInsets.all(4),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(monthNames[month], style: theme.textTheme.titleSmall),
                  const SizedBox(height: 4),
                  Row(
                    children: [
                      for (final letter in const ['M', 'T', 'W', 'T', 'F', 'S', 'S'])
                        Expanded(
                          child: Text(
                            letter,
                            textAlign: TextAlign.center,
                            style: TextStyle(fontSize: 9, color: scheme.onSurfaceVariant),
                          ),
                        ),
                    ],
                  ),
                  for (final week in monthGrid(first))
                    Expanded(
                      child: Row(
                        children: [
                          for (final day in week)
                            Expanded(
                              child: InkWell(
                                key: day.month == first.month ? Key('year-day-${localYmd(day)}') : null,
                                onTap: () => onOpenDay(day),
                                customBorder: const CircleBorder(),
                                child: Container(
                                  alignment: Alignment.center,
                                  decoration: BoxDecoration(
                                    shape: BoxShape.circle,
                                    color: day == today && day.month == first.month ? scheme.primary : null,
                                  ),
                                  child: Column(
                                    mainAxisAlignment: MainAxisAlignment.center,
                                    children: [
                                      Text(
                                        '${day.day}',
                                        style: TextStyle(
                                          fontSize: 10,
                                          color: day.month != first.month
                                              ? scheme.outline
                                              : day == today
                                              ? scheme.onPrimary
                                              : scheme.onSurface,
                                        ),
                                      ),
                                      if (day.month == first.month &&
                                          (events[localYmd(day)]?.isNotEmpty ?? false) &&
                                          day != today)
                                        Container(
                                          width: 3,
                                          height: 3,
                                          decoration: BoxDecoration(color: scheme.primary, shape: BoxShape.circle),
                                        ),
                                    ],
                                  ),
                                ),
                              ),
                            ),
                        ],
                      ),
                    ),
                ],
              ),
            );
          },
        );
      },
    );
  }
}

/// The Schedule view: the days ahead that have events, as a list. Empty days are left out.
class ScheduleListView extends StatelessWidget {
  const ScheduleListView({
    super.key,
    required this.days,
    required this.events,
    required this.onOpen,
    required this.onOpenDay,
  });

  final List<DateTime> days;
  final EventsByDay events;
  final ValueChanged<PlannerItem> onOpen;
  final ValueChanged<DateTime> onOpenDay;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final today = dayOnly(DateTime.now());
    final busy = days.where((d) => events[localYmd(d)]?.isNotEmpty ?? false).toList();
    if (busy.isEmpty) {
      return ListView(
        children: [
          Padding(
            padding: const EdgeInsets.all(32),
            child: Text(
              'Nothing planned for these ${days.length} days.',
              textAlign: TextAlign.center,
              style: TextStyle(color: scheme.onSurfaceVariant),
            ),
          ),
        ],
      );
    }
    return ListView.builder(
      itemCount: busy.length,
      itemBuilder: (context, i) {
        final day = busy[i];
        final isToday = day == today;
        return Padding(
          padding: const EdgeInsets.symmetric(vertical: 6),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              InkWell(
                onTap: () => onOpenDay(day),
                child: SizedBox(
                  width: 64,
                  child: Column(
                    children: [
                      Text(
                        shortWeekday(day),
                        style: theme.textTheme.labelSmall?.copyWith(
                          color: isToday ? scheme.primary : scheme.onSurfaceVariant,
                        ),
                      ),
                      Container(
                        width: 34,
                        height: 34,
                        alignment: Alignment.center,
                        decoration: BoxDecoration(shape: BoxShape.circle, color: isToday ? scheme.primary : null),
                        child: Text(
                          '${day.day}',
                          style: theme.textTheme.titleMedium?.copyWith(color: isToday ? scheme.onPrimary : null),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
              Expanded(
                child: Column(
                  children: [
                    for (final item in events[localYmd(day)]!)
                      Padding(
                        padding: const EdgeInsets.fromLTRB(0, 2, 12, 2),
                        child: Material(
                          key: Key('event-${item.id}-${item.occurrenceDate}'),
                          color: eventColor(context, item),
                          borderRadius: BorderRadius.circular(8),
                          child: InkWell(
                            onTap: () => onOpen(item),
                            child: Padding(
                              padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
                              child: Row(
                                children: [
                                  Expanded(
                                    child: Column(
                                      crossAxisAlignment: CrossAxisAlignment.start,
                                      children: [
                                        Text(
                                          item.title,
                                          style: theme.textTheme.bodyMedium?.copyWith(
                                            color: eventTextColor(context, item),
                                            fontWeight: FontWeight.w600,
                                            decoration: item.status != 'planned' ? TextDecoration.lineThrough : null,
                                          ),
                                        ),
                                        Text(
                                          item.isAllDay
                                              ? 'All day'
                                              : formatRange(item.scheduledTime!, item.endTime ?? item.scheduledTime!),
                                          style: theme.textTheme.bodySmall?.copyWith(
                                            color: eventTextColor(context, item),
                                          ),
                                        ),
                                      ],
                                    ),
                                  ),
                                  if (item.repeats) Icon(Icons.repeat, size: 16, color: eventTextColor(context, item)),
                                ],
                              ),
                            ),
                          ),
                        ),
                      ),
                  ],
                ),
              ),
            ],
          ),
        );
      },
    );
  }
}
