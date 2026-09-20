import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/screens/chat_screen.dart';
import 'package:personaos_mobile/voice_service.dart';

/// Streams one reply that proposes a change, then answers confirm/discard.
class _FakeChatApi extends PersonaOsApi {
  _FakeChatApi() : super(serverUrl: 'http://test');

  PendingAction pending = PendingAction(
    id: 'a1',
    tool: 'create_goal',
    summary: 'Create goal “Learn Rust”',
    status: 'pending',
  );

  @override
  Stream<ChatEvent> streamChat(String message, {int? conversationId}) async* {
    yield ChatEvent(type: 'start', conversationId: 1);
    yield ChatEvent(type: 'delta', text: 'I can add that goal.');
    yield ChatEvent(type: 'done', pending: [pending]);
  }

  @override
  Future<PendingAction> confirmAction(String id) async => PendingAction(
        id: id,
        tool: 'create_goal',
        summary: 'Create goal “Learn Rust”',
        status: 'confirmed',
        resultSummary: 'Learn Rust — month',
        resultOk: true,
      );

  @override
  Future<PendingAction> discardAction(String id) async => PendingAction(
        id: id,
        tool: 'create_goal',
        summary: 'Create goal “Learn Rust”',
        status: 'discarded',
      );
}

/// Records what the screen asks of speech, and never touches a platform plugin.
class _FakeVoice extends VoiceService {
  int listenCalls = 0;
  final List<String> spoken = [];

  @override
  Future<bool> ensureStt({
    void Function(String status)? onStatus,
    void Function(String error)? onError,
  }) async =>
      true;

  @override
  Future<void> listen({
    required void Function(String words) onResult,
    required void Function(String words) onFinal,
    Duration? pauseFor,
    Duration? listenFor,
  }) async {
    listenCalls++;
  }

  @override
  Future<void> speak(String text) async => spoken.add(text);

  @override
  Future<void> stopListening() async {}

  @override
  Future<void> stopSpeaking() async {}

  @override
  void dispose() {}
}

Future<void> _pumpChat(WidgetTester tester, _FakeChatApi api, _FakeVoice voice) async {
  await tester.pumpWidget(MaterialApp(
    home: ChatScreen(
      api: api,
      assistantNickname: 'Juno',
      voiceEnabled: true,
      voice: voice,
    ),
  ));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('a proposal pauses hands-free, and resolving it starts listening again',
      (tester) async {
    // Reported from the phone: after a confirm card the voice conversation stalled
    // for good, because nothing turned the loop back on.
    final api = _FakeChatApi();
    final voice = _FakeVoice();
    await _pumpChat(tester, api, voice);

    await tester.tap(find.byTooltip('Hands-free'));
    await tester.pumpAndSettle();
    expect(voice.listenCalls, 1);

    await tester.enterText(find.byType(TextField), 'add a goal to learn rust');
    await tester.tap(find.byIcon(Icons.send));
    await tester.pumpAndSettle();

    // The card is waiting, so the loop stopped rather than talking over it.
    expect(find.text('Confirm'), findsOneWidget);
    expect(find.textContaining('Hands-free paused'), findsOneWidget);
    expect(voice.listenCalls, 1);

    await tester.tap(find.text('Confirm'));
    await tester.pumpAndSettle();

    // The change ran, and the phone is listening again.
    expect(find.textContaining('✓'), findsOneWidget);
    expect(voice.listenCalls, 2);
  });

  testWidgets('discarding the card resumes hands-free too', (tester) async {
    final api = _FakeChatApi();
    final voice = _FakeVoice();
    await _pumpChat(tester, api, voice);

    await tester.tap(find.byTooltip('Hands-free'));
    await tester.pumpAndSettle();

    await tester.enterText(find.byType(TextField), 'add a goal');
    await tester.tap(find.byIcon(Icons.send));
    await tester.pumpAndSettle();

    await tester.tap(find.text('Discard'));
    await tester.pumpAndSettle();

    expect(voice.listenCalls, 2);
  });

  testWidgets('starting hands-free again by hand is not undone by the card', (tester) async {
    // The user's own decision settles it: resolving the card must not reopen the
    // microphone a second time on top of the loop they already restarted.
    final api = _FakeChatApi();
    final voice = _FakeVoice();
    await _pumpChat(tester, api, voice);

    await tester.tap(find.byTooltip('Hands-free'));
    await tester.pumpAndSettle();

    await tester.enterText(find.byType(TextField), 'add a goal');
    await tester.tap(find.byIcon(Icons.send));
    await tester.pumpAndSettle();

    // Let the "paused" snackbar clear the button it sits over.
    await tester.pump(const Duration(seconds: 5));
    await tester.pumpAndSettle();

    await tester.tap(find.byTooltip('Hands-free'));
    await tester.pumpAndSettle();
    expect(voice.listenCalls, 2);

    await tester.tap(find.text('Confirm'));
    await tester.pumpAndSettle();

    expect(voice.listenCalls, 2);
  });
}
