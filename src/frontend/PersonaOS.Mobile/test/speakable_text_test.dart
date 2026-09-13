import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/voice_service.dart';

void main() {
  group('speakableText', () {
    test('drops emphasis markers but keeps the words', () {
      // The bug this guards: read back verbatim, "**Learn Rust**" was spoken as
      // "asterisk asterisk Learn Rust asterisk asterisk".
      expect(speakableText('**Learn Rust**'), 'Learn Rust');
      expect(speakableText('_really_ good'), 'really good');
      expect(speakableText('`get_goals` ran'), 'get_goals ran');
    });

    test('keeps underscores inside identifiers', () {
      // Stripping every underscore spoke "get_goals" as "getgoals", and the
      // assistant names its tools constantly.
      expect(speakableText('The tool get_goals returned 3 items'),
          'The tool get_goals returned 3 items');
      expect(speakableText('update_goal_status failed'),
          'update_goal_status failed');
    });

    test('keeps numbered list numbers, drops bullet markers', () {
      final spoken = speakableText('1. **Learn Rust**\n- Progress: 0%');
      expect(spoken, contains('1. Learn Rust'));
      expect(spoken, contains('Progress: 0%'));
      expect(spoken, isNot(contains('-')));
    });

    test('reads a link label without the URL', () {
      expect(
        speakableText('See [the docs](https://example.com/a/b) for more'),
        'See the docs for more',
      );
    });

    test('drops a fenced code block rather than spelling it out', () {
      final spoken = speakableText('Here:\n```json\n{"a": 1}\n```\nDone');
      expect(spoken, contains('Here:'));
      expect(spoken, contains('Done'));
      expect(spoken, isNot(contains('json')));
      expect(spoken, isNot(contains('{')));
    });

    test('strips heading and quote markers', () {
      expect(speakableText('## Your goals'), 'Your goals');
      expect(speakableText('> quoted line'), 'quoted line');
    });

    test('leaves ordinary prose untouched', () {
      const plain = 'Here is a quick review of your current goals:';
      expect(speakableText(plain), plain);
    });

    test('a formatted reply survives as readable sentences', () {
      final spoken = speakableText(
        'Here\'s a quick review:\n\n'
        '1. **Learn Rust**\n'
        '   - **Progress:** 0%\n\n'
        'Would you like to update it?',
      );

      expect(spoken, contains('Here\'s a quick review:'));
      expect(spoken, contains('1. Learn Rust'));
      expect(spoken, contains('Progress: 0%'));
      expect(spoken, contains('Would you like to update it?'));
      expect(spoken, isNot(contains('*')));
    });
  });
}
