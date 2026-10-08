import 'dart:async';

import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../api/personaos_api.dart';
import '../date_utils.dart';
import '../planner_calendar.dart';
import 'planner_event.dart';
import 'planner_views.dart';

/// The planner as a calendar, after Google Calendar's phone app: Schedule, Day, 4 days, Week,
/// Month and Year; the title opens a date picker, Today comes back, a swipe steps a period, the
/// view menu holds the three toggles, and the button adds an event. In Day, 4 days and Week a long
/// press picks an event up to drag it. The view and toggles are remembered on this phone.
class PlannerScreen extends StatefulWidget {
  const PlannerScreen({super.key, required this.api, this.boardEnabled = false, this.initialDate, this.initialView});

  final PersonaOsApi api;

  /// Whether a new event's work can be picked from the sprint board.
  final bool boardEnabled;

  /// Opens on this day (a tapped notification), in [initialView] or the remembered view.
  final DateTime? initialDate;
  final CalendarView? initialView;

  @override
  State<PlannerScreen> createState() => _PlannerScreenState();
}

class _PlannerScreenState extends State<PlannerScreen> {
  static const _viewKey = 'planner.view';
  static const _weekendsKey = 'planner.showWeekends';
  static const _skippedKey = 'planner.showSkipped';
  static const _completedKey = 'planner.showCompleted';

  CalendarView _view = CalendarView.day;
  late DateTime _anchor = dayOnly(widget.initialDate ?? DateTime.now());
  bool _showWeekends = true;
  bool _showSkipped = true;
  bool _showCompleted = true;

  List<PlannerItem> _items = [];
  bool _loading = true;
  String? _error;

  /// Answers from a slow request for a range no longer shown are dropped.
  int _request = 0;

  @override
  void initState() {
    super.initState();
    unawaited(_restore());
  }

  Future<void> _restore() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      final saved = prefs.getString(_viewKey);
      _view = widget.initialView ?? CalendarView.values.where((v) => v.name == saved).firstOrNull ?? CalendarView.day;
      _showWeekends = prefs.getBool(_weekendsKey) ?? true;
      _showSkipped = prefs.getBool(_skippedKey) ?? true;
      _showCompleted = prefs.getBool(_completedKey) ?? true;
    } catch (_) {
      // Preferences unavailable: the defaults apply.
    }
    await _reload();
  }

  Future<void> _save() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setString(_viewKey, _view.name);
      await prefs.setBool(_weekendsKey, _showWeekends);
      await prefs.setBool(_skippedKey, _showSkipped);
      await prefs.setBool(_completedKey, _showCompleted);
    } catch (_) {
      // Not remembered this time; nothing else depends on it.
    }
  }

  Future<void> _reload() async {
    final request = ++_request;
    final range = viewRange(_view, _anchor);
    if (mounted) setState(() => _loading = true);
    try {
      final items = await widget.api.getPlannerRange(localYmd(range.from), localYmd(range.to));
      if (!mounted || request != _request) return;
      setState(() {
        _items = items;
        _error = null;
      });
    } on ApiException catch (e) {
      if (mounted && request == _request) setState(() => _error = e.message);
    } catch (_) {
      if (mounted && request == _request) setState(() => _error = 'Could not load the planner.');
    } finally {
      if (mounted && request == _request) setState(() => _loading = false);
    }
  }

  /// The loaded events, without the ones the toggles hide.
  List<PlannerItem> get _visible => [
    for (final item in _items)
      if ((_showSkipped || item.status != 'skipped') && (_showCompleted || item.status != 'done')) item,
  ];

  /// The visible events under every day they cover.
  EventsByDay _byDay(List<PlannerItem> visible) {
    final range = viewRange(_view, _anchor);
    return eventsByDay(visible, range.from, range.to);
  }

  void _go(CalendarView view, DateTime anchor) {
    setState(() {
      _view = view;
      _anchor = dayOnly(anchor);
    });
    unawaited(_save());
    unawaited(_reload());
  }

  void _step(int direction) => _go(_view, stepAnchor(_view, _anchor, direction));

  Future<void> _pickDay() async {
    final picked = await showDatePicker(
      context: context,
      initialDate: _anchor,
      firstDate: DateTime(2000),
      lastDate: DateTime(2100),
    );
    if (picked != null) _go(_view, picked);
  }

  Future<void> _open(PlannerItem item) async {
    if (await showEventQuickView(context, widget.api, item)) await _reload();
  }

  /// An event dragged to another time or day, or made longer or shorter. It shows there at once;
  /// for a repeating event the change waits for which events it is for, and a cancelled choice or
  /// a refused change puts it back.
  Future<void> _move(PlannerItem item, Map<String, Object?> change) async {
    setState(() => _items = [for (final i in _items) identical(i, item) ? _preview(i, change) : i]);
    try {
      if (item.repeats) {
        final scope = await askScope(context, delete: false);
        if (scope != null) await widget.api.updateEvent(item.id, change, scope: scope, occurrence: item.occurrenceDate);
      } else {
        await widget.api.updateEvent(item.id, change);
      }
    } on ApiException catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    }
    await _reload();
  }

  /// The event as it will look after [change], to show while the change is saved.
  static PlannerItem _preview(PlannerItem item, Map<String, Object?> change) {
    final date = change['date'] as String?;
    final shift = date == null ? 0 : dayDiff(parseDay(item.date), parseDay(date));
    return item.copyWith(
      date: date,
      endDate: shift == 0 ? null : localYmd(addDays(parseDay(item.lastDay), shift)),
      scheduledTime: change['scheduledTime'] as String?,
      endTime: change['endTime'] as String?,
    );
  }

  Future<void> _create({DateTime? day, String? start}) async {
    final saved = await Navigator.of(context).push<bool>(
      MaterialPageRoute(
        builder: (_) => EventEditorPage(
          api: widget.api,
          date: day ?? (_view == CalendarView.month || _view == CalendarView.year ? DateTime.now() : _anchor),
          start: start,
          boardEnabled: widget.boardEnabled,
        ),
      ),
    );
    if (saved == true) await _reload();
  }

  List<DateTime> get _gridDays {
    final range = viewRange(_view, _anchor);
    final days = daysBetween(range.from, range.to);
    return _view == CalendarView.day || _showWeekends ? days : days.where((d) => !isWeekend(d)).toList();
  }

  @override
  Widget build(BuildContext context) {
    final today = DateTime.now().day;
    return Scaffold(
      appBar: AppBar(
        titleSpacing: 0,
        title: TextButton(
          key: const Key('planner-day'),
          onPressed: _pickDay,
          style: TextButton.styleFrom(foregroundColor: Theme.of(context).colorScheme.onSurface),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Flexible(
                child: Text(
                  viewTitle(_view, _anchor),
                  key: const Key('planner-title'),
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.titleLarge,
                ),
              ),
              const Icon(Icons.arrow_drop_down),
            ],
          ),
        ),
        actions: [
          IconButton(
            key: const Key('planner-today'),
            tooltip: 'Today',
            onPressed: () => _go(_view, DateTime.now()),
            icon: Stack(
              alignment: Alignment.center,
              children: [
                const Icon(Icons.calendar_today_outlined),
                Padding(
                  padding: const EdgeInsets.only(top: 5),
                  child: Text('$today', style: const TextStyle(fontSize: 9, fontWeight: FontWeight.w700)),
                ),
              ],
            ),
          ),
          PopupMenuButton<String>(
            key: const Key('planner-view-menu'),
            tooltip: 'View',
            icon: const Icon(Icons.view_agenda_outlined),
            onSelected: (choice) {
              switch (choice) {
                case 'weekends':
                  setState(() => _showWeekends = !_showWeekends);
                case 'skipped':
                  setState(() => _showSkipped = !_showSkipped);
                case 'completed':
                  setState(() => _showCompleted = !_showCompleted);
                default:
                  _go(CalendarView.values.firstWhere((v) => v.name == choice), _anchor);
                  return;
              }
              unawaited(_save());
            },
            itemBuilder: (context) => [
              for (final option in viewOptions)
                CheckedPopupMenuItem(
                  key: Key('view-${option.view.name}'),
                  value: option.view.name,
                  checked: option.view == _view,
                  child: Text(option.label),
                ),
              const PopupMenuDivider(),
              CheckedPopupMenuItem(
                key: const Key('opt-weekends'),
                value: 'weekends',
                checked: _showWeekends,
                child: const Text('Show weekends'),
              ),
              CheckedPopupMenuItem(
                key: const Key('opt-skipped'),
                value: 'skipped',
                checked: _showSkipped,
                child: const Text('Show skipped events'),
              ),
              CheckedPopupMenuItem(
                key: const Key('opt-completed'),
                value: 'completed',
                checked: _showCompleted,
                child: const Text('Show completed events'),
              ),
            ],
          ),
        ],
      ),
      floatingActionButton: FloatingActionButton(
        key: const Key('planner-create'),
        tooltip: 'Create event',
        onPressed: _create,
        child: const Icon(Icons.add),
      ),
      body: Column(
        children: [
          SizedBox(height: 3, child: _loading ? const LinearProgressIndicator() : null),
          if (_error != null)
            MaterialBanner(
              content: Text(_error!),
              actions: [TextButton(onPressed: _reload, child: const Text('Try again'))],
            ),
          Expanded(
            // A swipe across steps a period, as on Google Calendar's phone app.
            child: GestureDetector(
              onHorizontalDragEnd: (details) {
                final velocity = details.primaryVelocity ?? 0;
                if (velocity.abs() < 300) return;
                _step(velocity < 0 ? 1 : -1);
              },
              child: _body(),
            ),
          ),
        ],
      ),
    );
  }

  Widget _body() {
    final visible = _visible;
    final events = _byDay(visible);
    switch (_view) {
      case CalendarView.month:
        return MonthGridView(
          anchor: _anchor,
          items: visible,
          showWeekends: _showWeekends,
          onOpen: _open,
          onOpenDay: (day) => _go(CalendarView.day, day),
        );
      case CalendarView.year:
        return YearGridView(anchor: _anchor, events: events, onOpenDay: (day) => _go(CalendarView.day, day));
      case CalendarView.schedule:
        final range = viewRange(_view, _anchor);
        return RefreshIndicator(
          onRefresh: _reload,
          child: ScheduleListView(
            days: daysBetween(range.from, range.to),
            events: events,
            onOpen: _open,
            onOpenDay: (day) => _go(CalendarView.day, day),
          ),
        );
      case CalendarView.day:
      case CalendarView.fourDay:
      case CalendarView.week:
        return TimeGridView(
          key: ValueKey('${_view.name}-${localYmd(_anchor)}-$_showWeekends'),
          days: _gridDays,
          items: visible,
          events: events,
          onOpen: _open,
          onCreate: (day, start) => _create(day: day, start: start),
          onOpenDay: (day) => _go(CalendarView.day, day),
          onMove: (item, change) => unawaited(_move(item, change)),
        );
    }
  }
}
