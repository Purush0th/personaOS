import 'dart:async';

import 'package:flutter/material.dart';

import '../reminder_alarms.dart';

/// A reminder ringing, shown the way an alarm clock is.
///
/// This is what appears over the lock screen when a reminder is due. It is deliberately
/// unlike the rest of the app: dark, large, and with exactly two big targets, because it is read
/// half-awake from across a room and has to be dealt with in one tap.
class AlarmScreen extends StatefulWidget {
  const AlarmScreen({super.key, required this.alarm, this.onDismiss, this.onSnooze});

  final ReminderAlarm alarm;

  /// Overridable so a test can exercise the buttons without the notification plugin.
  final Future<void> Function(ReminderAlarm alarm)? onDismiss;
  final Future<void> Function(ReminderAlarm alarm)? onSnooze;

  @override
  State<AlarmScreen> createState() => _AlarmScreenState();
}

class _AlarmScreenState extends State<AlarmScreen> {
  late DateTime _now = DateTime.now();
  late final Timer _clock = Timer.periodic(
    const Duration(seconds: 1),
    (_) => setState(() => _now = DateTime.now()),
  );

  bool _closing = false;

  @override
  void initState() {
    super.initState();
    _clock; // Start ticking.
  }

  @override
  void dispose() {
    _clock.cancel();
    super.dispose();
  }

  Future<void> _close(Future<void> Function(ReminderAlarm) action) async {
    if (_closing) return;
    setState(() => _closing = true);
    try {
      await action(widget.alarm);
    } catch (e) {
      // Logged, not rethrown: the screen closes either way, and an error escaping after the
      // screen is gone has nowhere useful to go.
      debugPrint('Alarm action failed: $e');
    } finally {
      // pop(), never maybePop(). This screen blocks Back with PopScope(canPop: false) so an
      // alarm cannot be walked away from while ringing — and maybePop() honours that block, so
      // Dismiss and Snooze stopped the sound but left the screen stuck open. An explicit pop()
      // is the deliberate close. In `finally` so the screen still goes away if the alarm
      // action itself throws: a stuck alarm screen is worse than a missed cancel.
      if (mounted) Navigator.of(context).pop();
    }
  }

  @override
  Widget build(BuildContext context) {
    final time =
        '${_now.hour.toString().padLeft(2, '0')}:${_now.minute.toString().padLeft(2, '0')}';

    return PopScope(
      // Back must not silently leave an alarm ringing — it has to be snoozed or dismissed.
      canPop: false,
      child: Scaffold(
        backgroundColor: const Color(0xFF14142B),
        body: SafeArea(
          child: Padding(
            padding: const EdgeInsets.fromLTRB(28, 48, 28, 32),
            child: Column(
              children: [
                const Icon(Icons.alarm, color: Color(0xFFB9B9FF), size: 40),
                const SizedBox(height: 18),
                Text(
                  time,
                  style: const TextStyle(
                    color: Colors.white,
                    fontSize: 76,
                    fontWeight: FontWeight.w300,
                    letterSpacing: -2,
                  ),
                ),
                const SizedBox(height: 8),
                Text(
                  widget.alarm.title,
                  style: const TextStyle(
                    color: Color(0xFFB9B9FF),
                    fontSize: 18,
                    fontWeight: FontWeight.w600,
                  ),
                ),
                const Spacer(),
                Text(
                  widget.alarm.message,
                  textAlign: TextAlign.center,
                  style: const TextStyle(
                    color: Colors.white,
                    fontSize: 30,
                    fontWeight: FontWeight.w600,
                    height: 1.25,
                  ),
                ),
                const Spacer(),
                SizedBox(
                  width: double.infinity,
                  height: 64,
                  child: FilledButton(
                    onPressed: _closing ? null : () => _close(widget.onDismiss ?? ReminderAlarms.instance.dismiss),
                    style: FilledButton.styleFrom(
                      backgroundColor: const Color(0xFF5B5BD6),
                      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(32)),
                      textStyle: const TextStyle(fontSize: 20, fontWeight: FontWeight.w700),
                    ),
                    child: const Text('Dismiss'),
                  ),
                ),
                const SizedBox(height: 14),
                SizedBox(
                  width: double.infinity,
                  height: 56,
                  child: OutlinedButton(
                    onPressed: _closing ? null : () => _close(widget.onSnooze ?? ReminderAlarms.instance.snooze),
                    style: OutlinedButton.styleFrom(
                      foregroundColor: Colors.white,
                      side: const BorderSide(color: Color(0xFF5B5BD6), width: 1.5),
                      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(28)),
                      textStyle: const TextStyle(fontSize: 17, fontWeight: FontWeight.w600),
                    ),
                    child: Text('Snooze ${ReminderAlarms.snoozeFor.inMinutes} min'),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
