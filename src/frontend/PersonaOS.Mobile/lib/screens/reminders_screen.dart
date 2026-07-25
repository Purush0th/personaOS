import 'package:flutter/material.dart';

import '../api/personaos_api.dart';
import '../date_utils.dart';

/// Reminders list: pending by default, with create / cancel / delete.
class RemindersScreen extends StatefulWidget {
  const RemindersScreen({super.key, required this.api});

  final PersonaOsApi api;

  @override
  State<RemindersScreen> createState() => _RemindersScreenState();
}

class _RemindersScreenState extends State<RemindersScreen> {
  late Future<List<Reminder>> _reminders;
  bool _includeCompleted = false;

  @override
  void initState() {
    super.initState();
    _reminders = widget.api.getReminders(includeCompleted: _includeCompleted);
  }

  void _reload() {
    setState(() {
      _reminders = widget.api.getReminders(includeCompleted: _includeCompleted);
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
    final created = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (_) => _AddReminderSheet(api: widget.api),
    );
    if (created == true) _reload();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Reminders'),
        actions: [
          IconButton(
            tooltip: _includeCompleted ? 'Pending only' : 'Show all',
            icon: Icon(_includeCompleted ? Icons.filter_list_off : Icons.filter_list),
            onPressed: () {
              setState(() => _includeCompleted = !_includeCompleted);
              _reload();
            },
          ),
        ],
      ),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: _add,
        icon: const Icon(Icons.add_alarm),
        label: const Text('New reminder'),
      ),
      body: RefreshIndicator(
        onRefresh: () async => _reload(),
        child: FutureBuilder<List<Reminder>>(
          future: _reminders,
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) {
              return const Center(child: CircularProgressIndicator());
            }
            if (snapshot.hasError) {
              return _messageList('Could not load reminders.',
                  color: Theme.of(context).colorScheme.error);
            }
            final items = snapshot.data!;
            if (items.isEmpty) {
              return _messageList(
                  'Nothing pending. Ask your assistant — "remind me to call the '
                  'dentist tomorrow at 9".');
            }
            return ListView.builder(
              padding: const EdgeInsets.fromLTRB(0, 8, 0, 88),
              itemCount: items.length,
              itemBuilder: (context, i) => _ReminderTile(
                reminder: items[i],
                onCancel: () => _run(() => widget.api.cancelReminder(items[i].id)),
                onDelete: () => _run(() => widget.api.deleteReminder(items[i].id)),
              ),
            );
          },
        ),
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
                textAlign: TextAlign.center,
                style: TextStyle(color: color ?? Colors.grey.shade600)),
          ),
        ),
      ],
    );
  }
}

class _ReminderTile extends StatelessWidget {
  const _ReminderTile({
    required this.reminder,
    required this.onCancel,
    required this.onDelete,
  });

  final Reminder reminder;
  final VoidCallback onCancel;
  final VoidCallback onDelete;

  @override
  Widget build(BuildContext context) {
    final pending = reminder.status == 'pending';
    final subtitle = [
      _formatDue(reminder.dueAtLocal),
      if (!pending) reminder.status,
      if (reminder.goalTitle != null) 'toward ${reminder.goalTitle}',
      if (reminder.plannerItemTitle != null) 'for ${reminder.plannerItemTitle}',
    ].join(' · ');

    return Opacity(
      opacity: pending ? 1 : 0.6,
      child: ListTile(
        leading: Icon(pending ? Icons.alarm : Icons.alarm_off),
        title: Text(
          reminder.message,
          style: TextStyle(
            decoration: pending ? null : TextDecoration.lineThrough,
          ),
        ),
        subtitle: Text(subtitle),
        trailing: PopupMenuButton<String>(
          onSelected: (choice) => choice == 'cancel' ? onCancel() : onDelete(),
          itemBuilder: (context) => [
            if (pending)
              const PopupMenuItem(value: 'cancel', child: Text('Cancel')),
            const PopupMenuItem(value: 'delete', child: Text('Delete')),
          ],
        ),
      ),
    );
  }

  String _formatDue(DateTime dt) {
    const months = [
      'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun',
      'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec',
    ];
    const weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
    final hh = dt.hour.toString().padLeft(2, '0');
    final mm = dt.minute.toString().padLeft(2, '0');
    return '${weekdays[dt.weekday - 1]} ${dt.day} ${months[dt.month - 1]}, $hh:$mm';
  }
}

/// Bottom sheet to create a reminder: message + a date/time picker.
class _AddReminderSheet extends StatefulWidget {
  const _AddReminderSheet({required this.api});

  final PersonaOsApi api;

  @override
  State<_AddReminderSheet> createState() => _AddReminderSheetState();
}

class _AddReminderSheetState extends State<_AddReminderSheet> {
  final _message = TextEditingController();
  DateTime? _due;
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _message.dispose();
    super.dispose();
  }

  Future<void> _pickWhen() async {
    final now = DateTime.now();
    final date = await showDatePicker(
      context: context,
      initialDate: _due ?? now,
      firstDate: now.subtract(const Duration(days: 1)),
      lastDate: now.add(const Duration(days: 365 * 5)),
    );
    if (date == null || !mounted) return;
    final time = await showTimePicker(
      context: context,
      initialTime: TimeOfDay.fromDateTime(_due ?? now.add(const Duration(hours: 1))),
    );
    if (time == null) return;
    setState(() {
      _due = DateTime(date.year, date.month, date.day, time.hour, time.minute);
    });
  }

  Future<void> _submit() async {
    final message = _message.text.trim();
    if (message.isEmpty) {
      setState(() => _error = 'What should we remind you about?');
      return;
    }
    if (_due == null) {
      setState(() => _error = 'Pick when.');
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await widget.api.createReminder(message, localDateTime(_due!));
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
    final due = _due;
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
          const Text('New reminder',
              style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold)),
          const SizedBox(height: 12),
          TextField(
            controller: _message,
            autofocus: true,
            decoration: InputDecoration(
              labelText: 'Remind me to…',
              border: const OutlineInputBorder(),
              errorText: _error,
            ),
          ),
          const SizedBox(height: 12),
          OutlinedButton.icon(
            onPressed: _pickWhen,
            icon: const Icon(Icons.event),
            label: Text(
              due == null
                  ? 'Pick date & time'
                  : '${localYmd(due)} at '
                      '${due.hour.toString().padLeft(2, '0')}:'
                      '${due.minute.toString().padLeft(2, '0')}',
            ),
          ),
          const SizedBox(height: 16),
          FilledButton(
            onPressed: _busy ? null : _submit,
            child: Text(_busy ? 'Adding…' : 'Add reminder'),
          ),
        ],
      ),
    );
  }
}
