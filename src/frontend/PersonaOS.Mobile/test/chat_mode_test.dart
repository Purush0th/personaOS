import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/screens/chat_screen.dart';

/// Records the mode each message was sent in.
class _Api extends PersonaOsApi {
  _Api() : super(serverUrl: 'http://test');

  final sentModes = <String?>[];

  @override
  Stream<ChatEvent> streamChat(String message, {int? conversationId, String? mode}) async* {
    sentModes.add(mode);
    yield ChatEvent(type: 'start', conversationId: 1);
    yield ChatEvent(type: 'delta', text: 'Here are some ideas.');
    yield ChatEvent(type: 'done');
  }
}

void main() {
  Future<_Api> open(WidgetTester tester) async {
    final api = _Api();
    await tester.pumpWidget(MaterialApp(
      home: ChatScreen(api: api, assistantNickname: 'Juno', voiceEnabled: false),
    ));
    await tester.pumpAndSettle();
    return api;
  }

  testWidgets('starts in Chat and says what it does', (tester) async {
    await open(tester);

    final chat = tester.widget<ChoiceChip>(find.widgetWithText(ChoiceChip, 'Chat'));
    expect(chat.selected, isTrue);
    expect(find.text('Answer and discuss; changes come as cards.'), findsOneWidget);
  });

  testWidgets('sends each message in the mode picked', (tester) async {
    final api = await open(tester);

    await tester.tap(find.widgetWithText(ChoiceChip, 'Brainstorm'));
    await tester.pumpAndSettle();
    expect(find.text('Explore options. Nothing is changed.'), findsOneWidget);

    await tester.enterText(find.byType(TextField), 'ideas for next quarter?');
    await tester.tap(find.byIcon(Icons.send));
    await tester.pumpAndSettle();

    expect(api.sentModes, ['brainstorm']);
  });

  testWidgets('a new chat starts in Chat again', (tester) async {
    final api = await open(tester);

    await tester.tap(find.widgetWithText(ChoiceChip, 'Reflect'));
    await tester.enterText(find.byType(TextField), 'how did the week go?');
    await tester.tap(find.byIcon(Icons.send));
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('New chat'));
    await tester.pumpAndSettle();

    expect(api.sentModes, ['reflect']);
    expect(tester.widget<ChoiceChip>(find.widgetWithText(ChoiceChip, 'Chat')).selected, isTrue);
  });
}
