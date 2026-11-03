import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/speech_detector.dart';

void main() {
  final start = DateTime(2026, 11, 3, 9);
  const frame = Duration(milliseconds: 32);

  /// Feeds [probabilities] one frame apart; returns the index of the frame that ended the turn.
  int? endsAt(SpeechEndpointer e, List<double> probabilities) {
    for (var i = 0; i < probabilities.length; i++) {
      if (e.add(probabilities[i], start.add(frame * i))) return i;
    }
    return null;
  }

  SpeechEndpointer endpointer() =>
      SpeechEndpointer(pauseFor: const Duration(milliseconds: 1500), noSpeechFor: const Duration(seconds: 10));

  test('a pause after speech ends the turn once it has lasted the pause', () {
    final e = endpointer();
    // One second of speech, then quiet.
    final at = endsAt(e, [...List.filled(31, 0.9), ...List.filled(100, 0.05)]);

    expect(e.heard, isTrue);
    // 1500 ms of quiet is 47 frames after the first quiet one.
    expect(at, 31 + 47);
  });

  test('a soft stretch between the thresholds does not count as a pause', () {
    final e = endpointer();
    final at = endsAt(e, [...List.filled(31, 0.9), ...List.filled(80, 0.4)]);

    expect(at, isNull);
  });

  test('a click too short to be speech is not heard, and does not end anything', () {
    final e = endpointer();
    final at = endsAt(e, [0.9, 0.9, ...List.filled(100, 0.05)]);

    expect(e.heard, isFalse);
    expect(at, isNull);
  });

  test('ten seconds with no speech ends the turn', () {
    final e = endpointer();
    final at = endsAt(e, List.filled(400, 0.05));

    expect(e.heard, isFalse);
    expect(at, isNotNull);
    expect(frame * at!, greaterThanOrEqualTo(const Duration(seconds: 10)));
  });

  test('audio arriving in odd-sized chunks comes out as whole 512-sample frames', () {
    final framer = PcmFramer();
    // 1.5 frames of the sample value 16384 (half scale), then the other half frame.
    final first = Uint8List(768 * 2);
    ByteData.sublistView(first).setInt16(0, 16384, Endian.little);

    expect(framer.add(first), hasLength(1));
    expect(framer.add(Uint8List(100)), isEmpty);
    expect(framer.add(Uint8List(924)), hasLength(1));
    expect(PcmFramer().add(first).first.first, closeTo(0.5, 1e-6));
  });

  test('the loudness of silence and of a full-scale signal', () {
    expect(frameDb(Float32List(512)), -100);
    expect(frameDb(Float32List(512)..fillRange(0, 512, 1.0)), closeTo(0, 1e-6));
  });

  test('a recording is wrapped as a 16 kHz mono WAV file', () {
    final wav = wavFile(Uint8List(3200));
    final header = ByteData.sublistView(wav);

    expect(String.fromCharCodes(wav.sublist(0, 4)), 'RIFF');
    expect(String.fromCharCodes(wav.sublist(8, 12)), 'WAVE');
    expect(header.getUint32(24, Endian.little), 16000);
    expect(header.getUint16(22, Endian.little), 1);
    expect(header.getUint32(40, Endian.little), 3200);
    expect(wav.length, 44 + 3200);
  });
}
