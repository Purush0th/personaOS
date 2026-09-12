import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/date_utils.dart';

void main() {
  group('ConversationDetail.fromJson', () {
    test('replays a stored conversation with both sides of the exchange', () {
      final detail = ConversationDetail.fromJson({
        'id': 7,
        'publicId': 'm74gjks6',
        'title': 'Learning Rust',
        'messages': [
          {'id': 1, 'role': 'user', 'content': 'Create a goal'},
          {'id': 2, 'role': 'assistant', 'content': 'Here is a proposal'},
        ],
      });

      expect(detail.id, 7);
      expect(detail.messages, hasLength(2));
      expect(detail.messages.first.role, 'user');
      expect(detail.messages.last.content, 'Here is a proposal');
    });

    test('carries receipts and proposals stored on a message', () {
      final detail = ConversationDetail.fromJson({
        'id': 1,
        'title': 'x',
        'messages': [
          {
            'id': 9,
            'role': 'assistant',
            'content': 'Done',
            'toolActions': [
              {'tool': 'create_goal', 'summary': 'Learn Rust', 'ok': true},
            ],
            'pendingActions': [
              {
                'id': 'a1',
                'tool': 'create_reminder',
                'summary': 'Remind me',
                'status': 'pending',
              },
            ],
          },
        ],
      });

      final message = detail.messages.single;
      expect(message.toolActions, hasLength(1));
      expect(message.pendingActions!.single.isPending, isTrue);
    });

    test('survives a conversation with no messages', () {
      final detail = ConversationDetail.fromJson({'id': 2, 'title': 'Empty'});
      expect(detail.messages, isEmpty);
    });
  });

  group('InstanceSettings.fromJson', () {
    test('reads the settings the server does return', () {
      final settings = InstanceSettings.fromJson({
        'assistantNickname': 'Juno',
        'personaTemplate': 'Warm and brief',
        'aiProvider': 'openai_compatible',
        'aiModel': 'qwen2.5:latest',
        'aiBaseUrl': 'http://localhost:11434/v1',
        'timeZone': 'Asia/Kolkata',
        'features': {'goals': true, 'docs': false},
        'hasAnthropicApiKey': true,
      });

      expect(settings.assistantNickname, 'Juno');
      expect(settings.features['goals'], isTrue);
      expect(settings.features['docs'], isFalse);
      expect(settings.hasApiKey, isTrue);
    });

    test('reports no stored key when the server says so', () {
      // The key itself is never returned, so the flag is the only signal the
      // settings screen has for whether a blank field means "keep" or "none".
      final settings = InstanceSettings.fromJson({
        'assistantNickname': 'Juno',
        'hasAnthropicApiKey': false,
      });

      expect(settings.hasApiKey, isFalse);
      expect(settings.features, isEmpty);
    });
  });

  group('DocumentDto.fromJson', () {
    test('reads a document without a description', () {
      final document = DocumentDto.fromJson({
        'id': 3,
        'fileName': 'notes.pdf',
        'contentType': 'application/pdf',
        'sizeBytes': 2048,
        'description': null,
        'createdAtUtc': '2026-09-12T10:00:00Z',
      });

      expect(document.fileName, 'notes.pdf');
      expect(document.sizeBytes, 2048);
      expect(document.description, isNull);
    });
  });

  group('parseServerUtc', () {
    test('treats an undesignated server timestamp as UTC, not local', () {
      // The API writes `...Utc` fields without a trailing Z. Reading one as
      // local backdates it by the offset — a file uploaded a second ago was
      // shown as "5h ago" on a UTC+5:30 phone.
      final parsed = parseServerUtc('2026-09-12T17:30:30.1007807');

      expect(parsed.isUtc, isTrue);
      expect(parsed.hour, 17);
      expect(parsed.minute, 30);
    });

    test('leaves an explicit zone alone', () {
      expect(parseServerUtc('2026-09-12T17:30:30Z').hour, 17);
      expect(parseServerUtc('2026-09-12T23:00:00+05:30').toUtc().hour, 17);
    });

    test('a freshly-stamped document reads as just now', () {
      final serverStyle =
          DateTime.now().toUtc().toIso8601String().replaceAll('Z', '');
      expect(relativeTime(parseServerUtc(serverStyle)), 'just now');
    });
  });

  group('relativeTime', () {
    test('compares in UTC, so a fresh timestamp is not reported hours old', () {
      // The bug this guards against: comparing a UTC instant against a local
      // `now` reports the timezone offset as elapsed time.
      expect(relativeTime(DateTime.now().toUtc()), 'just now');
    });

    test('counts minutes, hours and days', () {
      final now = DateTime.now().toUtc();
      expect(relativeTime(now.subtract(const Duration(minutes: 5))), '5m ago');
      expect(relativeTime(now.subtract(const Duration(hours: 3))), '3h ago');
      expect(relativeTime(now.subtract(const Duration(days: 2))), '2d ago');
    });

    test('falls back to a date once older than a week', () {
      final old = DateTime.now().toUtc().subtract(const Duration(days: 30));
      expect(relativeTime(old), localYmd(old.toLocal()));
    });
  });
}
