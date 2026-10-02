import 'package:flutter/material.dart';

import '../api/personaos_api.dart';

// Pieces of the board shared by every screen that shows tasks or sprints: the scope-change
// confirmation, sprint labels, and the task card with its points pill and goal chip.

/// Runs [attempt]; when the server says it changes a running sprint's scope, asks and retries
/// with the acknowledgement. Returns false when the user declines.
Future<bool> runWithScopeConfirmation(
  BuildContext context,
  Future<void> Function(bool acknowledge) attempt,
) async {
  try {
    await attempt(false);
    return true;
  } on ApiException catch (e) {
    if (e.code != scopeChangeCode || !context.mounted) rethrow;
    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Scope change'),
        content: Text(e.message),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Change scope')),
        ],
      ),
    );
    if (ok != true) return false;
    await attempt(true);
    return true;
  }
}

String sprintWhen(DateTime utc) {
  const weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
  const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  final t = utc.toLocal();
  final hh = t.hour.toString().padLeft(2, '0');
  final mm = t.minute.toString().padLeft(2, '0');
  return '${weekdays[t.weekday - 1]} ${t.day} ${months[t.month - 1]} $hh:$mm';
}

String sprintLabel(SprintInfo sprint) =>
    sprint.name == null ? sprint.key : '${sprint.key} · ${sprint.name}';

/// One task on the board.
class TaskCard extends StatelessWidget {
  const TaskCard({super.key, required this.task, this.onTap});

  final BoardTask task;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Card(
      key: Key('card-${task.key}'),
      // Lighter than the column in light mode and darker in dark, so a card stands out either way.
      color: theme.colorScheme.surfaceContainerLowest,
      margin: const EdgeInsets.only(bottom: 6),
      child: InkWell(
        borderRadius: BorderRadius.circular(12),
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Text(task.key, style: theme.textTheme.labelSmall?.copyWith(color: theme.colorScheme.outline)),
                  const Spacer(),
                  PointsPill(points: task.points),
                ],
              ),
              const SizedBox(height: 4),
              Text(task.title, style: theme.textTheme.bodyMedium),
              if (task.goalKey != null ||
                  task.carryOverCount > 0 ||
                  task.addedMidSprint ||
                  task.commentCount > 0 ||
                  task.attachmentCount > 0) ...[
                const SizedBox(height: 6),
                Wrap(
                  spacing: 6,
                  runSpacing: 4,
                  crossAxisAlignment: WrapCrossAlignment.center,
                  children: [
                    if (task.goalKey != null) GoalChip(goalKey: task.goalKey!, title: task.goalTitle ?? task.goalKey!),
                    if (task.carryOverCount > 0)
                      Text('↻ ${task.carryOverCount}', style: theme.textTheme.labelSmall),
                    if (task.addedMidSprint)
                      Text('added', style: theme.textTheme.labelSmall?.copyWith(color: const Color(0xFFB26A00))),
                    if (task.commentCount > 0)
                      Text('💬 ${task.commentCount}', style: theme.textTheme.labelSmall),
                    if (task.attachmentCount > 0)
                      Text('📎 ${task.attachmentCount}', style: theme.textTheme.labelSmall),
                  ],
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

/// Value points, or "–" while unestimated.
class PointsPill extends StatelessWidget {
  const PointsPill({super.key, required this.points});

  final int? points;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final none = points == null;
    return Container(
      constraints: const BoxConstraints(minWidth: 24),
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 1),
      decoration: BoxDecoration(
        color: none ? null : scheme.secondaryContainer,
        border: none ? Border.all(color: scheme.outlineVariant) : null,
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        none ? '–' : '$points',
        textAlign: TextAlign.center,
        style: Theme.of(context).textTheme.labelSmall?.copyWith(fontWeight: FontWeight.w700),
      ),
    );
  }
}

/// A goal's chip, coloured consistently per goal.
class GoalChip extends StatelessWidget {
  const GoalChip({super.key, required this.goalKey, required this.title});

  final String goalKey;
  final String title;

  /// Golden-angle steps keep neighbouring goals (GOAL-1, GOAL-2) clearly different.
  static double hueFor(String goalKey) {
    final n = int.tryParse(goalKey.replaceAll(RegExp(r'\D'), '')) ?? 0;
    return (n * 137.508) % 360;
  }

  @override
  Widget build(BuildContext context) {
    final dark = Theme.of(context).brightness == Brightness.dark;
    final hue = hueFor(goalKey);
    final background = HSLColor.fromAHSL(1, hue, 0.55, dark ? 0.25 : 0.9).toColor();
    final foreground = HSLColor.fromAHSL(1, hue, 0.5, dark ? 0.85 : 0.28).toColor();
    return Tooltip(
      message: '$goalKey $title',
      child: Container(
        constraints: const BoxConstraints(maxWidth: 180),
        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
        decoration: BoxDecoration(color: background, borderRadius: BorderRadius.circular(999)),
        child: Text(
          title,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: Theme.of(context).textTheme.labelSmall?.copyWith(color: foreground),
        ),
      ),
    );
  }
}


/// Where a task can be moved on the board: the sprint's other columns. A backlog task is in no
/// sprint, so it has none; it reaches a sprint through its sprint field or the Backlog tab.
List<String> boardMoveTargets(BoardTask task) =>
    task.sprintKey == null ? const [] : BoardColumns.board.where((c) => c != task.column).toList();

/// A task's column as a small coloured label: To do, In progress, Done or Backlog.
class StatusChip extends StatelessWidget {
  const StatusChip({super.key, required this.column});

  final String column;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final (background, foreground) = switch (column) {
      BoardColumns.done => (scheme.primaryContainer, scheme.onPrimaryContainer),
      BoardColumns.inProgress => (scheme.tertiaryContainer, scheme.onTertiaryContainer),
      _ => (scheme.surfaceContainerHighest, scheme.onSurfaceVariant),
    };
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
      decoration: BoxDecoration(color: background, borderRadius: BorderRadius.circular(6)),
      child: Text(
        BoardColumns.label(column),
        style: Theme.of(context).textTheme.labelSmall?.copyWith(color: foreground, fontWeight: FontWeight.w600),
      ),
    );
  }
}
