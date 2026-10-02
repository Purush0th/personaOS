import 'package:flutter/material.dart';

import '../api/personaos_api.dart';
import 'board_widgets.dart';

/// The sprint actions the Backlog tab, the Sprint tab and a sprint's page share: create or edit,
/// start, complete and delete. Each asks first where the web does, says why when the server
/// refuses, and returns whether anything changed.

Future<bool> _attempt(BuildContext context, Future<void> Function() action) async {
  try {
    await action();
    return true;
  } on ApiException catch (e) {
    if (context.mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    return false;
  }
}

Future<bool> _ask(BuildContext context, String title, String message, String action, {bool destructive = false}) async =>
    await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(title),
        content: Text(message),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
          FilledButton(
            style: destructive ? FilledButton.styleFrom(backgroundColor: Theme.of(context).colorScheme.error) : null,
            onPressed: () => Navigator.pop(context, true),
            child: Text(action),
          ),
        ],
      ),
    ) ==
    true;

/// Creates a sprint, or renames (and, while planned, moves) [sprint].
Future<bool> editSprintFlow(BuildContext context, PersonaOsApi api, [SprintInfo? sprint]) async {
  final result = await showDialog<_SprintDraft>(context: context, builder: (_) => _SprintDialog(sprint: sprint));
  if (result == null || !context.mounted) return false;
  return _attempt(
    context,
    () => sprint == null
        ? api.createSprint(name: result.name, startsOn: result.startsOn)
        : api.updateSprint(sprint.key, name: result.name, startsOn: result.startsOn),
  );
}

Future<bool> startSprintFlow(BuildContext context, PersonaOsApi api, SprintInfo sprint) async {
  final ok = await _ask(
    context,
    'Start ${sprintLabel(sprint)}?',
    '${sprint.totalPoints} points are committed when it starts. Adding work afterwards counts as a scope change.',
    'Start sprint',
  );
  return ok && context.mounted && await _attempt(context, () => api.startSprint(sprint.key));
}

/// Completes the running [sprint]; unfinished work goes to [next] (the next planned sprint) or,
/// by choice or when there is none, to the backlog.
Future<bool> completeSprintFlow(BuildContext context, PersonaOsApi api, SprintInfo sprint, {SprintInfo? next}) async {
  final open = sprint.taskCount - sprint.doneTaskCount;
  final toBacklog = await showDialog<bool>(
    context: context,
    builder: (context) => AlertDialog(
      title: Text('Complete ${sprintLabel(sprint)}?'),
      content: Text(open == 0
          ? 'Everything in it is done.'
          : '$open unfinished ${open == 1 ? 'task' : 'tasks'} will move on. Where to?'),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
        if (open > 0 && next != null)
          TextButton(onPressed: () => Navigator.pop(context, true), child: const Text('To the backlog')),
        FilledButton(
          onPressed: () => Navigator.pop(context, open > 0 && next == null),
          child: Text(open == 0
              ? 'Complete sprint'
              : next != null
                  ? 'To ${next.key}'
                  : 'To the backlog'),
        ),
      ],
    ),
  );
  if (toBacklog == null || !context.mounted) return false;
  return _attempt(context, () => api.completeSprint(sprint.key, toBacklog: toBacklog));
}

Future<bool> deleteSprintFlow(BuildContext context, PersonaOsApi api, SprintInfo sprint) async {
  final ok = await _ask(
    context,
    'Delete ${sprintLabel(sprint)}?',
    'Its tasks go back to the backlog. This cannot be undone.',
    'Delete',
    destructive: true,
  );
  return ok && context.mounted && await _attempt(context, () => api.deleteSprint(sprint.key));
}

/// What the sprint dialog returns: a name (blank clears it) and a start day ("2026-10-04").
class _SprintDraft {
  const _SprintDraft(this.name, this.startsOn);

  final String? name;
  final String? startsOn;
}

/// Creates a sprint, or renames and moves a planned one. The server works out the end: the first
/// Sunday after the start, the same week rhythm as the web.
class _SprintDialog extends StatefulWidget {
  const _SprintDialog({this.sprint});

  final SprintInfo? sprint;

  @override
  State<_SprintDialog> createState() => _SprintDialogState();
}

class _SprintDialogState extends State<_SprintDialog> {
  late final _name = TextEditingController(text: widget.sprint?.name ?? '');
  late DateTime? _startsOn = widget.sprint?.startsAtUtc.toLocal();

  /// The day it starts now: only a different day is sent, so saving a name keeps the dates.
  late final String? _originalDay = _startsOn == null ? null : _day(_startsOn!);

  /// A running sprint keeps its dates; only its name changes.
  bool get _canMove => widget.sprint == null || widget.sprint!.isPlanned;

  @override
  void dispose() {
    _name.dispose();
    super.dispose();
  }

  String _day(DateTime d) =>
      '${d.year}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')}';

  @override
  Widget build(BuildContext context) {
    final startsOn = _startsOn;
    return AlertDialog(
      title: Text(widget.sprint == null ? 'New sprint' : 'Edit ${widget.sprint!.key}'),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          TextField(
            controller: _name,
            autofocus: true,
            decoration: const InputDecoration(labelText: 'Name (optional)', hintText: 'e.g. Paperwork week'),
          ),
          if (_canMove) ...[
            const SizedBox(height: 12),
            OutlinedButton.icon(
              icon: const Icon(Icons.event, size: 18),
              label: Text(startsOn == null ? 'Starts: next free week' : 'Starts ${_day(startsOn)}'),
              onPressed: () async {
                final now = DateTime.now();
                final picked = await showDatePicker(
                  context: context,
                  initialDate: startsOn ?? now,
                  firstDate: DateTime(now.year, now.month, now.day),
                  lastDate: DateTime(now.year + 2),
                );
                if (picked != null) setState(() => _startsOn = picked);
              },
            ),
            Padding(
              padding: const EdgeInsets.only(top: 4),
              child: Text('It ends the first Sunday after it starts.', style: Theme.of(context).textTheme.bodySmall),
            ),
          ],
        ],
      ),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
        FilledButton(
          onPressed: () => Navigator.pop(
            context,
            _SprintDraft(
              _name.text.trim().isEmpty && widget.sprint?.name == null ? null : _name.text.trim(),
              _canMove && startsOn != null && _day(startsOn) != _originalDay ? _day(startsOn) : null,
            ),
          ),
          child: Text(widget.sprint == null ? 'Create' : 'Save'),
        ),
      ],
    );
  }
}
