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

  bool get isListening => _stt.isListening;

  /// Initializes STT once; returns whether the device can transcribe (and the
  /// user granted mic + recognition permission). Safe to call repeatedly.
  Future<bool> ensureStt({void Function(String status)? onStatus}) async {
    if (_sttReady) return true;
    _sttReady = await _stt.initialize(
      onStatus: (s) => onStatus?.call(s),
      onError: (_) {},
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
        listenMode: ListenMode.dictation,
        cancelOnError: true,
        partialResults: true,
      ),
    );
  }

  Future<void> stopListening() => _stt.stop();

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
