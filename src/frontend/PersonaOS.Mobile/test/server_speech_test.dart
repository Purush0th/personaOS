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
  }) async =>
      onFinal(transcript);

  @override
  Future<void> finish() async => finished = true;

  @override
  Future<void> speak(String text) async {
    if (failSpeaking) throw ApiException('No speech service.');
    spoken.add(text);
  }

  @override
  Future<void> stopSpeaking() async {}
}

/// The phone's own engines, recorded instead of run.
class _Voice extends VoiceService {
  final deviceSpoken = <String>[];

  @override
  Future<void> speakOnDevice(String text) async => deviceSpoken.add(text);
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
}
