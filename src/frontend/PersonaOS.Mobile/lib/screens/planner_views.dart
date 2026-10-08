import 'dart:async';
import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../api/personaos_api.dart';
import '../date_utils.dart';
import '../planner_calendar.dart';
import 'planner_event.dart';

/// The events of the loaded range by day (`yyyy-MM-dd`); one over several days is under each.
typedef EventsByDay = Map<String, List<PlannerItem>>;

/// An event dragged somewhere else or made longer or shorter: the event and the change, in the
/// server's field names.
typedef EventMoved = void Function(PlannerItem item, Map<String, Object?> change);

/// The height of one hour in the time grid.
const hourHeight = 52.0;

/// The height of one lane of bars in the strip above the hours.
const _barHeight = 20.0;

enum _DragKind { move, resize, bar }

/// A long-press drag in progress: what is dragged, how, and where it would land now.
class _Drag {
  _Drag({
    required this.kind,
    required this.item,
    required this.fromDay,
    required this.origin,
    required this.startMinutes,
    required this.endMinutes,
  }) : day = fromDay,
       start = startMinutes,
       end = endMinutes;

  final _DragKind kind;
  final PlannerItem item;

  /// The day under the finger when the press began, and the global point it began at.
  final DateTime fromDay;
  final Offset origin;
  final int startMinutes;
  final int endMinutes;

  DateTime day;
  int start;
  int end;
}

/// The Day, 4 days and Week views: a column per day, a strip on top for all-day events and events
/// over several days (bars across their days), the hours below with the events placed by time and
/// side by side where they overlap, and a red line at the current time. Tapping an empty slot
/// makes an event at that half hour.
///
/// As in Google Calendar's phone app, a long press picks an event up: drag it to another time or
/// day, or pick it up by its bottom edge to change the end; a bar on top moves along the days.
class TimeGridView extends StatefulWidget {
  const TimeGridView({
    super.key,
    required this.days,
    required this.items,
    required this.events,
    required this.onOpen,
    required this.onCreate,
    required this.onOpenDay,
    required this.onMove,
  });

  final List<DateTime> days;

  /// The visible events of the loaded range, once each.
  final List<PlannerItem> items;
  final EventsByDay events;
  final ValueChanged<PlannerItem> onOpen;
  final void Function(DateTime day, String? start) onCreate;
  final ValueChanged<DateTime> onOpenDay;
  final EventMoved onMove;

  @override
  State<TimeGridView> createState() => _TimeGridViewState();
}

class _TimeGridViewState extends State<TimeGridView> {
  late final ScrollController _scroll;
  final _headings = GlobalKey();
  Timer? _tick;
  DateTime _now = DateTime.now();
  _Drag? _drag;

  static const _gutter = 44.0;

  /// The bottom of a timed event that picks up its end rather than the whole event.
  static const _edge = 14.0;

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

  // --- Dragging ------------------------------------------------------------------------

  /// The day column under a point on the screen (the nearest one at the edges).
  DateTime _dayAt(Offset global) {
    final box = _headings.currentContext!.findRenderObject()! as RenderBox;
    final local = box.globalToLocal(global);
    final width = (box.size.width - _gutter) / widget.days.length;
    final column = ((local.dx - _gutter) / width).floor().clamp(0, widget.days.length - 1);
    return widget.days[column];
  }

  void _pickUp(_DragKind kind, PlannerItem item, Offset global, {int start = 0, int end = 0}) {
    unawaited(HapticFeedback.mediumImpact());
    setState(
      () => _drag = _Drag(
        kind: kind,
        item: item,
        fromDay: _dayAt(global),
        origin: global,
        startMinutes: start,
        endMinutes: end,
      ),
    );
  }

  void _dragTo(Offset global) {
    final drag = _drag;
    if (drag == null) return;
    final shift = snap((global.dy - drag.origin.dy) / hourHeight * 60);
    final length = drag.endMinutes - drag.startMinutes;
    setState(() {
      switch (drag.kind) {
        case _DragKind.bar:
          drag.day = _dayAt(global);
        case _DragKind.move:
          drag.day = _dayAt(global);
          drag.start = (drag.startMinutes + shift).clamp(0, 24 * 60 - snapMinutes);
          drag.end = math.min(drag.start + length, 24 * 60 - 1);
        case _DragKind.resize:
          drag.end = (drag.endMinutes + shift).clamp(drag.startMinutes + snapMinutes, 24 * 60 - 1);
      }
    });
  }

  void _drop() {
    final drag = _drag;
    setState(() => _drag = null);
    if (drag == null) return;
    final item = drag.item;
    switch (drag.kind) {
      case _DragKind.bar:
        final shift = dayDiff(drag.fromDay, drag.day);
        if (shift != 0) widget.onMove(item, {'date': localYmd(addDays(parseDay(item.date), shift))});
      case _DragKind.move:
        if (!sameDay(drag.day, drag.fromDay) || drag.start != drag.startMinutes) {
          widget.onMove(item, movedTo(item, drag.day, drag.start));
        }
      case _DragKind.resize:
        if (drag.end != drag.endMinutes) widget.onMove(item, {'endTime': timeOf(drag.end)});
    }
  }

  void _cancel() => setState(() => _drag = null);

  bool _lifted(PlannerItem item) => identical(_drag?.item, item);

  // --- Layout --------------------------------------------------------------------------

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Column(
      children: [
        // Day headings.
        Row(
          key: _headings,
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
        _strip(context),
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

  /// All-day events and events over several days, as bars across the days they cover.
  Widget _strip(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final bars = layoutBars(widget.days, widget.items.where(isBar));
    if (bars.isEmpty) return const SizedBox.shrink();
    final lanes = bars.fold(0, (most, b) => math.max(most, b.lane + 1));
    final today = dayOnly(_now);

    // Where a dragged bar would land: the same lane, shifted by the days dragged.
    PlacedBar? ghost;
    final drag = _drag;
    if (drag != null && drag.kind == _DragKind.bar) {
      final shift = dayDiff(drag.fromDay, drag.day);
      final item = drag.item;
      final moved = item.copyWith(
        date: localYmd(addDays(parseDay(item.date), shift)),
        endDate: localYmd(addDays(parseDay(item.lastDay), shift)),
      );
      ghost = layoutBars(widget.days, [moved]).firstOrNull;
      ghost?.lane = bars.where((b) => identical(b.item, item)).firstOrNull?.lane ?? 0;
    }

    return Container(
      height: lanes * (_barHeight + 2) + 4,
      decoration: BoxDecoration(
        border: Border(bottom: BorderSide(color: scheme.outlineVariant)),
      ),
      child: LayoutBuilder(
        builder: (context, constraints) {
          final width = (constraints.maxWidth - _gutter) / widget.days.length;
          Positioned place(PlacedBar bar, Widget child) => Positioned(
            left: _gutter + bar.start * width + (bar.startsBefore ? 0 : 1),
            width: bar.span * width - (bar.startsBefore ? 0 : 1) - (bar.endsAfter ? 0 : 2),
            top: bar.lane * (_barHeight + 2),
            height: _barHeight,
            child: child,
          );
          return Stack(
            children: [
              for (var i = 0; i < widget.days.length; i++)
                Positioned(
                  left: _gutter + i * width,
                  top: 0,
                  bottom: 0,
                  child: VerticalDivider(width: 1, thickness: 1, color: scheme.outlineVariant),
                ),
              for (final bar in bars)
                place(
                  bar,
                  GestureDetector(
                    onLongPressStart: (d) => _pickUp(_DragKind.bar, bar.item, d.globalPosition),
                    onLongPressMoveUpdate: (d) => _dragTo(d.globalPosition),
                    onLongPressEnd: (_) => _drop(),
                    onLongPressCancel: _cancel,
                    child: Opacity(
                      opacity: _lifted(bar.item) ? 0.35 : 1,
                      child: _Bar(
                        bar: bar,
                        past: parseDay(bar.item.lastDay).isBefore(today),
                        onTap: () => widget.onOpen(bar.item),
                      ),
                    ),
                  ),
                ),
              if (ghost != null) place(ghost, IgnorePointer(child: _Ghost(title: ghost.item.title))),
            ],
          );
        },
      ),
    );
  }

  Widget _dayColumn(BuildContext context, DateTime day) {
    final scheme = Theme.of(context).colorScheme;
    final placed = layoutDay(_on(day));
    final isToday = sameDay(day, _now);
    final nowMinutes = _now.hour * 60 + _now.minute;
    final drag = _drag;
    final ghost = drag != null && drag.kind != _DragKind.bar && sameDay(drag.day, day) ? drag : null;

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
                  () {
                    final height = ((e.endMinutes - e.startMinutes) / 60 * hourHeight - 2).clamp(16.0, double.infinity);
                    final start = minutesOf(e.item.scheduledTime!);
                    final end = e.item.endTime == null ? start + 60 : minutesOf(e.item.endTime!);
                    return Positioned(
                      top: e.startMinutes / 60 * hourHeight,
                      height: height,
                      left: width * e.column / e.columns + 1,
                      width: width / e.columns - 3,
                      child: GestureDetector(
                        // Held near its bottom edge, the end moves; anywhere else, the whole event.
                        onLongPressStart: (d) => _pickUp(
                          height >= 2 * _edge && d.localPosition.dy >= height - _edge ? _DragKind.resize : _DragKind.move,
                          e.item,
                          d.globalPosition,
                          start: start,
                          end: end > start ? end : start + 60,
                        ),
                        onLongPressMoveUpdate: (d) => _dragTo(d.globalPosition),
                        onLongPressEnd: (_) => _drop(),
                        onLongPressCancel: _cancel,
                        child: Opacity(
                          opacity: _lifted(e.item) ? 0.35 : 1,
                          child: _EventBlock(
                            event: e,
                            narrow: width / e.columns < 72,
                            past: dayOnly(day).isBefore(dayOnly(_now)) || (isToday && e.endMinutes <= nowMinutes),
                            onTap: () => widget.onOpen(e.item),
                          ),
                        ),
                      ),
                    );
                  }(),
                if (ghost != null)
                  Positioned(
                    top: ghost.start / 60 * hourHeight,
                    height: ((ghost.end - ghost.start) / 60 * hourHeight - 2).clamp(16.0, double.infinity),
                    left: 1,
                    right: 2,
                    child: IgnorePointer(
                      child: _Ghost(
                        key: const Key('drag-ghost'),
                        title: ghost.item.title,
                        time: formatRange(timeOf(ghost.start), timeOf(ghost.end)),
                      ),
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

/// An event in the strip on top: a bar over its days. An end that runs past the days shown is
/// square, as Google Calendar draws it.
class _Bar extends StatelessWidget {
  const _Bar({required this.bar, required this.past, required this.onTap});

  final PlacedBar bar;
  final bool past;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final item = bar.item;
    final fg = eventTextColor(context, item);
    const round = Radius.circular(4);
    return Opacity(
      opacity: past ? 0.6 : 1,
      child: Material(
        key: Key('event-${item.id}-${item.occurrenceDate}'),
        color: eventColor(context, item),
        borderRadius: BorderRadius.horizontal(
          left: bar.startsBefore ? Radius.zero : round,
          right: bar.endsAfter ? Radius.zero : round,
        ),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: onTap,
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 4),
            child: Align(
              alignment: Alignment.centerLeft,
              child: Text(
                // A timed event over several days says when it starts, on the day it starts.
                item.scheduledTime != null && !bar.startsBefore
                    ? '${formatTime(item.scheduledTime!)} ${item.title}'
                    : item.title,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(
                  fontSize: 11,
                  color: fg,
                  decoration: item.status != 'planned' ? TextDecoration.lineThrough : null,
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// Where a dragged event would land.
class _Ghost extends StatelessWidget {
  const _Ghost({super.key, required this.title, this.time});

  final String title;
  final String? time;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Material(
      elevation: 6,
      color: scheme.primaryContainer,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(6),
        side: BorderSide(color: scheme.primary, width: 2),
      ),
      clipBehavior: Clip.antiAlias,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 2),
        child: ClipRect(
          child: OverflowBox(
            alignment: Alignment.topLeft,
            maxHeight: double.infinity,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  title,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(fontSize: 11, fontWeight: FontWeight.w600, color: scheme.onPrimaryContainer),
                ),
                if (time != null)
                  Text(time!, maxLines: 1, style: TextStyle(fontSize: 10, color: scheme.onPrimaryContainer)),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// The Month view: whole weeks from the week of the 1st. An all-day event, or one over several
/// days, is a bar across its days; a timed one is a dot and its title; "+N" when a day is full.
/// Tapping a day opens it.
class MonthGridView extends StatelessWidget {
  const MonthGridView({
    super.key,
    required this.anchor,
    required this.items,
    required this.showWeekends,
    required this.onOpen,
    required this.onOpenDay,
  });

  final DateTime anchor;

  /// The visible events of the loaded range, once each.
  final List<PlannerItem> items;
  final bool showWeekends;
  final ValueChanged<PlannerItem> onOpen;
  final ValueChanged<DateTime> onOpenDay;

  static const _numberHeight = 24.0;
  static const _laneHeight = 14.0;

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
        for (final week in weeks) Expanded(child: _week(context, week, today)),
      ],
    );
  }

  Widget _week(BuildContext context, List<DateTime> days, DateTime today) {
    final scheme = Theme.of(context).colorScheme;
    final bars = layoutBars(days, items);
    final used = bars.fold(0, (most, b) => math.max(most, b.lane + 1));

    return LayoutBuilder(
      builder: (context, constraints) {
        final width = constraints.maxWidth / days.length;
        // As many lanes as fit; when they do not all fit, one row is left for "+N".
        final fit = math.max(0, ((constraints.maxHeight - _numberHeight) / _laneHeight).floor());
        final lanes = used <= fit ? used : math.max(0, fit - 1);
        final more = [
          for (var i = 0; i < days.length; i++)
            bars.where((b) => b.lane >= lanes && b.start <= i && i < b.start + b.span).length,
        ];

        return Stack(
          fit: StackFit.expand,
          children: [
            Row(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                for (final day in days)
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
                        alignment: Alignment.topCenter,
                        padding: const EdgeInsets.only(top: 1),
                        child: Container(
                          width: 22,
                          height: 22,
                          alignment: Alignment.center,
                          decoration: BoxDecoration(shape: BoxShape.circle, color: day == today ? scheme.primary : null),
                          child: Text(
                            '${day.day}',
                            style: TextStyle(
                              fontSize: 11,
                              color: day == today
                                  ? scheme.onPrimary
                                  : (day.month == anchor.month ? scheme.onSurface : scheme.outline),
                            ),
                          ),
                        ),
                      ),
                    ),
                  ),
              ],
            ),
            for (final bar in bars.where((b) => b.lane < lanes))
              Positioned(
                left: bar.start * width + (bar.startsBefore ? 0 : 1),
                width: bar.span * width - (bar.startsBefore ? 0 : 1) - (bar.endsAfter ? 0 : 2),
                top: _numberHeight + bar.lane * _laneHeight,
                height: _laneHeight - 1,
                child: GestureDetector(
                  onTap: () => onOpen(bar.item),
                  child: _monthEvent(context, bar, today),
                ),
              ),
            for (var i = 0; i < days.length; i++)
              if (more[i] > 0)
                Positioned(
                  left: i * width + 2,
                  width: width - 2,
                  top: _numberHeight + lanes * _laneHeight,
                  child: IgnorePointer(
                    child: Text('+${more[i]}', style: TextStyle(fontSize: 9.5, color: scheme.onSurfaceVariant)),
                  ),
                ),
          ],
        );
      },
    );
  }

  Widget _monthEvent(BuildContext context, PlacedBar bar, DateTime today) {
    final scheme = Theme.of(context).colorScheme;
    final item = bar.item;
    final filled = isBar(item);
    const round = Radius.circular(3);
    return Opacity(
      opacity: parseDay(item.lastDay).isBefore(today) ? 0.6 : 1,
      child: Container(
        key: Key('event-${item.id}-${item.occurrenceDate}'),
        padding: const EdgeInsets.symmetric(horizontal: 2),
        decoration: filled
            ? BoxDecoration(
                color: eventColor(context, item),
                borderRadius: BorderRadius.horizontal(
                  left: bar.startsBefore ? Radius.zero : round,
                  right: bar.endsAfter ? Radius.zero : round,
                ),
              )
            : null,
        child: Row(
          children: [
            if (!filled)
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
                  color: filled ? eventTextColor(context, item) : scheme.onSurface,
                  decoration: item.status != 'planned' ? TextDecoration.lineThrough : null,
                ),
              ),
            ),
          ],
        ),
      ),
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

/// The Schedule view: the days ahead that have events, as a list. Empty days are left out; an
/// event over several days is on each of them, titled "(Day 2/3)".
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

  /// "8:30 – 9:30am", "All day"; over several days "From 6pm" on the first, "Until 9am" on the last.
  static String _time(PlannerItem item, DateTime day) {
    final start = item.scheduledTime;
    final end = item.endTime;
    if (start == null || end == null) return 'All day';
    if (!item.isMultiDay) return formatRange(start, end);
    if (localYmd(day) == item.date) return 'From ${formatTime(start)}';
    if (localYmd(day) == item.lastDay) return 'Until ${formatTime(end)}';
    return 'All day';
  }

  static String _title(PlannerItem item, DateTime day) {
    if (!item.isMultiDay) return item.title;
    final span = dayOfSpan(item, day);
    return '${item.title} (Day ${span.n}/${span.of})';
  }

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
                          key: Key('event-${item.id}-${item.occurrenceDate}-${localYmd(day)}'),
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
                                          _title(item, day),
                                          style: theme.textTheme.bodyMedium?.copyWith(
                                            color: eventTextColor(context, item),
                                            fontWeight: FontWeight.w600,
                                            decoration: item.status != 'planned' ? TextDecoration.lineThrough : null,
                                          ),
                                        ),
                                        Text(
                                          _time(item, day),
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
