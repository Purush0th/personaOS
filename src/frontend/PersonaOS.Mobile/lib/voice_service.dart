import 'package:flutter_tts/flutter_tts.dart';
import 'package:speech_to_text/speech_to_text.dart';

/// Push-to-talk speech-to-text plus optional text-to-speech read-back.
///
/// Kept deliberately thin: the chat screen owns the UI state (whether the mic
/// is active, whether read-back is on); this just wraps the two plugins and
/// hides their init/availability handshakes.
class VoiceService {
  final SpeechToText _stt = SpeechToText();
  final FlutterTts _tts = FlutterTts();

  bool _sttReady = false;
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
  }) async {
    await _stt.listen(
      onResult: (result) {
        onResult(result.recognizedWords);
        if (result.finalResult) onFinal(result.recognizedWords);
      },
      listenOptions: SpeechListenOptions(
        localeId: await _usableLocaleId(),
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
  Future<void> speak(String text) async {
    if (text.trim().isEmpty) return;
    await _tts.stop();
    await _tts.speak(text);
  }

  Future<void> stopSpeaking() => _tts.stop();

  void dispose() {
    _stt.cancel();
    _tts.stop();
  }
}
