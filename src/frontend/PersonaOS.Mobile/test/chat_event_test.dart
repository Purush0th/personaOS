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

    test('reads proposed changes awaiting confirmation', () {
      // Without this the mobile client shows the reply and no way to confirm, which
      // leaves the assistant unable to change anything at all on a phone.
      final event = ChatEvent.fromJson({
        'type': 'done',
        'pending': [
          {
            'id': 'nr6wbqw4',
            'tool': 'create_goal',
            'summary': 'Create goal “Learn Rust” — month · 2026-09-01 · top-level',
            'status': 'pending',
          },
        ],
      });

      final proposal = event.pending!.single;
      expect(proposal.id, 'nr6wbqw4');
      expect(proposal.isPending, isTrue);
      expect(proposal.summary, contains('top-level'));
      // Nothing ran, so there is no receipt to show alongside it.
      expect(event.actions, isNull);
    });

    test('reads a confirmed proposal with its outcome', () {
      final event = ChatEvent.fromJson({
        'type': 'done',
        'pending': [
          {
            'id': 'nr6wbqw4',
            'tool': 'create_goal',
            'summary': 'Create goal “Learn Rust”',
            'status': 'confirmed',
            'resultSummary': 'Learn Rust — month · 2026-09-01 · active',
            'resultOk': true,
          },
        ],
      });

      final proposal = event.pending!.single;
      expect(proposal.isPending, isFalse);
      expect(proposal.resultOk, isTrue);
      expect(proposal.resultSummary, contains('Learn Rust'));
    });

    test('treats a discarded proposal as resolved', () {
      final event = ChatEvent.fromJson({
        'type': 'done',
        'pending': [
          {'id': 'a8k8jg5c', 'tool': 'create_goal', 'summary': 'Create goal', 'status': 'discarded'},
        ],
      });

      expect(event.pending!.single.isPending, isFalse);
    });

    test('leaves pending null when nothing was proposed', () {
      expect(ChatEvent.fromJson({'type': 'done'}).pending, isNull);
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
