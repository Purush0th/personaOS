import 'dart:async';

import 'package:flutter/material.dart';

import 'api/personaos_api.dart';
import 'phosphor_icons.dart';

/// Where the day stands at one moment: the item under way, how far through it is, and the one
/// after it.
class DayPosition {
  const DayPosition({this.current, this.next, this.progress = 0, this.minutesLeft = 0, this.minutesToNext = 0});

  /// Works out where [items] (one day's) stand at [now]. Done and skipped items are behind you.
  factory DayPosition.at(List<PlannerItem> items, DateTime now) {
    final minute = now.hour * 60 + now.minute;
    final timed = items.where((i) => !i.isAllDay && !i.isMultiDay && i.status == 'planned').toList()
      ..sort((a, b) => _minutes(a.scheduledTime!).compareTo(_minutes(b.scheduledTime!)));
    PlannerItem? current;
    for (final item in timed) {
      final start = _minutes(item.scheduledTime!);
      if (start <= minute && _end(item) > minute) current = item;
    }
    final next = timed.where((i) => _minutes(i.scheduledTime!) > minute).firstOrNull;
    if (current == null) {
      return DayPosition(next: next, minutesToNext: next == null ? 0 : _minutes(next.scheduledTime!) - minute);
    }
    final start = _minutes(current.scheduledTime!);
    final length = _end(current) - start;
    return DayPosition(
      current: current,
      next: next,
      progress: length <= 0 ? 0 : ((minute - start) + now.second / 60) / length,
      minutesLeft: _end(current) - minute,
      minutesToNext: next == null ? 0 : _minutes(next.scheduledTime!) - minute,
    );
  }

  final PlannerItem? current;
  final PlannerItem? next;

  /// How far through [current] the day is, 0 to 1.
  final double progress;
  final int minutesLeft;
  final int minutesToNext;

  bool get isEmpty => current == null && next == null;

  static int _minutes(String hhmm) {
    final parts = hhmm.split(':');
    return int.parse(parts[0]) * 60 + int.parse(parts[1]);
  }

  /// An item's end in minutes; one without an end lasts until the next would start, at most an hour.
  static int _end(PlannerItem item) =>
      item.endTime == null ? _minutes(item.scheduledTime!) + 60 : _minutes(item.endTime!);
}

/// "18 min", "1 h 5 min", "2 h".
String spanWords(int minutes) {
  if (minutes < 60) return '${minutes < 1 ? 1 : minutes} min';
  final h = minutes ~/ 60;
  final m = minutes % 60;
  return m == 0 ? '$h h' : '$h h $m min';
}

/// The home screen's "Now and next": what the day's plan says you are doing, a bar that fills as it
/// runs, how long is left, what comes after, and Done for the item under way. For a day planned to
/// the minute (the owner's has 27 items), a glance answers "where am I?" without opening the
/// planner (2026-11-04). It moves on by itself every 30 seconds.
class NowNextCard extends StatefulWidget {
  const NowNextCard({super.key, required this.items, required this.onDone, required this.onOpen, this.clock});

  final List<PlannerItem> items;

  /// Marks an item done; the card hides it straight away and moves on.
  final Future<void> Function(PlannerItem item) onDone;
  final VoidCallback onOpen;

  /// The time, for tests.
  final DateTime Function()? clock;

  @override
  State<NowNextCard> createState() => _NowNextCardState();
}

class _NowNextCardState extends State<NowNextCard> {
  Timer? _tick;
  final _done = <String>{};

  DateTime _now() => (widget.clock ?? DateTime.now)();

  @override
  void initState() {
    super.initState();
    _tick = Timer.periodic(const Duration(seconds: 30), (_) {
      if (mounted) setState(() {});
    });
  }

  @override
  void dispose() {
    _tick?.cancel();
    super.dispose();
  }

  String _key(PlannerItem i) => '${i.id}@${i.occurrenceDate}';

  Future<void> _markDone(PlannerItem item) async {
    setState(() => _done.add(_key(item)));
    try {
      await widget.onDone(item);
    } catch (_) {
      if (!mounted) return;
      setState(() => _done.remove(_key(item)));
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Could not mark it done. Try again.')));
    }
  }

  static String _hhmm(String time) => time.substring(0, 5);

  @override
  Widget build(BuildContext context) {
    final position = DayPosition.at(widget.items.where((i) => !_done.contains(_key(i))).toList(), _now());
    if (position.isEmpty) return const SizedBox.shrink();
    final scheme = Theme.of(context).colorScheme;
    final theme = Theme.of(context).textTheme;
    final current = position.current;
    final next = position.next;

    return Card(
      key: const Key('now-next'),
      margin: EdgeInsets.zero,
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: widget.onOpen,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(20, 18, 12, 18),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              if (current != null) ...[
                Row(
                  children: [
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            'Now · ${spanWords(position.minutesLeft)} left',
                            key: const Key('now-left'),
                            style: theme.labelLarge?.copyWith(color: scheme.primary),
                          ),
                          const SizedBox(height: 4),
                          Text(
                            current.title,
                            key: const Key('now-title'),
                            style: theme.titleLarge,
                            maxLines: 2,
                            overflow: TextOverflow.ellipsis,
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(width: 8),
                    FilledButton.tonalIcon(
                      key: const Key('now-done'),
                      onPressed: () => _markDone(current),
                      icon: const Icon(PhosphorIconsRegular.check, size: 18),
                      label: const Text('Done'),
                    ),
                  ],
                ),
                const SizedBox(height: 12),
                Padding(
                  padding: const EdgeInsets.only(right: 8),
                  child: Semantics(
                    label: '${(position.progress * 100).round()} percent through',
                    child: ClipRRect(
                      borderRadius: BorderRadius.circular(4),
                      child: LinearProgressIndicator(
                        value: position.progress.clamp(0.0, 1.0),
                        minHeight: 6,
                        backgroundColor: scheme.surfaceContainerHighest,
                      ),
                    ),
                  ),
                ),
                const SizedBox(height: 4),
                Padding(
                  padding: const EdgeInsets.only(right: 8),
                  child: Row(
                    children: [
                      Text(
                        _hhmm(current.scheduledTime!),
                        style: theme.bodySmall?.copyWith(color: scheme.onSurfaceVariant),
                      ),
                      const Spacer(),
                      if (current.endTime != null)
                        Text(_hhmm(current.endTime!), style: theme.bodySmall?.copyWith(color: scheme.onSurfaceVariant)),
                    ],
                  ),
                ),
              ],
              if (next != null) ...[
                if (current != null) const SizedBox(height: 12),
                Row(
                  key: const Key('next-row'),
                  children: [
                    Icon(PhosphorIconsRegular.arrowRight, size: 18, color: scheme.onSurfaceVariant),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Text.rich(
                        TextSpan(
                          children: [
                            TextSpan(
                              text: current == null ? 'Next, in ${spanWords(position.minutesToNext)}: ' : 'Then ',
                              style: TextStyle(color: scheme.onSurfaceVariant),
                            ),
                            TextSpan(
                              text: next.title,
                              style: const TextStyle(fontWeight: FontWeight.w600),
                            ),
                            TextSpan(
                              text: ' at ${_hhmm(next.scheduledTime!)}',
                              style: TextStyle(color: scheme.onSurfaceVariant),
                            ),
                          ],
                        ),
                        style: theme.bodyLarge,
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                      ),
                    ),
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
