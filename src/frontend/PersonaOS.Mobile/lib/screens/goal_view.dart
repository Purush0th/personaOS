import 'package:flutter/material.dart';

import '../api/personaos_api.dart';
import '../goal_calendar.dart';
import '../layout.dart';
import 'attachments_section.dart';
import 'board_widgets.dart';
import 'comments_section.dart';
import 'goal_forms.dart';
import 'task_views.dart';
// A goal is seen two ways, each with its own job:
// - the quick view: a short look from a list (where it sits, how far along it is, what is under
//   it), with completing it and a way to its full page;
// - the full page: everything, every action the goals list offers, its files and its discussion.

/// Opens a goal's quick view over the current screen; returns once it closes.
Future<void> showGoalQuickView(BuildContext context, PersonaOsApi api, String goalKey, {bool boardEnabled = true}) =>
    showQuickView<void>(
      context,
      builder: (_) => _GoalQuickLoader(api: api, goalKey: goalKey, boardEnabled: boardEnabled),
    );

class _GoalQuickLoader extends StatefulWidget {
  const _GoalQuickLoader({required this.api, required this.goalKey, required this.boardEnabled});

  final PersonaOsApi api;
  final String goalKey;
  final bool boardEnabled;

  @override
  State<_GoalQuickLoader> createState() => _GoalQuickLoaderState();
}

class _GoalQuickLoaderState extends State<_GoalQuickLoader> {
  late Future<Goal> _goal = widget.api.getGoal(widget.goalKey);

  @override
  Widget build(BuildContext context) => FutureBuilder<Goal>(
        future: _goal,
        builder: (context, snapshot) {
          if (snapshot.connectionState != ConnectionState.done) {
            return const SizedBox(height: 160, child: Center(child: CircularProgressIndicator()));
          }
          if (snapshot.hasError) {
            return const SizedBox(height: 120, child: Center(child: Text('Could not load that goal.')));
          }
          return GoalQuickView(
            api: widget.api,
            goal: snapshot.data!,
            boardEnabled: widget.boardEnabled,
            onChanged: () => setState(() {
              _goal = widget.api.getGoal(widget.goalKey);
            }),
          );
        },
      );
}

/// The quick view: key, type and slot, title, dates and priority, progress with what drives it,
/// the start of the description, and completing or reopening it.
class GoalQuickView extends StatelessWidget {
  const GoalQuickView({super.key, required this.api, required this.goal, this.boardEnabled = true, this.onChanged});

  final PersonaOsApi api;
  final Goal goal;
  final bool boardEnabled;
  final VoidCallback? onChanged;

  void _openPage(BuildContext context) {
    final navigator = Navigator.of(context);
    navigator.pop();
    navigator.push(MaterialPageRoute<void>(
      builder: (_) => GoalPage(api: api, goalKey: goal.key, boardEnabled: boardEnabled),
    ));
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final muted = theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.onSurfaceVariant);
    return SingleChildScrollView(
      key: Key('goal-quick-view-${goal.key}'),
      padding: const EdgeInsets.fromLTRB(20, 4, 12, 20),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  '${goal.key} · ${periodLabels[goal.periodType] ?? goal.periodType} · ${goal.slot}',
                  style: theme.textTheme.labelLarge?.copyWith(color: theme.colorScheme.outline),
                ),
              ),
              IconButton(
                key: const Key('open-full-page'),
                tooltip: 'Open full page',
                icon: const Icon(Icons.open_in_full),
                onPressed: () => _openPage(context),
              ),
              IconButton(tooltip: 'Close', icon: const Icon(Icons.close), onPressed: () => Navigator.pop(context)),
            ],
          ),
          Padding(
            padding: const EdgeInsets.only(right: 8),
            child: Text(goal.title, style: theme.textTheme.titleLarge),
          ),
          const SizedBox(height: 6),
          Text(_facts(goal), style: muted),
          const SizedBox(height: 12),
          _Progress(goal: goal),
          if (goal.description?.trim().isNotEmpty ?? false) ...[
            const SizedBox(height: 12),
            Padding(
              padding: const EdgeInsets.only(right: 8),
              child: Text(goal.description!.trim(), maxLines: 4, overflow: TextOverflow.ellipsis),
            ),
          ],
          const SizedBox(height: 16),
          Padding(
            padding: const EdgeInsets.only(right: 8),
            child: Wrap(
              alignment: WrapAlignment.end,
              spacing: 8,
              runSpacing: 8,
              crossAxisAlignment: WrapCrossAlignment.center,
              children: [
                _StatusButton(api: api, goal: goal, onChanged: onChanged),
                FilledButton.tonalIcon(
                  onPressed: () => _openPage(context),
                  icon: const Icon(Icons.open_in_full, size: 18),
                  label: const Text('Open goal'),
                ),
              ],
            ),
          ),
          if (goal.status != 'completed' && goal.completeProblem != null)
            Padding(
              padding: const EdgeInsets.only(top: 6, right: 8),
              child: Text(goal.completeProblem!, style: muted, textAlign: TextAlign.end),
            ),
        ],
      ),
    );
  }
}

/// Dates, status, priority and parent on one line.
String _facts(Goal goal) => [
      if (goal.periodStart != null && goal.periodEnd != null) formatGoalRange(goal.periodStart!, goal.periodEnd!),
      if (goal.status == 'completed') 'completed',
      'priority ${(priorities[goal.priority] ?? goal.priority).toLowerCase()}',
      if (goal.parentKey != null) 'under ${goal.parentKey}',
    ].join(' · ');

/// The progress bar and what drives it: child goals, tasks, or the user's own estimate.
class _Progress extends StatelessWidget {
  const _Progress({required this.goal});

  final Goal goal;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final driver = goal.childCount > 0
        ? '${goal.completedChildCount} of ${goal.childCount} ${goalsOfType(childTypeOf(goal.periodType) ?? 'month', goal.childCount)} completed'
        : goal.taskCount > 0
            ? '${goal.doneTaskCount} of ${goal.taskCount} ${goal.taskCount == 1 ? 'task' : 'tasks'} done'
            : 'set by hand';
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        ClipRRect(
          borderRadius: BorderRadius.circular(4),
          child: LinearProgressIndicator(value: goal.effectiveProgress / 100, minHeight: 6),
        ),
        const SizedBox(height: 4),
        Text('${goal.effectiveProgress}% · $driver', style: theme.textTheme.bodySmall),
      ],
    );
  }
}

/// Complete, or Reopen once completed. Completing is refused while something under the goal is
/// still open; the reason shows beside the disabled button.
class _StatusButton extends StatelessWidget {
  const _StatusButton({required this.api, required this.goal, this.onChanged});

  final PersonaOsApi api;
  final Goal goal;
  final VoidCallback? onChanged;

  Future<void> _set(BuildContext context, String status) async {
    try {
      await api.setGoalStatus(goal.id, status);
      onChanged?.call();
    } on ApiException catch (e) {
      if (context.mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    }
  }

  @override
  Widget build(BuildContext context) => goal.status == 'completed'
      ? OutlinedButton(onPressed: () => _set(context, 'active'), child: const Text('Reopen'))
      : OutlinedButton(
          onPressed: goal.completeProblem == null ? () => _set(context, 'completed') : null,
          child: const Text('Complete'),
        );
}

/// A goal on its own page: everything about it, every action the goals list offers (from the
/// menu), its files and its discussion. From [Breakpoints.sideBySide] the files and discussion
/// sit beside the goal instead of below it.
class GoalPage extends StatefulWidget {
  const GoalPage({super.key, required this.api, required this.goalKey, this.boardEnabled = true, this.today});

  final PersonaOsApi api;
  final String goalKey;
  final bool boardEnabled;

  /// For tests; otherwise today.
  final DateTime? today;

  @override
  State<GoalPage> createState() => _GoalPageState();
}

class _GoalPageState extends State<GoalPage> {
  late Future<(Goal, List<Goal>)> _data = _load();

  DateTime get _today {
    final now = widget.today ?? DateTime.now();
    return DateTime(now.year, now.month, now.day);
  }

  Future<(Goal, List<Goal>)> _load() =>
      (widget.api.getGoal(widget.goalKey), widget.api.getGoals().catchError((Object _) => <Goal>[])).wait;

  void _reload() => setState(() {
        _data = _load();
      });

  Future<void> _act(String choice, Goal goal, List<Goal> goals) async {
    final navigator = Navigator.of(context);
    bool? changed;
    switch (choice) {
      case 'progress':
        final value = await showDialog<int>(context: context, builder: (_) => ProgressDialog(initial: goal.progress));
        if (value == null || value == goal.progress) return;
        try {
          await widget.api.setGoalProgress(goal.id, value);
          changed = true;
        } on ApiException catch (e) {
          if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
        }
      case 'task':
        changed = await showModalBottomSheet<bool>(
          context: context,
          isScrollControlled: true,
          showDragHandle: true,
          builder: (_) => TaskSheet(api: widget.api, goals: const [], fixedGoalId: goal.id),
        );
      case 'child':
        changed = await showModalBottomSheet<bool>(
          context: context,
          isScrollControlled: true,
          showDragHandle: true,
          builder: (_) => GoalSheet(api: widget.api, goals: goals, parent: goal, today: _today),
        );
      case 'move':
        changed = await showDialog<bool>(
          context: context,
          builder: (_) => MoveGoalDialog(api: widget.api, goal: goal, goals: goals, today: _today),
        );
      case 'delete':
        final deleted = await showDialog<bool>(
          context: context,
          builder: (_) => DeleteGoalDialog(api: widget.api, goal: goal, goals: goals),
        );
        if (deleted == true) navigator.pop();
        return;
    }
    if (changed == true) _reload();
  }

  @override
  Widget build(BuildContext context) {
    return FutureBuilder<(Goal, List<Goal>)>(
      future: _data,
      builder: (context, snapshot) {
        final loaded = snapshot.connectionState == ConnectionState.done && !snapshot.hasError;
        final goal = loaded ? snapshot.data!.$1 : null;
        final goals = loaded ? snapshot.data!.$2 : const <Goal>[];
        final addChild = goal == null ? null : childTypeToAdd(goal);
        return Scaffold(
          appBar: AppBar(
            title: Text(widget.goalKey),
            actions: [
              if (goal != null)
                PopupMenuButton<String>(
                  key: const Key('goal-page-menu'),
                  tooltip: 'Goal actions',
                  onSelected: (choice) => _act(choice, goal, goals),
                  itemBuilder: (context) => [
                    if (goal.setsProgressByHand) const PopupMenuItem(value: 'progress', child: Text('Update progress')),
                    if (goal.periodType == 'month' && widget.boardEnabled && goal.status == 'active')
                      const PopupMenuItem(value: 'task', child: Text('Add task…')),
                    if (addChild != null) PopupMenuItem(value: 'child', child: Text('Add ${goalsOfType(addChild)}')),
                    if (goal.periodType != 'year') const PopupMenuItem(value: 'move', child: Text('Move')),
                    goal.childCount > 0
                        ? blockedMenuItem(context, 'Delete',
                            'Delete its ${goalsOfType(childTypeOf(goal.periodType) ?? 'month', 2)} first.')
                        : const PopupMenuItem(value: 'delete', child: Text('Delete')),
                  ],
                ),
            ],
          ),
          body: switch (snapshot) {
            _ when snapshot.connectionState != ConnectionState.done =>
              const Center(child: CircularProgressIndicator()),
            _ when snapshot.hasError => Center(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    const Text('Could not load that goal.'),
                    const SizedBox(height: 12),
                    OutlinedButton(onPressed: _reload, child: const Text('Try again')),
                  ],
                ),
              ),
            _ => ReadableWidth(
                maxWidth: 1100,
                child: GoalView(api: widget.api, goal: goal!, boardEnabled: widget.boardEnabled, onChanged: _reload),
              ),
          },
        );
      },
    );
  }
}

/// The body of a goal's full page: what it is, its progress and status, its description, the
/// goals or tasks under it (each opens its own quick view), its files and its discussion.
class GoalView extends StatelessWidget {
  const GoalView({super.key, required this.api, required this.goal, this.boardEnabled = true, this.onChanged});

  final PersonaOsApi api;
  final Goal goal;
  final bool boardEnabled;
  final VoidCallback? onChanged;

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        const pad = EdgeInsets.fromLTRB(16, 16, 16, 32);
        if (constraints.maxWidth < Breakpoints.sideBySide) {
          return ListView(
            key: Key('goal-view-${goal.key}'),
            padding: pad,
            children: [..._main(context), const Divider(height: 32), ..._discussion()],
          );
        }
        return SingleChildScrollView(
          key: Key('goal-view-${goal.key}'),
          padding: pad,
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(flex: 3, child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: _main(context))),
              const SizedBox(width: 24),
              Expanded(flex: 2, child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: _discussion())),
            ],
          ),
        );
      },
    );
  }

  List<Widget> _main(BuildContext context) {
    final theme = Theme.of(context);
    return [
      Text('${goal.key} · ${periodLabels[goal.periodType] ?? goal.periodType} · ${goal.slot}',
          style: theme.textTheme.labelLarge?.copyWith(color: theme.colorScheme.outline)),
      Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Expanded(child: Text(goal.title, style: theme.textTheme.headlineSmall)),
          IconButton(
            key: const Key('edit-goal'),
            tooltip: 'Edit goal',
            icon: const Icon(Icons.edit_outlined),
            onPressed: () => _edit(context),
          ),
        ],
      ),
      Text(_facts(goal), style: theme.textTheme.bodySmall),
      const SizedBox(height: 12),
      _Progress(goal: goal),
      const SizedBox(height: 12),
      Wrap(
        spacing: 8,
        runSpacing: 4,
        crossAxisAlignment: WrapCrossAlignment.center,
        children: [
          _StatusButton(api: api, goal: goal, onChanged: onChanged),
          if (goal.status != 'completed' && goal.completeProblem != null)
            Text(goal.completeProblem!, style: theme.textTheme.bodySmall),
        ],
      ),
      if (goal.description?.trim().isNotEmpty ?? false) ...[
        const SizedBox(height: 16),
        Text('Description', style: theme.textTheme.titleSmall),
        const SizedBox(height: 4),
        Text(goal.description!),
      ],
      if (goal.children.isNotEmpty) ...[
        const SizedBox(height: 16),
        Text('${periodLabels[childTypeOf(goal.periodType) ?? 'month']} goals', style: theme.textTheme.titleSmall),
        for (final child in goal.children)
          ListTile(
            key: Key('child-${child.key}'),
            contentPadding: EdgeInsets.zero,
            title: Text('${child.key} ${child.title}'),
            subtitle: Text('${child.slot} · ${child.effectiveProgress}%${child.status == 'completed' ? ' · completed' : ''}'),
            trailing: const Icon(Icons.chevron_right),
            onTap: () async {
              await showGoalQuickView(context, api, child.key, boardEnabled: boardEnabled);
              onChanged?.call();
            },
          ),
      ],
      if (goal.tasks.isNotEmpty) ...[
        const SizedBox(height: 16),
        Text('Tasks', style: theme.textTheme.titleSmall),
        for (final task in goal.tasks)
          ListTile(
            key: Key('goal-task-${task.key}'),
            contentPadding: EdgeInsets.zero,
            title: Text('${task.key} ${task.title}'),
            subtitle: Text(BoardColumns.label(task.column)),
            trailing: PointsPill(points: task.points),
            onTap: () async {
              if (await showTaskQuickView(context, api, task.key)) onChanged?.call();
            },
          ),
      ],
    ];
  }

  List<Widget> _discussion() => [
        AttachmentsSection(api: api, itemType: 'goal', itemKey: goal.key),
        const Divider(height: 32),
        CommentsSection(api: api, itemType: 'goal', itemKey: goal.key),
      ];

  /// Title, description and priority, as on the goal's web page.
  Future<void> _edit(BuildContext context) async {
    final changes = await showDialog<(String, String, String)>(
      context: context,
      builder: (_) => _GoalEditDialog(goal: goal),
    );
    if (changes == null) return;
    final (title, description, priority) = changes;
    try {
      await api.updateGoal(
        goal.id,
        title: title == goal.title ? null : title,
        description: description == (goal.description ?? '').trim() ? null : description,
        priority: priority == goal.priority ? null : priority,
      );
      onChanged?.call();
    } on ApiException catch (e) {
      if (context.mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    }
  }
}

class _GoalEditDialog extends StatefulWidget {
  const _GoalEditDialog({required this.goal});

  final Goal goal;

  @override
  State<_GoalEditDialog> createState() => _GoalEditDialogState();
}

class _GoalEditDialogState extends State<_GoalEditDialog> {
  late final _title = TextEditingController(text: widget.goal.title);
  late final _description = TextEditingController(text: widget.goal.description ?? '');
  late String _priority = priorities.containsKey(widget.goal.priority) ? widget.goal.priority : 'medium';

  @override
  void dispose() {
    _title.dispose();
    _description.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: Text('Edit ${widget.goal.key}'),
      content: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            TextField(controller: _title, decoration: const InputDecoration(labelText: 'Title')),
            const SizedBox(height: 8),
            TextField(
              controller: _description,
              minLines: 2,
              maxLines: 6,
              decoration: const InputDecoration(labelText: 'Description'),
            ),
            const SizedBox(height: 8),
            DropdownButtonFormField<String>(
              initialValue: _priority,
              decoration: const InputDecoration(labelText: 'Priority'),
              items: [for (final p in priorities.entries) DropdownMenuItem(value: p.key, child: Text(p.value))],
              onChanged: (v) => setState(() => _priority = v ?? _priority),
            ),
          ],
        ),
      ),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
        FilledButton(
          onPressed: () {
            final title = _title.text.trim();
            if (title.isEmpty) return;
            Navigator.pop(context, (title, _description.text.trim(), _priority));
          },
          child: const Text('Save'),
        ),
      ],
    );
  }
}
