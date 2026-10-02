import 'package:flutter/material.dart';

import '../api/personaos_api.dart';
import '../date_utils.dart';
import '../layout.dart';
import 'attachments_section.dart';
import 'board_widgets.dart';
import 'comments_section.dart';
import 'goal_view.dart';

// A task is seen three ways, each with its own job:
// - the quick view: a short look from a list, with the common actions (move it, open it);
// - the full page: every field, its files and its discussion, for working on it;
// - the new-task sheet: only what a task needs to exist.

/// Opens a task's quick view; true when something about the task changed (moved, edited on its
/// full page, deleted), so the list it was opened from reloads. Pass [task] when it is already
/// loaded; otherwise it is read by [taskKey].
Future<bool> showTaskQuickView(
  BuildContext context,
  PersonaOsApi api,
  String taskKey, {
  BoardTask? task,
  List<Goal> goals = const [],
}) async {
  final BoardTask loaded;
  try {
    loaded = task ?? await api.getTask(taskKey);
  } on ApiException catch (e) {
    if (context.mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    return false;
  }
  if (!context.mounted) return false;
  var changed = false;
  await showQuickView<void>(
    context,
    builder: (_) => TaskQuickView(api: api, task: loaded, goals: goals, onChanged: () => changed = true),
  );
  return changed;
}

/// The quick view: key, status and title, the facts that matter at a glance (priority, points,
/// sprint, goal), the start of the description, how much discussion and how many files there are,
/// and the actions taken most often from a list: move it to another column, or open it.
class TaskQuickView extends StatefulWidget {
  const TaskQuickView({super.key, required this.api, required this.task, this.goals = const [], this.onChanged});

  final PersonaOsApi api;
  final BoardTask task;

  /// Goals a task may sit under, passed on to the full page.
  final List<Goal> goals;
  final VoidCallback? onChanged;

  @override
  State<TaskQuickView> createState() => _TaskQuickViewState();
}

class _TaskQuickViewState extends State<TaskQuickView> {
  late BoardTask _task = widget.task;
  bool _busy = false;

  Future<void> _moveTo(String column) async {
    setState(() => _busy = true);
    try {
      final moved = await runWithScopeConfirmation(
        context,
        (ack) => widget.api.moveTask(
          _task.key,
          column: column,
          sprintKey: column == BoardColumns.backlog ? null : _task.sprintKey,
          acknowledgeScopeChange: ack,
        ),
      );
      if (!moved) return;
      widget.onChanged?.call();
      final fresh = await widget.api.getTask(_task.key);
      if (mounted) setState(() => _task = fresh);
    } on ApiException catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  /// The full page replaces the quick view: coming back lands on the list, which reloads.
  Future<void> _openPage() async {
    final navigator = Navigator.of(context);
    widget.onChanged?.call();
    navigator.pop();
    await navigator.push(MaterialPageRoute<bool>(
      builder: (_) => TaskPage(api: widget.api, taskKey: _task.key, goals: widget.goals),
    ));
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final task = _task;
    final muted = theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.onSurfaceVariant);
    final targets = boardMoveTargets(task);
    final discussion = [
      if (task.commentCount > 0) '${task.commentCount} ${task.commentCount == 1 ? 'comment' : 'comments'}',
      if (task.attachmentCount > 0) '${task.attachmentCount} ${task.attachmentCount == 1 ? 'file' : 'files'}',
      if (task.updatedAtUtc != null) 'updated ${relativeTime(task.updatedAtUtc!)}',
    ];

    return SingleChildScrollView(
      key: Key('task-quick-view-${task.key}'),
      padding: const EdgeInsets.fromLTRB(20, 4, 12, 20),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Text(task.key, style: theme.textTheme.labelLarge?.copyWith(color: theme.colorScheme.outline)),
              const SizedBox(width: 8),
              StatusChip(column: task.column),
              const Spacer(),
              IconButton(
                key: const Key('open-full-page'),
                tooltip: 'Open full page',
                icon: const Icon(Icons.open_in_full),
                onPressed: _busy ? null : _openPage,
              ),
              IconButton(
                tooltip: 'Close',
                icon: const Icon(Icons.close),
                onPressed: () => Navigator.pop(context),
              ),
            ],
          ),
          Padding(
            padding: const EdgeInsets.only(right: 8),
            child: Text(task.title, style: theme.textTheme.titleLarge),
          ),
          const SizedBox(height: 10),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              _Fact(icon: Icons.flag_outlined, text: priorities[task.priority] ?? task.priority),
              PointsPill(points: task.points),
              _Fact(icon: Icons.directions_run, text: task.sprintKey ?? 'Backlog'),
              if (task.goalKey != null && task.goalTitle != null)
                InkWell(
                  borderRadius: BorderRadius.circular(999),
                  onTap: () => showGoalQuickView(context, widget.api, task.goalKey!),
                  child: GoalChip(goalKey: task.goalKey!, title: task.goalTitle!),
                ),
            ],
          ),
          if (task.description?.trim().isNotEmpty ?? false) ...[
            const SizedBox(height: 12),
            Padding(
              padding: const EdgeInsets.only(right: 8),
              child: Text(task.description!.trim(), maxLines: 4, overflow: TextOverflow.ellipsis),
            ),
          ],
          if (discussion.isNotEmpty || task.carryOverCount > 0) ...[
            const SizedBox(height: 10),
            Text(
              [
                ...discussion,
                if (task.carryOverCount > 0)
                  'carried over ${task.carryOverCount} ${task.carryOverCount == 1 ? 'time' : 'times'}',
              ].join(' · '),
              style: muted,
            ),
          ],
          if (targets.isNotEmpty) ...[
            const SizedBox(height: 16),
            Text('Move to', style: theme.textTheme.labelLarge),
            const SizedBox(height: 6),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final column in targets)
                  OutlinedButton(
                    key: Key('quick-move-$column'),
                    onPressed: _busy ? null : () => _moveTo(column),
                    child: Text(BoardColumns.label(column)),
                  ),
              ],
            ),
          ],
          const SizedBox(height: 16),
          Align(
            alignment: Alignment.centerRight,
            child: Padding(
              padding: const EdgeInsets.only(right: 8),
              child: FilledButton.tonalIcon(
                onPressed: _busy ? null : _openPage,
                icon: const Icon(Icons.edit_outlined, size: 18),
                label: const Text('Edit, files and comments'),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// A small icon and label, for one fact about an item.
class _Fact extends StatelessWidget {
  const _Fact({required this.icon, required this.text});

  final IconData icon;
  final String text;

  @override
  Widget build(BuildContext context) {
    final style = Theme.of(context).textTheme.labelMedium;
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(icon, size: 16, color: Theme.of(context).colorScheme.outline),
        const SizedBox(width: 4),
        Text(text, style: style),
      ],
    );
  }
}

/// A task on its own page: every field, its files and its discussion. Read afresh, so it is
/// current however it was reached; pops with true when anything changed.
class TaskPage extends StatefulWidget {
  const TaskPage({super.key, required this.api, required this.taskKey, this.goals = const []});

  final PersonaOsApi api;
  final String taskKey;

  /// Goals a task may sit under; read here when none are passed.
  final List<Goal> goals;

  @override
  State<TaskPage> createState() => _TaskPageState();
}

class _TaskPageState extends State<TaskPage> {
  late Future<(BoardTask, List<SprintInfo>, List<Goal>)> _data = _load();
  bool _changed = false;

  Future<(BoardTask, List<SprintInfo>, List<Goal>)> _load() async {
    final task = widget.api.getTask(widget.taskKey);
    final sprints = widget.api
        .getPlan()
        .then((p) => p.sprints.map((s) => s.sprint).toList(), onError: (Object _) => <SprintInfo>[]);
    final goals = widget.goals.isNotEmpty
        ? Future.value(widget.goals)
        // Tasks sit only under open monthly goals; with goals switched off there are none to pick.
        : widget.api.getGoals().then(
            (all) => all.where((g) => g.periodType == 'month' && g.status == 'active').toList(),
            onError: (Object _) => <Goal>[],
          );
    return (task, sprints, goals).wait;
  }

  void _reload() {
    _changed = true;
    setState(() {
      _data = _load();
    });
  }

  @override
  Widget build(BuildContext context) {
    return PopScope<Object?>(
      canPop: false,
      onPopInvokedWithResult: (didPop, _) {
        if (!didPop) Navigator.of(context).pop(_changed);
      },
      child: Scaffold(
        appBar: AppBar(title: Text(widget.taskKey)),
        body: FutureBuilder<(BoardTask, List<SprintInfo>, List<Goal>)>(
          future: _data,
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) {
              return const Center(child: CircularProgressIndicator());
            }
            if (snapshot.hasError) {
              return Center(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    const Text('Could not load that task.'),
                    const SizedBox(height: 12),
                    OutlinedButton(onPressed: _reload, child: const Text('Try again')),
                  ],
                ),
              );
            }
            final (task, sprints, goals) = snapshot.data!;
            return ReadableWidth(
              maxWidth: 1100,
              child: TaskSheet(
                // A fresh form after each change, so it shows what the server now holds.
                key: ValueKey('${task.key}-${task.updatedAtUtc}-${task.column}-${task.sprintKey}'),
                api: widget.api,
                goals: goals,
                task: task,
                sprints: sprints,
                onChanged: _reload,
                onDeleted: () {
                  _changed = true;
                  Navigator.of(context).pop(true);
                },
              ),
            );
          },
        ),
      ),
    );
  }
}

/// The task form. Without [task] it is the new-task sheet: title, points, priority, goal and
/// sprint, and "Add task". With [task] it is the body of the task's full page: every field, the
/// moves, delete, and its files and comments; from [Breakpoints.sideBySide] the details sit in a
/// panel beside the text, as on the web.
class TaskSheet extends StatefulWidget {
  const TaskSheet({
    super.key,
    required this.api,
    required this.goals,
    this.task,
    this.sprints = const [],
    this.defaultSprintKey,
    this.fixedGoalId,
    this.onChanged,
    this.onDeleted,
  });

  final PersonaOsApi api;
  final List<Goal> goals;
  final BoardTask? task;

  /// Sprints a task can be moved into: the running one and those planned after it.
  final List<SprintInfo> sprints;

  /// Where a new task goes by default; null puts it in the backlog.
  final String? defaultSprintKey;

  /// For a task added from a goal: the goal is set and not offered as a choice.
  final int? fixedGoalId;

  /// An existing task was saved or moved; the page reloads it.
  final VoidCallback? onChanged;

  /// An existing task was deleted; the page closes.
  final VoidCallback? onDeleted;

  @override
  State<TaskSheet> createState() => _TaskSheetState();
}

class _TaskSheetState extends State<TaskSheet> {
  late final _title = TextEditingController(text: widget.task?.title ?? '');
  late final _description = TextEditingController(text: widget.task?.description ?? '');
  late String _priority = widget.task?.priority ?? 'medium';
  late int? _points = widget.task?.points;
  late int? _goalId = widget.fixedGoalId ?? widget.task?.goalId;
  late String? _sprintKey = widget.task?.sprintKey ?? widget.defaultSprintKey;
  bool _busy = false;
  String? _error;

  bool get _editing => widget.task != null;

  @override
  void dispose() {
    _title.dispose();
    _description.dispose();
    super.dispose();
  }

  /// Runs [action]. A new task closes the sheet once added; an existing one stays on its page,
  /// which reloads it.
  Future<void> _guard(Future<bool> Function() action, {String? done}) async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final completed = await action();
      if (!mounted) return;
      setState(() => _busy = false);
      if (!completed) return;
      if (!_editing) {
        Navigator.pop(context, true);
        return;
      }
      if (done != null) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(done)));
      widget.onChanged?.call();
    } on ApiException catch (e) {
      if (mounted) {
        setState(() {
          _busy = false;
          _error = e.message;
        });
      }
    }
  }

  Future<void> _save() async {
    final title = _title.text.trim();
    if (title.isEmpty) {
      setState(() => _error = 'Give the task a title.');
      return;
    }
    await _guard(done: 'Saved.', () async {
      if (_editing) {
        final task = widget.task!;
        final description = _description.text.trim();
        await widget.api.updateTask(
          task.key,
          title: title,
          points: _points,
          goalId: _goalId,
          // Only a change is sent: an untouched empty field must not "clear" nothing.
          description: description == (task.description ?? '').trim() ? null : description,
          priority: _priority == task.priority ? null : _priority,
        );
        // Moving between sprints is its own call, and may be a scope change.
        if (_sprintKey != task.sprintKey) {
          if (!mounted) return true;
          return runWithScopeConfirmation(
            context,
            (ack) => widget.api.moveTask(
              task.key,
              column: _sprintKey == null ? BoardColumns.backlog : BoardColumns.todo,
              sprintKey: _sprintKey,
              acknowledgeScopeChange: ack,
            ),
          );
        }
        return true;
      }
      return runWithScopeConfirmation(
        context,
        (ack) => widget.api.createTask(
          title: title,
          priority: _priority,
          points: _points,
          goalId: _goalId,
          sprintKey: _sprintKey,
          acknowledgeScopeChange: ack,
        ),
      );
    });
  }

  Future<void> _moveTo(String column) => _guard(
        done: 'Moved to ${BoardColumns.label(column)}.',
        () => runWithScopeConfirmation(
          context,
          (ack) => widget.api.moveTask(
            widget.task!.key,
            column: column,
            sprintKey: column == BoardColumns.backlog ? null : widget.task!.sprintKey,
            acknowledgeScopeChange: ack,
          ),
        ),
      );

  Future<void> _delete() async {
    final task = widget.task!;
    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text('Delete ${task.key}?'),
        content: Text('“${task.title}” will be deleted, with its comments and files.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Delete')),
        ],
      ),
    );
    if (ok != true || !mounted) return;
    setState(() => _busy = true);
    try {
      await widget.api.deleteTask(task.key);
      widget.onDeleted?.call();
    } on ApiException catch (e) {
      if (mounted) {
        setState(() {
          _busy = false;
          _error = e.message;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    if (!_editing) return _newTaskSheet(context);
    return LayoutBuilder(
      builder: (context, constraints) {
        final wide = constraints.maxWidth >= Breakpoints.sideBySide;
        const pad = EdgeInsets.fromLTRB(16, 16, 16, 32);
        if (!wide) {
          return ListView(
            padding: pad,
            children: [
              ..._textFields(),
              const SizedBox(height: 16),
              ..._details(context),
              const Divider(height: 32),
              ..._discussion(),
            ],
          );
        }
        return SingleChildScrollView(
          padding: pad,
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [..._textFields(), const Divider(height: 32), ..._discussion()],
                ),
              ),
              const SizedBox(width: 24),
              SizedBox(
                width: 320,
                child: Card(
                  key: const Key('task-details-panel'),
                  margin: EdgeInsets.zero,
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        Text('Details', style: Theme.of(context).textTheme.titleMedium),
                        const SizedBox(height: 12),
                        ..._details(context),
                      ],
                    ),
                  ),
                ),
              ),
            ],
          ),
        );
      },
    );
  }

  /// The new-task sheet: what a task needs to exist, and nothing it can only have once it does.
  Widget _newTaskSheet(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: EdgeInsets.fromLTRB(16, 0, 16, MediaQuery.of(context).viewInsets.bottom + 16),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('New task', style: theme.textTheme.titleLarge),
            const SizedBox(height: 12),
            ..._textFields(),
            const SizedBox(height: 12),
            ..._fields(context),
            const SizedBox(height: 16),
            Align(
              alignment: Alignment.centerRight,
              child: FilledButton(
                onPressed: _busy ? null : _save,
                child: Text(_busy ? 'Saving…' : 'Add task'),
              ),
            ),
          ],
        ),
      ),
    );
  }

  List<Widget> _textFields() => [
        TextField(
          controller: _title,
          autofocus: !_editing,
          textCapitalization: TextCapitalization.sentences,
          decoration: InputDecoration(labelText: 'Task', border: const OutlineInputBorder(), errorText: _error),
        ),
        if (_editing) ...[
          const SizedBox(height: 12),
          TextField(
            key: const Key('task-description'),
            controller: _description,
            minLines: 3,
            maxLines: 12,
            textCapitalization: TextCapitalization.sentences,
            decoration: const InputDecoration(labelText: 'Description', border: OutlineInputBorder()),
          ),
        ],
      ];

  /// Points, priority, goal and sprint: shared by the new-task sheet and the details.
  List<Widget> _fields(BuildContext context) {
    final theme = Theme.of(context);
    return [
      Text('Value points', style: theme.textTheme.labelLarge),
      const SizedBox(height: 6),
      Wrap(
        spacing: 6,
        runSpacing: 6,
        children: [
          ChoiceChip(label: const Text('–'), selected: _points == null, onSelected: (_) => setState(() => _points = null)),
          for (final p in valuePoints)
            ChoiceChip(label: Text('$p'), selected: _points == p, onSelected: (_) => setState(() => _points = p)),
        ],
      ),
      if ((_points ?? 0) >= 13)
        Padding(
          padding: const EdgeInsets.only(top: 6),
          child: Text('Consider splitting it.', style: theme.textTheme.bodySmall?.copyWith(color: const Color(0xFFB26A00))),
        ),
      const SizedBox(height: 12),
      DropdownButtonFormField<String>(
        key: const Key('task-priority'),
        initialValue: priorities.containsKey(_priority) ? _priority : 'medium',
        decoration: const InputDecoration(labelText: 'Priority', border: OutlineInputBorder()),
        items: [for (final p in priorities.entries) DropdownMenuItem(value: p.key, child: Text(p.value))],
        onChanged: (v) => setState(() => _priority = v ?? _priority),
      ),
      if (widget.fixedGoalId == null && (widget.goals.isNotEmpty || _goalId != null)) ...[
        const SizedBox(height: 12),
        DropdownButtonFormField<int?>(
          initialValue: _goalId,
          isExpanded: true,
          decoration: const InputDecoration(labelText: 'Goal', border: OutlineInputBorder()),
          items: [
            const DropdownMenuItem<int?>(value: null, child: Text('No goal')),
            for (final g in widget.goals)
              DropdownMenuItem<int?>(value: g.id, child: Text('${g.key} ${g.title} · ${g.slot}', overflow: TextOverflow.ellipsis)),
            // Its own goal, even when not among those offered: a dropdown whose value is not
            // among its items cannot build.
            if (_goalId != null && !widget.goals.any((g) => g.id == _goalId))
              DropdownMenuItem<int?>(
                value: _goalId,
                child: Text(widget.task?.goalKey ?? 'Its goal', overflow: TextOverflow.ellipsis),
              ),
          ],
          onChanged: (v) => setState(() => _goalId = v),
        ),
      ],
      if (widget.fixedGoalId == null) ...[
        const SizedBox(height: 12),
        DropdownButtonFormField<String?>(
          initialValue: _sprintKey,
          isExpanded: true,
          decoration: const InputDecoration(labelText: 'Sprint', border: OutlineInputBorder()),
          items: [
            const DropdownMenuItem<String?>(value: null, child: Text('Backlog (no sprint)')),
            for (final s in widget.sprints)
              DropdownMenuItem<String?>(value: s.key, child: Text(sprintLabel(s), overflow: TextOverflow.ellipsis)),
            // Its own sprint, even when not among those offered (a closed one, or a view opened
            // without the plan).
            if (_sprintKey != null && !widget.sprints.any((s) => s.key == _sprintKey))
              DropdownMenuItem<String?>(value: _sprintKey, child: Text(_sprintKey!)),
          ],
          onChanged: (v) => setState(() => _sprintKey = v),
        ),
      ],
    ];
  }

  /// An existing task's details: status and its moves, the fields, save and delete, and when it
  /// was made and last changed.
  List<Widget> _details(BuildContext context) {
    final theme = Theme.of(context);
    final task = widget.task!;
    return [
      Row(
        children: [
          Text('Status', style: theme.textTheme.labelLarge),
          const SizedBox(width: 8),
          StatusChip(column: task.column),
        ],
      ),
      if (boardMoveTargets(task).isNotEmpty) ...[
        const SizedBox(height: 8),
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            for (final column in boardMoveTargets(task))
              OutlinedButton(
                onPressed: _busy ? null : () => _moveTo(column),
                child: Text('Move to ${BoardColumns.label(column)}'),
              ),
          ],
        ),
      ],
      if (task.carryOverCount > 0) ...[
        const SizedBox(height: 8),
        Text('Carried over ${task.carryOverCount} ${task.carryOverCount == 1 ? 'time' : 'times'}.',
            style: theme.textTheme.bodySmall),
      ],
      const SizedBox(height: 16),
      ..._fields(context),
      const SizedBox(height: 16),
      Row(
        children: [
          TextButton(
            onPressed: _busy ? null : _delete,
            child: Text('Delete', style: TextStyle(color: theme.colorScheme.error)),
          ),
          const Spacer(),
          FilledButton(onPressed: _busy ? null : _save, child: Text(_busy ? 'Saving…' : 'Save')),
        ],
      ),
      if (task.createdAtUtc != null)
        Padding(
          padding: const EdgeInsets.only(top: 8),
          child: Text(
            [
              'Created ${relativeTime(task.createdAtUtc!)}',
              if (task.updatedAtUtc != null) 'updated ${relativeTime(task.updatedAtUtc!)}',
            ].join(' · '),
            style: theme.textTheme.bodySmall,
          ),
        ),
    ];
  }

  List<Widget> _discussion() => [
        AttachmentsSection(api: widget.api, itemType: 'task', itemKey: widget.task!.key),
        const Divider(height: 32),
        CommentsSection(api: widget.api, itemType: 'task', itemKey: widget.task!.key),
      ];
}
