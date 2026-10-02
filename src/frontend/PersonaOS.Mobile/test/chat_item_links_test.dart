import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/screens/chat_screen.dart';

/// A reply naming a task: the key is a link, and the link opens the task's quick view.
class _Api extends PersonaOsApi {
  _Api() : super(serverUrl: 'http://test');

  final opened = <String>[];

  @override
  Stream<ChatEvent> streamChat(String message, {int? conversationId, String? mode}) async* {
    yield ChatEvent(type: 'start', conversationId: 1);
    yield ChatEvent(type: 'delta', text: 'TASK-7 is still to do.');
    yield ChatEvent(type: 'done');
  }

  @override
  Future<BoardTask> getTask(String key) async {
    opened.add(key);
    return BoardTask.fromJson({
      'id': 7,
      'key': key,
      'title': 'File taxes',
      'column': 'todo',
      'sprintKey': 'SPRINT-2',
      'carryOverCount': 0,
      'addedMidSprint': false,
    });
  }
}

void main() {
  testWidgets('a task key in a reply opens the task', (tester) async {
    final api = _Api();
    await tester.pumpWidget(MaterialApp(home: ChatScreen(api: api, assistantNickname: 'Juno', voiceEnabled: false)));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField), 'what about taxes?');
    await tester.tap(find.byIcon(Icons.send));
    await tester.pumpAndSettle();

    // The reply starts with the key, so its first characters are the link.
    final reply = find.textContaining('is still to do.', findRichText: true);
    expect(reply, findsOneWidget);
    await tester.tapAt(tester.getTopLeft(reply) + const Offset(12, 8));
    await tester.pumpAndSettle();

    expect(api.opened, ['TASK-7']);
    expect(find.byKey(const Key('task-quick-view-TASK-7')), findsOneWidget);
  });
}
