import 'package:flutter/material.dart';

import '../api/personaos_api.dart';
import '../date_utils.dart';
import '../goal_calendar.dart';

// The forms and dialogs a goal is created and changed with, shared by the goals list and a goal's
// own page: the new-goal sheet, Move, Delete, Update progress, and the menu helpers.

/// "quarter" or "month": what can still be added under this goal, or null.
String? childTypeToAdd(Goal goal) {
  final type = childTypeOf(goal.periodType);
  if (type == null || goal.status != 'active') return null;
  return goal.childCount < (type == 'quarter' ? 4 : 3) ? type : null;
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

/// Sets a goal's hand-kept progress with a slider in 5% steps, the number shown above it. The
/// web opens the same dialog from Update progress.
class ProgressDialog extends StatefulWidget {
  const ProgressDialog({super.key, required this.initial});

  final int initial;

  @override
  State<ProgressDialog> createState() => _ProgressDialogState();
}

class _ProgressDialogState extends State<ProgressDialog> {
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

/// A menu item that cannot be used yet, with why under its label.
PopupMenuItem<String> blockedMenuItem(BuildContext context, String label, String reason) {
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
