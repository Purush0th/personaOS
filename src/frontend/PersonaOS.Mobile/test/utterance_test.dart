import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/utterance.dart';

void main() {
  test('a sentence cut off mid-thought sounds unfinished', () {
    expect(soundsUnfinished('Can you find me the tasks that...'), isTrue);
    expect(soundsUnfinished('Remind me to call the'), isTrue);
    expect(soundsUnfinished('Add milk and.'), isTrue);
    expect(soundsUnfinished("What's my plan today?"), isFalse);
    expect(soundsUnfinished('Start the sprint.'), isFalse);
    expect(soundsUnfinished(''), isFalse);
  });

  test('the parts are joined into one sentence', () {
    expect(
      joinUtterance('Can you find me the tasks that...', 'Are due today?'),
      'Can you find me the tasks that Are due today?',
    );
    expect(joinUtterance('', 'Hello'), 'Hello');
    expect(joinUtterance('Remind me to call', 'Mum at six'), 'Remind me to call Mum at six');
  });
}
