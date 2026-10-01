import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../api/personaos_api.dart';
import '../goal_calendar.dart';
import '../goal_tree.dart';
import 'goal_view.dart';

/// The Timeline tab of Goals: the goals as a roadmap for one year, like the web's: a bar per goal on a month axis,
/// years above their quarters above their months, each labelled with its title and filled to its
/// progress. Standalone quarters and months sit at their own level. Goals with child goals fold.
/// The year scrolls sideways under a pinned label column and opens a month before today. A bar
/// opens the goal's details.
class TimelineView extends StatefulWidget {
  const TimelineView({super.key, required this.goals, this.today, this.api});

  /// When given, a bar opens the goal's full quick view (with its comments) instead of a summary.
  final PersonaOsApi? api;

  /// The goals the Goals tab loaded, so both tabs show the same list.
  final Future<List<Goal>> goals;

  /// The local today; the device's own when null. Tests pin it.
  final DateTime? today;

  @override
  State<TimelineView> createState() => _TimelineViewState();
}

/// The narrowest a month gets on the axis: room for its name and a month bar's "40%". On a wider
/// screen (a tablet, a phone on its side) months grow so the year fills the width.
const double monthWidth = 48;
const double _labelWidth = 128;

/// The label column on a wide screen, where titles have room to be read whole.
const double _wideLabelWidth = 220;
const double _rowHeight = 40;
const double _axisHeight = 44;

const _levels = {'year': 0, 'quarter': 1, 'month': 2};

/// Swim lanes: a top-level goal and everything under it share a band, with a gap between bands.
const double _laneGap = 10;
const double _lanePad = 4;

class _TimelineViewState extends State<TimelineView> {
  late Future<List<Goal>> _goals;
  late int _year = _today.year;
  final Set<int> _collapsed = {};
  final _scroll = ScrollController();
  bool _scrolledToToday = false;

  /// One month's width and the label column's, for the width the chart was last laid out in.
  double _month = monthWidth;
  double _labels = _labelWidth;

  DateTime get _today {
    final now = widget.today ?? DateTime.now();
    return DateTime(now.year, now.month, now.day);
  }

  @override
  void initState() {
    super.initState();
    _goals = widget.goals;
  }

  @override
  void didUpdateWidget(TimelineView old) {
    super.didUpdateWidget(old);
    if (old.goals != widget.goals) _goals = widget.goals;
  }

  @override
  void dispose() {
    _scroll.dispose();
    super.dispose();
  }

  List<TreeRow> _rows(List<Goal> goals) => treeRows(goals.where((g) => g.periodEnd?.year == _year).toList());

  /// Where [day] starts on the axis, in pixels from 1 January; with [end], where it ends.
  double _x(DateTime day, {bool end = false}) {
    final daysInMonth = DateTime(day.year, day.month + 1, 0).day;
    return (day.month - 1 + (day.day - (end ? 0 : 1)) / daysInMonth) * _month;
  }

  void _scrollToToday() {
    if (_scrolledToToday || _year != _today.year) return;
    _scrolledToToday = true;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!_scroll.hasClients) return;
      final target = (_x(_today) - _month).clamp(0.0, _scroll.position.maxScrollExtent);
      _scroll.jumpTo(target);
    });
  }

  Future<void> _details(Goal goal) {
    final theme = Theme.of(context);
    return showModalBottomSheet<void>(
      context: context,
      showDragHandle: true,
      builder: (context) => Padding(
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('${goal.key} · ${periodLabels[goal.periodType]} · ${goal.slot}',
                style: theme.textTheme.labelMedium?.copyWith(color: theme.colorScheme.outline)),
            const SizedBox(height: 4),
            Text(goal.title, style: theme.textTheme.titleMedium),
            if (goal.periodStart != null && goal.periodEnd != null)
              Text(formatGoalRange(goal.periodStart!, goal.periodEnd!), style: theme.textTheme.bodySmall),
            const SizedBox(height: 12),
            LinearProgressIndicator(value: goal.effectiveProgress / 100, minHeight: 6),
            const SizedBox(height: 4),
            Text('${goal.effectiveProgress}%${goal.status == 'completed' ? ' · completed' : ''}',
                style: theme.textTheme.bodySmall),
            for (final child in goal.children)
              Padding(
                padding: const EdgeInsets.only(top: 8),
                child: Text('${child.key} ${child.title} · ${child.slot} · ${child.effectiveProgress}%'),
              ),
            for (final task in goal.tasks)
              Padding(
                padding: const EdgeInsets.only(top: 8),
                child: Text('${task.key} ${task.title} · ${BoardColumns.label(task.column)}'),
              ),
          ],
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return FutureBuilder<List<Goal>>(
      future: _goals,
      builder: (context, snapshot) {
        final goals = snapshot.data ?? const <Goal>[];
        final years = {_today.year, ...goals.map((g) => g.periodEnd?.year).whereType<int>()}.toList()..sort();
        final rows = _rows(goals);
        final visible = unfolded(rows, _collapsed);
        final anyCollapsed = _collapsed.isNotEmpty;

        // The tab's own controls: fold everything, and pick the year.
        final toolbar = Padding(
          padding: const EdgeInsets.fromLTRB(8, 4, 8, 0),
          child: Row(
            children: [
              const Spacer(),
              if (rows.any((r) => r.hasChildren))
                IconButton(
                  tooltip: anyCollapsed ? 'Expand all' : 'Collapse all',
                  icon: Icon(anyCollapsed ? Icons.unfold_more : Icons.unfold_less),
                  onPressed: () => setState(() {
                    if (anyCollapsed) {
                      _collapsed.clear();
                    } else {
                      _collapsed.addAll(rows.where((r) => r.hasChildren).map((r) => r.goal.id));
                    }
                  }),
                ),
              Padding(
                padding: const EdgeInsets.only(right: 8),
                child: DropdownButton<int>(
                  key: const Key('timeline-year'),
                  value: _year,
                  underline: const SizedBox.shrink(),
                  items: [for (final y in years) DropdownMenuItem(value: y, child: Text('$y'))],
                  onChanged: (y) => setState(() {
                    _year = y!;
                    _collapsed.clear();
                    if (_scroll.hasClients) _scroll.jumpTo(0);
                  }),
                ),
              ),
            ],
          ),
        );

        return switch (snapshot.connectionState) {
          ConnectionState.done when snapshot.hasError => const Center(child: Text('Could not load goals.')),
          ConnectionState.done => Column(
              children: [
                toolbar,
                Expanded(
                  child: visible.isEmpty
                      ? Center(child: Padding(padding: const EdgeInsets.all(32), child: Text('No goals in $_year.')))
                      : LayoutBuilder(builder: (context, constraints) {
                          // Months share the width left beside the labels, never below their minimum;
                          // a narrow phone scrolls the year sideways instead.
                          const padding = 16.0;
                          final available = constraints.maxWidth - padding;
                          // Wider labels only where the whole year still fits beside them.
                          _labels = available - _wideLabelWidth >= monthWidth * 12 ? _wideLabelWidth : _labelWidth;
                          _month = math.max(monthWidth, (available - _labels) / 12);
                          return _chart(context, visible);
                        }),
                ),
              ],
            ),
          _ => const Center(child: CircularProgressIndicator()),
        };
      },
    );
  }

  Widget _chart(BuildContext context, List<TreeRow> rows) {
    final theme = Theme.of(context);
    _scrollToToday();

    // Consecutive rows of one lane, drawn as one rounded band on each side of the chart.
    final lanes = byLane(rows);
    // See-through, so the month lines painted under the year still show on the band.
    final fill = theme.colorScheme.onSurface.withValues(alpha: 0.06);
    Widget band(List<Widget> children, BorderRadius radius) => DecoratedBox(
          decoration: BoxDecoration(color: fill, borderRadius: radius),
          child: Padding(padding: const EdgeInsets.symmetric(vertical: _lanePad), child: Column(children: children)),
        );
    final height = _axisHeight + rows.length * _rowHeight + lanes.length * 2 * _lanePad + (lanes.length - 1) * _laneGap;
    const left = BorderRadius.horizontal(left: Radius.circular(8));
    const right = BorderRadius.horizontal(right: Radius.circular(8));

    return SingleChildScrollView(
      padding: const EdgeInsets.fromLTRB(8, 8, 8, 24),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // Labels stay put while the year scrolls sideways beside them.
          SizedBox(
            width: _labels,
            child: Column(
              children: [
                const SizedBox(height: _axisHeight),
                for (final (i, lane) in lanes.indexed) ...[
                  if (i > 0) const SizedBox(height: _laneGap),
                  band([for (final row in lane) _label(theme, row)], left),
                ],
              ],
            ),
          ),
          Expanded(
            child: SingleChildScrollView(
              key: const Key('timeline-scroll'),
              controller: _scroll,
              scrollDirection: Axis.horizontal,
              child: SizedBox(
                width: _month * 12,
                height: height,
                child: CustomPaint(
                  painter: _GridPainter(
                    monthWidth: _month,
                    lineColor: theme.colorScheme.outlineVariant,
                    todayColor: theme.colorScheme.error,
                    todayX: _year == _today.year ? (_x(_today) + _x(_today, end: true)) / 2 : null,
                    top: _axisHeight / 2,
                  ),
                  child: Column(
                    children: [
                      _axis(theme),
                      for (final (i, lane) in lanes.indexed) ...[
                        if (i > 0) const SizedBox(height: _laneGap),
                        band([for (final row in lane) _track(theme, row)], right),
                      ],
                    ],
                  ),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _axis(ThemeData theme) {
    final style = theme.textTheme.labelSmall;
    return SizedBox(
      height: _axisHeight,
      child: Column(
        children: [
          Row(children: [
            for (var q = 1; q <= 4; q++)
              SizedBox(
                width: _month * 3,
                height: _axisHeight / 2,
                child: Padding(
                  padding: const EdgeInsets.only(left: 4, top: 4),
                  child: Text('Q$q', style: style?.copyWith(fontWeight: FontWeight.w700)),
                ),
              ),
          ]),
          Row(children: [
            for (final name in monthNames)
              SizedBox(
                width: _month,
                height: _axisHeight / 2,
                child: Padding(padding: const EdgeInsets.only(left: 4, top: 4), child: Text(name, style: style)),
              ),
          ]),
        ],
      ),
    );
  }

  Widget _label(ThemeData theme, TreeRow row) {
    final level = _levels[row.goal.periodType] ?? 0;
    final collapsed = _collapsed.contains(row.goal.id);
    return SizedBox(
      height: _rowHeight,
      child: Row(
        children: [
          SizedBox(width: level * 8.0),
          if (row.hasChildren)
            InkWell(
              key: Key('fold-${row.goal.key}'),
              onTap: () => setState(() => collapsed ? _collapsed.remove(row.goal.id) : _collapsed.add(row.goal.id)),
              child: Icon(collapsed ? Icons.chevron_right : Icons.expand_more, size: 20,
                  semanticLabel: '${collapsed ? 'Expand' : 'Collapse'} ${row.goal.key}'),
            )
          else
            const SizedBox(width: 20),
          const SizedBox(width: 4),
          Expanded(
            child: Text(row.goal.title, maxLines: 2, overflow: TextOverflow.ellipsis, style: theme.textTheme.bodySmall),
          ),
        ],
      ),
    );
  }

  Widget _track(ThemeData theme, TreeRow row) {
    final goal = row.goal;
    final start = goal.periodStart ?? DateTime(_year, 1, 1);
    final end = goal.periodEnd ?? DateTime(_year, 12, 31);
    final left = _x(start);
    final right = _x(end, end: true);
    final height = switch (goal.periodType) { 'year' => 24.0, 'quarter' => 20.0, _ => 16.0 };
    final completed = goal.status == 'completed';
    final overdue = !completed && end.isBefore(_today);
    final background = completed
        ? theme.colorScheme.tertiaryContainer
        : overdue
            ? theme.colorScheme.errorContainer
            : theme.colorScheme.primaryContainer;
    final foreground = completed
        ? theme.colorScheme.onTertiaryContainer
        : overdue
            ? theme.colorScheme.onErrorContainer
            : theme.colorScheme.onPrimaryContainer;

    return SizedBox(
      height: _rowHeight,
      child: Stack(
        children: [
          Positioned(
            left: left,
            width: (right - left).clamp(4.0, _month * 12),
            top: (_rowHeight - height) / 2,
            height: height,
            child: Semantics(
              button: true,
              label: '${goal.key} ${goal.title}, ${periodLabels[goal.periodType]!.toLowerCase()} ${goal.slot}, '
                  '${goal.effectiveProgress}% done${completed ? ', completed' : overdue ? ', overdue' : ''}',
              child: GestureDetector(
                key: Key('bar-${goal.key}'),
                onTap: () => widget.api == null ? _details(goal) : showGoalQuickView(context, widget.api!, goal.key),
                child: ClipRRect(
                  borderRadius: BorderRadius.circular(4),
                  child: Stack(
                    children: [
                      Positioned.fill(child: ColoredBox(color: background)),
                      FractionallySizedBox(
                        widthFactor: goal.effectiveProgress / 100,
                        heightFactor: 1,
                        child: ColoredBox(color: theme.colorScheme.primary.withValues(alpha: 0.35)),
                      ),
                      Padding(
                        padding: const EdgeInsets.symmetric(horizontal: 6),
                        child: Align(
                          alignment: Alignment.centerLeft,
                          child: Text(
                            // A month's bar sits under its month already; there is room only for the number.
                            goal.periodType == 'month' ? '${goal.effectiveProgress}%' : '${goal.slot} · ${goal.effectiveProgress}%',
                            maxLines: 1,
                            overflow: TextOverflow.clip,
                            softWrap: false,
                            style: theme.textTheme.labelSmall?.copyWith(color: foreground, fontSize: 10),
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// Month lines under the whole chart, and today's line.
class _GridPainter extends CustomPainter {
  _GridPainter({
    required this.monthWidth,
    required this.lineColor,
    required this.todayColor,
    required this.todayX,
    required this.top,
  });

  final double monthWidth;
  final Color lineColor;
  final Color todayColor;
  final double? todayX;
  final double top;

  @override
  void paint(Canvas canvas, Size size) {
    final line = Paint()
      ..color = lineColor
      ..strokeWidth = 1;
    for (var m = 0; m < 12; m++) {
      final x = m * monthWidth;
      canvas.drawLine(Offset(x, top), Offset(x, size.height), line);
    }
    if (todayX case final x?) {
      canvas.drawLine(Offset(x, 0), Offset(x, size.height), Paint()
        ..color = todayColor
        ..strokeWidth = 2);
    }
  }

  @override
  bool shouldRepaint(_GridPainter old) =>
      old.monthWidth != monthWidth ||
      old.lineColor != lineColor ||
      old.todayColor != todayColor ||
      old.todayX != todayX ||
      old.top != top;
}
