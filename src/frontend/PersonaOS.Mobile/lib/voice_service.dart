import 'package:flutter_tts/flutter_tts.dart';
import 'package:speech_to_text/speech_to_text.dart';

/// Strips Markdown so a reply can be spoken rather than recited.
///
/// Models format their answers, and a screen reader does not know that `**` is
/// emphasis: read back verbatim, "**Learn Rust**" comes out as "asterisk
/// asterisk Learn Rust asterisk asterisk", and a fenced code block is read
/// character by character. Only the visible words should be spoken.
String speakableText(String markdown) {
  var text = markdown;

  // Fenced code: unreadable aloud, so drop it rather than spell it out.
  text = text.replaceAll(RegExp(r'```[\s\S]*?```'), ' ');
  // Links: keep the label, drop the URL.
  text = text.replaceAllMapped(
      RegExp(r'\[([^\]]+)\]\([^)]*\)'), (m) => m.group(1) ?? '');
  // Inline code, bold, strikethrough — markers only, keep the words.
  text = text.replaceAll(RegExp(r'[`*~]'), '');
  // Underscore emphasis, but ONLY around a whole word. Stripping every
  // underscore turns `get_goals` into "getgoals", and the assistant names its
  // tools constantly.
  text = text.replaceAllMapped(
      RegExp(r'(?<![A-Za-z0-9_])_([^_\n]+)_(?![A-Za-z0-9_])'),
      (m) => m.group(1) ?? '');
  // Headings and blockquote markers at the start of a line.
  text = text.replaceAll(RegExp(r'^\s{0,3}#{1,6}\s*', multiLine: true), '');
  text = text.replaceAll(RegExp(r'^\s{0,3}>\s?', multiLine: true), '');
  // Bullet markers. Numbered items keep their number: "1." reads naturally.
  text = text.replaceAll(RegExp(r'^\s*[-+]\s+', multiLine: true), '');
  // Horizontal rules.
  text = text.replaceAll(RegExp(r'^\s*([-*_]\s*){3,}$', multiLine: true), ' ');

  return text.replaceAll(RegExp(r'[ \t]+'), ' ').trim();
}

/// Push-to-talk speech-to-text plus optional text-to-speech read-back.
///
/// Kept deliberately thin: the chat screen owns the UI state (whether the mic
/// is active, whether read-back is on); this just wraps the two plugins and
/// hides their init/availability handshakes.
class VoiceService {
  final SpeechToText _stt = SpeechToText();
  final FlutterTts _tts = FlutterTts();

  bool _sttReady = false;
  bool _ttsAwaits = false;
  String? _localeId;

  bool get isListening => _stt.isListening;

  /// Initializes STT once; returns whether the device can transcribe (and the
  /// user granted mic + recognition permission). Safe to call repeatedly.
  ///
  /// [onError] matters more than it looks: initialization succeeding only means
  /// a recognizer exists, not that it can transcribe. A device with no language
  /// pack for the current locale accepts `listen`, turns the microphone on, and
  /// then fails asynchronously — so without surfacing the error the caller is
  /// left with a live mic that never produces a word.
  Future<bool> ensureStt({
    void Function(String status)? onStatus,
    void Function(String error)? onError,
  }) async {
    if (_sttReady) return true;
    _sttReady = await _stt.initialize(
      onStatus: (s) => onStatus?.call(s),
      onError: (e) => onError?.call(e.errorMsg),
    );
    return _sttReady;
  }

  /// Streams recognized words to [onResult]; [onFinal] fires once when the
  /// utterance is complete (so the caller can stop the mic UI).
  Future<void> listen({
    required void Function(String words) onResult,
    required void Function(String words) onFinal,
    Duration? pauseFor,
    Duration? listenFor,
  }) async {
    await _stt.listen(
      onResult: (result) {
        onResult(result.recognizedWords);
        if (result.finalResult) onFinal(result.recognizedWords);
      },
      listenOptions: SpeechListenOptions(
        localeId: await _usableLocaleId(),
        pauseFor: pauseFor,
        listenFor: listenFor,
        listenMode: ListenMode.dictation,
        cancelOnError: true,
        partialResults: true,
      ),
    );
  }

  Future<void> stopListening() => _stt.stop();

  /// The locale to dictate in: the device's own if the recognizer supports it,
  /// otherwise the first one it does support.
  ///
  /// Passing null here means "device default", which fails outright on a device
  /// whose recognizer has no pack for that locale. Falling back to a supported
  /// locale transcribes in the wrong language at worst, rather than not at all.
  Future<String?> _usableLocaleId() async {
    if (_localeId != null) return _localeId;

    final supported = await _stt.locales();
    if (supported.isEmpty) return null;

    final system = await _stt.systemLocale();
    final preferred = system?.localeId;
    final match = supported.where((l) => l.localeId == preferred).firstOrNull;

    _localeId = (match ?? supported.first).localeId;
    return _localeId;
  }

  /// Speaks [text], interrupting any read-back already in progress.
  ///
  /// Awaits until the speech actually finishes, not merely until it starts.
  /// Hands-free mode depends on that: it must not reopen the microphone while
  /// the phone is still talking, or the assistant transcribes its own voice.
  Future<void> speak(String text) async {
    text = speakableText(text);
    if (text.trim().isEmpty) return;
    if (!_ttsAwaits) {
      await _tts.awaitSpeakCompletion(true);
      _ttsAwaits = true;
    }
    await _tts.stop();
    await _tts.speak(text);
  }

  Future<void> stopSpeaking() => _tts.stop();

  void dispose() {
    _stt.cancel();
    _tts.stop();
  }
}
