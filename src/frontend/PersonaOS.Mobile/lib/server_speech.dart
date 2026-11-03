import 'dart:async';
import 'dart:io';
import 'dart:typed_data';

import 'package:audioplayers/audioplayers.dart';
import 'package:flutter/foundation.dart';
import 'package:path_provider/path_provider.dart';
import 'package:record/record.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'api/personaos_api.dart';
import 'pause_detector.dart';
import 'speech_detector.dart';

/// Which engine the phone uses for each direction of speech. "server" is only
/// honoured while the server reports that direction set up; otherwise the
/// phone's own engine is used, so voice never breaks because a server went away.
class VoicePrefs {
  VoicePrefs({this.serverStt = false, this.serverTts = false});

  static const _sttKey = 'voice.serverStt';
  static const _ttsKey = 'voice.serverTts';

  final bool serverStt;
  final bool serverTts;

  static Future<VoicePrefs> load() async {
    final prefs = await SharedPreferences.getInstance();
    return VoicePrefs(serverStt: prefs.getBool(_sttKey) ?? false, serverTts: prefs.getBool(_ttsKey) ?? false);
  }

  static Future<void> save({bool? serverStt, bool? serverTts}) async {
    final prefs = await SharedPreferences.getInstance();
    if (serverStt != null) await prefs.setBool(_sttKey, serverStt);
    if (serverTts != null) await prefs.setBool(_ttsKey, serverTts);
  }
}

/// Speech through the PersonaOS server: the phone records, the server
/// transcribes with its speech service; the server synthesizes, the phone plays.
/// The phone never talks to the speech service itself.
class ServerSpeech {
  ServerSpeech(this.api);

  final PersonaOsApi api;
  // Created on first use: a phone that never records never loads the plugins.
  AudioRecorder? _recorderInstance;
  AudioPlayer? _playerInstance;
  AudioRecorder get _recorder => _recorderInstance ??= AudioRecorder();
  AudioPlayer get _player => _playerInstance ??= AudioPlayer();

  StreamSubscription<Uint8List>? _audio;
  Timer? _limit;
  void Function(String text)? _onFinal;
  void Function(String error)? _onError;
  bool _recording = false;
  bool _heard = false;
  final _pcm = BytesBuilder(copy: false);

  bool get isRecording => _recording;

  /// How loud the microphone is right now, 0 to 1, for the hands-free screen to move with.
  final level = ValueNotifier<double>(0);

  /// How long to wait for the speaker to start before ending the recording.
  static const _noSpeechFor = Duration(seconds: 10);

  /// The speech detector, loaded on first use; null when it cannot run on this phone, and the
  /// loudness detector ([PauseDetector]) is used instead.
  SileroVad? _vad;
  bool _vadTried = false;

  /// Raw 16 kHz audio, so every 32 ms can go to the speech detector as it is recorded. Recorded as
  /// Android's speech recognizer records: the voice-recognition source, which is tuned for speech,
  /// with noise suppression, and echo cancelling so hands-free does not pick up the reply it is
  /// playing.
  static const recordConfig = RecordConfig(
    encoder: AudioEncoder.pcm16bits,
    sampleRate: SileroVad.sampleRate,
    numChannels: 1,
    noiseSuppress: true,
    echoCancel: true,
    androidConfig: AndroidRecordConfig(audioSource: AndroidAudioSource.voiceRecognition),
  );

  Future<bool> ensureMic() => _recorder.hasPermission();

  /// Records one utterance: it ends after the speaker pauses for [pauseFor] (once they have said
  /// something; see [SpeechEndpointer]), after 10 s with no speech, after [listenFor] at most, or
  /// when [finish] is called. The recording is then transcribed on the server and handed to
  /// [onFinal] (empty when nothing was said).
  Future<void> listen({
    required void Function(String text) onFinal,
    required void Function(String error) onError,
    Duration pauseFor = const Duration(seconds: 2),
    Duration listenFor = const Duration(seconds: 30),
  }) async {
    if (_recording) return;
    _onFinal = onFinal;
    _onError = onError;
    _heard = false;
    _pcm.clear();

    if (!_vadTried) {
      _vadTried = true;
      try {
        _vad = await SileroVad.load();
      } catch (e) {
        debugPrint('Speech detector unavailable, using loudness instead: $e');
      }
    }
    var vad = _vad?..reset();
    final endpointer = SpeechEndpointer(pauseFor: pauseFor, noSpeechFor: _noSpeechFor);
    final loudness = PauseDetector(pauseFor: pauseFor, noSpeechFor: _noSpeechFor);
    final framer = PcmFramer();

    final stream = await _recorder.startStream(recordConfig);
    _recording = true;
    // Frames are scored one after another, in order, however the chunks arrive.
    var scoring = Future<void>.value();
    _audio = stream.listen((chunk) {
      _pcm.add(chunk);
      final frames = framer.add(chunk);
      scoring = scoring.then((_) async {
        for (final frame in frames) {
          if (!_recording) return;
          final db = frameDb(frame);
          level.value = ((db + 60) / 50).clamp(0.0, 1.0);
          final now = DateTime.now();
          double? probability;
          try {
            probability = await vad?.score(frame);
          } catch (e) {
            // A model that loads but cannot run here: the loudness detector takes over for good.
            debugPrint('Speech detector failed, using loudness instead: $e');
            vad = _vad = null;
          }
          bool over;
          if (probability != null) {
            over = endpointer.add(probability, now);
            _heard = endpointer.heard;
          } else {
            over = loudness.add(db, now);
            _heard = loudness.heard;
          }
          if (over) {
            unawaited(finish());
            return;
          }
        }
      });
    });
    _limit = Timer(listenFor, finish);
  }

  /// Stops recording and sends what was recorded for transcription.
  Future<void> finish() async {
    if (!_recording) return;
    _recording = false;
    level.value = 0;
    _limit?.cancel();
    await _recorder.stop();
    await _audio?.cancel();
    final onFinal = _onFinal;
    final onError = _onError;
    final pcm = _pcm.takeBytes();
    // Nothing that sounded like speech: whisper would only make words up from the room noise.
    if (pcm.isEmpty || (_vad != null && !_heard)) {
      onFinal?.call('');
      return;
    }

    final dir = await getTemporaryDirectory();
    final file = File('${dir.path}/speech-${DateTime.now().millisecondsSinceEpoch}.wav');
    try {
      await file.writeAsBytes(wavFile(pcm), flush: true);
      // The phone's own language, so whisper does not have to guess it.
      final text = await api.transcribe(file.path, language: PlatformDispatcher.instance.locale.languageCode);
      onFinal?.call(text);
    } on ApiException catch (e) {
      onError?.call(e.message);
    } finally {
      unawaited(file.delete().catchError((_) => file));
    }
  }

  /// Stops without sending anything.
  Future<void> cancel() async {
    _recording = false;
    level.value = 0;
    _limit?.cancel();
    await _audio?.cancel();
    _pcm.clear();
    await _recorderInstance?.cancel();
  }

  /// Speaks [text] in the server's voice, returning once playback ends.
  /// Throws [ApiException] when the server cannot, so the caller can fall back.
  Future<void> speak(String text) async => play(await synthesize(text));

  /// The server's voice reading [text], as audio. Throws [ApiException] when the server cannot.
  Future<List<int>> synthesize(String text) => api.speak(text);

  /// Plays [audio] from [synthesize], returning once playback ends or [stopSpeaking] cuts it.
  Future<void> play(List<int> audio) async {
    _stopped = Completer<void>();
    final stopped = _stopped;
    final dir = await getTemporaryDirectory();
    final file = File('${dir.path}/reply-${DateTime.now().microsecondsSinceEpoch}.mp3');
    await file.writeAsBytes(audio, flush: true);
    try {
      if (stopped.isCompleted) return;
      await _player.stop();
      final done = _player.onPlayerComplete.first;
      await _player.play(DeviceFileSource(file.path));
      // A stop from outside ends playback without "complete": don't wait forever.
      await Future.any([done, stopped.future]);
    } finally {
      unawaited(file.delete().catchError((_) => file));
    }
  }

  Completer<void> _stopped = Completer<void>();

  Future<void> stopSpeaking() async {
    await _playerInstance?.stop();
    if (!_stopped.isCompleted) _stopped.complete();
  }

  void dispose() {
    unawaited(cancel());
    unawaited(_playerInstance?.dispose());
    unawaited(_recorderInstance?.dispose());
    unawaited(_vad?.close());
  }
}
