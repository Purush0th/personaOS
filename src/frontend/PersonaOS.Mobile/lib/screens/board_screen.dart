import 'package:flutter/material.dart';

import '../api/personaos_api.dart';
import '../date_utils.dart';
import '../layout.dart';
import 'attachments_section.dart';
import 'backlog_view.dart';
import 'comments_section.dart';
import 'reports_view.dart';
import 'sprint_actions.dart';
import 'sprint_page.dart';

/// The sprint board: To do, In progress and Done for the sprint that is running.
///
/// The backlog and the sprints to come live on the web app's Backlog page; the phone is for
/// working the current week. A phone cannot show three columns side by side, so columns are pages
/// to swipe between, and dragging a card (long press) raises a row of drop targets at the top, so
/// a card can reach any column while held. Every drag has a menu alternative in the task sheet.
class BoardScreen extends StatefulWidget {
  const BoardScreen({super.key, required this.api});

  final PersonaOsApi api;

  @override
  State<BoardScreen> createState() => _BoardScreenState();
}

class _BoardScreenState extends State<BoardScreen> with SingleTickerProviderStateMixin {
  final _pages = PageController(viewportFraction: 0.9);

  /// Sprint | Backlog | Reports, as on the web.
  late final _tabs = TabController(length: 3, vsync: this)..addListener(_tabChanged);

  /// Bumped whenever the board changes, so the Reports tab loads afresh.
  int _revision = 0;
  BoardView? _board;
  PlanView? _plan;
  List<Goal> _goals = const [];
  String? _error;
  bool _loading = true;
  bool _dragging = false;
  int _page = 0;

  @override
  void initState() {
    super.initState();
    _reload();
    _loadContext();
  }

  @override
  void dispose() {
    _tabs.dispose();
    _pages.dispose();
    super.dispose();
  }

  void _tabChanged() {
    if (!_tabs.indexIsChanging) setState(() {});
  }

  Future<void> _reload() async {
    try {
      final board = await widget.api.getBoard();
      final plan = await widget.api.getPlan();
      if (!mounted) return;
      setState(() {
        _board = board;
        _plan = plan;
        _revision++;
        _error = null;
        _loading = false;
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

  Future<void> _loadContext() async {
    try {
      final goals = await widget.api.getGoals();
      // Tasks sit only under open monthly goals.
      if (mounted) {
        setState(() => _goals = goals.where((g) => g.periodType == 'month' && g.status == 'active').toList());
      }
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
          task.key,
          column: column,
          sprintKey: column == BoardColumns.backlog ? null : _board?.sprint?.key,
          index: index,
          acknowledgeScopeChange: ack,
        ),
      );

  Future<void> _openTask({BoardTask? task}) async {
    final changed = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => TaskSheet(
        api: widget.api,
        goals: _goals,
        task: task,
        sprints: _plan?.sprints.map((s) => s.sprint).toList() ?? const [],
        defaultSprintKey: _board?.sprint?.key,
      ),
    );
    if (changed == true) await _reload();
  }

  Future<void> _openSprint(SprintInfo sprint) async {
    final changed = await Navigator.of(context)
        .push<bool>(MaterialPageRoute(builder: (_) => SprintPage(api: widget.api, sprintKey: sprint.key)));
    if (changed == true) await _reload();
  }

  Future<void> _completeSprint(SprintInfo sprint) async {
    final next = _plan?.sprints.map((p) => p.sprint).where((s) => s.isPlanned).firstOrNull;
    if (await completeSprintFlow(context, widget.api, sprint, next: next)) await _reload();
  }

  @override
  Widget build(BuildContext context) {
    final board = _board;
    final sprint = board?.sprint;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Board'),
        actions: [
          // The running sprint's own page and its completion, as on the web's Sprint tab.
          if (sprint != null && _tabs.index == 0)
            PopupMenuButton<String>(
              key: const Key('sprint-menu'),
              tooltip: 'Sprint actions',
              onSelected: (action) => action == 'open' ? _openSprint(sprint) : _completeSprint(sprint),
              itemBuilder: (_) => const [
                PopupMenuItem(value: 'open', child: Text('Open sprint')),
                PopupMenuItem(value: 'complete', child: Text('Complete sprint')),
              ],
            ),
        ],
        bottom: TabBar(
          controller: _tabs,
          tabs: const [Tab(text: 'Sprint'), Tab(text: 'Backlog'), Tab(text: 'Reports')],
        ),
      ),
      floatingActionButton: sprint == null || _tabs.index != 0
          ? null
          : FloatingActionButton.extended(
              onPressed: () => _openTask(),
              icon: const Icon(Icons.add),
              label: const Text('Task'),
            ),
      // Tabs change by tapping: the Sprint tab swipes between its columns.
      body: TabBarView(
        controller: _tabs,
        physics: const NeverScrollableScrollPhysics(),
        children: [
          _sprintTab(board, sprint),
          BacklogView(api: widget.api, goals: _goals, onChanged: _reload),
          ReportsView(key: ValueKey(_revision), api: widget.api),
        ],
      ),
    );
  }

  Widget _sprintTab(BoardView? board, SprintInfo? sprint) {
    return _loading
          ? const Center(child: CircularProgressIndicator())
          : board == null
              ? _Message(text: _error ?? 'Could not load the board.', onRetry: _reload)
              : sprint == null
                  ? _NoSprint(plan: _plan, onReload: _reload)
                  : RefreshIndicator(
                      onRefresh: _reload,
                      // Wide enough (a tablet, or a phone on its side): every column side by side,
                      // the way the web board shows them. Narrow: one column per page.
                      child: LayoutBuilder(
                        builder: (context, constraints) => Column(
                          children: [
                            _SprintHeader(sprint: sprint, velocity: board.velocity),
                            if (constraints.maxWidth >= Breakpoints.board)
                              Expanded(
                                child: _SideBySide(
                                  columns: board.visibleColumns,
                                  page: (column) => _columnPage(board, column),
                                ),
                              )
                            else ...[
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
                                  children: [for (final column in board.visibleColumns) _columnPage(board, column)],
                                ),
                              ),
                            ],
                          ],
                        ),
                      ),
                    );
  }

  Widget _columnPage(BoardView board, String column) => _ColumnPage(
        key: Key('column-$column'),
        column: column,
        tasks: board.columns[column]!,
        overWip: column == BoardColumns.inProgress && board.columns[column]!.length > board.wipLimit,
        wipLimit: board.wipLimit,
        onOpen: (task) => _openTask(task: task),
        onDragChanged: (dragging) => setState(() => _dragging = dragging),
        onDropBefore: (task, before) {
          final others = board.columns[column]!.where((t) => t.key != task.key).toList();
          final index = before == null ? others.length : others.indexWhere((t) => t.key == before.key);
          _move(task, column, index: index);
        },
      );
}

/// Every column at once, for a wide screen. Columns share the width, but never get narrower than
/// a card reads well; past that the row scrolls sideways instead of squeezing them.
class _SideBySide extends StatelessWidget {
  const _SideBySide({required this.columns, required this.page});

  final List<String> columns;
  final Widget Function(String column) page;

  static const _minColumnWidth = 160.0;

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(builder: (context, constraints) {
      const padding = 8.0;
      final fits = (constraints.maxWidth - padding * 2) / columns.length >= _minColumnWidth;
      if (fits) {
        return Padding(
          padding: const EdgeInsets.symmetric(horizontal: padding),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [for (final column in columns) Expanded(child: page(column))],
          ),
        );
      }
      return ListView(
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.symmetric(horizontal: padding),
        children: [for (final column in columns) SizedBox(width: _minColumnWidth, child: page(column))],
      );
    });
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

/// Shown when nothing is running: the phone does not plan sprints, it works them.
class _NoSprint extends StatelessWidget {
  const _NoSprint({required this.plan, required this.onReload});

  final PlanView? plan;
  final VoidCallback onReload;

  @override
  Widget build(BuildContext context) {
    final planned = plan?.sprints.where((s) => s.sprint.status == 'planned').toList() ?? const [];
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Text('No sprint is running.', style: TextStyle(fontWeight: FontWeight.w600)),
            const SizedBox(height: 8),
            Text(
              planned.isEmpty
                  ? 'Plan one in the Backlog tab, then start it there.'
                  : '${sprintLabel(planned.first.sprint)} is planned and waiting. '
                      'Start it from the Backlog tab.',
              textAlign: TextAlign.center,
              style: Theme.of(context).textTheme.bodySmall,
            ),
            const SizedBox(height: 16),
            OutlinedButton(onPressed: onReload, child: const Text('Refresh')),
          ],
        ),
      ),
    );
  }
}

class _SprintHeader extends StatelessWidget {
  const _SprintHeader({required this.sprint, required this.velocity});

  final SprintInfo sprint;
  final double? velocity;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final stat = theme.textTheme.bodySmall;
    const warn = Color(0xFFB26A00);

    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 10, 16, 4),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(child: Text(sprintLabel(sprint), style: theme.textTheme.titleMedium)),
              Text('running', style: stat?.copyWith(color: const Color(0xFF3D8B4F))),
            ],
          ),
          Text('${sprintWhen(sprint.startsAtUtc)} – ${sprintWhen(sprint.endsAtUtc)}', style: stat),
          const SizedBox(height: 4),
          Wrap(
            spacing: 12,
            children: [
              Text('${sprint.committedPoints ?? sprint.totalPoints} committed', style: stat),
              Text('${sprint.completedPoints} done', style: stat),
              if (sprint.addedPoints > 0)
                Text('+${sprint.addedPoints} added', style: stat?.copyWith(color: warn)),
              if (sprint.removedPoints > 0)
                Text('−${sprint.removedPoints} removed', style: stat?.copyWith(color: warn)),
              if (sprint.unestimatedCount > 0) Text('${sprint.unestimatedCount} unestimated', style: stat),
              if (velocity != null) Text('velocity $velocity', style: stat),
            ],
          ),
        ],
      ),
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
                  // Side by side on a tablet a column can be narrow: the name gives way first.
                  Expanded(
                    child: Text(BoardColumns.label(column),
                        maxLines: 1, overflow: TextOverflow.ellipsis, style: Theme.of(context).textTheme.titleSmall),
                  ),
                  const SizedBox(width: 8),
                  Text('${tasks.length} · $points pts', style: Theme.of(context).textTheme.bodySmall),
                ],
              ),
            ),
            if (overWip)
              Padding(
                padding: const EdgeInsets.fromLTRB(4, 0, 4, 6),
                child: Text('Over $wipLimit in progress. Finish some first.',
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
                            if (details.data.key != task.key) onDropBefore(details.data, task);
                          },
                          builder: (context, candidates, _) => Column(
                            children: [
                              if (candidates.any((c) => c?.key != task.key))
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

/// Create a task, or edit, move and delete an existing one: the quick view a card opens, and the
/// body of the task's full page ([TaskPage]). An existing task shows its description and comments.
class TaskSheet extends StatefulWidget {
  const TaskSheet({
    super.key,
    required this.api,
    required this.goals,
    this.task,
    this.sprints = const [],
    this.defaultSprintKey,
    this.fixedGoalId,
    this.fullPage = false,
  });

  /// Shown as the task's own page rather than over a list: no title row, no "Open full page".
  final bool fullPage;

  final PersonaOsApi api;
  final List<Goal> goals;
  final BoardTask? task;

  /// Sprints a task can be moved into: the running one and those planned after it.
  final List<SprintInfo> sprints;

  /// Where a new task goes by default; null puts it in the backlog.
  final String? defaultSprintKey;

  /// For a task added from a goal: the goal is set and not offered as a choice.
  final int? fixedGoalId;

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

  /// The task's own page, over this sheet; coming back closes the sheet so the list reloads.
  Future<void> _openPage() async {
    final task = widget.task!;
    await Navigator.of(context).push(MaterialPageRoute<void>(
      builder: (_) => TaskPage(api: widget.api, taskKey: task.key, goals: widget.goals, sprints: widget.sprints),
    ));
    if (mounted) Navigator.pop(context, true);
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
        final description = _description.text.trim();
        await widget.api.updateTask(
          widget.task!.key,
          title: title,
          points: _points,
          goalId: _goalId,
          // Only a change is sent: an untouched empty field must not "clear" nothing.
          description: description == (widget.task!.description ?? '').trim() ? null : description,
          priority: _priority == widget.task!.priority ? null : _priority,
        );
        // Moving between sprints is its own call, and may be a scope change.
        if (_sprintKey != widget.task!.sprintKey) {
          if (!mounted) return true;
          return runWithScopeConfirmation(
            context,
            (ack) => widget.api.moveTask(
              widget.task!.key,
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

  Future<void> _moveTo(String column) => _guard(() => runWithScopeConfirmation(
        context,
        (ack) => widget.api.moveTask(
          widget.task!.key,
          column: column,
          sprintKey: column == BoardColumns.backlog ? null : widget.task!.sprintKey,
          acknowledgeScopeChange: ack,
        ),
      ));

  Future<void> _delete() async {
    final task = widget.task!;
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
    if (ok == true) {
      await _guard(() async {
        await widget.api.deleteTask(task.key);
        return true;
      });
    }
  }

  List<String> get _moveTargets {
    final task = widget.task;
    if (task == null || task.sprintKey == null) return const [];
    return BoardColumns.board.where((c) => c != task.column).toList();
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
            if (!widget.fullPage)
              Row(
                children: [
                  Expanded(child: Text(_editing ? widget.task!.key : 'New task', style: theme.textTheme.titleLarge)),
                  if (_editing)
                    IconButton(
                      tooltip: 'Open full page',
                      icon: const Icon(Icons.open_in_full),
                      onPressed: _busy ? null : _openPage,
                    ),
                ],
              ),
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
            if (_editing) ...[
              const SizedBox(height: 12),
              TextField(
                key: const Key('task-description'),
                controller: _description,
                minLines: 2,
                maxLines: 8,
                textCapitalization: TextCapitalization.sentences,
                decoration: const InputDecoration(labelText: 'Description', border: OutlineInputBorder()),
              ),
            ],
            const SizedBox(height: 12),
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
                child: Text('Consider splitting it.',
                    style: theme.textTheme.bodySmall?.copyWith(color: const Color(0xFFB26A00))),
              ),
            const SizedBox(height: 12),
            DropdownButtonFormField<String>(
              key: const Key('task-priority'),
              initialValue: priorities.containsKey(_priority) ? _priority : 'medium',
              decoration: const InputDecoration(labelText: 'Priority', border: OutlineInputBorder()),
              items: [for (final p in priorities.entries) DropdownMenuItem(value: p.key, child: Text(p.value))],
              onChanged: (v) => setState(() => _priority = v ?? _priority),
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
                      child: Text('${g.key} ${g.title} · ${g.slot}', overflow: TextOverflow.ellipsis),
                    ),
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
                  // without the plan): a dropdown whose value is not among its items cannot build.
                  if (_sprintKey != null && !widget.sprints.any((s) => s.key == _sprintKey))
                    DropdownMenuItem<String?>(value: _sprintKey, child: Text(_sprintKey!)),
                ],
                onChanged: (v) => setState(() => _sprintKey = v),
              ),
            ],
            if (_editing && _moveTargets.isNotEmpty) ...[
              const SizedBox(height: 16),
              Text('Move to', style: theme.textTheme.labelLarge),
              const SizedBox(height: 6),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  for (final column in _moveTargets)
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
            if (_editing) ...[
              if (widget.task!.createdAtUtc != null)
                Padding(
                  padding: const EdgeInsets.only(top: 8),
                  child: Text(
                    [
                      'Created ${relativeTime(widget.task!.createdAtUtc!)}',
                      if (widget.task!.updatedAtUtc != null) 'updated ${relativeTime(widget.task!.updatedAtUtc!)}',
                    ].join(' · '),
                    style: theme.textTheme.bodySmall,
                  ),
                ),
              const Divider(height: 32),
              AttachmentsSection(api: widget.api, itemType: 'task', itemKey: widget.task!.key),
              const Divider(height: 32),
              CommentsSection(api: widget.api, itemType: 'task', itemKey: widget.task!.key),
            ],
          ],
        ),
      ),
    );
  }
}

/// A task on its own page: the same fields as the quick view, with room for its description and
/// comments. Read afresh, so it is current however it was reached.
class TaskPage extends StatefulWidget {
  const TaskPage({
    super.key,
    required this.api,
    required this.taskKey,
    this.goals = const [],
    this.sprints = const [],
  });

  final PersonaOsApi api;
  final String taskKey;
  final List<Goal> goals;
  final List<SprintInfo> sprints;

  @override
  State<TaskPage> createState() => _TaskPageState();
}

class _TaskPageState extends State<TaskPage> {
  late final Future<BoardTask> _task = widget.api.getTask(widget.taskKey);

  /// The sprints it can move to: those passed in, or the plan's when it was opened without them.
  late final Future<List<SprintInfo>> _sprints = widget.sprints.isNotEmpty
      ? Future.value(widget.sprints)
      : widget.api.getPlan().then((p) => p.sprints.map((s) => s.sprint).toList(), onError: (_) => <SprintInfo>[]);

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text(widget.taskKey)),
      body: FutureBuilder<(BoardTask, List<SprintInfo>)>(
        future: (_task, _sprints).wait,
        builder: (context, snapshot) {
          if (snapshot.connectionState != ConnectionState.done) {
            return const Center(child: CircularProgressIndicator());
          }
          if (snapshot.hasError) return const _Message(text: 'Could not load that task.');
          return ReadableWidth(
            child: Padding(
              padding: const EdgeInsets.only(top: 12),
              child: TaskSheet(
                api: widget.api,
                goals: widget.goals,
                task: snapshot.data!.$1,
                sprints: snapshot.data!.$2,
                fullPage: true,
              ),
            ),
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
