import 'package:flutter/material.dart';

import '../api/personaos_api.dart';
import '../date_utils.dart';
import 'board_screen.dart';

/// Goals are the epics of the sprint board. They do not nest: a goal is broken down into tasks
/// with story points, and its progress comes from those tasks.
class GoalsScreen extends StatefulWidget {
  const GoalsScreen({super.key, required this.api, this.boardEnabled = true});

  final PersonaOsApi api;

  /// Whether tasks can be added under a goal (the board module is on).
  final bool boardEnabled;

  @override
  State<GoalsScreen> createState() => _GoalsScreenState();
}

class _GoalsScreenState extends State<GoalsScreen> {
  late Future<List<Goal>> _goals;
  bool _includeDropped = false;

  @override
  void initState() {
    super.initState();
    _goals = widget.api.getGoals(includeDropped: _includeDropped);
  }

  void _reload() {
    setState(() {
      _goals = widget.api.getGoals(includeDropped: _includeDropped);
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

  Future<void> _addGoal() async {
    final created = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (_) => _AddGoalSheet(api: widget.api),
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

  Future<void> _editProgress(Goal goal) async {
    final controller = TextEditingController(text: goal.progress.toString());
    final value = await showDialog<int>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Set progress'),
        content: TextField(
          controller: controller,
          autofocus: true,
          keyboardType: TextInputType.number,
          decoration: const InputDecoration(suffixText: '%'),
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
          FilledButton(
            onPressed: () => Navigator.pop(context, int.tryParse(controller.text.trim())),
            child: const Text('Save'),
          ),
        ],
      ),
    );
    if (value != null) {
      await _run(() => widget.api.setGoalProgress(goal.id, value.clamp(0, 100)));
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Goals'),
        actions: [
          IconButton(
            tooltip: _includeDropped ? 'Hide dropped' : 'Show dropped',
            icon: Icon(_includeDropped ? Icons.visibility_off : Icons.visibility),
            onPressed: () {
              setState(() => _includeDropped = !_includeDropped);
              _reload();
            },
          ),
        ],
      ),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: _addGoal,
        icon: const Icon(Icons.add),
        label: const Text('New goal'),
      ),
      body: RefreshIndicator(
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
            return ListView.builder(
              padding: const EdgeInsets.fromLTRB(12, 12, 12, 88),
              itemCount: goals.length,
              itemBuilder: (context, i) => _GoalTile(
                goal: goals[i],
                boardEnabled: widget.boardEnabled,
                onAddTask: () => _addTask(goals[i]),
                onProgress: () => _editProgress(goals[i]),
                onStatus: (status) => _run(() => widget.api.setGoalStatus(goals[i].id, status)),
                onDelete: () => _run(() => widget.api.deleteGoal(goals[i].id)),
              ),
            );
          },
        ),
      ),
    );
  }
}

class _GoalTile extends StatelessWidget {
  const _GoalTile({
    required this.goal,
    required this.boardEnabled,
    required this.onAddTask,
    required this.onProgress,
    required this.onStatus,
    required this.onDelete,
  });

  final Goal goal;
  final bool boardEnabled;
  final VoidCallback onAddTask;
  final VoidCallback onProgress;
  final ValueChanged<String> onStatus;
  final VoidCallback onDelete;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final dropped = goal.status == 'dropped';
    return Opacity(
      opacity: dropped ? 0.55 : 1,
      child: Card(
        margin: const EdgeInsets.only(top: 6),
        child: Padding(
          padding: const EdgeInsets.fromLTRB(12, 10, 4, 10),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Text(goal.key, style: theme.textTheme.labelSmall?.copyWith(color: theme.colorScheme.outline)),
                  const SizedBox(width: 8),
                  Expanded(
                    child: Text(goal.title, style: const TextStyle(fontWeight: FontWeight.w600)),
                  ),
                  Chip(
                    label: Text(goal.periodType),
                    visualDensity: VisualDensity.compact,
                    materialTapTargetSize: MaterialTapTargetSize.shrinkWrap,
                  ),
                  _actions(),
                ],
              ),
              if (goal.status != 'active')
                Text(goal.status, style: theme.textTheme.labelSmall),
              Padding(
                padding: const EdgeInsets.only(right: 8, top: 6),
                child: ClipRRect(
                  borderRadius: BorderRadius.circular(4),
                  child: LinearProgressIndicator(value: goal.effectiveProgress / 100, minHeight: 6),
                ),
              ),
              const SizedBox(height: 4),
              Text(
                goal.taskCount == 0
                    ? '${goal.effectiveProgress}%'
                    : '${goal.effectiveProgress}% · ${goal.donePoints} of ${goal.totalPoints} points · '
                        '${goal.doneTaskCount} of ${goal.taskCount} tasks',
                style: theme.textTheme.bodySmall,
              ),
              for (final task in goal.tasks)
                Padding(
                  padding: const EdgeInsets.only(top: 6, right: 8),
                  child: Row(
                    children: [
                      Text(task.key, style: theme.textTheme.labelSmall?.copyWith(color: theme.colorScheme.outline)),
                      const SizedBox(width: 8),
                      Expanded(
                        child: Text(
                          task.title,
                          style: theme.textTheme.bodyMedium?.copyWith(
                            decoration: task.column == BoardColumns.done ? TextDecoration.lineThrough : null,
                          ),
                        ),
                      ),
                      Text(BoardColumns.label(task.column), style: theme.textTheme.labelSmall),
                      const SizedBox(width: 6),
                      PointsPill(points: task.points),
                    ],
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _actions() {
    return PopupMenuButton<String>(
      icon: const Icon(Icons.more_horiz),
      onSelected: (choice) {
        switch (choice) {
          case 'task':
            onAddTask();
          case 'progress':
            onProgress();
          case 'complete':
            onStatus('completed');
          case 'drop':
            onStatus('dropped');
          case 'reactivate':
            onStatus('active');
          case 'delete':
            onDelete();
        }
      },
      itemBuilder: (context) => [
        if (boardEnabled) const PopupMenuItem(value: 'task', child: Text('Add task…')),
        if (goal.taskCount == 0) const PopupMenuItem(value: 'progress', child: Text('Set progress…')),
        if (goal.status != 'completed') const PopupMenuItem(value: 'complete', child: Text('Complete')),
        if (goal.status == 'active')
          const PopupMenuItem(value: 'drop', child: Text('Drop'))
        else
          const PopupMenuItem(value: 'reactivate', child: Text('Reactivate')),
        const PopupMenuItem(value: 'delete', child: Text('Delete')),
      ],
    );
  }
}

/// Bottom sheet to add a goal.
class _AddGoalSheet extends StatefulWidget {
  const _AddGoalSheet({required this.api});

  final PersonaOsApi api;

  @override
  State<_AddGoalSheet> createState() => _AddGoalSheetState();
}

class _AddGoalSheetState extends State<_AddGoalSheet> {
  final _title = TextEditingController();
  String _period = 'month';
  bool _busy = false;
  String? _error;

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
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await widget.api.createGoal(title: title, periodType: _period, periodStart: localYmd(DateTime.now()));
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
    return Padding(
      padding: EdgeInsets.only(left: 16, right: 16, top: 16, bottom: MediaQuery.of(context).viewInsets.bottom + 16),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const Text('New goal', style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold)),
          const SizedBox(height: 12),
          TextField(
            controller: _title,
            autofocus: true,
            decoration: InputDecoration(
              labelText: 'What do you want to achieve?',
              border: const OutlineInputBorder(),
              errorText: _error,
            ),
            onSubmitted: (_) => _submit(),
          ),
          const SizedBox(height: 12),
          SegmentedButton<String>(
            segments: const [
              ButtonSegment(value: 'year', label: Text('Yearly')),
              ButtonSegment(value: 'quarter', label: Text('Quarterly')),
              ButtonSegment(value: 'month', label: Text('Monthly')),
            ],
            selected: {_period},
            onSelectionChanged: (s) => setState(() => _period = s.first),
          ),
          const SizedBox(height: 16),
          FilledButton(onPressed: _busy ? null : _submit, child: Text(_busy ? 'Adding…' : 'Add goal')),
        ],
      ),
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
