import 'package:flutter/material.dart';

import '../api/personaos_api.dart';
import '../goal_calendar.dart';
import '../goal_tree.dart';
import 'board_widgets.dart';
import 'goal_forms.dart';
import 'goal_view.dart';
import 'task_views.dart';
import 'timeline_screen.dart';

/// Goals, with two tabs as on the web: Goals and Timeline. On Goals the years, their quarters and
/// those quarters' months are cards, each indented under its parent; a top-level goal and
/// everything under it share a swim lane, and a goal with child goals folds. Monthly goals carry
/// the tasks. Every action is in the goal's menu; one that cannot be taken yet says why instead of
/// failing. Timeline draws the same goals on the year.
class GoalsScreen extends StatefulWidget {
  const GoalsScreen({super.key, required this.api, this.boardEnabled = true, this.today});

  final PersonaOsApi api;

  /// Whether tasks can be added under a goal (the board module is on).
  final bool boardEnabled;

  /// The local today; the device's own when null. Tests pin it.
  final DateTime? today;

  @override
  State<GoalsScreen> createState() => _GoalsScreenState();
}

class _GoalsScreenState extends State<GoalsScreen> {
  late Future<List<Goal>> _goals;

  /// Goals folded shut: their child goals are hidden.
  final Set<int> _collapsed = {};

  DateTime get _today {
    final now = widget.today ?? DateTime.now();
    return DateTime(now.year, now.month, now.day);
  }

  @override
  void initState() {
    super.initState();
    _goals = widget.api.getGoals();
  }

  void _reload() {
    // A block, not an arrow: an arrow would hand setState the Future it assigns.
    setState(() {
      _goals = widget.api.getGoals();
    });
  }

  Future<void> _run(Future<void> Function() action) async {
    try {
      await action();
      _reload();
    } on ApiException catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
      }
    }
  }

  Future<void> _newGoal(List<Goal> goals, {Goal? parent}) async {
    final created = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => GoalSheet(api: widget.api, goals: goals, parent: parent, today: _today),
    );
    if (created == true) _reload();
  }

  Future<void> _addTask(Goal goal) async {
    final created = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => TaskSheet(api: widget.api, goals: const [], fixedGoalId: goal.id),
    );
    if (created == true) _reload();
  }

  Future<void> _move(Goal goal, List<Goal> goals) async {
    final moved = await showDialog<bool>(
      context: context,
      builder: (_) => MoveGoalDialog(api: widget.api, goal: goal, goals: goals, today: _today),
    );
    if (moved == true) _reload();
  }

  Future<void> _delete(Goal goal, List<Goal> goals) async {
    final deleted = await showDialog<bool>(
      context: context,
      builder: (_) => DeleteGoalDialog(api: widget.api, goal: goal, goals: goals),
    );
    if (deleted == true) _reload();
  }

  /// Reopens a task (back to the Backlog) or deletes it, from the goal it sits under.
  Future<void> _taskAction(GoalTaskSummary task, String action) async {
    if (action == 'reopen') {
      await _run(() => widget.api.moveTask(task.key, column: BoardColumns.backlog, acknowledgeScopeChange: true));
      return;
    }

    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text('Delete ${task.key}?'),
        content: Text('“${task.title}” will be deleted.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Delete')),
        ],
      ),
    );
    if (ok == true) await _run(() => widget.api.deleteTask(task.key));
  }

  Future<void> _editProgress(Goal goal) async {
    final value = await showDialog<int>(
      context: context,
      builder: (context) => ProgressDialog(initial: goal.progress),
    );
    if (value != null && value != goal.progress) {
      await _run(() => widget.api.setGoalProgress(goal.id, value));
    }
  }

  @override
  Widget build(BuildContext context) {
    return DefaultTabController(
      length: 2,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('Goals'),
          bottom: const TabBar(tabs: [Tab(text: 'Goals'), Tab(text: 'Timeline')]),
        ),
        floatingActionButton: FutureBuilder<List<Goal>>(
          future: _goals,
          builder: (context, snapshot) => FloatingActionButton.extended(
            onPressed: () => _newGoal(snapshot.data ?? const []),
            icon: const Icon(Icons.add),
            label: const Text('New goal'),
          ),
        ),
        // Tabs change by tapping: the Timeline scrolls sideways, and a swipe there must move the
        // year, not the tab.
        body: TabBarView(
          physics: const NeverScrollableScrollPhysics(),
          children: [
            _goalsTab(),
            TimelineView(goals: _goals, today: _today, api: widget.api),
          ],
        ),
      ),
    );
  }

  Widget _goalsTab() {
    return RefreshIndicator(
      onRefresh: () async => _reload(),
      child: FutureBuilder<List<Goal>>(
        future: _goals,
        builder: (context, snapshot) {
          if (snapshot.connectionState != ConnectionState.done) {
            return const Center(child: CircularProgressIndicator());
          }
          if (snapshot.hasError) {
            return const _MessageList(message: 'Could not load goals.', error: true);
          }
          final goals = snapshot.data!;
          if (goals.isEmpty) {
            return const _MessageList(message: 'No goals yet. Add one, or ask your assistant in chat.');
          }
          final rows = treeRows(goals);
          final lanes = byLane(unfolded(rows, _collapsed));
          final canFold = rows.any((r) => r.hasChildren);
          final anyCollapsed = _collapsed.isNotEmpty;
          final band = Theme.of(context).colorScheme.surfaceContainerHigh;

          return ListView(
            padding: const EdgeInsets.fromLTRB(12, 4, 12, 88),
            children: [
              if (canFold)
                Align(
                  alignment: Alignment.centerRight,
                  child: TextButton.icon(
                    key: const Key('goals-fold-all'),
                    icon: Icon(anyCollapsed ? Icons.unfold_more : Icons.unfold_less, size: 18),
                    label: Text(anyCollapsed ? 'Expand all' : 'Collapse all'),
                    onPressed: () => setState(() {
                      if (anyCollapsed) {
                        _collapsed.clear();
                      } else {
                        _collapsed.addAll(rows.where((r) => r.hasChildren).map((r) => r.goal.id));
                      }
                    }),
                  ),
                ),
              // A swim lane per top-level goal: it and everything under it on one band.
              for (final lane in lanes)
                Container(
                  key: Key('lane-${lane.first.goal.key}'),
                  margin: const EdgeInsets.only(top: 10),
                  padding: const EdgeInsets.fromLTRB(6, 0, 6, 6),
                  decoration: BoxDecoration(color: band, borderRadius: BorderRadius.circular(16)),
                  child: Column(
                    children: [
                      for (final row in lane)
                        _GoalTile(
                          key: Key('goal-${row.goal.key}'),
                          goal: row.goal,
                          depth: row.depth,
                          today: _today,
                          boardEnabled: widget.boardEnabled,
                          folds: row.hasChildren,
                          collapsed: _collapsed.contains(row.goal.id),
                          onFold: () => setState(() {
                            if (!_collapsed.remove(row.goal.id)) _collapsed.add(row.goal.id);
                          }),
                          onOpen: () async {
                            await showGoalQuickView(context, widget.api, row.goal.key, boardEnabled: widget.boardEnabled);
                            _reload();
                          },
                          onOpenTask: (task) async {
                            if (await showTaskQuickView(context, widget.api, task.key)) _reload();
                          },
                          onAddTask: () => _addTask(row.goal),
                          onAddChild: () => _newGoal(goals, parent: row.goal),
                          onTaskAction: _taskAction,
                          onProgress: () => _editProgress(row.goal),
                          onStatus: (status) => _run(() => widget.api.setGoalStatus(row.goal.id, status)),
                          onMove: () => _move(row.goal, goals),
                          onDelete: () => _delete(row.goal, goals),
                        ),
                    ],
                  ),
                ),
            ],
          );
        },
      ),
    );
  }
}

class _GoalTile extends StatelessWidget {
  const _GoalTile({
    super.key,
    required this.onOpen,
    required this.onOpenTask,
    required this.goal,
    required this.depth,
    required this.today,
    required this.boardEnabled,
    required this.folds,
    required this.collapsed,
    required this.onFold,
    required this.onAddTask,
    required this.onAddChild,
    required this.onTaskAction,
    required this.onProgress,
    required this.onStatus,
    required this.onMove,
    required this.onDelete,
  });

  final Goal goal;
  final int depth;
  final DateTime today;
  final bool boardEnabled;

  /// Whether the goal has child goals here, which a chevron folds away.
  final bool folds;
  final bool collapsed;
  final VoidCallback onFold;
  final VoidCallback onAddTask;
  final VoidCallback onAddChild;

  /// The goal's quick view, from its key or title.
  final VoidCallback onOpen;

  /// A task's quick view, from its row.
  final ValueChanged<GoalTaskSummary> onOpenTask;

  /// Reopen or delete one of the goal's tasks.
  final void Function(GoalTaskSummary task, String action) onTaskAction;
  final VoidCallback onProgress;
  final ValueChanged<String> onStatus;
  final VoidCallback onMove;
  final VoidCallback onDelete;

  bool get _isMonth => goal.periodType == 'month';

  String get _detail {
    if (goal.childCount > 0) {
      final word = goalsOfType(goal.periodType == 'year' ? 'quarter' : 'month', goal.childCount);
      return '${goal.effectiveProgress}% · ${goal.completedChildCount} of ${goal.childCount} $word done';
    }
    if (goal.taskCount > 0) {
      return '${goal.effectiveProgress}% · ${goal.doneTaskCount} of ${goal.taskCount} '
          '${goal.taskCount == 1 ? 'task' : 'tasks'} done';
    }
    return '${goal.effectiveProgress}%';
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final completed = goal.status == 'completed';
    final addChild = childTypeToAdd(goal);
    final card = Card(
      margin: const EdgeInsets.only(top: 6),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(12, 10, 4, 6),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                if (folds)
                  InkWell(
                    key: Key('fold-${goal.key}'),
                    onTap: onFold,
                    child: Padding(
                      padding: const EdgeInsets.only(right: 6),
                      child: Icon(collapsed ? Icons.chevron_right : Icons.expand_more,
                          size: 20, semanticLabel: '${collapsed ? 'Expand' : 'Collapse'} ${goal.key}'),
                    ),
                  ),
                Expanded(
                  child: InkWell(
                    key: Key('open-${goal.key}'),
                    onTap: onOpen,
                    borderRadius: BorderRadius.circular(6),
                    child: Row(
                      children: [
                        Text(goal.key,
                            style: theme.textTheme.labelSmall?.copyWith(
                                color: theme.colorScheme.primary, decoration: TextDecoration.underline)),
                        const SizedBox(width: 8),
                        Expanded(child: Text(goal.title, style: const TextStyle(fontWeight: FontWeight.w600))),
                      ],
                    ),
                  ),
                ),
                Chip(
                  label: Text(goal.slot),
                  visualDensity: VisualDensity.compact,
                  materialTapTargetSize: MaterialTapTargetSize.shrinkWrap,
                ),
                _actions(context),
              ],
            ),
            if (goal.periodStart != null && goal.periodEnd != null)
              Text.rich(
                TextSpan(children: [
                  TextSpan(text: formatGoalRange(goal.periodStart!, goal.periodEnd!)),
                  if (completed) const TextSpan(text: '  · completed'),
                  if (!completed && goal.periodEnd!.isBefore(today))
                    TextSpan(
                      text: '  · overdue',
                      style: TextStyle(color: theme.colorScheme.error, fontWeight: FontWeight.w600),
                    ),
                ]),
                style: theme.textTheme.labelSmall?.copyWith(color: theme.colorScheme.outline),
              ),
            Padding(
              padding: const EdgeInsets.only(right: 8, top: 6),
              child: ClipRRect(
                borderRadius: BorderRadius.circular(4),
                child: LinearProgressIndicator(value: goal.effectiveProgress / 100, minHeight: 6),
              ),
            ),
            const SizedBox(height: 4),
            Text(_detail, style: theme.textTheme.bodySmall),
            for (final task in goal.tasks) _taskRow(theme, task),
            // Tasks sit only under monthly goals; a year or quarter is broken into goals below it.
            if (_isMonth && !completed && boardEnabled)
              TextButton.icon(onPressed: onAddTask, icon: const Icon(Icons.add, size: 18), label: const Text('Create task'))
            else if (addChild != null)
              TextButton.icon(
                  onPressed: onAddChild, icon: const Icon(Icons.add, size: 18), label: Text('Add ${goalsOfType(addChild)}')),
          ],
        ),
      ),
    );

    return Padding(
      padding: EdgeInsets.only(left: depth * 14.0),
      child: Opacity(
        opacity: completed ? 0.75 : 1,
        child: depth == 0
            ? card
            : DecoratedBox(
                // A child is joined to its parent by a line down its left edge.
                decoration: BoxDecoration(
                  border: Border(left: BorderSide(color: theme.colorScheme.outlineVariant, width: 3)),
                ),
                child: Padding(padding: const EdgeInsets.only(left: 4), child: card),
              ),
      ),
    );
  }

  Widget _taskRow(ThemeData theme, GoalTaskSummary task) {
    return Padding(
      padding: const EdgeInsets.only(top: 6),
      child: Row(
        children: [
          Expanded(
            child: InkWell(
              key: Key('open-${task.key}'),
              onTap: () => onOpenTask(task),
              borderRadius: BorderRadius.circular(6),
              child: Row(
                children: [
                  Text(task.key,
                      style: theme.textTheme.labelSmall?.copyWith(
                          color: theme.colorScheme.primary, decoration: TextDecoration.underline)),
                  const SizedBox(width: 8),
                  Expanded(
                    child: Text(
                      task.title,
                      style: theme.textTheme.bodyMedium?.copyWith(
                        decoration: task.column == BoardColumns.done ? TextDecoration.lineThrough : null,
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
          Text(BoardColumns.label(task.column), style: theme.textTheme.labelSmall),
          const SizedBox(width: 6),
          PointsPill(points: task.points),
          // Work finished outside a sprint never reaches the board, so reopening and deleting a
          // task has to be possible from here too.
          PopupMenuButton<String>(
            key: Key('task-menu-${task.key}'),
            icon: const Icon(Icons.more_vert, size: 18),
            padding: EdgeInsets.zero,
            onSelected: (choice) => onTaskAction(task, choice),
            itemBuilder: (context) => [
              if (task.column == BoardColumns.done) const PopupMenuItem(value: 'reopen', child: Text('Reopen')),
              const PopupMenuItem(value: 'delete', child: Text('Delete')),
            ],
          ),
        ],
      ),
    );
  }



  Widget _actions(BuildContext context) {
    final addChild = childTypeToAdd(goal);
    return PopupMenuButton<String>(
      key: Key('goal-menu-${goal.key}'),
      icon: const Icon(Icons.more_horiz),
      onSelected: (choice) {
        switch (choice) {
          case 'task':
            onAddTask();
          case 'child':
            onAddChild();
          case 'progress':
            onProgress();
          case 'move':
            onMove();
          case 'complete':
            onStatus('completed');
          case 'reopen':
            onStatus('active');
          case 'delete':
            onDelete();
        }
      },
      itemBuilder: (context) => [
        if (goal.setsProgressByHand) const PopupMenuItem(value: 'progress', child: Text('Update progress')),
        if (_isMonth && boardEnabled && goal.status == 'active') const PopupMenuItem(value: 'task', child: Text('Add task…')),
        if (addChild != null) PopupMenuItem(value: 'child', child: Text('Add ${goalsOfType(addChild)}')),
        if (goal.periodType != 'year') const PopupMenuItem(value: 'move', child: Text('Move')),
        if (goal.status == 'active')
          goal.completeProblem == null
              ? const PopupMenuItem(value: 'complete', child: Text('Complete'))
              : blockedMenuItem(context, 'Complete', goal.completeProblem!)
        else
          const PopupMenuItem(value: 'reopen', child: Text('Reopen')),
        goal.childCount > 0
            ? blockedMenuItem(context, 'Delete',
                'Delete its ${goalsOfType(goal.periodType == 'year' ? 'quarter' : 'month', 2)} first.')
            : const PopupMenuItem(value: 'delete', child: Text('Delete')),
      ],
    );
  }
}

class _MessageList extends StatelessWidget {
  const _MessageList({required this.message, this.error = false});

  final String message;
  final bool error;

  @override
  Widget build(BuildContext context) {
    return ListView(
      children: [
        Padding(
          padding: const EdgeInsets.all(32),
          child: Center(
            child: Text(
              message,
              textAlign: TextAlign.center,
              style: TextStyle(color: error ? Theme.of(context).colorScheme.error : Colors.grey.shade600),
            ),
          ),
        ),
      ],
    );
  }
}
