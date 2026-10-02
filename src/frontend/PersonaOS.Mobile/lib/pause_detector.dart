/// Decides from microphone levels when someone has finished speaking.
///
/// A fixed threshold does not work on phones: the background level differs per
/// phone and per room, and Android raises the gain while nobody speaks, so a
/// quiet room can sit above any fixed "silence" line and a recording never
/// ends. Instead this learns the background level as it goes and judges each
/// reading against it: [speechMargin] above it is speech, within
/// [silenceMargin] of it is silence, and a level between the two is neither.
class PauseDetector {
  PauseDetector({required this.pauseFor, required this.noSpeechFor, this.speechMargin = 12, this.silenceMargin = 6});

  /// How long the silence after speech lasts before the utterance is over.
  final Duration pauseFor;

  /// How long to wait for any speech at all before giving up.
  final Duration noSpeechFor;

  /// Decibels above the background that count as speech.
  final double speechMargin;

  /// Decibels above the background that still count as silence.
  final double silenceMargin;

  /// Readings at or below this mean "no audio yet" (the recorder reports
  /// -160 dB before its first buffer), not a very quiet room.
  static const _noReading = -100.0;

  /// How far the background moves toward a non-speech reading above it, per
  /// reading. Slow enough that a word does not raise it, fast enough to follow
  /// the gain rising during a pause (about a second at ten readings a second).
  static const _riseRate = 0.05;

  double? _background;
  DateTime? _startedAt;
  DateTime? _quietSince;
  bool _heard = false;
  double? _loudest;

  /// Whether any speech has been heard yet.
  bool get heard => _heard;

  /// Feeds one level reading in dBFS taken at [at]; returns true once the
  /// utterance is over: a pause after speech, or no speech for [noSpeechFor].
  bool add(double level, DateTime at) {
    _startedAt ??= at;
    if (level <= _noReading) return _gaveUp(at);

    final background = _background;
    if (background == null || level < background) {
      _background = level;
    } else if (level < background + speechMargin) {
      _background = background + (level - background) * _riseRate;
    }

    final floor = _background!;
    // Someone who starts talking at once is heard before the background is
    // known: once it turns out lower, the earlier loud readings were speech.
    _loudest = _loudest == null || level > _loudest! ? level : _loudest;
    // Those readings looked like background then, so the pause starts now.
    if (!_heard && _loudest! >= floor + speechMargin) {
      _heard = true;
      _quietSince = null;
    }

    if (level >= floor + speechMargin) {
      _heard = true;
      _quietSince = null;
      return false;
    }
    if (level < floor + silenceMargin) {
      _quietSince ??= at;
      if (_heard && at.difference(_quietSince!) >= pauseFor) return true;
    } else {
      // Between the lines: maybe soft speech. Do not count it as pause, so a
      // quiet word never cuts the speaker off; the background catches up with
      // steady noise there within about a second.
      _quietSince = null;
    }
    return _gaveUp(at);
  }

  bool _gaveUp(DateTime at) => !_heard && at.difference(_startedAt!) >= noSpeechFor;
}
