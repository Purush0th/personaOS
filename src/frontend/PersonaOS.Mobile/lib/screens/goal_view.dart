import 'package:flutter/material.dart';

import '../api/personaos_api.dart';
import '../goal_calendar.dart';
import '../layout.dart';
import 'board_screen.dart';
import 'comments_section.dart';

/// Opens a goal's quick view over the current screen; returns once it closes.
Future<void> showGoalQuickView(BuildContext context, PersonaOsApi api, String goalKey) =>
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => FractionallySizedBox(
        heightFactor: 0.85,
        child: _GoalLoader(api: api, goalKey: goalKey, sheet: true),
      ),
    );

/// Opens a task's quick view from its key (a goal lists only a summary of it); true when it changed.
Future<bool> showTaskQuickView(BuildContext context, PersonaOsApi api, String taskKey) async {
  final BoardTask task;
  List<SprintInfo> sprints = const [];
  try {
    task = await api.getTask(taskKey);
  } on ApiException catch (e) {
    if (context.mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    return false;
  }
  try {
    sprints = (await api.getPlan()).sprints.map((p) => p.sprint).toList();
  } catch (_) {
    // Without the plan the task still opens; it just cannot be moved to another sprint here.
  }
  if (!context.mounted) return false;
  final changed = await showModalBottomSheet<bool>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    builder: (_) => TaskSheet(api: api, goals: const [], task: task, sprints: sprints),
  );
  return changed == true;
}

/// A goal on its own page: its details, the goals and tasks under it, and its comments.
class GoalPage extends StatelessWidget {
  const GoalPage({super.key, required this.api, required this.goalKey});

  final PersonaOsApi api;
  final String goalKey;

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(title: Text(goalKey)),
        body: ReadableWidth(child: _GoalLoader(api: api, goalKey: goalKey, sheet: false)),
      );
}

/// Reads the goal afresh, so the view is current however it was reached.
class _GoalLoader extends StatefulWidget {
  const _GoalLoader({required this.api, required this.goalKey, required this.sheet});

  final PersonaOsApi api;
  final String goalKey;
  final bool sheet;

  @override
  State<_GoalLoader> createState() => _GoalLoaderState();
}

class _GoalLoaderState extends State<_GoalLoader> {
  late Future<Goal> _goal = widget.api.getGoal(widget.goalKey);

  void _reload() => setState(() {
        _goal = widget.api.getGoal(widget.goalKey);
      });

  @override
  Widget build(BuildContext context) {
    return FutureBuilder<Goal>(
      future: _goal,
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const Center(child: CircularProgressIndicator());
        }
        if (snapshot.hasError) return const Center(child: Text('Could not load that goal.'));
        return GoalView(api: widget.api, goal: snapshot.data!, sheet: widget.sheet, onChanged: _reload);
      },
    );
  }
}

/// What a goal is: its key, slot and dates, progress, description, the goals or tasks under it,
/// and its comments. A child goal or a task opens its own quick view.
class GoalView extends StatelessWidget {
  const GoalView({super.key, required this.api, required this.goal, required this.sheet, this.onChanged});

  final PersonaOsApi api;
  final Goal goal;

  /// Shown over a list: offers the full page.
  final bool sheet;
  final VoidCallback? onChanged;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final outline = theme.textTheme.labelMedium?.copyWith(color: theme.colorScheme.outline);
    return ListView(
      key: Key('goal-view-${goal.key}'),
      padding: const EdgeInsets.fromLTRB(16, 0, 16, 24),
      children: [
        Row(
          children: [
            Expanded(
              child: Text('${goal.key} · ${periodLabels[goal.periodType] ?? goal.periodType} · ${goal.slot}',
                  style: outline),
            ),
            if (sheet)
              IconButton(
                tooltip: 'Open full page',
                icon: const Icon(Icons.open_in_full),
                onPressed: () {
                  final navigator = Navigator.of(context);
                  navigator.pop();
                  navigator.push(MaterialPageRoute<void>(builder: (_) => GoalPage(api: api, goalKey: goal.key)));
                },
              ),
          ],
        ),
        Text(goal.title, style: theme.textTheme.titleLarge),
        if (goal.periodStart != null && goal.periodEnd != null)
          Text(
            '${formatGoalRange(goal.periodStart!, goal.periodEnd!)}${goal.status == 'completed' ? ' · completed' : ''}',
            style: theme.textTheme.bodySmall,
          ),
        if (goal.parentKey != null) Text('Under ${goal.parentKey}', style: theme.textTheme.bodySmall),
        const SizedBox(height: 12),
        ClipRRect(
          borderRadius: BorderRadius.circular(4),
          child: LinearProgressIndicator(value: goal.effectiveProgress / 100, minHeight: 6),
        ),
        const SizedBox(height: 4),
        Text('${goal.effectiveProgress}%', style: theme.textTheme.bodySmall),
        if (goal.description?.trim().isNotEmpty ?? false) ...[
          const SizedBox(height: 12),
          Text('Description', style: theme.textTheme.titleSmall),
          const SizedBox(height: 4),
          Text(goal.description!),
        ],
        if (goal.children.isNotEmpty) ...[
          const SizedBox(height: 16),
          Text('${periodLabels[goal.periodType == 'year' ? 'quarter' : 'month']} goals', style: theme.textTheme.titleSmall),
          for (final child in goal.children)
            ListTile(
              key: Key('child-${child.key}'),
              dense: true,
              contentPadding: EdgeInsets.zero,
              title: Text('${child.key} ${child.title}'),
              subtitle: Text('${child.slot} · ${child.effectiveProgress}%${child.status == 'completed' ? ' · completed' : ''}'),
              trailing: const Icon(Icons.chevron_right),
              onTap: () => showGoalQuickView(context, api, child.key),
            ),
        ],
        if (goal.tasks.isNotEmpty) ...[
          const SizedBox(height: 16),
          Text('Tasks', style: theme.textTheme.titleSmall),
          for (final task in goal.tasks)
            ListTile(
              key: Key('goal-task-${task.key}'),
              dense: true,
              contentPadding: EdgeInsets.zero,
              title: Text('${task.key} ${task.title}'),
              subtitle: Text(BoardColumns.label(task.column)),
              trailing: PointsPill(points: task.points),
              onTap: () async {
                if (await showTaskQuickView(context, api, task.key)) onChanged?.call();
              },
            ),
        ],
        const Divider(height: 32),
        CommentsSection(api: api, itemType: 'goal', itemKey: goal.key),
      ],
    );
  }
}
