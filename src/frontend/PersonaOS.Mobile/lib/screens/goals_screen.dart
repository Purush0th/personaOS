import 'package:flutter/material.dart';

import '../api/personaos_api.dart';
import '../date_utils.dart';

/// Goal hierarchy: an indented flat list with rollup progress bars and
/// add / set-progress / complete / drop / delete actions.
class GoalsScreen extends StatefulWidget {
  const GoalsScreen({super.key, required this.api});

  final PersonaOsApi api;

  @override
  State<GoalsScreen> createState() => _GoalsScreenState();
}

class _GoalsScreenState extends State<GoalsScreen> {
  late Future<List<GoalNode>> _goals;
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
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(e.message)));
      }
    }
  }

  Future<void> _addGoal({int? parentId}) async {
    final created = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (_) => _AddGoalSheet(api: widget.api, parentId: parentId),
    );
    if (created == true) _reload();
  }

  Future<void> _editProgress(GoalNode goal) async {
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
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () {
              final parsed = int.tryParse(controller.text.trim());
              Navigator.pop(context, parsed);
            },
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
        onPressed: () => _addGoal(),
        icon: const Icon(Icons.add),
        label: const Text('New goal'),
      ),
      body: RefreshIndicator(
        onRefresh: () async => _reload(),
        child: FutureBuilder<List<GoalNode>>(
          future: _goals,
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) {
              return const Center(child: CircularProgressIndicator());
            }
            if (snapshot.hasError) {
              return _ErrorList(message: 'Could not load goals.');
            }
            final rows = flattenGoals(snapshot.data!);
            if (rows.isEmpty) {
              return _EmptyList(
                message: 'No goals yet. Add one, or ask your assistant in chat.',
              );
            }
            return ListView.builder(
              padding: const EdgeInsets.fromLTRB(12, 12, 12, 88),
              itemCount: rows.length,
              itemBuilder: (context, i) => _GoalTile(
                row: rows[i],
                onAddSub: () => _addGoal(parentId: rows[i].goal.id),
                onProgress: () => _editProgress(rows[i].goal),
                onStatus: (status) =>
                    _run(() => widget.api.setGoalStatus(rows[i].goal.id, status)),
                onDelete: () => _run(() => widget.api.deleteGoal(rows[i].goal.id)),
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
    required this.row,
    required this.onAddSub,
    required this.onProgress,
    required this.onStatus,
    required this.onDelete,
  });

  final GoalRow row;
  final VoidCallback onAddSub;
  final VoidCallback onProgress;
  final ValueChanged<String> onStatus;
  final VoidCallback onDelete;

  @override
  Widget build(BuildContext context) {
    final goal = row.goal;
    final dropped = goal.status == 'dropped';
    return Opacity(
      opacity: dropped ? 0.55 : 1,
      child: Card(
        margin: EdgeInsets.only(left: row.depth * 20.0, top: 6),
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(
                      goal.title,
                      style: const TextStyle(fontWeight: FontWeight.w600),
                    ),
                  ),
                  Chip(
                    label: Text(goal.periodType),
                    visualDensity: VisualDensity.compact,
                    materialTapTargetSize: MaterialTapTargetSize.shrinkWrap,
                  ),
                  if (goal.status != 'active') ...[
                    const SizedBox(width: 6),
                    Text(goal.status,
                        style: Theme.of(context).textTheme.labelSmall),
                  ],
                ],
              ),
              const SizedBox(height: 8),
              ClipRRect(
                borderRadius: BorderRadius.circular(4),
                child: LinearProgressIndicator(
                  value: goal.effectiveProgress / 100,
                  minHeight: 6,
                ),
              ),
              const SizedBox(height: 4),
              Row(
                children: [
                  Text('${goal.effectiveProgress}%',
                      style: Theme.of(context).textTheme.bodySmall),
                  if (goal.children.isNotEmpty)
                    Text('  · rolled up from ${goal.children.length}',
                        style: Theme.of(context).textTheme.bodySmall),
                  const Spacer(),
                  _actions(goal),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _actions(GoalNode goal) {
    return PopupMenuButton<String>(
      icon: const Icon(Icons.more_horiz),
      onSelected: (choice) {
        switch (choice) {
          case 'progress':
            onProgress();
          case 'sub':
            onAddSub();
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
        if (goal.children.isEmpty)
          const PopupMenuItem(value: 'progress', child: Text('Set progress…')),
        const PopupMenuItem(value: 'sub', child: Text('Add sub-goal…')),
        if (goal.status != 'completed')
          const PopupMenuItem(value: 'complete', child: Text('Complete')),
        if (goal.status == 'active')
          const PopupMenuItem(value: 'drop', child: Text('Drop'))
        else
          const PopupMenuItem(value: 'reactivate', child: Text('Reactivate')),
        const PopupMenuItem(value: 'delete', child: Text('Delete')),
      ],
    );
  }
}

/// Bottom sheet to add a goal (or a sub-goal when [parentId] is set).
class _AddGoalSheet extends StatefulWidget {
  const _AddGoalSheet({required this.api, this.parentId});

  final PersonaOsApi api;
  final int? parentId;

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
      await widget.api.createGoal(
        title: title,
        periodType: _period,
        periodStart: localYmd(DateTime.now()),
        parentGoalId: widget.parentId,
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
    return Padding(
      padding: EdgeInsets.only(
        left: 16,
        right: 16,
        top: 16,
        bottom: MediaQuery.of(context).viewInsets.bottom + 16,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            widget.parentId == null ? 'New goal' : 'New sub-goal',
            style: const TextStyle(fontSize: 18, fontWeight: FontWeight.bold),
          ),
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
          FilledButton(
            onPressed: _busy ? null : _submit,
            child: Text(_busy ? 'Adding…' : 'Add goal'),
          ),
        ],
      ),
    );
  }
}

class _EmptyList extends StatelessWidget {
  const _EmptyList({required this.message});
  final String message;

  @override
  Widget build(BuildContext context) {
    return ListView(
      children: [
        Padding(
          padding: const EdgeInsets.all(32),
          child: Center(
            child: Text(message,
                textAlign: TextAlign.center,
                style: TextStyle(color: Colors.grey.shade600)),
          ),
        ),
      ],
    );
  }
}

class _ErrorList extends StatelessWidget {
  const _ErrorList({required this.message});
  final String message;

  @override
  Widget build(BuildContext context) {
    return ListView(
      children: [
        Padding(
          padding: const EdgeInsets.all(32),
          child: Center(
            child: Text(message,
                style: TextStyle(color: Theme.of(context).colorScheme.error)),
          ),
        ),
      ],
    );
  }
}
