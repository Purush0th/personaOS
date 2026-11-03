import 'dart:async';
import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/server_speech.dart';
import 'package:personaos_mobile/voice_service.dart';

/// Stands in for the server's speech service: no microphone, no player.
class _FakeServer extends ServerSpeech {
  _FakeServer() : super(PersonaOsApi(serverUrl: 'http://test'));

  final spoken = <String>[];
  bool failSpeaking = false;
  String transcript = 'remind me to call mum';
  bool finished = false;

  @override
  Future<bool> ensureMic() async => true;

  @override
  Future<void> listen({
    required void Function(String text) onFinal,
    required void Function(String error) onError,
    Duration pauseFor = const Duration(seconds: 2),
    Duration listenFor = const Duration(seconds: 30),
  }) async => onFinal(transcript);

  @override
  Future<void> finish() async => finished = true;

  /// Stands in for the server's audio: the text itself, so playing it records what was said.
  @override
  Future<List<int>> synthesize(String text) async {
    if (failSpeaking) throw ApiException('No speech service.');
    return utf8.encode(text);
  }

  @override
  Future<void> play(List<int> audio) async => spoken.add(utf8.decode(audio));

  @override
  Future<void> stopSpeaking() async {}
}

/// The phone's own engines, recorded instead of run.
class _Voice extends VoiceService {
  final deviceSpoken = <String>[];

  @override
  Future<void> speakOnDevice(String text) async => deviceSpoken.add(text);

  @override
  Future<void> stopOnDevice() async {}
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  test('with the server chosen, a whole utterance arrives once as the transcript', () async {
    final voice = VoiceService()
      ..server = _FakeServer()
      ..useServerStt = true;
    final results = <String>[];
    final finals = <String>[];

    expect(await voice.ensureStt(), isTrue);
    await voice.listen(onResult: results.add, onFinal: finals.add);

    expect(finals, ['remind me to call mum']);
    expect(results, ['remind me to call mum']);
  });

  test('stopping push-to-talk sends what was recorded', () async {
    final server = _FakeServer();
    final voice = VoiceService()
      ..server = server
      ..useServerStt = true;

    await voice.stopListening();

    expect(server.finished, isTrue);
  });

  test("replies are read in the server's voice, without Markdown", () async {
    final server = _FakeServer();
    final voice = _Voice()
      ..server = server
      ..useServerTts = true;

    await voice.speak('**Learn Rust** today');

    expect(server.spoken, ['Learn Rust today']);
    expect(voice.deviceSpoken, isEmpty);
  });

  test("when the server cannot speak, the phone's own voice reads it", () async {
    final server = _FakeServer()..failSpeaking = true;
    final voice = _Voice()
      ..server = server
      ..useServerTts = true;

    await voice.speak('Hello');

    expect(server.spoken, isEmpty);
    expect(voice.deviceSpoken, ['Hello']);
  });

  test("a reply stopped midway is not read again in the phone's voice", () async {
    // Owner, 2026-10-11: leaving the chat while a reply played cut the server's voice, and the
    // phone's own voice then started reading the same reply.
    final server = _StoppableServer();
    final voice = _Voice()
      ..server = server
      ..useServerTts = true;

    final speaking = voice.speak('A long answer');
    await voice.stopSpeaking();
    await speaking;

    expect(voice.deviceSpoken, isEmpty);
  });

  test('a long reply is voiced a sentence at a time, the next made while one plays', () async {
    final server = _FakeServer();
    final voice = _Voice()
      ..server = server
      ..useServerTts = true;

    await voice.speak(
      'Your morning starts at six with the bed made and a short prayer. '
      'Then you read twenty pages before creating content at seven. '
      'Lunch is at a quarter to one.',
    );

    expect(server.spoken, [
      'Your morning starts at six with the bed made and a short prayer.',
      'Then you read twenty pages before creating content at seven.',
      'Lunch is at a quarter to one.',
    ]);
  });

  test("when the server's voice fails partway, the phone reads only what is left", () async {
    final server = _FailsSecond();
    final voice = _Voice()
      ..server = server
      ..useServerTts = true;

    await voice.speak('The first sentence is long enough on its own. The second one is never voiced by the server.');

    expect(server.spoken, ['The first sentence is long enough on its own.']);
    expect(voice.deviceSpoken, ['The second one is never voiced by the server.']);
  });
}

/// Plays until stopped, then fails the way a player cut off mid-reply does.
class _StoppableServer extends _FakeServer {
  final _stopped = Completer<void>();

  @override
  Future<void> play(List<int> audio) async {
    await _stopped.future;
    throw StateError('Player stopped');
  }

  @override
  Future<void> stopSpeaking() async => _stopped.complete();
}

/// Voices the first piece, then fails.
class _FailsSecond extends _FakeServer {
  var _made = 0;

  @override
  Future<List<int>> synthesize(String text) async {
    if (++_made > 1) throw ApiException('Speech service went away.');
    return utf8.encode(text);
  }
}
