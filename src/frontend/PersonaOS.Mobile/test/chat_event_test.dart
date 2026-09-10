import 'package:flutter_test/flutter_test.dart';

import 'package:personaos_mobile/api/personaos_api.dart';

/// Parsing of the chat SSE contract. Tool receipts are the app's record of what
/// actually ran — if they fail to parse the mobile client silently loses the only
/// evidence a user has that a reply's claims are true.
void main() {
  group('ChatEvent.fromJson', () {
    test('reads tool receipts from a done event', () {
      final event = ChatEvent.fromJson({
        'type': 'done',
        'conversationId': 7,
        'actions': [
          {
            'tool': 'create_reminder',
            'ok': true,
            'summary': 'Call the bank — 2026-09-06 18:00 · pending',
          },
        ],
      });

      expect(event.type, 'done');
      expect(event.conversationId, 7);
      expect(event.actions, hasLength(1));
      expect(event.actions!.single.tool, 'create_reminder');
      expect(event.actions!.single.ok, isTrue);
      // The stored local time must survive: misreporting it is what receipts exist to catch.
      expect(event.actions!.single.summary, contains('18:00'));
    });

    test('marks a failed tool as not ok', () {
      final event = ChatEvent.fromJson({
        'type': 'done',
        'actions': [
          {'tool': 'cancel_reminder', 'ok': false, 'summary': 'Reminder 99 does not exist.'},
        ],
      });

      expect(event.actions!.single.ok, isFalse);
      expect(event.actions!.single.summary, 'Reminder 99 does not exist.');
    });

    test('tolerates a receipt with no summary', () {
      final event = ChatEvent.fromJson({
        'type': 'done',
        'actions': [
          {'tool': 'get_goals', 'ok': true},
        ],
      });

      expect(event.actions!.single.summary, isNull);
      expect(event.actions!.single.tool, 'get_goals');
    });

    test('leaves actions null when no tool ran', () {
      final event = ChatEvent.fromJson({'type': 'done', 'conversationId': 1});

      expect(event.actions, isNull);
    });

    test('reads the corrected reply text carried on done', () {
      // Sent when the server stripped a tool call the model wrote as prose; the
      // client must replace the streamed text with this, including before read-back.
      final event = ChatEvent.fromJson({'type': 'done', 'text': 'I will check.'});

      expect(event.text, 'I will check.');
    });

    test('still parses the ordinary delta and error shapes', () {
      expect(ChatEvent.fromJson({'type': 'delta', 'text': 'Hi'}).text, 'Hi');

      final error = ChatEvent.fromJson({'type': 'error', 'error': 'Something went wrong.'});
      expect(error.error, 'Something went wrong.');
      expect(error.actions, isNull);
    });
  });
}
