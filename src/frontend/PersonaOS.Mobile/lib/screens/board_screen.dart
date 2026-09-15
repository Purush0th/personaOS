import 'package:flutter/material.dart';

import '../api/personaos_api.dart';

/// The sprint board: Backlog, This week, In progress and Done for the current or next sprint.
///
/// A phone cannot show four columns side by side, so columns are pages to swipe between, and
/// dragging a card (long press) raises a row of drop targets — one per column — at the top, so
/// a card can reach any column without scrolling while held. Every drag has a menu
/// alternative in the task sheet.
class BoardScreen extends StatefulWidget {
  const BoardScreen({super.key, required this.api});

  final PersonaOsApi api;

  @override
  State<BoardScreen> createState() => _BoardScreenState();
}

class _BoardScreenState extends State<BoardScreen> {
  final _pages = PageController(viewportFraction: 0.9);
  String _view = 'current';
  BoardView? _board;
  List<Goal> _goals = const [];
  String? _error;
  bool _loading = true;
  bool _dragging = false;
  int _page = 0;

  @override
  void initState() {
    super.initState();
    _reload();
    _loadGoals();
  }

  @override
  void dispose() {
    _pages.dispose();
    super.dispose();
  }

  Future<void> _reload() async {
    try {
      final board = await widget.api.getBoard(sprint: _view);
      if (!mounted) return;
      setState(() {
        _board = board;
        _error = null;
        _loading = false;
        _page = _page.clamp(0, board.visibleColumns.length - 1);
      });
    } on ApiException catch (e) {
      if (mounted) {
        setState(() {
          _error = e.message;
          _loading = false;
        });
      }
    } catch (_) {
      if (mounted) {
        setState(() {
          _error = 'Could not load the board.';
          _loading = false;
        });
      }
    }
  }

  Future<void> _loadGoals() async {
    try {
      final goals = await widget.api.getGoals();
      if (mounted) setState(() => _goals = goals.where((g) => g.status == 'active').toList());
    } catch (_) {
      // Goals may be switched off; tasks then simply have no goal to pick.
    }
  }

  /// Runs a change and reloads, showing any error. A scope change is confirmed first.
  Future<void> _change(Future<void> Function(bool acknowledge) attempt) async {
    try {
      await runWithScopeConfirmation(context, attempt);
    } on ApiException catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
      }
    }
    await _reload();
  }

  Future<void> _move(BoardTask task, String column, {int? index}) => _change(
        (ack) => widget.api.moveTask(
          task.id,
          column: column,
          sprint: column == BoardColumns.backlog ? null : _view,
          index: index,
          acknowledgeScopeChange: ack,
        ),
      );

  Future<void> _switchView(String view) async {
    if (view == _view) return;
    setState(() {
      _view = view;
      _page = 0;
    });
    if (_pages.hasClients) _pages.jumpToPage(0);
    await _reload();
  }

  Future<void> _startSprint() async {
    final board = _board;
    if (board == null) return;
    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text('Start sprint ${board.sprint.number}?'),
        content: Text('It starts now with ${board.sprint.totalPoints} points and ends on Sunday at 18:00.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Not yet')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Start')),
        ],
      ),
    );
    if (ok == true) await _change((_) => widget.api.startSprint());
  }

  Future<void> _openTask({BoardTask? task, String? column}) async {
    final changed = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => TaskSheet(
        api: widget.api,
        goals: _goals,
        task: task,
        destination: column == BoardColumns.backlog || column == null ? 'backlog' : _view,
        moveTargets: task == null ? const [] : _moveTargets(task),
        sprintView: _view,
      ),
    );
    if (changed == true) await _reload();
  }

  List<String> _moveTargets(BoardTask task) {
    final board = _board;
    if (board == null) return const [];
    return board.visibleColumns
        .where((c) => c != task.column)
        .where((c) => board.sprint.isActive || c == BoardColumns.backlog || c == BoardColumns.todo)
        .toList();
  }

  @override
  Widget build(BuildContext context) {
    final board = _board;
    return Scaffold(
      appBar: AppBar(
        title: const Text('Board'),
        actions: [
          IconButton(
            tooltip: 'Sprint report',
            icon: const Icon(Icons.insights_outlined),
            onPressed: () => Navigator.of(context).push(MaterialPageRoute(
              builder: (_) => SprintReportScreen(api: widget.api),
            )),
          ),
        ],
      ),
      floatingActionButton: board == null
          ? null
          : FloatingActionButton.extended(
              onPressed: () {
                final column = board.visibleColumns[_page];
                _openTask(column: column == BoardColumns.backlog ? column : BoardColumns.todo);
              },
              icon: const Icon(Icons.add),
              label: const Text('Task'),
            ),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : board == null
              ? _Message(text: _error ?? 'Could not load the board.', onRetry: _reload)
              : RefreshIndicator(
                  onRefresh: _reload,
                  child: Column(
                    children: [
                      Padding(
                        padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
                        child: SegmentedButton<String>(
                          segments: const [
                            ButtonSegment(value: 'current', label: Text('Current')),
                            ButtonSegment(value: 'next', label: Text('Next')),
                          ],
                          selected: {_view},
                          onSelectionChanged: (s) => _switchView(s.first),
                        ),
                      ),
                      _SprintHeader(board: board, onStart: _startSprint),
                      AnimatedSwitcher(
                        duration: const Duration(milliseconds: 150),
                        child: _dragging
                            ? _DropBar(
                                key: const ValueKey('drop-bar'),
                                columns: board.visibleColumns,
                                onDrop: (task, column) => _move(task, column),
                              )
                            : _ColumnTabs(
                                key: const ValueKey('tabs'),
                                board: board,
                                selected: _page,
                                onSelect: (i) => _pages.animateToPage(i,
                                    duration: const Duration(milliseconds: 250), curve: Curves.easeOut),
                              ),
                      ),
                      Expanded(
                        child: PageView(
                          controller: _pages,
                          onPageChanged: (i) => setState(() => _page = i),
                          children: [
                            for (final column in board.visibleColumns)
                              _ColumnPage(
                                key: Key('column-$column'),
                                column: column,
                                tasks: board.columns[column]!,
                                overWip: column == BoardColumns.inProgress &&
                                    board.columns[column]!.length > board.wipLimit,
                                wipLimit: board.wipLimit,
                                onOpen: (task) => _openTask(task: task),
                                onDragChanged: (dragging) => setState(() => _dragging = dragging),
                                onDropBefore: (task, before) {
                                  final others = board.columns[column]!.where((t) => t.id != task.id).toList();
                                  final index = before == null
                                      ? others.length
                                      : others.indexWhere((t) => t.id == before.id);
                                  _move(task, column, index: index);
                                },
                              ),
                          ],
                        ),
                      ),
                    ],
                  ),
                ),
    );
  }
}

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

String _when(DateTime utc) {
  const weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
  const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  final t = utc.toLocal();
  final hh = t.hour.toString().padLeft(2, '0');
  final mm = t.minute.toString().padLeft(2, '0');
  return '${weekdays[t.weekday - 1]} ${t.day} ${months[t.month - 1]} $hh:$mm';
}

class _SprintHeader extends StatelessWidget {
  const _SprintHeader({required this.board, required this.onStart});

  final BoardView board;
  final VoidCallback onStart;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final sprint = board.sprint;
    final stat = theme.textTheme.bodySmall;
    const warn = Color(0xFFB26A00);

    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 10, 16, 4),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Text('Sprint ${sprint.number}', style: theme.textTheme.titleMedium),
              const SizedBox(width: 8),
              Text(sprint.isActive ? 'running' : 'planning',
                  style: stat?.copyWith(color: sprint.isActive ? const Color(0xFF3D8B4F) : warn)),
              const Spacer(),
              if (board.canStartSprint)
                FilledButton.tonal(onPressed: onStart, child: const Text('Start sprint')),
            ],
          ),
          Text('${_when(sprint.startsAtUtc)} – ${_when(sprint.endsAtUtc)}', style: stat),
          const SizedBox(height: 4),
          Wrap(
            spacing: 12,
            children: [
              Text(
                sprint.committedPoints == null
                    ? '${sprint.totalPoints} planned'
                    : '${sprint.committedPoints} committed',
                style: stat,
              ),
              if (sprint.isActive) Text('${sprint.completedPoints} done', style: stat),
              if (sprint.addedPoints > 0) Text('+${sprint.addedPoints} added', style: stat?.copyWith(color: warn)),
              if (sprint.removedPoints > 0)
                Text('−${sprint.removedPoints} removed', style: stat?.copyWith(color: warn)),
              if (sprint.unestimatedCount > 0) Text('${sprint.unestimatedCount} unestimated', style: stat),
              if (board.velocity != null) Text('velocity ${board.velocity}', style: stat),
            ],
          ),
          if (board.inPlanningWindow && board.view == 'current')
            _Banner(
              text: 'Planning time. Sprint ${sprint.number} starts at 20:00 by itself — '
                  "pick this week's work, then start it or let it start.",
            )
          else if (board.view == 'next')
            const _Banner(text: 'Planning ahead for next week. Adding here is not a scope change.', quiet: true),
        ],
      ),
    );
  }
}

class _Banner extends StatelessWidget {
  const _Banner({required this.text, this.quiet = false});

  final String text;
  final bool quiet;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Container(
      width: double.infinity,
      margin: const EdgeInsets.only(top: 8),
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
      decoration: BoxDecoration(
        color: quiet ? scheme.surfaceContainerHighest : const Color(0x1FB26A00),
        border: Border(left: BorderSide(color: quiet ? scheme.outline : const Color(0xFFB26A00), width: 3)),
      ),
      child: Text(text, style: Theme.of(context).textTheme.bodySmall),
    );
  }
}

class _ColumnTabs extends StatelessWidget {
  const _ColumnTabs({super.key, required this.board, required this.selected, required this.onSelect});

  final BoardView board;
  final int selected;
  final ValueChanged<int> onSelect;

  @override
  Widget build(BuildContext context) {
    final columns = board.visibleColumns;
    return SizedBox(
      height: 48,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
        itemCount: columns.length,
        separatorBuilder: (_, _) => const SizedBox(width: 6),
        itemBuilder: (context, i) => ChoiceChip(
          label: Text('${BoardColumns.label(columns[i])} ${board.columns[columns[i]]!.length}'),
          selected: i == selected,
          onSelected: (_) => onSelect(i),
        ),
      ),
    );
  }
}

/// Drop targets for every column, shown while a card is held.
class _DropBar extends StatelessWidget {
  const _DropBar({super.key, required this.columns, required this.onDrop});

  final List<String> columns;
  final void Function(BoardTask task, String column) onDrop;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return SizedBox(
      height: 48,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
        child: Row(
          children: [
            for (final column in columns)
              Expanded(
                child: DragTarget<BoardTask>(
                  key: Key('drop-$column'),
                  onWillAcceptWithDetails: (details) => details.data.column != column,
                  onAcceptWithDetails: (details) => onDrop(details.data, column),
                  builder: (context, candidates, _) => Container(
                    margin: const EdgeInsets.symmetric(horizontal: 3),
                    alignment: Alignment.center,
                    decoration: BoxDecoration(
                      color: candidates.isNotEmpty ? scheme.primaryContainer : scheme.surfaceContainerHighest,
                      border: Border.all(color: candidates.isNotEmpty ? scheme.primary : scheme.outlineVariant),
                      borderRadius: BorderRadius.circular(10),
                    ),
                    child: Text(
                      BoardColumns.label(column),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: Theme.of(context).textTheme.labelSmall,
                    ),
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }
}

class _ColumnPage extends StatelessWidget {
  const _ColumnPage({
    super.key,
    required this.column,
    required this.tasks,
    required this.overWip,
    required this.wipLimit,
    required this.onOpen,
    required this.onDragChanged,
    required this.onDropBefore,
  });

  final String column;
  final List<BoardTask> tasks;
  final bool overWip;
  final int wipLimit;
  final ValueChanged<BoardTask> onOpen;
  final ValueChanged<bool> onDragChanged;

  /// A card dropped on this page: before [before], or at the end when it is null.
  final void Function(BoardTask task, BoardTask? before) onDropBefore;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final points = tasks.fold<int>(0, (sum, t) => sum + (t.points ?? 0));

    return DragTarget<BoardTask>(
      onAcceptWithDetails: (details) => onDropBefore(details.data, null),
      builder: (context, candidates, _) => Container(
        margin: const EdgeInsets.fromLTRB(4, 4, 4, 88),
        padding: const EdgeInsets.all(8),
        decoration: BoxDecoration(
          color: scheme.surfaceContainerLow,
          borderRadius: BorderRadius.circular(14),
          border: Border.all(color: candidates.isNotEmpty ? scheme.primary : Colors.transparent, width: 2),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(4, 2, 4, 6),
              child: Row(
                children: [
                  Text(BoardColumns.label(column), style: Theme.of(context).textTheme.titleSmall),
                  const Spacer(),
                  Text('${tasks.length} · $points pts', style: Theme.of(context).textTheme.bodySmall),
                ],
              ),
            ),
            if (overWip)
              Padding(
                padding: const EdgeInsets.fromLTRB(4, 0, 4, 6),
                child: Text('More than $wipLimit in progress. Finishing beats starting.',
                    style: Theme.of(context).textTheme.bodySmall?.copyWith(color: const Color(0xFFB26A00))),
              ),
            Expanded(
              child: tasks.isEmpty
                  ? ListView(children: [
                      Padding(
                        padding: const EdgeInsets.all(24),
                        child: Center(
                          child: Text('Nothing here.', style: Theme.of(context).textTheme.bodySmall),
                        ),
                      ),
                    ])
                  : ListView.builder(
                      itemCount: tasks.length,
                      itemBuilder: (context, i) {
                        final task = tasks[i];
                        return DragTarget<BoardTask>(
                          // Accepts its own card too, so dropping a card back where it was is a no-op
                          // here rather than falling through to the column and moving it to the end.
                          onAcceptWithDetails: (details) {
                            if (details.data.id != task.id) onDropBefore(details.data, task);
                          },
                          builder: (context, candidates, _) => Column(
                            children: [
                              if (candidates.any((c) => c?.id != task.id))
                                Container(height: 3, color: scheme.primary, margin: const EdgeInsets.only(bottom: 4)),
                              LongPressDraggable<BoardTask>(
                                data: task,
                                onDragStarted: () => onDragChanged(true),
                                onDragEnd: (_) => onDragChanged(false),
                                feedback: Material(
                                  elevation: 6,
                                  borderRadius: BorderRadius.circular(12),
                                  child: SizedBox(width: 280, child: TaskCard(task: task)),
                                ),
                                childWhenDragging: Opacity(opacity: 0.35, child: TaskCard(task: task)),
                                child: TaskCard(task: task, onTap: () => onOpen(task)),
                              ),
                            ],
                          ),
                        );
                      },
                    ),
            ),
          ],
        ),
      ),
    );
  }
}

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
              if (task.goalKey != null || task.carryOverCount > 0 || task.addedMidSprint) ...[
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

/// Story points, or "?" while unestimated.
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
        none ? '?' : '$points',
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

/// Create a task, or edit, move and delete an existing one.
class TaskSheet extends StatefulWidget {
  const TaskSheet({
    super.key,
    required this.api,
    required this.goals,
    this.task,
    this.destination = 'backlog',
    this.moveTargets = const [],
    this.sprintView = 'current',
    this.fixedGoalId,
  });

  final PersonaOsApi api;
  final List<Goal> goals;
  final BoardTask? task;

  /// Where a new task goes: backlog, current or next.
  final String destination;
  final List<String> moveTargets;
  final String sprintView;

  /// For a task added from a goal: the goal is set and not offered as a choice.
  final int? fixedGoalId;

  @override
  State<TaskSheet> createState() => _TaskSheetState();
}

class _TaskSheetState extends State<TaskSheet> {
  late final _title = TextEditingController(text: widget.task?.title ?? '');
  late int? _points = widget.task?.points;
  late int? _goalId = widget.fixedGoalId ?? widget.task?.goalId;
  late String _destination = widget.destination;
  bool _busy = false;
  String? _error;

  bool get _editing => widget.task != null;

  @override
  void dispose() {
    _title.dispose();
    super.dispose();
  }

  Future<void> _guard(Future<bool> Function() action) async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final done = await action();
      if (mounted) {
        if (done) {
          Navigator.pop(context, true);
        } else {
          setState(() => _busy = false);
        }
      }
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
    await _guard(() async {
      if (_editing) {
        await widget.api.updateTask(widget.task!.id, title: title, points: _points, goalId: _goalId);
        return true;
      }
      return runWithScopeConfirmation(
        context,
        (ack) => widget.api.createTask(
          title: title,
          points: _points,
          goalId: _goalId,
          destination: _destination,
          acknowledgeScopeChange: ack,
        ),
      );
    });
  }

  Future<void> _moveTo(String column) => _guard(() => runWithScopeConfirmation(
        context,
        (ack) => widget.api.moveTask(
          widget.task!.id,
          column: column,
          sprint: column == BoardColumns.backlog ? null : widget.sprintView,
          acknowledgeScopeChange: ack,
        ),
      ));

  Future<void> _delete() async {
    final task = widget.task!;
    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text('Delete ${task.key}?'),
        content: Text('“${task.title}” will be deleted, and its number reused for the next task.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Delete')),
        ],
      ),
    );
    if (ok == true) {
      await _guard(() async {
        await widget.api.deleteTask(task.id);
        return true;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: EdgeInsets.fromLTRB(16, 0, 16, MediaQuery.of(context).viewInsets.bottom + 16),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(_editing ? widget.task!.key : 'New task', style: theme.textTheme.titleLarge),
            const SizedBox(height: 12),
            TextField(
              controller: _title,
              autofocus: !_editing,
              textCapitalization: TextCapitalization.sentences,
              decoration: InputDecoration(
                labelText: 'Task',
                border: const OutlineInputBorder(),
                errorText: _error,
              ),
            ),
            const SizedBox(height: 12),
            Text('Story points', style: theme.textTheme.labelLarge),
            const SizedBox(height: 6),
            Wrap(
              spacing: 6,
              runSpacing: 6,
              children: [
                ChoiceChip(label: const Text('?'), selected: _points == null, onSelected: (_) => setState(() => _points = null)),
                for (final p in storyPoints)
                  ChoiceChip(label: Text('$p'), selected: _points == p, onSelected: (_) => setState(() => _points = p)),
              ],
            ),
            if ((_points ?? 0) >= 13)
              Padding(
                padding: const EdgeInsets.only(top: 6),
                child: Text("That's big — consider splitting it into smaller tasks.",
                    style: theme.textTheme.bodySmall?.copyWith(color: const Color(0xFFB26A00))),
              ),
            if (widget.fixedGoalId == null && widget.goals.isNotEmpty) ...[
              const SizedBox(height: 12),
              DropdownButtonFormField<int?>(
                initialValue: _goalId,
                isExpanded: true,
                decoration: const InputDecoration(labelText: 'Goal', border: OutlineInputBorder()),
                items: [
                  const DropdownMenuItem<int?>(value: null, child: Text('No goal')),
                  for (final g in widget.goals)
                    DropdownMenuItem<int?>(
                      value: g.id,
                      child: Text('${g.key} ${g.title}', overflow: TextOverflow.ellipsis),
                    ),
                ],
                onChanged: (v) => setState(() => _goalId = v),
              ),
            ],
            if (!_editing && widget.fixedGoalId == null) ...[
              const SizedBox(height: 12),
              SegmentedButton<String>(
                segments: [
                  const ButtonSegment(value: 'backlog', label: Text('Backlog')),
                  ButtonSegment(
                    value: widget.sprintView,
                    label: Text(widget.sprintView == 'next' ? 'Next week' : 'This week'),
                  ),
                ],
                selected: {_destination},
                onSelectionChanged: (s) => setState(() => _destination = s.first),
              ),
            ],
            if (_editing && widget.moveTargets.isNotEmpty) ...[
              const SizedBox(height: 16),
              Text('Move to', style: theme.textTheme.labelLarge),
              const SizedBox(height: 6),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  for (final column in widget.moveTargets)
                    OutlinedButton(
                      onPressed: _busy ? null : () => _moveTo(column),
                      child: Text(BoardColumns.label(column)),
                    ),
                ],
              ),
            ],
            if (_editing && widget.task!.carryOverCount > 0) ...[
              const SizedBox(height: 8),
              Text('Carried over ${widget.task!.carryOverCount} '
                  '${widget.task!.carryOverCount == 1 ? 'time' : 'times'}.',
                  style: theme.textTheme.bodySmall),
            ],
            const SizedBox(height: 16),
            Row(
              children: [
                if (_editing)
                  TextButton(
                    onPressed: _busy ? null : _delete,
                    child: Text('Delete', style: TextStyle(color: theme.colorScheme.error)),
                  ),
                const Spacer(),
                FilledButton(
                  onPressed: _busy ? null : _save,
                  child: Text(_busy ? 'Saving…' : (_editing ? 'Save' : 'Add task')),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

/// Past sprints: committed against completed, no charts.
class SprintReportScreen extends StatelessWidget {
  const SprintReportScreen({super.key, required this.api});

  final PersonaOsApi api;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Sprint report')),
      body: FutureBuilder<SprintReport>(
        future: api.getSprintReport(),
        builder: (context, snapshot) {
          if (snapshot.connectionState != ConnectionState.done) {
            return const Center(child: CircularProgressIndicator());
          }
          if (snapshot.hasError) {
            return const _Message(text: 'Could not load the sprint report.');
          }
          final report = snapshot.data!;
          final theme = Theme.of(context);
          return ListView(
            padding: const EdgeInsets.all(12),
            children: [
              if (report.velocity != null)
                Padding(
                  padding: const EdgeInsets.fromLTRB(4, 0, 4, 8),
                  child: Text('Velocity ${report.velocity} — average completed points of the last three sprints.',
                      style: theme.textTheme.bodySmall),
                ),
              if (report.sprints.isEmpty) const _Message(text: 'No sprints yet.'),
              for (final s in report.sprints)
                Card(
                  child: ListTile(
                    title: Text('Sprint ${s.number}${s.isActive ? ' (running)' : ''}'),
                    subtitle: Text(
                      [
                        '${_when(s.startsAtUtc)} – ${_when(s.endsAtUtc)}',
                        'committed ${s.committedPoints ?? '—'} · added ${s.addedPoints} · removed ${s.removedPoints}',
                        if (s.carriedOverPoints != null) 'carried over ${s.carriedOverPoints}',
                      ].join('\n'),
                    ),
                    isThreeLine: true,
                    trailing: Column(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        Text('${s.completedPoints}', style: theme.textTheme.titleLarge),
                        Text('done', style: theme.textTheme.labelSmall),
                      ],
                    ),
                  ),
                ),
            ],
          );
        },
      ),
    );
  }
}

class _Message extends StatelessWidget {
  const _Message({required this.text, this.onRetry});

  final String text;
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(text, textAlign: TextAlign.center),
            if (onRetry != null) ...[
              const SizedBox(height: 12),
              OutlinedButton(onPressed: onRetry, child: const Text('Try again')),
            ],
          ],
        ),
      ),
    );
  }
}
