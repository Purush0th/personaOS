import 'package:flutter/material.dart';

import '../api/personaos_api.dart';
import '../date_utils.dart';
import '../goal_calendar.dart';
import '../goal_tree.dart';
import 'board_screen.dart';
import 'goal_view.dart';
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
      builder: (context) => _ProgressDialog(initial: goal.progress),
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
                            await showGoalQuickView(context, widget.api, row.goal.key);
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

/// "quarter" or "month": what can still be added under this goal, or null.
String? _childTypeToAdd(Goal goal) {
  final type = childTypeOf(goal.periodType);
  if (type == null || goal.status != 'active') return null;
  return goal.childCount < (type == 'quarter' ? 4 : 3) ? type : null;
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
    final addChild = _childTypeToAdd(goal);
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

  /// A menu item that cannot be used yet, with why under its label.
  PopupMenuItem<String> _blocked(BuildContext context, String label, String reason) {
    final theme = Theme.of(context);
    return PopupMenuItem<String>(
      enabled: false,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(label),
          Text(reason, style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.outline)),
        ],
      ),
    );
  }

  Widget _actions(BuildContext context) {
    final addChild = _childTypeToAdd(goal);
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
              : _blocked(context, 'Complete', goal.completeProblem!)
        else
          const PopupMenuItem(value: 'reopen', child: Text('Reopen')),
        goal.childCount > 0
            ? _blocked(context, 'Delete',
                'Delete its ${goalsOfType(goal.periodType == 'year' ? 'quarter' : 'month', 2)} first.')
            : const PopupMenuItem(value: 'delete', child: Text('Delete')),
      ],
    );
  }
}

/// One option of a slot picker; a slot that cannot be used says why instead of being picked.
typedef SlotOption = ({int value, String label, String? blocked});

/// The quarters (under a year, or Q1-Q4 of [year]) or months (of a quarter, or all twelve) a goal
/// of [type] can take, each marked over, too short or taken where it cannot.
List<SlotOption> slotOptions(String type, int year, DateTime today, {Goal? parent, int? movingId}) {
  final numbers = type == 'quarter'
      ? [1, 2, 3, 4]
      : parent?.periodEnd != null
          ? monthsOfQuarter(parent!.periodEnd!)
          : List.generate(12, (i) => i + 1);
  final taken = parent?.children.where((c) => c.id != movingId).map((c) => c.slot).toSet() ?? const <String>{};
  final parentSlot = _parentSlot(parent);
  return [
    for (final value in numbers)
      () {
        final (_, end) = type == 'quarter' ? quarterDates(year, value) : monthDates(year, value);
        final blocked = taken.contains(slotLabel(type, end))
            ? 'taken'
            : end.isBefore(today)
                ? 'over'
                : resolveGoalDates(type, year, value, null, today, parent: parentSlot).problem != null
                    ? 'too short'
                    : null;
        return (value: value, label: type == 'quarter' ? 'Q$value' : monthNames[value - 1], blocked: blocked);
      }(),
  ];
}

ParentSlot? _parentSlot(Goal? parent) => parent == null || parent.periodStart == null || parent.periodEnd == null
    ? null
    : ParentSlot(type: parent.periodType, start: parent.periodStart!, end: parent.periodEnd!);

Widget _slotItemText(BuildContext context, SlotOption option) => Text.rich(TextSpan(children: [
      TextSpan(text: option.label),
      if (option.blocked != null)
        TextSpan(
          text: ' · ${option.blocked}',
          style: TextStyle(color: Theme.of(context).colorScheme.outline, fontSize: 12),
        ),
    ]));

/// New goal (or Add quarter / Add month under [parent]): pick the type and its calendar slot and
/// the dates follow. A year picks its start day; a quarter or month picks its slot. "Under" nests
/// it in a year or quarter. The web has the same form.
class GoalSheet extends StatefulWidget {
  const GoalSheet({super.key, required this.api, required this.goals, this.parent, required this.today});

  final PersonaOsApi api;
  final List<Goal> goals;
  final Goal? parent;
  final DateTime today;

  @override
  State<GoalSheet> createState() => _GoalSheetState();
}

class _GoalSheetState extends State<GoalSheet> {
  final _title = TextEditingController();
  late String _type = widget.parent == null ? 'month' : childTypeOf(widget.parent!.periodType)!;
  late int _year = widget.parent?.periodEnd?.year ?? widget.today.year;
  late DateTime _start = widget.today;
  int? _slot;
  late int? _parentId = widget.parent?.id;
  bool _busy = false;
  String? _error;

  Goal? get _parent => widget.parent ?? widget.goals.where((g) => g.id == _parentId).firstOrNull;

  List<Goal> get _parents {
    final type = parentTypeOf(_type);
    return widget.goals
        .where((g) => g.periodType == type && g.status == 'active' && g.periodEnd?.year == _year)
        .toList();
  }

  ({DateTime? start, DateTime? end, String? problem}) get _resolved => resolveGoalDates(
        _type,
        _year,
        _type == 'year' ? null : _slot,
        _type == 'year' ? _start : null,
        widget.today,
        parent: _parentSlot(_parent),
      );

  @override
  void dispose() {
    _title.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final title = _title.text.trim();
    if (title.isEmpty) {
      setState(() => _error = 'Give the goal a title.');
      return;
    }
    if (_resolved.start == null) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await widget.api.createGoal(
        title: title,
        periodType: _type,
        year: _year,
        quarter: _type == 'quarter' ? _slot : null,
        month: _type == 'month' ? _slot : null,
        periodStart: _type == 'year' ? localYmd(_start) : null,
        parentId: _parent?.id,
      );
      if (mounted) Navigator.pop(context, true);
    } on ApiException catch (e) {
      setState(() {
        _busy = false;
        _error = e.message;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final resolved = _resolved;
    final parent = widget.parent;
    // Nothing picked yet is not a problem to show in red; the empty field says it.
    final problem = resolved.problem?.startsWith('Pick a') == true ? null : resolved.problem;
    final years = List.generate(6, (i) => widget.today.year + i);

    return Padding(
      padding: EdgeInsets.only(left: 16, right: 16, bottom: MediaQuery.of(context).viewInsets.bottom + 16),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(parent == null ? 'New goal' : 'Add ${goalsOfType(_type)}',
                style: const TextStyle(fontSize: 18, fontWeight: FontWeight.bold)),
            if (parent != null)
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Text('Under ${parent.key} ${parent.title} · ${parent.slot}', style: theme.textTheme.bodyMedium),
              ),
            const SizedBox(height: 12),
            TextField(
              controller: _title,
              autofocus: true,
              decoration: InputDecoration(labelText: 'Title', border: const OutlineInputBorder(), errorText: _error),
              onSubmitted: (_) => _submit(),
            ),
            if (parent == null) ...[
              const SizedBox(height: 12),
              SegmentedButton<String>(
                segments: [
                  for (final type in periodLabels.keys) ButtonSegment(value: type, label: Text(periodLabels[type]!)),
                ],
                selected: {_type},
                onSelectionChanged: (s) => setState(() {
                  _type = s.first;
                  _slot = null;
                  _parentId = null;
                }),
              ),
              const SizedBox(height: 12),
              DropdownButtonFormField<int>(
                key: const Key('goal-year'),
                initialValue: _year,
                decoration: const InputDecoration(labelText: 'Year', border: OutlineInputBorder()),
                items: [for (final y in years) DropdownMenuItem(value: y, child: Text('$y'))],
                onChanged: (y) => setState(() {
                  _year = y!;
                  _slot = null;
                  _parentId = null;
                  if (_start.year != y) _start = y == widget.today.year ? widget.today : DateTime(y, 1, 1);
                }),
              ),
            ],
            const SizedBox(height: 12),
            if (_type == 'year')
              OutlinedButton.icon(
                icon: const Icon(Icons.event, size: 18),
                label: Text('Starts ${localYmd(_start)}'),
                onPressed: () async {
                  final picked = await showDatePicker(
                    context: context,
                    initialDate: _start,
                    firstDate: _year == widget.today.year ? widget.today : DateTime(_year, 1, 1),
                    lastDate: DateTime(_year, 12, 31),
                  );
                  if (picked != null) setState(() => _start = picked);
                },
              )
            else
              DropdownButtonFormField<int>(
                key: ValueKey('goal-slot-$_type-$_year-${_parent?.id}'),
                initialValue: _slot,
                decoration: InputDecoration(
                  labelText: _type == 'quarter' ? 'Quarter' : 'Month',
                  border: const OutlineInputBorder(),
                ),
                items: [
                  for (final option in slotOptions(_type, _year, widget.today, parent: _parent))
                    DropdownMenuItem(
                      value: option.value,
                      enabled: option.blocked == null,
                      child: _slotItemText(context, option),
                    ),
                ],
                onChanged: (v) => setState(() => _slot = v),
              ),
            if (parent == null && parentTypeOf(_type) != null) ...[
              const SizedBox(height: 12),
              DropdownButtonFormField<int>(
                key: ValueKey('goal-parent-$_type-$_year'),
                // 0 stands for "none": a dropdown shows null as empty, and "–" is a real choice.
                initialValue: _parentId ?? 0,
                isExpanded: true,
                decoration: const InputDecoration(labelText: 'Under', border: OutlineInputBorder()),
                items: [
                  const DropdownMenuItem(value: 0, child: Text('–')),
                  for (final p in _parents)
                    DropdownMenuItem(
                      value: p.id,
                      child: Text('${p.key} ${p.title} · ${p.slot}', overflow: TextOverflow.ellipsis),
                    ),
                ],
                onChanged: (v) => setState(() {
                  _parentId = v == 0 ? null : v;
                  _slot = null;
                }),
              ),
            ],
            const SizedBox(height: 8),
            if (problem != null)
              Text(problem, style: TextStyle(color: theme.colorScheme.error))
            else if (resolved.start != null)
              Text('${formatGoalRange(resolved.start!, resolved.end!)} · ${goalDays(resolved.start!, resolved.end!)} days',
                  style: theme.textTheme.bodySmall),
            const SizedBox(height: 16),
            FilledButton(
              onPressed: _busy || resolved.start == null ? null : _submit,
              child: Text(_busy ? 'Adding…' : 'Add goal'),
            ),
          ],
        ),
      ),
    );
  }
}

/// Move: a quarter into a year or a month into a quarter, in the slot picked there — its dates
/// change to that slot and its child months move with it. "–" detaches it, keeping its dates.
class MoveGoalDialog extends StatefulWidget {
  const MoveGoalDialog({super.key, required this.api, required this.goal, required this.goals, required this.today});

  final PersonaOsApi api;
  final Goal goal;
  final List<Goal> goals;
  final DateTime today;

  @override
  State<MoveGoalDialog> createState() => _MoveGoalDialogState();
}

class _MoveGoalDialogState extends State<MoveGoalDialog> {
  late int? _parentId = widget.goal.parentId;
  int? _slot;
  bool _busy = false;
  String? _error;

  Goal? get _parent => widget.goals.where((g) => g.id == _parentId).firstOrNull;

  List<Goal> get _parents {
    final type = parentTypeOf(widget.goal.periodType);
    return widget.goals.where((g) => g.periodType == type && g.status == 'active' && g.id != widget.goal.id).toList();
  }

  bool get _canMove {
    final parent = _parent;
    if (parent == null) return widget.goal.parentId != null; // detaching
    return _slot != null &&
        resolveGoalDates(widget.goal.periodType, parent.periodEnd!.year, _slot, null, widget.today,
                    parent: _parentSlot(parent))
                .start !=
            null;
  }

  Future<void> _move() async {
    final parent = _parent;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final type = widget.goal.periodType;
      await widget.api.moveGoal(
        widget.goal.id,
        parentId: parent?.id,
        year: parent?.periodEnd?.year,
        quarter: parent != null && type == 'quarter' ? _slot : null,
        month: parent != null && type == 'month' ? _slot : null,
      );
      if (mounted) Navigator.pop(context, true);
    } on ApiException catch (e) {
      setState(() {
        _busy = false;
        _error = e.message;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final parent = _parent;
    final type = widget.goal.periodType;
    return AlertDialog(
      title: Text('Move ${widget.goal.key}'),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text('${widget.goal.title} · ${widget.goal.slot}'),
          const SizedBox(height: 16),
          DropdownButtonFormField<int>(
            initialValue: _parentId ?? 0,
            isExpanded: true,
            decoration: const InputDecoration(labelText: 'Under', border: OutlineInputBorder()),
            items: [
              const DropdownMenuItem(value: 0, child: Text('–')),
              for (final p in _parents)
                DropdownMenuItem(value: p.id, child: Text('${p.key} ${p.title} · ${p.slot}', overflow: TextOverflow.ellipsis)),
            ],
            onChanged: (v) => setState(() {
              _parentId = v == 0 ? null : v;
              _slot = null;
            }),
          ),
          if (parent != null) ...[
            const SizedBox(height: 16),
            DropdownButtonFormField<int>(
              key: ValueKey('move-slot-${parent.id}'),
              initialValue: _slot,
              decoration: InputDecoration(labelText: type == 'quarter' ? 'Quarter' : 'Month', border: const OutlineInputBorder()),
              items: [
                for (final option in slotOptions(type, parent.periodEnd!.year, widget.today, parent: parent, movingId: widget.goal.id))
                  DropdownMenuItem(value: option.value, enabled: option.blocked == null, child: _slotItemText(context, option)),
              ],
              onChanged: (v) => setState(() => _slot = v),
            ),
          ] else
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: Text(widget.goal.parentId != null ? 'It becomes standalone and keeps its dates.' : 'Pick where it goes.',
                  style: Theme.of(context).textTheme.bodySmall),
            ),
          if (_error != null)
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
            ),
        ],
      ),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
        FilledButton(onPressed: _busy || !_canMove ? null : _move, child: const Text('Move')),
      ],
    );
  }
}

/// Delete a goal (one with no child goals). When it has tasks the user picks what happens to
/// them: keep them without a goal, delete them too, or move each to its own monthly goal.
class DeleteGoalDialog extends StatefulWidget {
  const DeleteGoalDialog({super.key, required this.api, required this.goal, required this.goals});

  final PersonaOsApi api;
  final Goal goal;
  final List<Goal> goals;

  @override
  State<DeleteGoalDialog> createState() => _DeleteGoalDialogState();
}

class _DeleteGoalDialogState extends State<DeleteGoalDialog> {
  String _action = 'keep';
  final Map<String, String?> _mapping = {};
  bool _busy = false;
  String? _error;

  /// Open monthly goals other than this one: where a task can go.
  List<Goal> get _targets =>
      widget.goals.where((g) => g.periodType == 'month' && g.status == 'active' && g.id != widget.goal.id).toList();

  Future<void> _delete() async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await widget.api.deleteGoal(
        widget.goal.id,
        taskAction: _action,
        reassign: _action == 'reassign' ? {for (final t in widget.goal.tasks) t.key: _mapping[t.key]} : null,
      );
      if (mounted) Navigator.pop(context, true);
    } on ApiException catch (e) {
      setState(() {
        _busy = false;
        _error = e.message;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final tasks = widget.goal.tasks;
    return AlertDialog(
      title: Text('Delete ${widget.goal.key}?'),
      content: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('${widget.goal.title} · ${widget.goal.slot}'),
            if (tasks.isNotEmpty) ...[
              const SizedBox(height: 16),
              DropdownButtonFormField<String>(
                key: const Key('delete-task-action'),
                initialValue: _action,
                isExpanded: true,
                decoration: InputDecoration(
                  labelText: 'Its ${tasks.length} ${tasks.length == 1 ? 'task' : 'tasks'}',
                  border: const OutlineInputBorder(),
                ),
                items: [
                  const DropdownMenuItem(value: 'keep', child: Text('Keep without a goal')),
                  if (_targets.isNotEmpty) const DropdownMenuItem(value: 'reassign', child: Text('Move to other goals')),
                  const DropdownMenuItem(value: 'delete', child: Text('Delete them too')),
                ],
                onChanged: (v) => setState(() => _action = v!),
              ),
              if (_action == 'reassign')
                for (final task in tasks)
                  Padding(
                    padding: const EdgeInsets.only(top: 12),
                    child: DropdownButtonFormField<String>(
                      key: Key('reassign-${task.key}'),
                      // "none", not null: a dropdown shows null as empty, and "No goal" is a real choice.
                      initialValue: _mapping[task.key] ?? 'none',
                      isExpanded: true,
                      decoration: InputDecoration(labelText: '${task.key} ${task.title}', border: const OutlineInputBorder()),
                      items: [
                        const DropdownMenuItem(value: 'none', child: Text('No goal')),
                        for (final g in _targets)
                          DropdownMenuItem(value: g.key, child: Text('${g.key} ${g.title} · ${g.slot}', overflow: TextOverflow.ellipsis)),
                      ],
                      onChanged: (v) => setState(() => _mapping[task.key] = v == 'none' ? null : v),
                    ),
                  ),
            ],
            const SizedBox(height: 12),
            Text('This cannot be undone.', style: theme.textTheme.bodySmall),
            if (_error != null)
              Padding(
                padding: const EdgeInsets.only(top: 8),
                child: Text(_error!, style: TextStyle(color: theme.colorScheme.error)),
              ),
          ],
        ),
      ),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
        FilledButton(
          style: FilledButton.styleFrom(backgroundColor: theme.colorScheme.error, foregroundColor: theme.colorScheme.onError),
          onPressed: _busy ? null : _delete,
          child: const Text('Delete'),
        ),
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

/// Sets a goal's hand-kept progress with a slider in 5% steps, the number shown above it. The
/// web opens the same dialog from Update progress.
class _ProgressDialog extends StatefulWidget {
  const _ProgressDialog({required this.initial});

  final int initial;

  @override
  State<_ProgressDialog> createState() => _ProgressDialogState();
}

class _ProgressDialogState extends State<_ProgressDialog> {
  late double _value = (widget.initial.clamp(0, 100) / 5).round() * 5.0;

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: const Text('Update progress'),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text('${_value.round()}%', style: Theme.of(context).textTheme.headlineSmall),
          Slider(
            value: _value,
            max: 100,
            divisions: 20,
            label: '${_value.round()}%',
            onChanged: (v) => setState(() => _value = v),
          ),
        ],
      ),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
        FilledButton(onPressed: () => Navigator.pop(context, _value.round()), child: const Text('Save')),
      ],
    );
  }
}
