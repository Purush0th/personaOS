import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../api/personaos_api.dart';
import '../layout.dart';
import 'board_screen.dart';
import 'goal_view.dart';
import 'sprint_page.dart';

/// The Reports tab of the board, like the web's: pick any started sprint (the running one first),
/// see it in a few numbers and its burndown, and every task in it by status. Velocity across
/// sprints closes the page.
class ReportsView extends StatefulWidget {
  const ReportsView({super.key, required this.api});

  final PersonaOsApi api;

  @override
  State<ReportsView> createState() => _ReportsViewState();
}

class _ReportsViewState extends State<ReportsView> {
  late Future<SprintReport> _report = widget.api.getSprintReport();
  String? _selected;
  Future<SprintDetail>? _detail;

  Future<void> _reload() async {
    final report = widget.api.getSprintReport();
    setState(() {
      _report = report;
      _detail = null;
    });
    await report;
  }

  void _pick(String key) => setState(() {
        _selected = key;
        _detail = widget.api.getSprintDetail(key);
      });

  @override
  Widget build(BuildContext context) {
    return FutureBuilder<SprintReport>(
      future: _report,
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const Center(child: CircularProgressIndicator());
        }
        if (snapshot.hasError) {
          return Center(
            child: Column(mainAxisSize: MainAxisSize.min, children: [
              const Text('Could not load the reports.'),
              const SizedBox(height: 12),
              OutlinedButton(onPressed: _reload, child: const Text('Retry')),
            ]),
          );
        }

        final report = snapshot.data!;
        final started = report.sprints.where((s) => !s.isPlanned).toList();
        if (started.isEmpty) {
          return const Center(
            child: Padding(
              padding: EdgeInsets.all(32),
              child: Text('No sprint has started yet. Start one from the Backlog tab.', textAlign: TextAlign.center),
            ),
          );
        }

        // The running sprint by default, else the latest finished one.
        if (_selected == null || !started.any((s) => s.key == _selected)) {
          final first = started.firstWhere((s) => s.isActive, orElse: () => started.first);
          _selected = first.key;
          _detail = widget.api.getSprintDetail(first.key);
        }
        _detail ??= widget.api.getSprintDetail(_selected!);

        return RefreshIndicator(
          onRefresh: _reload,
          child: ReadableWidth(
            maxWidth: 960,
            child: ListView(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
              children: [
                DropdownButtonFormField<String>(
                  key: const Key('report-sprint'),
                  initialValue: _selected,
                  isExpanded: true,
                  decoration: const InputDecoration(labelText: 'Sprint'),
                  items: [
                    for (final s in started)
                      DropdownMenuItem(
                        value: s.key,
                        child: Text('${sprintLabel(s)}${s.isActive ? ' · running' : ''}',
                            overflow: TextOverflow.ellipsis),
                      ),
                  ],
                  onChanged: (key) => key == null ? null : _pick(key),
                ),
                const SizedBox(height: 12),
                FutureBuilder<SprintDetail>(
                  future: _detail,
                  builder: (context, detail) {
                    if (detail.connectionState != ConnectionState.done) {
                      return const Padding(
                        padding: EdgeInsets.all(32),
                        child: Center(child: CircularProgressIndicator()),
                      );
                    }
                    if (detail.hasError) {
                      return const Padding(
                        padding: EdgeInsets.all(24),
                        child: Text('Could not load that sprint.'),
                      );
                    }
                    final sprintKey = detail.data!.sprint.key;
                    return Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        Align(
                          alignment: Alignment.centerLeft,
                          child: TextButton.icon(
                            key: const Key('open-sprint'),
                            onPressed: () async {
                              final changed = await Navigator.of(context).push<bool>(MaterialPageRoute(
                                builder: (_) => SprintPage(api: widget.api, sprintKey: sprintKey),
                              ));
                              if (changed == true) await _reload();
                            },
                            icon: const Icon(Icons.open_in_full, size: 18),
                            label: Text('Open $sprintKey'),
                          ),
                        ),
                        SprintSummary(
                          detail: detail.data!,
                          searchable: true,
                          onOpenTask: (task) async {
                            if (await showTaskQuickView(context, widget.api, task.key)) _pick(sprintKey);
                          },
                        ),
                      ],
                    );
                  },
                ),
                if (report.velocity != null)
                  Padding(
                    padding: const EdgeInsets.only(top: 16),
                    child: Text(
                      'Velocity ${report.velocity}: the points completed per sprint, on average over the last three finished ones.',
                      style: Theme.of(context).textTheme.bodySmall,
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

/// A started sprint summed up: its numbers, a breakdown by status and priority, its burndown and
/// its tasks by status. Shared by the Reports tab and the sprint's own page.
class SprintSummary extends StatefulWidget {
  const SprintSummary({super.key, required this.detail, this.onOpenTask, this.searchable = false});

  final SprintDetail detail;
  final ValueChanged<BoardTask>? onOpenTask;

  /// Offers a search over the tasks, as the web's report table does.
  final bool searchable;

  @override
  State<SprintSummary> createState() => _SprintSummaryState();
}

class _SprintSummaryState extends State<SprintSummary> {
  String _query = '';

  @override
  Widget build(BuildContext context) {
    final detail = widget.detail;
    final theme = Theme.of(context);
    final s = detail.sprint;
    final words = _query.toLowerCase().split(RegExp(r'\s+')).where((w) => w.isNotEmpty);
    final tasks = detail.tasks
        .where((t) => words.every((w) => '${t.key} ${t.title} ${t.goalTitle ?? ''}'.toLowerCase().contains(w)))
        .toList();
    int pointsWhere(bool Function(BoardTask t) test) =>
        detail.tasks.where(test).fold<int>(0, (sum, t) => sum + (t.points ?? 0));
    final committed = s.committedPoints ?? s.totalPoints;
    final numbers = <(String, String)>[
      ('Committed', '$committed'),
      ('Added', '${s.addedPoints}'),
      ('Removed', '${s.removedPoints}'),
      ('Completed', '${s.completedPoints}'),
      if (s.carriedOverPoints != null) ('Carried over', '${s.carriedOverPoints}'),
      ('Tasks done', '${s.doneTaskCount} of ${s.taskCount}'),
    ];

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text('${sprintWhen(s.startsAtUtc)} – ${sprintWhen(s.endsAtUtc)}', style: theme.textTheme.bodySmall),
        const SizedBox(height: 12),
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            for (final (label, value) in numbers)
              Container(
                width: 120,
                padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
                decoration: BoxDecoration(
                  color: theme.colorScheme.surfaceContainerLow,
                  borderRadius: BorderRadius.circular(12),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(value, style: theme.textTheme.titleMedium),
                    Text(label, style: theme.textTheme.labelSmall),
                  ],
                ),
              ),
          ],
        ),
        const SizedBox(height: 16),
        Text('By status', style: theme.textTheme.titleSmall),
        const SizedBox(height: 4),
        Text(
          [
            for (final c in [BoardColumns.todo, BoardColumns.inProgress, BoardColumns.done])
              '${BoardColumns.label(c)} ${pointsWhere((t) => t.column == c)} pts',
          ].join(' · '),
          key: const Key('by-status'),
          style: theme.textTheme.bodySmall,
        ),
        const SizedBox(height: 8),
        Text('By priority', style: theme.textTheme.titleSmall),
        const SizedBox(height: 4),
        Text(
          [
            for (final p in priorities.entries)
              if (detail.tasks.any((t) => t.priority == p.key))
                '${p.value} ${detail.tasks.where((t) => t.priority == p.key).length}',
          ].join(' · '),
          key: const Key('by-priority'),
          style: theme.textTheme.bodySmall,
        ),
        const SizedBox(height: 16),
        Text('Burndown', style: theme.textTheme.titleSmall),
        const SizedBox(height: 8),
        SizedBox(
          height: 180,
          child: detail.burndown.isEmpty
              ? Center(child: Text('Nothing to chart yet.', style: theme.textTheme.bodySmall))
              : CustomPaint(
                  key: const Key('burndown'),
                  painter: BurndownPainter(
                    points: detail.burndown,
                    lineColor: theme.colorScheme.primary,
                    idealColor: theme.colorScheme.outline,
                    gridColor: theme.colorScheme.outlineVariant,
                  ),
                  child: const SizedBox.expand(),
                ),
        ),
        const SizedBox(height: 4),
        Row(children: [
          _Legend(color: theme.colorScheme.primary, label: 'Points left'),
          const SizedBox(width: 16),
          _Legend(color: theme.colorScheme.outline, label: 'Ideal', dashed: true),
        ]),
        const SizedBox(height: 16),
        if (widget.searchable)
          Padding(
            padding: const EdgeInsets.only(bottom: 12),
            child: TextField(
              key: const Key('report-search'),
              decoration: const InputDecoration(
                hintText: 'Search tasks',
                prefixIcon: Icon(Icons.search),
                isDense: true,
                border: OutlineInputBorder(),
              ),
              onChanged: (q) => setState(() => _query = q),
            ),
          ),
        for (final column in [BoardColumns.todo, BoardColumns.inProgress, BoardColumns.done]) ...[
          TaskGroup(
            title: BoardColumns.label(column),
            tasks: tasks.where((t) => t.column == column).toList(),
            onOpen: widget.onOpenTask,
          ),
        ],
      ],
    );
  }
}

/// A status's tasks, with their count and points; a task opens through [onOpen].
class TaskGroup extends StatelessWidget {
  const TaskGroup({super.key, required this.title, required this.tasks, this.onOpen});

  final String title;
  final List<BoardTask> tasks;
  final ValueChanged<BoardTask>? onOpen;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final points = tasks.fold<int>(0, (sum, t) => sum + (t.points ?? 0));
    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text('$title · ${tasks.length} · $points pts', style: theme.textTheme.titleSmall),
          if (tasks.isEmpty)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 6),
              child: Text('None.', style: theme.textTheme.bodySmall),
            ),
          for (final task in tasks)
            ListTile(
              dense: true,
              contentPadding: EdgeInsets.zero,
              title: Text(task.title, maxLines: 2, overflow: TextOverflow.ellipsis),
              subtitle: Text(
                [task.key, if (task.addedMidSprint) 'added mid-sprint', if (task.goalTitle != null) task.goalTitle!]
                    .join(' · '),
                style: theme.textTheme.bodySmall,
              ),
              trailing: PointsPill(points: task.points),
              onTap: onOpen == null ? null : () => onOpen!(task),
            ),
        ],
      ),
    );
  }
}

class _Legend extends StatelessWidget {
  const _Legend({required this.color, required this.label, this.dashed = false});

  final Color color;
  final String label;
  final bool dashed;

  @override
  Widget build(BuildContext context) => Row(mainAxisSize: MainAxisSize.min, children: [
        Container(width: 16, height: dashed ? 1 : 3, color: color),
        const SizedBox(width: 6),
        Text(label, style: Theme.of(context).textTheme.labelSmall),
      ]);
}

/// Points left per day as a line, over the ideal straight line from the first day's total to none.
class BurndownPainter extends CustomPainter {
  BurndownPainter({required this.points, required this.lineColor, required this.idealColor, required this.gridColor});

  final List<BurndownPoint> points;
  final Color lineColor;
  final Color idealColor;
  final Color gridColor;

  @override
  void paint(Canvas canvas, Size size) {
    if (points.isEmpty) return;
    const left = 28.0, bottom = 18.0, top = 6.0;
    final width = size.width - left;
    final height = size.height - bottom - top;
    final top0 = points.map((p) => p.remainingPoints + p.completedPoints).fold<int>(0, math.max);
    final maxY = math.max(1, math.max(top0, points.map((p) => p.remainingPoints).fold<int>(0, math.max)));
    final steps = math.max(1, points.length - 1);
    double x(int i) => left + width * i / steps;
    double y(num v) => top + height * (1 - v / maxY);

    final grid = Paint()
      ..color = gridColor
      ..strokeWidth = 1;
    canvas.drawLine(Offset(left, top), Offset(left, top + height), grid);
    canvas.drawLine(Offset(left, top + height), Offset(size.width, top + height), grid);

    final text = TextPainter(textDirection: TextDirection.ltr);
    void label(String s, Offset at) {
      text.text = TextSpan(text: s, style: TextStyle(color: idealColor, fontSize: 10));
      text.layout();
      text.paint(canvas, at);
    }

    label('$maxY', const Offset(0, 0));
    label('0', Offset(0, top + height - 8));
    label('${points.first.date.day}/${points.first.date.month}', Offset(left, top + height + 4));
    final last = points.last.date;
    text.text = TextSpan(text: '${last.day}/${last.month}', style: TextStyle(color: idealColor, fontSize: 10));
    text.layout();
    text.paint(canvas, Offset(size.width - text.width, top + height + 4));

    // The ideal: from the first day's points straight down to none on the last day, dashed.
    final ideal = Paint()
      ..color = idealColor
      ..strokeWidth = 1;
    final start = Offset(x(0), y(points.first.remainingPoints + points.first.completedPoints));
    final end = Offset(x(steps), y(0));
    const dash = 6.0;
    final length = (end - start).distance;
    for (var d = 0.0; d < length; d += dash * 2) {
      final a = Offset.lerp(start, end, d / length)!;
      final b = Offset.lerp(start, end, math.min(1, (d + dash) / length))!;
      canvas.drawLine(a, b, ideal);
    }

    final line = Paint()
      ..color = lineColor
      ..strokeWidth = 2.5
      ..style = PaintingStyle.stroke;
    final path = Path()..moveTo(x(0), y(points.first.remainingPoints));
    for (var i = 1; i < points.length; i++) {
      path.lineTo(x(i), y(points[i].remainingPoints));
    }
    canvas.drawPath(path, line);
  }

  @override
  bool shouldRepaint(BurndownPainter old) =>
      old.points != points || old.lineColor != lineColor || old.idealColor != idealColor || old.gridColor != gridColor;
}
