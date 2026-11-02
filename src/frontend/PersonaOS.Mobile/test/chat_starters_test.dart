import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/screens/chat_screen.dart';

/// Records each message sent.
class _Api extends PersonaOsApi {
  _Api() : super(serverUrl: 'http://test');

  final sent = <String>[];

  @override
  Future<List<ConversationSummary>> getConversations() async => const [];

  @override
  Stream<ChatEvent> streamChat(String message, {int? conversationId, String? mode}) async* {
    sent.add(message);
    yield ChatEvent(type: 'start', conversationId: 1);
    yield ChatEvent(type: 'delta', text: 'Nothing planned today.');
    yield ChatEvent(type: 'done');
  }
}

void main() {
  Future<_Api> open(WidgetTester tester) async {
    final api = _Api();
    await tester.pumpWidget(
      MaterialApp(
        home: ChatScreen(api: api, assistantNickname: 'Juno'),
      ),
    );
    await tester.pumpAndSettle();
    return api;
  }

  testWidgets('a whole-question starter sends at once', (tester) async {
    final api = await open(tester);

    await tester.tap(find.byKey(const Key("starter-Today's plan")));
    await tester.pumpAndSettle();

    expect(api.sent, ["What's on my planner today?"]);
    expect(find.text('Nothing planned today.'), findsOneWidget);
  });

  testWidgets('a starter to finish fills the composer and sends nothing', (tester) async {
    final api = await open(tester);

    await tester.tap(find.byKey(const Key('starter-Remind me')));
    await tester.pumpAndSettle();

    expect(api.sent, isEmpty);
    final field = tester.widget<TextField>(find.byType(TextField));
    expect(field.controller!.text, 'Remind me to ');
    expect(field.focusNode!.hasFocus, isTrue);
  });
}
