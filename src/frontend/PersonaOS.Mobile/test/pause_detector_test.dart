import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/pause_detector.dart';

/// Feeds [levels] (dBFS) to [detector] at ten readings a second, starting at
/// [from] seconds, and returns the time in seconds at which it called the
/// utterance over, or null if it never did.
double? _feed(PauseDetector detector, List<double> levels, {double from = 0}) {
  final start = DateTime(2026, 10, 2, 9);
  for (var i = 0; i < levels.length; i++) {
    final seconds = from + i / 10;
    if (detector.add(levels[i], start.add(Duration(milliseconds: (seconds * 1000).round())))) return seconds;
  }
  return null;
}

List<double> _hold(double level, double seconds) => List.filled((seconds * 10).round(), level);

PauseDetector _detector() =>
    PauseDetector(pauseFor: const Duration(seconds: 2), noSpeechFor: const Duration(seconds: 10));

void main() {
  test('ends two seconds after speech stops in a quiet room', () {
    final ended = _feed(_detector(), [..._hold(-60, 1), ..._hold(-25, 2), ..._hold(-60, 5)]);

    expect(ended, closeTo(5.0, 0.15));
  });

  test('ends in a noisy room whose background is above the old fixed -38 dB line', () {
    // The case that never ended before: background at -32 dB is "speech" to a fixed -38 dB line.
    final ended = _feed(_detector(), [..._hold(-32, 1), ..._hold(-12, 2), ..._hold(-32, 5)]);

    expect(ended, closeTo(5.0, 0.15));
  });

  test('ends when the gain rises during the pause', () {
    // Android raises the gain when the voice stops: the background climbs from -50 to -42 dB.
    final rising = [for (var i = 0; i < 50; i++) -50.0 + (i < 20 ? i * 0.4 : 8)];
    final ended = _feed(_detector(), [..._hold(-50, 1), ..._hold(-20, 2), ...rising]);

    expect(ended, isNotNull);
    expect(ended!, lessThan(6.5));
  });

  test('a short breath between words does not end it', () {
    final ended = _feed(_detector(), [..._hold(-60, 1), ..._hold(-25, 2), ..._hold(-60, 1.5), ..._hold(-25, 2)]);

    expect(ended, isNull);
  });

  test('soft speech between the lines keeps the pause from counting', () {
    // After a loud phrase, a soft one 9 dB above the background: neither speech nor silence.
    final ended = _feed(_detector(), [..._hold(-60, 1), ..._hold(-25, 1), ..._hold(-51, 2.5)]);

    expect(ended, isNull);
  });

  test('starting to speak at once still works out the background', () {
    final ended = _feed(_detector(), [..._hold(-25, 2), ..._hold(-60, 3)]);

    expect(ended, closeTo(4.0, 0.15));
  });

  test('gives up after ten seconds with no speech', () {
    final detector = _detector();
    final ended = _feed(detector, _hold(-60, 12));

    expect(ended, closeTo(10.0, 0.15));
    expect(detector.heard, isFalse);
  });

  test('readings before the first buffer (-160 dB) do not set the background', () {
    final ended = _feed(_detector(), [..._hold(-160, 0.5), ..._hold(-60, 1), ..._hold(-25, 2), ..._hold(-60, 3)]);

    expect(ended, closeTo(5.5, 0.15));
  });
}
