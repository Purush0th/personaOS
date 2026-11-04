import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/screens/chat_screen.dart';
import 'package:personaos_mobile/screens/voice_mode.dart';
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

  final sent = <String>[];

  @override
  Stream<ChatEvent> streamChat(String message, {int? conversationId, String? mode}) async* {
    sent.add(message);
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
  Future<PendingAction> discardAction(String id) async =>
      PendingAction(id: id, tool: 'create_goal', summary: 'Create goal “Learn Rust”', status: 'discarded');
}

/// Records what the screen asks of speech, and never touches a platform plugin.
class _FakeVoice extends VoiceService {
  int listenCalls = 0;
  final List<String> spoken = [];
  void Function(String words)? _onResult;
  void Function(String words)? _onFinal;

  /// The user saying [words] to the open microphone, then pausing.
  void say(String words) {
    _onResult?.call(words);
    _onFinal?.call(words);
  }

  @override
  Future<bool> ensureStt({void Function(String status)? onStatus, void Function(String error)? onError}) async => true;

  @override
  Future<void> listen({
    required void Function(String words) onResult,
    required void Function(String words) onFinal,
    Duration? pauseFor,
    Duration? listenFor,
  }) async {
    listenCalls++;
    _onResult = onResult;
    _onFinal = onFinal;
  }

  @override
  Future<void> speak(String text) async => spoken.add(text);

  int stopListeningCalls = 0;

  @override
  Future<void> stopListening() async => stopListeningCalls++;

  @override
  Future<void> stopSpeaking() async {}

  @override
  void dispose() {}
}

Future<void> _pumpChat(WidgetTester tester, _FakeChatApi api, _FakeVoice voice) async {
  // The hands-free screen moves for as long as it is open; still, the tests can wait for it to settle.
  tester.platformDispatcher.accessibilityFeaturesTestValue = const FakeAccessibilityFeatures(disableAnimations: true);
  addTearDown(tester.platformDispatcher.clearAccessibilityFeaturesTestValue);
  await tester.pumpWidget(
    MaterialApp(
      home: ChatScreen(api: api, assistantNickname: 'Juno', voiceEnabled: true, voice: voice),
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('a proposal pauses hands-free, and resolving it starts listening again', (tester) async {
    // Reported from the phone: after a confirm card the voice conversation stalled
    // for good, because nothing turned the loop back on.
    final api = _FakeChatApi();
    final voice = _FakeVoice();
    await _pumpChat(tester, api, voice);

    await tester.tap(find.byTooltip('Hands-free'));
    await tester.pumpAndSettle();
    expect(voice.listenCalls, 1);

    voice.say('add a goal to learn rust');
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

    voice.say('add a goal');
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

    voice.say('add a goal');
    await tester.pumpAndSettle();

    // Let the "paused" snackbar clear the button it sits over.
    await tester.pump(const Duration(seconds: 5));
    await tester.pumpAndSettle();

    await tester.tap(find.byTooltip('Hands-free'));
    await tester.pumpAndSettle();
    expect(voice.listenCalls, 2);

    // The card sits under the hands-free screen; resolve it as a tap on it would.
    tester
        .widget<ButtonStyleButton>(
          find.ancestor(of: find.text('Confirm'), matching: find.bySubtype<ButtonStyleButton>()),
        )
        .onPressed!();
    await tester.pumpAndSettle();

    expect(voice.listenCalls, 2);
  });

  testWidgets('hands-free fills the screen, and the Orb sends now and one button ends it', (tester) async {
    // Owner, 2026-11-03: a banner was not enough; voice should take the whole screen.
    final api = _FakeChatApi();
    final voice = _FakeVoice();
    await _pumpChat(tester, api, voice);

    await tester.tap(find.byTooltip('Hands-free'));
    await tester.pumpAndSettle();
    expect(find.byType(VoiceModeView), findsOneWidget);
    expect(find.text('Listening'), findsOneWidget);
    // Owner, 2026-11-04: one button only, the way out; the Orb itself sends early.
    expect(find.descendant(of: find.byType(VoiceModeView), matching: find.byType(IconButton)), findsOneWidget);

    await tester.tap(find.byKey(const Key('voice-orb')));
    await tester.pump();
    expect(voice.stopListeningCalls, 1);

    await tester.tap(find.descendant(of: find.byType(VoiceModeView), matching: find.byTooltip('End hands-free')));
    await tester.pumpAndSettle();
    expect(find.byType(VoiceModeView), findsNothing);
    expect(find.byTooltip('Hands-free'), findsOneWidget);
  });

  testWidgets('back leaves hands-free, not the chat', (tester) async {
    final api = _FakeChatApi();
    final voice = _FakeVoice();
    await _pumpChat(tester, api, voice);

    await tester.tap(find.byTooltip('Hands-free'));
    await tester.pumpAndSettle();
    await tester.binding.handlePopRoute();
    await tester.pumpAndSettle();

    expect(find.byType(VoiceModeView), findsNothing);
    expect(find.byType(ChatScreen), findsOneWidget);
  });

  testWidgets('a sentence cut off by a pause waits for the rest before it is sent', (tester) async {
    // Owner's chat b4c7pvc2, 2026-11-04: "Can you find me the tasks that..." went off on its own.
    final api = _FakeChatApi();
    final voice = _FakeVoice();
    await _pumpChat(tester, api, voice);

    await tester.tap(find.byTooltip('Hands-free'));
    await tester.pumpAndSettle();
    voice.say('Can you find me the tasks that...');
    await tester.pumpAndSettle();
    expect(api.sent, isEmpty);
    expect(voice.listenCalls, 2);

    voice.say('are due today?');
    await tester.pumpAndSettle();
    expect(api.sent, ['Can you find me the tasks that are due today?']);
  });
}
