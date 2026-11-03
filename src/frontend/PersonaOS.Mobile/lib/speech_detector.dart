import 'dart:async';
import 'dart:math' as math;
import 'dart:typed_data';

import 'package:flutter_onnxruntime/flutter_onnxruntime.dart';

/// Decides from speech probabilities when someone has finished speaking.
///
/// Fed one probability per 32 ms frame by [SileroVad]. Speech starts once a few frames in a row
/// score as speech; the turn is over once the scores have stayed low for [pauseFor] after it. A
/// cough or a click (speech shorter than [minSpeech]) does not count as having spoken.
///
/// Levels of loudness could not do this reliably: the phone's own gain control lifts a quiet room
/// during a pause until it sounds like talking, and hands-free never ended a turn (owner,
/// 2026-11-03). A model trained on speech tells a voice from a room.
class SpeechEndpointer {
  SpeechEndpointer({
    required this.pauseFor,
    required this.noSpeechFor,
    this.minSpeech = const Duration(milliseconds: 250),
    this.speechThreshold = 0.5,
    this.silenceThreshold = 0.35,
  });

  /// How long the quiet after speech lasts before the turn is over.
  final Duration pauseFor;

  /// How long to wait for any speech at all before giving up.
  final Duration noSpeechFor;

  /// The shortest run of speech that counts as having said something.
  final Duration minSpeech;

  /// A frame at or above this is speech.
  final double speechThreshold;

  /// A frame below this is quiet; between the two, nothing changes (a soft word does not end it).
  final double silenceThreshold;

  DateTime? _startedAt;
  DateTime? _speechSince;
  DateTime? _quietSince;
  bool _heard = false;

  /// Whether a real stretch of speech has been heard yet.
  bool get heard => _heard;

  /// Feeds the speech probability of the frame that ended at [at]; returns true once the turn is
  /// over: a pause after speech, or no speech for [noSpeechFor].
  bool add(double probability, DateTime at) {
    _startedAt ??= at;
    if (probability >= speechThreshold) {
      _speechSince ??= at;
      _quietSince = null;
      if (!_heard && at.difference(_speechSince!) >= minSpeech) _heard = true;
      return false;
    }
    if (probability < silenceThreshold) {
      _quietSince ??= at;
      // A burst too short to be speech is forgotten once it is over.
      if (!_heard) _speechSince = null;
      if (_heard && at.difference(_quietSince!) >= pauseFor) return true;
    }
    return !_heard && at.difference(_startedAt!) >= noSpeechFor;
  }
}

/// Silero VAD v5 on the phone: the probability that a 32 ms frame of 16 kHz audio is speech.
///
/// The model keeps a recurrent state between frames and reads each frame with the 64 samples
/// before it, as the reference implementation does; [reset] starts a new recording afresh.
class SileroVad {
  SileroVad._(this._session, this._sampleRate);

  static const sampleRate = 16000;

  /// Samples per frame the model takes at 16 kHz.
  static const frameSamples = 512;
  static const _contextSamples = 64;

  final OrtSession _session;
  final OrtValue _sampleRate;

  /// The model's recurrent state, kept on the native side: each frame's output state is the next
  /// frame's input as it is, so only the audio crosses into the runtime and only the probability
  /// comes back. Every crossing is a platform-channel call, and at eleven a frame scoring could not
  /// keep up with the microphone.
  OrtValue? _state;
  var _context = Float32List(_contextSamples);

  static Future<SileroVad> load() async {
    final session = await OnnxRuntime().createSessionFromAsset('assets/models/silero_vad.onnx');
    return SileroVad._(session, await OrtValue.fromList(Int64List.fromList([sampleRate]), []));
  }

  void reset() {
    final state = _state;
    _state = null;
    _context = Float32List(_contextSamples);
    if (state != null) unawaited(state.dispose());
  }

  /// The speech probability of [frame] ([frameSamples] samples, -1 to 1).
  Future<double> score(Float32List frame) async {
    final samples = Float32List(_contextSamples + frame.length)
      ..setAll(0, _context)
      ..setAll(_contextSamples, frame);
    _context = Float32List.fromList(frame.sublist(frame.length - _contextSamples));
    final input = await OrtValue.fromList(samples, [1, samples.length]);
    final state = _state ?? await OrtValue.fromList(Float32List(2 * 128), [2, 1, 128]);
    OrtValue? next;
    try {
      final out = await _session.run({'input': input, 'state': state, 'sr': _sampleRate});
      next = out['stateN'];
      final probability = out['output']!;
      try {
        return ((await probability.asFlattenedList()).first as num).toDouble();
      } finally {
        await probability.dispose();
      }
    } finally {
      await input.dispose();
      // The state just used is spent; after a failure the next frame starts from silence.
      await state.dispose();
      _state = next;
    }
  }

  Future<void> close() async {
    reset();
    await _sampleRate.dispose();
    await _session.close();
  }
}

/// Cuts a stream of 16-bit little-endian PCM into frames of [SileroVad.frameSamples] samples.
class PcmFramer {
  final _pending = BytesBuilder(copy: false);

  /// The whole frames [chunk] completes, as samples from -1 to 1.
  List<Float32List> add(Uint8List chunk) {
    _pending.add(chunk);
    final bytes = _pending.takeBytes();
    const frameBytes = SileroVad.frameSamples * 2;
    final whole = bytes.length ~/ frameBytes;
    final data = ByteData.sublistView(bytes);
    final frames = [
      for (var f = 0; f < whole; f++)
        Float32List.fromList([
          for (var i = 0; i < SileroVad.frameSamples; i++) data.getInt16(f * frameBytes + i * 2, Endian.little) / 32768,
        ]),
    ];
    if (bytes.length > whole * frameBytes) _pending.add(Uint8List.sublistView(bytes, whole * frameBytes));
    return frames;
  }
}

/// The loudness of a frame in dBFS (-100 for silence), for the level the screen shows and for the
/// fallback detector when the model cannot load.
double frameDb(Float32List frame) {
  var sum = 0.0;
  for (final s in frame) {
    sum += s * s;
  }
  final rms = math.sqrt(sum / frame.length);
  return rms <= 0.00001 ? -100 : 20 * math.log(rms) / math.ln10;
}

/// A WAV file of 16 kHz mono 16-bit [pcm], which the server's speech-to-text reads as is.
Uint8List wavFile(Uint8List pcm) {
  final header = ByteData(44);
  void ascii(int at, String text) {
    for (var i = 0; i < text.length; i++) {
      header.setUint8(at + i, text.codeUnitAt(i));
    }
  }

  ascii(0, 'RIFF');
  header.setUint32(4, 36 + pcm.length, Endian.little);
  ascii(8, 'WAVE');
  ascii(12, 'fmt ');
  header
    ..setUint32(16, 16, Endian.little)
    ..setUint16(20, 1, Endian.little)
    ..setUint16(22, 1, Endian.little)
    ..setUint32(24, SileroVad.sampleRate, Endian.little)
    ..setUint32(28, SileroVad.sampleRate * 2, Endian.little)
    ..setUint16(32, 2, Endian.little)
    ..setUint16(34, 16, Endian.little);
  ascii(36, 'data');
  header.setUint32(40, pcm.length, Endian.little);
  return (BytesBuilder(copy: false)
        ..add(header.buffer.asUint8List())
        ..add(pcm))
      .takeBytes();
}
