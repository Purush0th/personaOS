import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/item_links.dart';

/// Keys in a reply become links to the item, so a reply that names TASK-7 opens it in one tap.
void main() {
  group('linkItemKeys', () {
    test('links task and goal keys in prose, in brackets and in lists', () {
      expect(linkItemKeys('Move TASK-7 to done.'), 'Move [TASK-7](personaos:TASK-7) to done.');
      expect(linkItemKeys('(GOAL-12) is due'), '([GOAL-12](personaos:GOAL-12)) is due');
      expect(linkItemKeys('- TASK-1: File taxes'), '- [TASK-1](personaos:TASK-1): File taxes');
    });

    test('leaves code, existing links, longer words and unknown keys as written', () {
      expect(linkItemKeys('Run `TASK-7` here'), 'Run `TASK-7` here');
      expect(linkItemKeys('[TASK-7](https://x/TASK-7)'), '[TASK-7](https://x/TASK-7)');
      expect(linkItemKeys('SUBTASK-7 and TASK-7a and TASK-7-2'), 'SUBTASK-7 and TASK-7a and TASK-7-2');
      expect(linkItemKeys('TASK-9 does not exist', unknown: ['TASK-9']), 'TASK-9 does not exist');
    });

    test('a longer key is never cut short inside a link', () {
      expect(linkItemKeys('[TASK-12](x)'), '[TASK-12](x)');
    });
  });

  group('itemKeyFromLink', () {
    test('reads only links to items', () {
      expect(itemKeyFromLink('personaos:TASK-7'), 'TASK-7');
      expect(itemKeyFromLink('personaos:GOAL-4'), 'GOAL-4');
      expect(itemKeyFromLink('https://example.com'), isNull);
      expect(itemKeyFromLink('personaos:TASK-7; drop'), isNull);
      expect(itemKeyFromLink(null), isNull);
    });
  });
}
