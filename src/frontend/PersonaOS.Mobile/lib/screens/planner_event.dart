import 'dart:async';

import 'package:flutter/material.dart';

import '../api/personaos_api.dart';
import '../date_utils.dart';
import '../layout.dart';
import '../planner_calendar.dart';
import 'board_widgets.dart';
import 'goal_view.dart';
import 'task_views.dart';

/// Asks which events of a repeating series a change or delete is for, as Google Calendar does:
/// This event, This and following events, All events. Null when cancelled. With [seriesOnly]
/// (the change touches the repeat, the notification or a link) "This event" is not offered.
Future<EditScope?> askScope(BuildContext context, {required bool delete, bool seriesOnly = false}) {
  var scope = seriesOnly ? EditScope.following : EditScope.thisEvent;
  return showDialog<EditScope>(
    context: context,
    builder: (context) => StatefulBuilder(
      builder: (context, setState) => AlertDialog(
        title: Text(delete ? 'Delete recurring event' : 'Edit recurring event'),
        content: RadioGroup<EditScope>(
          groupValue: scope,
          onChanged: (value) => setState(() => scope = value ?? scope),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              if (!seriesOnly)
                const RadioListTile(key: Key('scope-this'), value: EditScope.thisEvent, title: Text('This event')),
              const RadioListTile(
                key: Key('scope-following'),
                value: EditScope.following,
                title: Text('This and following events'),
              ),
              const RadioListTile(key: Key('scope-all'), value: EditScope.all, title: Text('All events')),
              if (seriesOnly)
                Padding(
                  padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
                  child: Text(
                    'The repeat and the notification apply to the whole series, so they cannot change for one event alone.',
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                ),
            ],
          ),
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
          FilledButton(
            key: const Key('scope-ok'),
            onPressed: () => Navigator.pop(context, scope),
            child: const Text('OK'),
          ),
        ],
      ),
    ),
  );
}

/// The quick look at an event, like Google Calendar's: when, how it repeats, the notification,
/// notes and links, with edit, delete, done and skip. True when something changed.
Future<bool> showEventQuickView(BuildContext context, PersonaOsApi api, PlannerItem item) async {
  final changed = await showQuickView<bool>(
    context,
    builder: (_) => _EventQuickView(api: api, item: item),
  );
  return changed ?? false;
}

class _EventQuickView extends StatefulWidget {
  const _EventQuickView({required this.api, required this.item});

  final PersonaOsApi api;
  final PlannerItem item;

  @override
  State<_EventQuickView> createState() => _EventQuickViewState();
}

class _EventQuickViewState extends State<_EventQuickView> {
  String? _error;

  PlannerItem get item => widget.item;

  Future<void> _act(Future<void> Function() action) async {
    try {
      await action();
      if (mounted) Navigator.pop(context, true);
    } on ApiException catch (e) {
      setState(() => _error = e.message);
    }
  }

  Future<void> _edit() async {
    final saved = await Navigator.of(context).push<bool>(
      MaterialPageRoute(
        builder: (_) => EventEditorPage(api: widget.api, item: item),
      ),
    );
    if (saved == true && mounted) Navigator.pop(context, true);
  }

  Future<void> _delete() async {
    if (item.repeats) {
      final scope = await askScope(context, delete: true);
      if (scope == null) return;
      await _act(() => widget.api.deletePlannerItem(item.id, scope: scope, occurrence: item.occurrenceDate));
      return;
    }
    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Delete event?'),
        content: Text('“${item.title}” will be deleted.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
          FilledButton(
            key: const Key('confirm-delete'),
            style: FilledButton.styleFrom(backgroundColor: Theme.of(context).colorScheme.error),
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Delete'),
          ),
        ],
      ),
    );
    if (ok == true) await _act(() => widget.api.deletePlannerItem(item.id));
  }

  Future<void> _status(String status) =>
      _act(() => widget.api.setPlannerStatus(item.id, status, occurrence: item.repeats ? item.occurrenceDate : null));

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final muted = theme.colorScheme.onSurfaceVariant;
    final planned = item.status == 'planned';

    Widget line(IconData icon, Widget child) => Padding(
      padding: const EdgeInsets.only(top: 14),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, size: 20, color: muted),
          const SizedBox(width: 16),
          Expanded(child: child),
        ],
      ),
    );

    return SingleChildScrollView(
      key: Key('event-quick-view-${item.id}-${item.occurrenceDate}'),
      padding: const EdgeInsets.fromLTRB(20, 0, 12, 16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.end,
            children: [
              IconButton(
                key: const Key('event-edit'),
                tooltip: 'Edit event',
                icon: const Icon(Icons.edit_outlined),
                onPressed: _edit,
              ),
              IconButton(
                key: const Key('event-delete'),
                tooltip: 'Delete event',
                icon: const Icon(Icons.delete_outline),
                onPressed: _delete,
              ),
            ],
          ),
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Padding(
                padding: const EdgeInsets.only(top: 6),
                child: Container(
                  width: 14,
                  height: 14,
                  decoration: BoxDecoration(color: eventColor(context, item), borderRadius: BorderRadius.circular(4)),
                ),
              ),
              const SizedBox(width: 16),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      item.title,
                      style: theme.textTheme.titleLarge?.copyWith(
                        decoration: planned ? null : TextDecoration.lineThrough,
                        color: planned ? null : muted,
                      ),
                    ),
                    const SizedBox(height: 4),
                    Text(describeWhen(item)),
                    if (item.recurrenceText != null) Text(item.recurrenceText!, style: TextStyle(color: muted)),
                  ],
                ),
              ),
            ],
          ),
          line(Icons.notifications_outlined, Text(notifyLabel(item.notifyBeforeMinutes))),
          if (item.notes != null && item.notes!.isNotEmpty) line(Icons.notes, Text(item.notes!)),
          if (item.taskKey != null || item.goalTitle != null)
            line(
              Icons.flag_outlined,
              Wrap(
                spacing: 8,
                runSpacing: 4,
                crossAxisAlignment: WrapCrossAlignment.center,
                children: [
                  if (item.taskKey != null)
                    InkWell(
                      key: Key('planner-task-${item.taskKey}'),
                      onTap: () => showTaskQuickView(context, widget.api, item.taskKey!),
                      child: Text(
                        item.taskKey!,
                        style: TextStyle(color: theme.colorScheme.primary, decoration: TextDecoration.underline),
                      ),
                    ),
                  if (item.goalTitle != null)
                    InkWell(
                      onTap: item.goalKey == null ? null : () => showGoalQuickView(context, widget.api, item.goalKey!),
                      child: GoalChip(goalKey: item.goalKey ?? '', title: item.goalTitle!),
                    ),
                ],
              ),
            ),
          if (!planned)
            line(
              item.status == 'done' ? Icons.check_circle_outline : Icons.remove_circle_outline,
              Text(item.status == 'done' ? 'Done' : 'Skipped', style: TextStyle(color: muted)),
            ),
          if (_error != null)
            Padding(
              padding: const EdgeInsets.only(top: 12),
              child: Text(_error!, style: TextStyle(color: theme.colorScheme.error)),
            ),
          const SizedBox(height: 16),
          Row(
            mainAxisAlignment: MainAxisAlignment.end,
            children: [
              if (planned) ...[
                TextButton(
                  key: const Key('event-skip'),
                  onPressed: () => _status('skipped'),
                  child: const Text('Skip'),
                ),
                const SizedBox(width: 8),
                FilledButton(
                  key: const Key('event-done'),
                  onPressed: () => _status('done'),
                  child: const Text('Mark done'),
                ),
              ] else
                TextButton(
                  key: const Key('event-undo'),
                  onPressed: () => _status('planned'),
                  child: const Text('Mark not done'),
                ),
            ],
          ),
        ],
      ),
    );
  }
}

/// An event's colour: its goal's hue, or the theme's primary container.
Color eventColor(BuildContext context, PlannerItem item) {
  final scheme = Theme.of(context).colorScheme;
  final key = item.goalKey;
  if (key == null) return scheme.primaryContainer;
  final dark = Theme.of(context).brightness == Brightness.dark;
  return HSLColor.fromAHSL(1, GoalChip.hueFor(key), 0.55, dark ? 0.35 : 0.8).toColor();
}

Color eventTextColor(BuildContext context, PlannerItem item) {
  final scheme = Theme.of(context).colorScheme;
  return item.goalKey == null ? scheme.onPrimaryContainer : scheme.onSurface;
}

/// Creates or edits a planner event, full screen as on Google Calendar's phone app: title, all
/// day or start and end (on a later day for an event over several days), repeat (presets and custom), notification (presets and a custom number
/// with its unit), notes, a board task (new events) and a goal. Pops true once saved.
class EventEditorPage extends StatefulWidget {
  const EventEditorPage({super.key, required this.api, this.item, this.date, this.start, this.boardEnabled = false});

  final PersonaOsApi api;

  /// The event to edit; null for a new one.
  final PlannerItem? item;

  /// For a new event: the day, and the start when made from a time slot (null: 9 am).
  final DateTime? date;
  final String? start;
  final bool boardEnabled;

  @override
  State<EventEditorPage> createState() => _EventEditorPageState();
}

class _EventEditorPageState extends State<EventEditorPage> {
  final _title = TextEditingController();
  final _notes = TextEditingController();
  late DateTime _date;

  /// The last day: [_date] unless the event lasts several days.
  late DateTime _endDate;
  late bool _allDay;
  late String _start; // HH:mm
  late String _end;

  /// Null: does not repeat.
  String? _rule;
  bool _custom = false;
  late CustomRepeat _customRepeat;

  int? _notify;
  int? _goalId;
  int? _taskId;
  List<Goal> _goals = [];
  List<BoardTask> _tasks = [];
  bool _saving = false;
  String? _error;

  bool get _editing => widget.item != null;

  @override
  void initState() {
    super.initState();
    final item = widget.item;
    _title.text = item?.title ?? '';
    _notes.text = item?.notes ?? '';
    _date = item != null ? parseDay(item.date) : dayOnly(widget.date ?? DateTime.now());
    _endDate = item != null ? parseDay(item.lastDay) : _date;
    _allDay = item != null ? item.isAllDay : false;
    _start = (item?.scheduledTime ?? widget.start ?? '09:00').substring(0, 5);
    _end = item?.endTime?.substring(0, 5) ?? timeOf(minutesOf(_start) + 60);
    _notify = item != null ? item.notifyBeforeMinutes : defaultNotifyMinutes;
    _goalId = item?.goalId;
    _customRepeat = CustomRepeat.forDay(_date);
    final rule = item?.recurrence;
    if (rule != null) {
      if (repeatOptions(_date).any((o) => o.rule == rule)) {
        _rule = rule;
      } else {
        _custom = true;
        _customRepeat = CustomRepeat.parse(rule, _date);
      }
    }
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final goals = await widget.api.getGoals();
      if (mounted) setState(() => _goals = goals.where((g) => g.status == 'active' || g.id == _goalId).toList());
    } catch (_) {
      // Goals are optional here.
    }
    if (!_editing && widget.boardEnabled) {
      try {
        final board = await widget.api.getBoard();
        if (mounted) {
          setState(() => _tasks = [...board.columns[BoardColumns.inProgress]!, ...board.columns[BoardColumns.todo]!]);
        }
      } catch (_) {
        // The board is optional here.
      }
    }
  }

  @override
  void dispose() {
    _title.dispose();
    _notes.dispose();
    super.dispose();
  }

  String? get _recurrence => _custom ? _customRepeat.rule(_date) : _rule;

  bool get _multiDay => _endDate.isAfter(_date);

  String? get _timeError =>
      !_allDay && !_multiDay && minutesOf(_end) <= minutesOf(_start) ? 'The end must be after the start.' : null;

  bool get _notifyTooFar => (_notify ?? 0) > maxNotifyMinutes;

  bool get _canSave => _title.text.trim().isNotEmpty && _timeError == null && !_notifyTooFar && !_saving;

  String _repeatLabel() {
    if (_custom) return 'Custom: ${_customRepeat.rule(_date)}';
    return repeatOptions(
      _date,
    ).firstWhere((o) => o.rule == _rule, orElse: () => const RepeatOption('Does not repeat', null)).label;
  }

  Future<void> _pickDate() async {
    final picked = await showDatePicker(
      context: context,
      initialDate: _date,
      firstDate: DateTime(2000),
      lastDate: DateTime(2100),
    );
    if (picked == null) return;
    setState(() {
      // "Weekly on Saturday" follows the day: keep the same kind of choice for the new day.
      final index = repeatOptions(_date).indexWhere((o) => o.rule == _rule && !o.custom);
      // The last day moves with the first, so the event keeps its length.
      _endDate = addDays(dayOnly(picked), dayDiff(_date, _endDate));
      _date = dayOnly(picked);
      if (_rule != null && index > 0) {
        final options = repeatOptions(_date).where((o) => !o.custom).toList();
        _rule = options[index.clamp(0, options.length - 1)].rule;
      }
    });
  }

  Future<void> _pickEndDate() async {
    final picked = await showDatePicker(
      context: context,
      initialDate: _endDate,
      firstDate: _date,
      lastDate: DateTime(2100),
    );
    if (picked != null) setState(() => _endDate = dayOnly(picked));
  }

  /// "Wed, 30 Sep 2026", short enough to share a row with a time on a small phone.
  static String _dayLabel(DateTime d) =>
      '${weekdayName(d).substring(0, 3)}, ${d.day} ${monthNames[d.month - 1].substring(0, 3)} ${d.year}';

  Future<void> _pickTime({required bool start}) async {
    final current = minutesOf(start ? _start : _end);
    final picked = await showTimePicker(
      context: context,
      initialTime: TimeOfDay(hour: current ~/ 60, minute: current % 60),
    );
    if (picked == null) return;
    final value = timeOf(picked.hour * 60 + picked.minute);
    setState(() {
      if (start) {
        // Moving the start keeps the length, as Google Calendar does (over several days the end
        // is on another day and stays).
        final length = minutesOf(_end) - minutesOf(_start);
        _start = value;
        if (!_multiDay) _end = timeOf(minutesOf(value) + (length > 0 ? length : 60));
      } else {
        _end = value;
      }
    });
  }

  Future<void> _pickRepeat() async {
    final options = repeatOptions(_date);
    final picked = await showModalBottomSheet<RepeatOption>(
      context: context,
      showDragHandle: true,
      // Taller than the default half screen, so "Custom…" is not below the fold on a small phone.
      isScrollControlled: true,
      builder: (context) => SafeArea(
        child: ListView(
          shrinkWrap: true,
          children: [
            for (final option in options)
              ListTile(
                title: Text(option.label),
                trailing: (option.custom ? _custom : !_custom && option.rule == _rule) ? const Icon(Icons.check) : null,
                onTap: () => Navigator.pop(context, option),
              ),
          ],
        ),
      ),
    );
    if (picked == null || !mounted) return;
    if (!picked.custom) {
      setState(() {
        _custom = false;
        _rule = picked.rule;
      });
      return;
    }
    final custom = await showDialog<CustomRepeat>(
      context: context,
      builder: (_) => _CustomRepeatDialog(day: _date, initial: _custom ? _customRepeat : CustomRepeat.forDay(_date)),
    );
    if (custom != null) {
      setState(() {
        _custom = true;
        _customRepeat = custom;
      });
    }
  }

  Future<void> _pickNotify() async {
    const customChoice = -2;
    final picked = await showModalBottomSheet<int>(
      context: context,
      showDragHandle: true,
      isScrollControlled: true,
      builder: (context) => SafeArea(
        child: ListView(
          shrinkWrap: true,
          children: [
            for (final preset in notifyPresets)
              ListTile(
                title: Text(preset.label),
                trailing: preset.minutes == _notify ? const Icon(Icons.check) : null,
                onTap: () => Navigator.pop(context, preset.minutes ?? -1),
              ),
            ListTile(
              key: const Key('notify-custom'),
              title: const Text('Custom…'),
              trailing: notifyPresets.any((p) => p.minutes == _notify) ? null : const Icon(Icons.check),
              onTap: () => Navigator.pop(context, customChoice),
            ),
          ],
        ),
      ),
    );
    if (picked == null || !mounted) return;
    if (picked != customChoice) {
      setState(() => _notify = picked == -1 ? null : picked);
      return;
    }
    final custom = await showDialog<int>(
      context: context,
      builder: (_) => CustomNotifyDialog(day: _date, start: _allDay ? null : _start, initialMinutes: _notify ?? 60),
    );
    if (custom != null) setState(() => _notify = custom);
  }

  Future<void> _save() async {
    if (!_canSave) return;
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      final item = widget.item;
      if (item == null) {
        await widget.api.createEvent(
          title: _title.text.trim(),
          date: localYmd(_date),
          endDate: _multiDay ? localYmd(_endDate) : null,
          notes: _notes.text.trim().isEmpty ? null : _notes.text.trim(),
          scheduledTime: _allDay ? null : _start,
          endTime: _allDay ? null : _end,
          notifyBeforeMinutes: _notify,
          recurrence: _recurrence,
          goalId: _goalId,
          taskId: _taskId,
        );
        if (mounted) Navigator.pop(context, true);
        return;
      }
      final saved = await _saveChange(item);
      if (saved && mounted) Navigator.pop(context, true);
    } on ApiException catch (e) {
      setState(() => _error = e.message);
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  /// Sends only what changed, asking which events it is for when the event repeats. False when cancelled.
  Future<bool> _saveChange(PlannerItem item) async {
    final change = <String, Object?>{};
    final title = _title.text.trim();
    final notes = _notes.text.trim();
    if (title != item.title) change['title'] = title;
    if (notes != (item.notes ?? '')) change['notes'] = notes;
    if (localYmd(_date) != item.date) change['date'] = localYmd(_date);
    // The server keeps the length when only the first day moves; a different length is sent.
    if (dayDiff(_date, _endDate) != dayDiff(parseDay(item.date), parseDay(item.lastDay))) {
      change['endDate'] = localYmd(_endDate);
    }

    final oldStart = item.scheduledTime?.substring(0, 5);
    final oldEnd = item.endTime?.substring(0, 5);
    if (_allDay && oldStart != null) change['clearScheduledTime'] = true;
    if (!_allDay && _start != oldStart) change['scheduledTime'] = _start;
    if (!_allDay && _end != oldEnd) change['endTime'] = _end;

    final recurrence = _recurrence;
    final seriesChanged =
        _notify != item.notifyBeforeMinutes || recurrence != item.recurrence || _goalId != item.goalId;
    if (_notify != item.notifyBeforeMinutes) {
      if (_notify == null) {
        change['clearNotify'] = true;
      } else {
        change['notifyBeforeMinutes'] = _notify;
      }
    }
    if (recurrence != item.recurrence) {
      if (recurrence == null) {
        change['clearRecurrence'] = true;
      } else {
        change['recurrence'] = recurrence;
      }
    }
    if (_goalId != item.goalId) {
      if (_goalId == null) {
        change['clearGoal'] = true;
      } else {
        change['goalId'] = _goalId;
      }
    }
    if (change.isEmpty) return true;

    if (!item.repeats) {
      await widget.api.updateEvent(item.id, change);
      return true;
    }
    if (!mounted) return false;
    final scope = await askScope(context, delete: false, seriesOnly: seriesChanged);
    if (scope == null) return false;
    await widget.api.updateEvent(item.id, change, scope: scope, occurrence: item.occurrenceDate);
    return true;
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final muted = theme.colorScheme.onSurfaceVariant;
    final notifyHint = _notify == null
        ? null
        : _notifyTooFar
        ? 'Too far ahead: the most is 4 weeks before.'
        : 'You will be notified ${formatMoment(notifyAt(_date, _allDay ? null : _start, _notify!))}.';

    return Scaffold(
      appBar: AppBar(
        leading: IconButton(
          icon: const Icon(Icons.close),
          tooltip: 'Cancel',
          onPressed: () => Navigator.pop(context, false),
        ),
        title: Text(_editing ? 'Edit event' : 'New event'),
        actions: [
          Padding(
            padding: const EdgeInsets.only(right: 8),
            child: FilledButton(
              key: const Key('event-save'),
              onPressed: _canSave ? _save : null,
              child: Text(_saving ? 'Saving…' : 'Save'),
            ),
          ),
        ],
      ),
      body: ReadableWidth(
        maxWidth: 640,
        child: ListView(
          padding: const EdgeInsets.only(bottom: 32),
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(56, 8, 16, 8),
              child: TextField(
                key: const Key('event-title'),
                controller: _title,
                autofocus: !_editing,
                style: theme.textTheme.headlineSmall,
                decoration: const InputDecoration(hintText: 'Add title', border: InputBorder.none),
                onChanged: (_) => setState(() {}),
              ),
            ),
            const Divider(height: 1),
            SwitchListTile(
              key: const Key('event-all-day'),
              secondary: const Icon(Icons.schedule),
              title: const Text('All day'),
              value: _allDay,
              onChanged: (value) => setState(() {
                _allDay = value;
                // A new timed event gets the usual heads-up; a new all-day one starts without.
                if (!_editing) _notify = value ? null : defaultNotifyMinutes;
              }),
            ),
            // The start and the end, each a day and (unless all day) a time, as the phone app has them.
            ListTile(
              key: const Key('event-date'),
              leading: const SizedBox(width: 24),
              title: Text(_dayLabel(_date)),
              trailing: _allDay
                  ? null
                  : TextButton(
                      key: const Key('event-start'),
                      onPressed: () => _pickTime(start: true),
                      child: Text(formatTime(_start)),
                    ),
              onTap: _pickDate,
            ),
            ListTile(
              key: const Key('event-end-date'),
              leading: const SizedBox(width: 24),
              title: Text(_dayLabel(_endDate)),
              trailing: _allDay
                  ? null
                  : TextButton(
                      key: const Key('event-end'),
                      onPressed: () => _pickTime(start: false),
                      child: Text(formatTime(_end)),
                    ),
              subtitle: _timeError == null ? null : Text(_timeError!, style: TextStyle(color: theme.colorScheme.error)),
              onTap: _pickEndDate,
            ),
            const Divider(height: 1),
            ListTile(
              key: const Key('event-repeat'),
              leading: const Icon(Icons.repeat),
              title: Text(_custom ? describeCustom(_customRepeat, _date) : _repeatLabel()),
              onTap: _pickRepeat,
            ),
            ListTile(
              key: const Key('event-notify'),
              leading: const Icon(Icons.notifications_outlined),
              title: Text(notifyLabel(_notify)),
              subtitle: notifyHint == null
                  ? null
                  : Text(notifyHint, style: TextStyle(color: _notifyTooFar ? theme.colorScheme.error : muted)),
              onTap: _pickNotify,
            ),
            const Divider(height: 1),
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 8),
              child: TextField(
                controller: _notes,
                minLines: 1,
                maxLines: 5,
                decoration: const InputDecoration(
                  icon: Icon(Icons.notes),
                  hintText: 'Add notes',
                  border: InputBorder.none,
                ),
              ),
            ),
            if (_tasks.isNotEmpty)
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
                child: DropdownButtonFormField<int?>(
                  key: const Key('event-task'),
                  initialValue: _taskId,
                  isExpanded: true,
                  decoration: const InputDecoration(
                    labelText: 'From the board',
                    icon: Icon(Icons.view_kanban_outlined),
                  ),
                  items: [
                    const DropdownMenuItem(value: null, child: Text('No task')),
                    for (final task in _tasks)
                      DropdownMenuItem(
                        value: task.id,
                        child: Text('${task.key} ${task.title}', overflow: TextOverflow.ellipsis),
                      ),
                  ],
                  onChanged: (id) => setState(() {
                    _taskId = id;
                    final task = _tasks.where((t) => t.id == id).firstOrNull;
                    if (task == null) return;
                    // Planning a board task: the event takes its title (unless typed) and its goal.
                    if (_title.text.trim().isEmpty) _title.text = task.title;
                    if (task.goalId != null) _goalId = task.goalId;
                  }),
                ),
              ),
            if (_goals.isNotEmpty)
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
                child: DropdownButtonFormField<int?>(
                  key: ValueKey('event-goal-$_goalId'),
                  initialValue: _goalId,
                  isExpanded: true,
                  decoration: const InputDecoration(labelText: 'Goal', icon: Icon(Icons.flag_outlined)),
                  items: [
                    const DropdownMenuItem(value: null, child: Text('No goal')),
                    for (final goal in _goals)
                      DropdownMenuItem(
                        value: goal.id,
                        child: Text('${goal.key} ${goal.title}', overflow: TextOverflow.ellipsis),
                      ),
                  ],
                  onChanged: (id) => setState(() => _goalId = id),
                ),
              ),
            if (_error != null)
              Padding(
                padding: const EdgeInsets.all(16),
                child: Text(_error!, style: TextStyle(color: theme.colorScheme.error)),
              ),
          ],
        ),
      ),
    );
  }
}

/// A custom repeat in words: "Every 2 weeks on MO, SA, until 31 Dec 2026".
String describeCustom(CustomRepeat r, DateTime day) {
  final unit = switch (r.unit) {
    RepeatUnit.daily => 'day',
    RepeatUnit.weekly => 'week',
    RepeatUnit.monthly => 'month',
    RepeatUnit.yearly => 'year',
  };
  final every = r.interval == 1 ? 'Every $unit' : 'Every ${r.interval} ${unit}s';
  final on = switch (r.unit) {
    RepeatUnit.weekly =>
      ' on ${weekdayNames.where((n) => r.days.contains(n.substring(0, 2).toUpperCase())).join(', ')}',
    RepeatUnit.monthly => r.byWeekday ? ' on the same weekday' : ' on day ${day.day}',
    _ => '',
  };
  final end = switch (r.end) {
    RepeatEnd.never => '',
    RepeatEnd.on => ', until ${r.until.day} ${monthNames[r.until.month - 1].substring(0, 3)} ${r.until.year}',
    RepeatEnd.after => ', ${r.count} times',
  };
  return '$every$on$end';
}

class _CustomRepeatDialog extends StatefulWidget {
  const _CustomRepeatDialog({required this.day, required this.initial});

  final DateTime day;
  final CustomRepeat initial;

  @override
  State<_CustomRepeatDialog> createState() => _CustomRepeatDialogState();
}

class _CustomRepeatDialogState extends State<_CustomRepeatDialog> {
  late final CustomRepeat _r = CustomRepeat(
    interval: widget.initial.interval,
    unit: widget.initial.unit,
    days: {...widget.initial.days},
    byWeekday: widget.initial.byWeekday,
    end: widget.initial.end,
    until: widget.initial.until,
    count: widget.initial.count,
  );

  static const _codes = ['MO', 'TU', 'WE', 'TH', 'FR', 'SA', 'SU'];

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return AlertDialog(
      key: const Key('custom-repeat'),
      title: const Text('Custom repeat'),
      scrollable: true,
      content: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          Wrap(
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              const Text('Every '),
              _Stepper(value: _r.interval, min: 1, max: 999, onChanged: (v) => setState(() => _r.interval = v)),
              const SizedBox(width: 8),
              DropdownButton<RepeatUnit>(
                value: _r.unit,
                items: [
                  for (final unit in RepeatUnit.values)
                    DropdownMenuItem(
                      value: unit,
                      child: Text(
                        '${switch (unit) {
                          RepeatUnit.daily => 'day',
                          RepeatUnit.weekly => 'week',
                          RepeatUnit.monthly => 'month',
                          RepeatUnit.yearly => 'year',
                        }}${_r.interval == 1 ? '' : 's'}',
                      ),
                    ),
                ],
                onChanged: (unit) => setState(() => _r.unit = unit ?? _r.unit),
              ),
            ],
          ),
          if (_r.unit == RepeatUnit.weekly) ...[
            const SizedBox(height: 12),
            Wrap(
              spacing: 6,
              children: [
                for (final code in _codes)
                  ChoiceChip(
                    label: Text(code.substring(0, 1)),
                    selected: _r.days.contains(code),
                    showCheckmark: false,
                    selectedColor: scheme.primary,
                    labelStyle: TextStyle(color: _r.days.contains(code) ? scheme.onPrimary : null),
                    onSelected: (on) => setState(() {
                      if (on) {
                        _r.days.add(code);
                      } else if (_r.days.length > 1) {
                        _r.days.remove(code);
                      }
                    }),
                  ),
              ],
            ),
          ],
          if (_r.unit == RepeatUnit.monthly)
            RadioGroup<bool>(
              groupValue: _r.byWeekday,
              onChanged: (v) => setState(() => _r.byWeekday = v ?? false),
              child: const Column(
                children: [
                  RadioListTile(
                    value: false,
                    title: Text('On the same day of the month'),
                    contentPadding: EdgeInsets.zero,
                  ),
                  RadioListTile(
                    value: true,
                    title: Text('On the same weekday of the month'),
                    contentPadding: EdgeInsets.zero,
                  ),
                ],
              ),
            ),
          const SizedBox(height: 12),
          Text('Ends', style: Theme.of(context).textTheme.labelLarge),
          RadioGroup<RepeatEnd>(
            groupValue: _r.end,
            onChanged: (v) => setState(() => _r.end = v ?? _r.end),
            child: Column(
              children: [
                const RadioListTile(value: RepeatEnd.never, title: Text('Never'), contentPadding: EdgeInsets.zero),
                RadioListTile(
                  value: RepeatEnd.on,
                  contentPadding: EdgeInsets.zero,
                  title: Wrap(
                    crossAxisAlignment: WrapCrossAlignment.center,
                    children: [
                      const Text('On '),
                      TextButton(
                        onPressed: _r.end != RepeatEnd.on
                            ? null
                            : () async {
                                final picked = await showDatePicker(
                                  context: context,
                                  initialDate: _r.until,
                                  firstDate: widget.day,
                                  lastDate: DateTime(2100),
                                );
                                if (picked != null) setState(() => _r.until = dayOnly(picked));
                              },
                        child: Text(
                          '${_r.until.day} ${monthNames[_r.until.month - 1].substring(0, 3)} ${_r.until.year}',
                        ),
                      ),
                    ],
                  ),
                ),
                RadioListTile(
                  value: RepeatEnd.after,
                  contentPadding: EdgeInsets.zero,
                  title: Wrap(
                    crossAxisAlignment: WrapCrossAlignment.center,
                    children: [
                      const Text('After '),
                      _Stepper(
                        value: _r.count,
                        min: 1,
                        max: 999,
                        enabled: _r.end == RepeatEnd.after,
                        onChanged: (v) => setState(() => _r.count = v),
                      ),
                      const Text(' times'),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
        FilledButton(
          key: const Key('custom-repeat-done'),
          onPressed: () => Navigator.pop(context, _r),
          child: const Text('Done'),
        ),
      ],
    );
  }
}

/// The custom notification: a number that steps by one, its unit, and when that would be. A
/// lead past four weeks says when it would fire, that the most is four weeks, and cannot be saved.
class CustomNotifyDialog extends StatefulWidget {
  const CustomNotifyDialog({super.key, required this.day, required this.start, required this.initialMinutes});

  final DateTime day;

  /// Start "HH:mm", or null for an all-day event (counts from 9 am).
  final String? start;
  final int initialMinutes;

  @override
  State<CustomNotifyDialog> createState() => _CustomNotifyDialogState();
}

class _CustomNotifyDialogState extends State<CustomNotifyDialog> {
  late NotifyUnit _unit = largestUnit(widget.initialMinutes);
  late int _amount = widget.initialMinutes ~/ _unit.size;

  int get _minutes => _amount * _unit.size;
  bool get _tooFar => _minutes > maxNotifyMinutes;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final at = formatMoment(notifyAt(widget.day, widget.start, _minutes));
    final hint = _tooFar
        ? 'Too far ahead: ${leadText(_minutes)} before would be $at. The most is 4 weeks (40,320 minutes) before.'
        : '${widget.start == null ? 'All-day events count from 9:00 am. ' : ''}You will be notified $at.';
    return AlertDialog(
      key: const Key('custom-notify'),
      title: const Text('Custom notification'),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // Wraps on a narrow phone: the number and its unit stay together, "before" follows.
          Wrap(
            crossAxisAlignment: WrapCrossAlignment.center,
            spacing: 8,
            children: [
              _Stepper(
                value: _amount,
                min: 0,
                max: 99999,
                onChanged: (v) => setState(() => _amount = v),
                keyPrefix: 'notify',
              ),
              DropdownButton<NotifyUnit>(
                key: const Key('notify-unit'),
                value: _unit,
                items: [for (final unit in NotifyUnit.values) DropdownMenuItem(value: unit, child: Text(unit.name))],
                onChanged: (unit) => setState(() => _unit = unit ?? _unit),
              ),
              const Text('before'),
            ],
          ),
          const SizedBox(height: 12),
          Text(
            hint,
            key: const Key('notify-hint'),
            style: theme.textTheme.bodySmall?.copyWith(
              color: _tooFar ? theme.colorScheme.error : theme.colorScheme.onSurfaceVariant,
            ),
          ),
        ],
      ),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
        FilledButton(
          key: const Key('notify-done'),
          onPressed: _tooFar ? null : () => Navigator.pop(context, _minutes),
          child: const Text('Done'),
        ),
      ],
    );
  }
}

/// A whole number with − and + buttons that step by one, and a field to type it.
class _Stepper extends StatefulWidget {
  const _Stepper({
    required this.value,
    required this.min,
    required this.max,
    required this.onChanged,
    this.enabled = true,
    this.keyPrefix,
  });

  final int value;
  final int min;
  final int max;
  final bool enabled;
  final ValueChanged<int> onChanged;
  final String? keyPrefix;

  @override
  State<_Stepper> createState() => _StepperState();
}

class _StepperState extends State<_Stepper> {
  late final _controller = TextEditingController(text: '${widget.value}');

  @override
  void didUpdateWidget(_Stepper old) {
    super.didUpdateWidget(old);
    if ('${widget.value}' != _controller.text) _controller.text = '${widget.value}';
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _set(int value) => widget.onChanged(value.clamp(widget.min, widget.max));

  @override
  Widget build(BuildContext context) {
    final prefix = widget.keyPrefix;
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        IconButton(
          key: prefix == null ? null : Key('$prefix-minus'),
          tooltip: 'One less',
          icon: const Icon(Icons.remove),
          onPressed: widget.enabled && widget.value > widget.min ? () => _set(widget.value - 1) : null,
        ),
        SizedBox(
          width: 56,
          child: TextField(
            key: prefix == null ? null : Key('$prefix-amount'),
            controller: _controller,
            enabled: widget.enabled,
            keyboardType: TextInputType.number,
            textAlign: TextAlign.center,
            decoration: const InputDecoration(isDense: true),
            onChanged: (text) {
              final n = int.tryParse(text);
              if (n != null) _set(n);
            },
          ),
        ),
        IconButton(
          key: prefix == null ? null : Key('$prefix-plus'),
          tooltip: 'One more',
          icon: const Icon(Icons.add),
          onPressed: widget.enabled && widget.value < widget.max ? () => _set(widget.value + 1) : null,
        ),
      ],
    );
  }
}
