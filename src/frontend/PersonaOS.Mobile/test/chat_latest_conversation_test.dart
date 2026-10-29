import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/screens/chat_screen.dart';
import 'package:personaos_mobile/voice_service.dart';

/// Has two stored conversations, the most recent first, as the server lists them.
class _FakeApi extends PersonaOsApi {
  _FakeApi({this.conversations = const []}) : super(serverUrl: 'http://test');

  final List<ConversationSummary> conversations;
  final opened = <String>[];

  @override
  Future<List<ConversationSummary>> getConversations() async => conversations;

  @override
  Future<ConversationDetail> getConversation(String idOrPublicId) async {
    opened.add(idOrPublicId);
    return ConversationDetail(
      id: 7,
      title: 'Groceries',
      messages: [
        ChatMessageDto(role: 'user', content: 'Remind me to buy milk.'),
        ChatMessageDto(role: 'assistant', content: 'Here is the reminder.'),
      ],
    );
  }
}

class _QuietVoice extends VoiceService {
  @override
  Future<void> release() async {}
}

Future<void> _open(WidgetTester tester, _FakeApi api) async {
  await tester.pumpWidget(
    MaterialApp(
      home: ChatScreen(api: api, assistantNickname: 'Juno', voice: _QuietVoice()),
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('the chat opens on the conversation used last', (tester) async {
    // Owner, 2026-10-11: every visit to the chat started an empty one.
    final api = _FakeApi(
      conversations: [
        ConversationSummary(
          id: 7,
          publicId: 'k3n9x2qp',
          title: 'Groceries',
          updatedAtUtc: DateTime.utc(2026, 10, 11, 15),
        ),
        ConversationSummary(id: 3, publicId: 'a1b2c3d4', title: 'Older', updatedAtUtc: DateTime.utc(2026, 10, 1)),
      ],
    );

    await _open(tester, api);

    expect(api.opened, ['k3n9x2qp']);
    expect(find.text('Remind me to buy milk.'), findsOneWidget);
  });

  testWidgets('with no conversations yet, the chat starts empty', (tester) async {
    final api = _FakeApi();

    await _open(tester, api);

    expect(api.opened, isEmpty);
    expect(find.text("What's on your mind?"), findsOneWidget);
  });
}
