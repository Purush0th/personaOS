import 'package:flutter/material.dart';

import '../api/personaos_api.dart';
import '../layout.dart';
import 'board_widgets.dart';
import 'reports_view.dart';
import 'sprint_actions.dart';
import 'task_views.dart';

/// One sprint on its own page, like the web's /board/sprints/SPRINT-2: its dates and numbers,
/// its burndown once it has started, every task in it by status, and what can be done with it
/// now (edit, start, complete). A task opens its quick view.
class SprintPage extends StatefulWidget {
  const SprintPage({super.key, required this.api, required this.sprintKey});

  final PersonaOsApi api;
  final String sprintKey;

  @override
  State<SprintPage> createState() => _SprintPageState();
}

class _SprintPageState extends State<SprintPage> {
  late Future<(SprintDetail, PlanView?)> _data = _load();

  /// Whether anything changed here, so the screen it was opened from reloads.
  bool _changed = false;

  Future<(SprintDetail, PlanView?)> _load() async {
    final detail = await widget.api.getSprintDetail(widget.sprintKey);
    PlanView? plan;
    try {
      plan = await widget.api.getPlan();
    } catch (_) {
      // Without the plan the page still shows; only "start" cannot tell whether one is running.
    }
    return (detail, plan);
  }

  void _reload() {
    _changed = true;
    setState(() {
      _data = _load();
    });
  }

  Future<void> _act(Future<bool> Function() flow) async {
    if (await flow()) _reload();
  }

  @override
  Widget build(BuildContext context) {
    return PopScope<Object?>(
      canPop: false,
      onPopInvokedWithResult: (didPop, _) {
        if (!didPop) Navigator.of(context).pop(_changed);
      },
      child: Scaffold(
        appBar: AppBar(title: Text(widget.sprintKey)),
        body: FutureBuilder<(SprintDetail, PlanView?)>(
          future: _data,
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) {
              return const Center(child: CircularProgressIndicator());
            }
            if (snapshot.hasError) return const Center(child: Text('Could not load that sprint.'));
            final (detail, plan) = snapshot.data!;
            final sprint = detail.sprint;
            final anyRunning = plan?.sprints.any((p) => p.sprint.isActive) ?? true;
            final next = plan?.sprints.map((p) => p.sprint).where((s) => s.isPlanned).firstOrNull;
            final theme = Theme.of(context);

            return ReadableWidth(
              maxWidth: 960,
              child: ListView(
                padding: const EdgeInsets.fromLTRB(16, 12, 16, 32),
                children: [
                  Text(sprintLabel(sprint), style: theme.textTheme.titleLarge),
                  // A started sprint's dates head its summary below; a planned one has none.
                  Text(
                    [
                      sprint.status == 'active' ? 'running' : sprint.status,
                      if (sprint.isPlanned) '${sprintWhen(sprint.startsAtUtc)} – ${sprintWhen(sprint.endsAtUtc)}',
                    ].join(' · '),
                    style: theme.textTheme.bodySmall,
                  ),
                  const SizedBox(height: 12),
                  Wrap(
                    spacing: 8,
                    runSpacing: 8,
                    children: [
                      if (!sprint.isClosed)
                        OutlinedButton.icon(
                          onPressed: () => _act(() => editSprintFlow(context, widget.api, sprint)),
                          icon: const Icon(Icons.edit_outlined, size: 18),
                          label: const Text('Edit'),
                        ),
                      if (sprint.isPlanned && !anyRunning)
                        FilledButton(
                          onPressed: () => _act(() => startSprintFlow(context, widget.api, sprint)),
                          child: const Text('Start sprint'),
                        ),
                      if (sprint.isActive)
                        FilledButton(
                          onPressed: () => _act(() => completeSprintFlow(context, widget.api, sprint, next: next)),
                          child: const Text('Complete sprint'),
                        ),
                    ],
                  ),
                  if (sprint.isPlanned && anyRunning)
                    Padding(
                      padding: const EdgeInsets.only(top: 6),
                      child: Text('It can start once the running sprint is complete.', style: theme.textTheme.bodySmall),
                    ),
                  const SizedBox(height: 16),
                  // A planned sprint has no burndown and nothing committed yet; its tasks are listed.
                  if (!sprint.isPlanned)
                    SprintSummary(detail: detail, onOpenTask: _openTask)
                  else
                    for (final column in [BoardColumns.todo, BoardColumns.inProgress, BoardColumns.done])
                      TaskGroup(
                        title: BoardColumns.label(column),
                        tasks: detail.tasks.where((t) => t.column == column).toList(),
                        onOpen: _openTask,
                      ),
                ],
              ),
            );
          },
        ),
      ),
    );
  }

  Future<void> _openTask(BoardTask task) async {
    if (await showTaskQuickView(context, widget.api, task.key)) _reload();
  }
}
