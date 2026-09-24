import 'package:flutter/material.dart';

import '../api/personaos_api.dart';
import '../date_utils.dart';
import 'board_screen.dart';

/// Daily planner day-view: navigate days, add tasks (typed, or picked from the running sprint),
/// cycle status, move a task to the next day, delete.
class PlannerScreen extends StatefulWidget {
  const PlannerScreen({super.key, required this.api, this.boardEnabled = false});

  final PersonaOsApi api;

  /// Whether the day's work can be picked from the sprint board.
  final bool boardEnabled;

  @override
  State<PlannerScreen> createState() => _PlannerScreenState();
}

class _PlannerScreenState extends State<PlannerScreen> {
  DateTime _day = DateTime.now();
  late Future<List<PlannerItem>> _items;
  final _title = TextEditingController();
  TimeOfDay? _time;

  @override
  void initState() {
    super.initState();
    _items = widget.api.getPlannerDay(localYmd(_day));
  }

  @override
  void dispose() {
    _title.dispose();
    super.dispose();
  }

  void _reload() {
    setState(() => _items = widget.api.getPlannerDay(localYmd(_day)));
  }

  void _shiftDay(int days) {
    setState(() {
      _day = _day.add(Duration(days: days));
      _items = widget.api.getPlannerDay(localYmd(_day));
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

  Future<void> _add() async {
    final title = _title.text.trim();
    if (title.isEmpty) return;
    final time = _time;
    final scheduled = time == null
        ? null
        : '${time.hour.toString().padLeft(2, '0')}:'
            '${time.minute.toString().padLeft(2, '0')}';
    await _run(() async {
      await widget.api
          .addPlannerItem(title: title, date: localYmd(_day), scheduledTime: scheduled);
      _title.clear();
      setState(() => _time = null);
    });
  }

  /// Plans a board task for this day: the item takes the task's title and goal.
  Future<void> _pickFromBoard() async {
    final List<BoardTask> tasks;
    try {
      final board = await widget.api.getBoard();
      tasks = [...board.columns[BoardColumns.inProgress]!, ...board.columns[BoardColumns.todo]!];
    } on ApiException catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
      return;
    }
    if (!mounted) return;

    final picked = await showModalBottomSheet<BoardTask>(
      context: context,
      showDragHandle: true,
      builder: (context) => tasks.isEmpty
          ? const Padding(
              padding: EdgeInsets.all(32),
              child: Text('Nothing left in the sprint.', textAlign: TextAlign.center),
            )
          : ListView(
              shrinkWrap: true,
              children: [
                const Padding(
                  padding: EdgeInsets.fromLTRB(16, 0, 16, 8),
                  child: Text('From the sprint', style: TextStyle(fontWeight: FontWeight.w600)),
                ),
                for (final task in tasks)
                  ListTile(
                    title: Text(task.title),
                    subtitle: Text([
                      task.key,
                      if (task.column == BoardColumns.inProgress) 'in progress',
                      ?task.goalTitle,
                    ].join(' · ')),
                    trailing: PointsPill(points: task.points),
                    onTap: () => Navigator.pop(context, task),
                  ),
              ],
            ),
    );
    if (picked == null) return;

    final time = _time;
    final scheduled = time == null
        ? null
        : '${time.hour.toString().padLeft(2, '0')}:${time.minute.toString().padLeft(2, '0')}';
    await _run(() async {
      await widget.api.addPlannerItem(title: '', date: localYmd(_day), taskId: picked.id, scheduledTime: scheduled);
      setState(() => _time = null);
    });
  }

  /// planned → done → skipped → planned
  String _nextStatus(String status) => switch (status) {
        'planned' => 'done',
        'done' => 'skipped',
        _ => 'planned',
      };

  String get _dayLabel {
    const months = [
      'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun',
      'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec',
    ];
    const weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
    return '${weekdays[_day.weekday - 1]} ${_day.day} ${months[_day.month - 1]}';
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Planner'),
        actions: [
          IconButton(
            icon: const Icon(Icons.chevron_left),
            onPressed: () => _shiftDay(-1),
          ),
          Center(child: Text(_dayLabel)),
          IconButton(
            icon: const Icon(Icons.chevron_right),
            onPressed: () => _shiftDay(1),
          ),
        ],
      ),
      body: Column(
        children: [
          _addBar(),
          const Divider(height: 1),
          Expanded(
            child: RefreshIndicator(
              onRefresh: () async => _reload(),
              child: FutureBuilder<List<PlannerItem>>(
                future: _items,
                builder: (context, snapshot) {
                  if (snapshot.connectionState != ConnectionState.done) {
                    return const Center(child: CircularProgressIndicator());
                  }
                  if (snapshot.hasError) {
                    return _messageList('Could not load that day.',
                        color: Theme.of(context).colorScheme.error);
                  }
                  final items = snapshot.data!;
                  if (items.isEmpty) {
                    return _messageList('Nothing planned for this day.');
                  }
                  return ListView.builder(
                    padding: const EdgeInsets.symmetric(vertical: 8),
                    itemCount: items.length,
                    itemBuilder: (context, i) => _PlannerTile(
                      item: items[i],
                      onToggle: () => _run(() => widget.api
                          .setPlannerStatus(items[i].id, _nextStatus(items[i].status))),
                      onMove: () => _run(() => widget.api.movePlannerItem(
                          items[i].id, localYmd(_day.add(const Duration(days: 1))))),
                      onDelete: () =>
                          _run(() => widget.api.deletePlannerItem(items[i].id)),
                    ),
                  );
                },
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _addBar() {
    return Padding(
      padding: const EdgeInsets.fromLTRB(12, 8, 12, 8),
      child: Row(
        children: [
          Expanded(
            child: TextField(
              controller: _title,
              textInputAction: TextInputAction.done,
              decoration: const InputDecoration(
                hintText: 'What needs doing?',
                border: OutlineInputBorder(),
                isDense: true,
              ),
              onSubmitted: (_) => _add(),
            ),
          ),
          IconButton(
            tooltip: _time == null ? 'Add a time' : _time!.format(context),
            icon: Icon(_time == null ? Icons.schedule : Icons.schedule_outlined,
                color: _time == null ? null : Theme.of(context).colorScheme.primary),
            onPressed: () async {
              final picked = await showTimePicker(
                context: context,
                initialTime: TimeOfDay.now(),
              );
              if (picked != null) setState(() => _time = picked);
            },
          ),
          if (widget.boardEnabled)
            IconButton(
              tooltip: 'From the board',
              icon: const Icon(Icons.view_kanban_outlined),
              onPressed: _pickFromBoard,
            ),
          IconButton.filled(onPressed: _add, icon: const Icon(Icons.add)),
        ],
      ),
    );
  }

  Widget _messageList(String message, {Color? color}) {
    return ListView(
      children: [
        Padding(
          padding: const EdgeInsets.all(32),
          child: Center(
            child: Text(message,
                style: TextStyle(color: color ?? Colors.grey.shade600)),
          ),
        ),
      ],
    );
  }
}

class _PlannerTile extends StatelessWidget {
  const _PlannerTile({
    required this.item,
    required this.onToggle,
    required this.onMove,
    required this.onDelete,
  });

  final PlannerItem item;
  final VoidCallback onToggle;
  final VoidCallback onMove;
  final VoidCallback onDelete;

  @override
  Widget build(BuildContext context) {
    final done = item.status == 'done';
    final skipped = item.status == 'skipped';
    final icon = switch (item.status) {
      'done' => Icons.check_circle,
      'skipped' => Icons.remove_circle_outline,
      _ => Icons.radio_button_unchecked,
    };
    final time = item.scheduledTime?.substring(0, 5); // HH:mm

    return Opacity(
      opacity: skipped ? 0.6 : 1,
      child: ListTile(
        leading: IconButton(
          icon: Icon(icon),
          tooltip: 'Status: ${item.status} — tap to change',
          onPressed: onToggle,
        ),
        title: Text(
          item.title,
          style: TextStyle(
            decoration: done ? TextDecoration.lineThrough : null,
            color: done ? Colors.grey : null,
          ),
        ),
        subtitle: (time != null || item.taskKey != null || item.goalTitle != null)
            ? Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Wrap(
                  spacing: 8,
                  runSpacing: 4,
                  crossAxisAlignment: WrapCrossAlignment.center,
                  children: [
                    if (time != null) Text(time),
                    if (item.taskKey != null) Text(item.taskKey!),
                    if (item.goalTitle != null) GoalChip(goalKey: item.goalKey ?? '', title: item.goalTitle!),
                  ],
                ),
              )
            : null,
        trailing: PopupMenuButton<String>(
          onSelected: (choice) => choice == 'move' ? onMove() : onDelete(),
          itemBuilder: (context) => const [
            PopupMenuItem(value: 'move', child: Text('Move to tomorrow')),
            PopupMenuItem(value: 'delete', child: Text('Delete')),
          ],
        ),
      ),
    );
  }
}
