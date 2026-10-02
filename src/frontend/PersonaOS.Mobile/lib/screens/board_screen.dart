import 'dart:async';

import 'package:flutter/material.dart';

import '../api/personaos_api.dart';
import '../layout.dart';
import 'backlog_view.dart';
import 'board_widgets.dart';
import 'reports_view.dart';
import 'sprint_actions.dart';
import 'sprint_page.dart';
import 'task_views.dart';

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
    unawaited(_reload());
    unawaited(_loadContext());
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

  /// An existing task opens its quick view; without one, the new-task sheet.
  Future<void> _openTask({BoardTask? task}) async {
    final changed = task != null
        ? await showTaskQuickView(context, widget.api, task.key, task: task, goals: _goals)
        : await showModalBottomSheet<bool>(
              context: context,
              isScrollControlled: true,
              showDragHandle: true,
              builder: (_) => TaskSheet(
                api: widget.api,
                goals: _goals,
                sprints: _plan?.sprints.map((s) => s.sprint).toList() ?? const [],
                defaultSprintKey: _board?.sprint?.key,
              ),
            ) ==
            true;
    if (changed) await _reload();
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
              onPressed: _openTask,
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
                                        onDrop: _move,
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
          unawaited(_move(task, column, index: index));
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
