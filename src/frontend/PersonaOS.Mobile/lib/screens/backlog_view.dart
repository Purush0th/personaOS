import 'package:flutter/material.dart';

import '../api/personaos_api.dart';
import '../layout.dart';
import 'board_screen.dart';
import 'sprint_actions.dart';
import 'sprint_page.dart';

/// The Backlog tab of the board, like the web's: the running sprint, the sprints planned after it
/// and the backlog, each with its tasks. Sprints are created, started, completed and deleted here,
/// and tasks moved between them; a task opens its sheet to edit it.
class BacklogView extends StatefulWidget {
  const BacklogView({super.key, required this.api, required this.goals, this.onChanged});

  final PersonaOsApi api;

  /// Open monthly goals a task can sit under, for the task sheet.
  final List<Goal> goals;

  /// Called after anything changed, so the Sprint tab shows it too.
  final VoidCallback? onChanged;

  @override
  State<BacklogView> createState() => _BacklogViewState();
}

/// Where a task can go: a sprint by its key, or the backlog.
const _backlog = 'backlog';

class _BacklogViewState extends State<BacklogView> {
  late Future<PlanView> _plan = widget.api.getPlan();
  final Set<String> _collapsed = {};

  Future<void> _reload() async {
    final reloaded = widget.api.getPlan();
    setState(() {
      _plan = reloaded;
    });
    await reloaded;
  }

  /// Runs a change, says why when it fails, and reloads both tabs.
  Future<void> _change(Future<void> Function() action) async {
    try {
      await action();
    } on ApiException catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    }
    await _reload();
    widget.onChanged?.call();
  }

  // ------------------------------------------------------------ sprints

  /// Runs one of the shared sprint flows; when it changed something, both tabs reload.
  Future<void> _sprint(Future<bool> Function() flow) async {
    if (await flow()) {
      await _reload();
      widget.onChanged?.call();
    }
  }

  Future<void> _editSprint([SprintInfo? sprint]) => _sprint(() => editSprintFlow(context, widget.api, sprint));

  Future<void> _start(SprintInfo sprint) => _sprint(() => startSprintFlow(context, widget.api, sprint));

  Future<void> _complete(SprintInfo sprint, List<SprintPlan> plan) => _sprint(() => completeSprintFlow(
      context, widget.api, sprint,
      next: plan.map((p) => p.sprint).where((s) => s.isPlanned).firstOrNull));

  Future<void> _delete(SprintInfo sprint) => _sprint(() => deleteSprintFlow(context, widget.api, sprint));

  /// The sprint's own page; whatever was done there reloads both tabs.
  Future<void> _openSprint(SprintInfo sprint) => _sprint(() async =>
      await Navigator.of(context).push<bool>(
          MaterialPageRoute(builder: (_) => SprintPage(api: widget.api, sprintKey: sprint.key))) ==
      true);

  // -------------------------------------------------------------- tasks

  Future<void> _openTask(List<SprintPlan> plan, {BoardTask? task, String? sprintKey}) async {
    final changed = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => TaskSheet(
        api: widget.api,
        goals: widget.goals,
        task: task,
        sprints: plan.map((p) => p.sprint).toList(),
        defaultSprintKey: sprintKey,
      ),
    );
    if (changed == true) {
      await _reload();
      widget.onChanged?.call();
    }
  }

  Future<void> _move(BoardTask task, String target) async {
    final toBacklog = target == _backlog;
    await _change(() async {
      if (!mounted) return;
      await runWithScopeConfirmation(
        context,
        (ack) => widget.api.moveTask(
          task.key,
          column: toBacklog ? BoardColumns.backlog : BoardColumns.todo,
          sprintKey: toBacklog ? null : target,
          acknowledgeScopeChange: ack,
        ),
      );
    });
  }

  @override
  Widget build(BuildContext context) {
    return FutureBuilder<PlanView>(
      future: _plan,
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const Center(child: CircularProgressIndicator());
        }
        if (snapshot.hasError) {
          return Center(
            child: Column(mainAxisSize: MainAxisSize.min, children: [
              const Text('Could not load the backlog.'),
              const SizedBox(height: 12),
              OutlinedButton(onPressed: _reload, child: const Text('Retry')),
            ]),
          );
        }

        final plan = snapshot.data!;
        final anyRunning = plan.sprints.any((p) => p.sprint.isActive);
        final targets = [
          for (final p in plan.sprints) (p.sprint.key, sprintLabel(p.sprint)),
          (_backlog, 'Backlog'),
        ];

        return RefreshIndicator(
          onRefresh: _reload,
          child: ReadableWidth(
            maxWidth: 960,
            child: ListView(
              padding: const EdgeInsets.fromLTRB(12, 8, 12, 32),
              children: [
                for (final p in plan.sprints)
                  _Section(
                    key: Key('section-${p.sprint.key}'),
                    title: sprintLabel(p.sprint),
                    status: p.sprint.isActive ? 'running' : 'planned',
                    subtitle: '${sprintWhen(p.sprint.startsAtUtc)} – ${sprintWhen(p.sprint.endsAtUtc)}',
                    tasks: p.tasks,
                    collapsed: _collapsed.contains(p.sprint.key),
                    onToggle: () => setState(() => _collapsed.contains(p.sprint.key)
                        ? _collapsed.remove(p.sprint.key)
                        : _collapsed.add(p.sprint.key)),
                    actions: [
                      _Action('Open sprint', () => _openSprint(p.sprint)),
                      if (p.sprint.isPlanned && !anyRunning)
                        _Action('Start sprint', () => _start(p.sprint)),
                      if (p.sprint.isActive) _Action('Complete sprint', () => _complete(p.sprint, plan.sprints)),
                      _Action('Edit', () => _editSprint(p.sprint)),
                      if (p.sprint.isPlanned) _Action('Delete', () => _delete(p.sprint), destructive: true),
                    ],
                    targets: targets.where((t) => t.$1 != p.sprint.key).toList(),
                    onOpen: (task) => _openTask(plan.sprints, task: task),
                    onMove: _move,
                    onCreate: () => _openTask(plan.sprints, sprintKey: p.sprint.key),
                  ),
                _Section(
                  key: const Key('section-backlog'),
                  title: 'Backlog',
                  tasks: plan.backlog,
                  collapsed: _collapsed.contains(_backlog),
                  onToggle: () => setState(
                      () => _collapsed.contains(_backlog) ? _collapsed.remove(_backlog) : _collapsed.add(_backlog)),
                  header: FilledButton.tonalIcon(
                    onPressed: () => _editSprint(),
                    icon: const Icon(Icons.add, size: 18),
                    label: const Text('Create sprint'),
                  ),
                  actions: const [],
                  targets: targets.where((t) => t.$1 != _backlog).toList(),
                  onOpen: (task) => _openTask(plan.sprints, task: task),
                  onMove: _move,
                  onCreate: () => _openTask(plan.sprints),
                ),
              ],
            ),
          ),
        );
      },
    );
  }
}

class _Action {
  const _Action(this.label, this.run, {this.destructive = false});

  final String label;
  final VoidCallback run;
  final bool destructive;
}

/// One sprint, or the backlog: a header that folds, its tasks, and a way to add one.
class _Section extends StatelessWidget {
  const _Section({
    super.key,
    required this.title,
    required this.tasks,
    required this.collapsed,
    required this.onToggle,
    required this.actions,
    required this.targets,
    required this.onOpen,
    required this.onMove,
    required this.onCreate,
    this.status,
    this.subtitle,
    this.header,
  });

  final String title;
  final String? status;
  final String? subtitle;
  final List<BoardTask> tasks;
  final bool collapsed;
  final VoidCallback onToggle;
  final List<_Action> actions;

  /// Where a task here can be moved: (sprint key or backlog, label).
  final List<(String, String)> targets;
  final ValueChanged<BoardTask> onOpen;
  final void Function(BoardTask task, String target) onMove;
  final VoidCallback onCreate;

  /// Extra control on the header's right (the backlog's "Create sprint").
  final Widget? header;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final points = tasks.fold<int>(0, (sum, t) => sum + (t.points ?? 0));

    return Card(
      margin: const EdgeInsets.only(bottom: 12),
      color: scheme.surfaceContainerLow,
      elevation: 0,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(4, 4, 4, 8),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                IconButton(
                  tooltip: collapsed ? 'Expand' : 'Collapse',
                  icon: Icon(collapsed ? Icons.chevron_right : Icons.expand_more),
                  onPressed: onToggle,
                ),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Wrap(
                        spacing: 8,
                        crossAxisAlignment: WrapCrossAlignment.center,
                        children: [
                          Text(title, style: theme.textTheme.titleSmall),
                          if (status != null)
                            Text(status!,
                                style: theme.textTheme.labelSmall?.copyWith(
                                    color: status == 'running' ? const Color(0xFF3D8B4F) : scheme.outline)),
                          Text('${tasks.length} ${tasks.length == 1 ? 'task' : 'tasks'} · $points pts',
                              style: theme.textTheme.bodySmall),
                        ],
                      ),
                      if (subtitle != null) Text(subtitle!, style: theme.textTheme.bodySmall),
                    ],
                  ),
                ),
                ?header,
                if (actions.isNotEmpty)
                  PopupMenuButton<_Action>(
                    tooltip: 'Sprint actions',
                    onSelected: (action) => action.run(),
                    itemBuilder: (context) => [
                      for (final action in actions)
                        PopupMenuItem(
                          value: action,
                          child: Text(action.label,
                              style: action.destructive ? TextStyle(color: scheme.error) : null),
                        ),
                    ],
                  ),
              ],
            ),
            if (!collapsed) ...[
              if (tasks.isEmpty)
                Padding(
                  padding: const EdgeInsets.all(12),
                  child: Text('No tasks here yet.', style: theme.textTheme.bodySmall),
                ),
              for (final task in tasks)
                _TaskRow(
                  task: task,
                  targets: targets,
                  onOpen: () => onOpen(task),
                  onMove: (target) => onMove(task, target),
                ),
              Align(
                alignment: Alignment.centerLeft,
                child: TextButton.icon(
                  onPressed: onCreate,
                  icon: const Icon(Icons.add, size: 18),
                  label: const Text('Task'),
                ),
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class _TaskRow extends StatelessWidget {
  const _TaskRow({required this.task, required this.targets, required this.onOpen, required this.onMove});

  final BoardTask task;
  final List<(String, String)> targets;
  final VoidCallback onOpen;
  final ValueChanged<String> onMove;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Card(
      key: Key('row-${task.key}'),
      margin: const EdgeInsets.symmetric(horizontal: 4, vertical: 2),
      elevation: 0,
      child: ListTile(
        dense: true,
        contentPadding: const EdgeInsets.only(left: 12, right: 4),
        onTap: onOpen,
        title: Text(task.title, maxLines: 2, overflow: TextOverflow.ellipsis),
        subtitle: Text(
          [task.key, BoardColumns.label(task.column), if (task.goalTitle != null) task.goalTitle!].join(' · '),
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: theme.textTheme.bodySmall,
        ),
        trailing: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            PointsPill(points: task.points),
            PopupMenuButton<String>(
              tooltip: 'Move to',
              icon: const Icon(Icons.drive_file_move_outline, size: 20),
              onSelected: onMove,
              itemBuilder: (context) => [
                for (final (key, label) in targets) PopupMenuItem(value: key, child: Text('Move to $label')),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
