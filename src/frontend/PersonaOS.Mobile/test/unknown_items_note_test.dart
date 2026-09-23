import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/main.dart';
import 'package:personaos_mobile/screens/chat_screen.dart';

void main() {
  group('wording', () {
    test('says nothing when every item exists', () {
      expect(UnknownItemsNote.messageFor([]), isNull);
    });

    test('matches the web app, singular and plural', () {
      expect(UnknownItemsNote.messageFor(['TASK-6']),
          'This reply mentions TASK-6, which does not exist. Check before relying on it.');
      expect(UnknownItemsNote.messageFor(['TASK-6', 'GOAL-9', 'SPRINT-4']),
          'This reply mentions TASK-6, GOAL-9 and SPRINT-4, which do not exist. Check before relying on it.');
    });
  });

  // Same constraint as the other note: inside a bubble capped at 320 wide, a Row that cannot size
  // itself draws nothing in a release build.
  for (final (name, theme) in [('light', personaOsTheme), ('dark', personaOsDarkTheme)]) {
    testWidgets('shows the whole note inside a narrow bubble ($name)', (tester) async {
      await tester.pumpWidget(MaterialApp(
        theme: theme,
        home: Scaffold(
          body: Align(
            alignment: Alignment.centerLeft,
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 320),
              child: const Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text('TASK-6 is already done.'),
                  UnknownItemsNote(keys: ['TASK-6']),
                ],
              ),
            ),
          ),
        ),
      ));

      expect(tester.takeException(), isNull);
      expect(find.text(UnknownItemsNote.messageFor(['TASK-6'])!), findsOneWidget);
      expect(tester.getSize(find.byType(UnknownItemsNote)).width, lessThanOrEqualTo(320));
    });
  }
}
