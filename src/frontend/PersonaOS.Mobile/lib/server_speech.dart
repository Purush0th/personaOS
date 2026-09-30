import 'dart:async';
import 'dart:io';

import 'package:audioplayers/audioplayers.dart';
import 'package:path_provider/path_provider.dart';
import 'package:record/record.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'api/personaos_api.dart';

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
    return VoicePrefs(
      serverStt: prefs.getBool(_sttKey) ?? false,
      serverTts: prefs.getBool(_ttsKey) ?? false,
    );
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

  StreamSubscription<Amplitude>? _level;
  Timer? _limit;
  String? _path;
  void Function(String text)? _onFinal;
  void Function(String error)? _onError;
  bool _recording = false;

  bool get isRecording => _recording;

  /// Louder than this (dBFS) counts as speech; quieter as a pause.
  static const _speechLevel = -38.0;

  Future<bool> ensureMic() => _recorder.hasPermission();

  /// Records one utterance: it ends after the speaker pauses for [pauseFor]
  /// (once they have said something), after [listenFor] at most, or when
  /// [finish] is called. The recording is then transcribed on the server and
  /// handed to [onFinal] (empty when nothing was said).
  Future<void> listen({
    required void Function(String text) onFinal,
    required void Function(String error) onError,
    Duration pauseFor = const Duration(seconds: 2),
    Duration listenFor = const Duration(seconds: 30),
  }) async {
    if (_recording) return;
    _onFinal = onFinal;
    _onError = onError;

    final dir = await getTemporaryDirectory();
    _path = '${dir.path}/speech-${DateTime.now().millisecondsSinceEpoch}.m4a';
    await _recorder.start(
      const RecordConfig(encoder: AudioEncoder.aacLc, sampleRate: 16000, numChannels: 1, bitRate: 48000),
      path: _path!,
    );
    _recording = true;

    var heard = false;
    var lastLoud = DateTime.now();
    _level = _recorder.onAmplitudeChanged(const Duration(milliseconds: 200)).listen((level) {
      final now = DateTime.now();
      if (level.current > _speechLevel) {
        heard = true;
        lastLoud = now;
      } else if (heard && now.difference(lastLoud) >= pauseFor) {
        finish();
      }
    });
    _limit = Timer(listenFor, finish);
  }

  /// Stops recording and sends what was recorded for transcription.
  Future<void> finish() async {
    if (!_recording) return;
    _recording = false;
    await _level?.cancel();
    _limit?.cancel();
    final path = await _recorder.stop() ?? _path;
    final onFinal = _onFinal;
    final onError = _onError;
    if (path == null) {
      onFinal?.call('');
      return;
    }

    try {
      final text = await api.transcribe(path);
      onFinal?.call(text);
    } on ApiException catch (e) {
      onError?.call(e.message);
    } finally {
      unawaited(File(path).delete().catchError((_) => File(path)));
    }
  }

  /// Stops without sending anything.
  Future<void> cancel() async {
    _recording = false;
    await _level?.cancel();
    _limit?.cancel();
    await _recorderInstance?.cancel();
  }

  /// Speaks [text] in the server's voice, returning once playback ends.
  /// Throws [ApiException] when the server cannot, so the caller can fall back.
  Future<void> speak(String text) async {
    _stopped = Completer<void>();
    final bytes = await api.speak(text);
    final dir = await getTemporaryDirectory();
    final file = File('${dir.path}/reply-${DateTime.now().millisecondsSinceEpoch}.mp3');
    await file.writeAsBytes(bytes, flush: true);
    try {
      await _player.stop();
      final done = _player.onPlayerComplete.first;
      await _player.play(DeviceFileSource(file.path));
      // A stop from outside ends playback without "complete": don't wait forever.
      await Future.any([done, _stopped.future]);
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
    cancel();
    _playerInstance?.dispose();
    _recorderInstance?.dispose();
  }
}
