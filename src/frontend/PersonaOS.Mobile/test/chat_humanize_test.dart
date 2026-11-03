import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/layout.dart';
import 'package:personaos_mobile/screens/chat_screen.dart';
import 'package:personaos_mobile/voice_service.dart';

/// One stored conversation with [turns] exchanges, opened as the one used last.
class _FakeApi extends PersonaOsApi {
  _FakeApi(this.turns) : super(serverUrl: 'http://test');

  final int turns;

  @override
  Future<List<ConversationSummary>> getConversations() async => [
    ConversationSummary(id: 7, publicId: 'k3n9x2qp', title: 'Plans', updatedAtUtc: DateTime.utc(2026, 11, 2)),
  ];

  @override
  Future<ConversationDetail> getConversation(String idOrPublicId) async => ConversationDetail(
    id: 7,
    title: 'Plans',
    messages: [
      for (var i = 0; i < turns; i++) ...[
        ChatMessageDto(role: 'user', content: 'Question $i'),
        ChatMessageDto(role: 'assistant', content: 'Answer $i'),
      ],
    ],
  );
}

class _QuietVoice extends VoiceService {
  @override
  Future<void> release() async {}
}

/// The message list, not the horizontal row of mode chips under it.
final _messages = find.byWidgetPredicate((w) => w is ListView && w.scrollDirection == Axis.vertical);

Future<void> _open(WidgetTester tester, int turns) async {
  await tester.pumpWidget(
    MaterialApp(
      home: ChatScreen(api: _FakeApi(turns), assistantNickname: 'Juno', voice: _QuietVoice()),
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('finished replies are written without the Orb beside them', (tester) async {
    // Owner, 2026-11-02: the Orb beside every reply was "overuse of the logo".
    await _open(tester, 2);

    expect(find.text('Answer 1'), findsOneWidget);
    expect(find.descendant(of: _messages, matching: find.byType(Orb)), findsNothing);
  });

  testWidgets('an empty chat shows one Orb: the send button is a plain circle', (tester) async {
    // Owner, 2026-11-04: "I don't like the shiny orb in two places."
    await _open(tester, 0);
    expect(find.byType(Orb), findsOneWidget);
    expect(find.byType(SendFace), findsOneWidget);
  });

  testWidgets('reading back shows Latest, which returns to the newest message', (tester) async {
    // Owner, 2026-11-02: "i hate the scroll" - new text pulled the list down while reading back.
    await _open(tester, 30);
    expect(find.byKey(const Key('to-latest')), findsNothing);

    await tester.drag(_messages, const Offset(0, 600));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('to-latest')), findsOneWidget);

    await tester.tap(find.byKey(const Key('to-latest')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('to-latest')), findsNothing);
    expect(find.text('Answer 29'), findsOneWidget);
  });
}
