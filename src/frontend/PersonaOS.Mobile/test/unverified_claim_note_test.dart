import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/main.dart';
import 'package:personaos_mobile/screens/chat_screen.dart';

/// The warning sits inside a reply bubble: a Column capped at 320 wide. A Row child that cannot
/// size itself there draws nothing in a release build — the Confirm button vanished that way — so
/// lay it out the way the bubble does and check the whole message is on screen.
void main() {
  for (final (name, theme) in [('light', personaOsTheme), ('dark', personaOsDarkTheme)]) {
    testWidgets('shows the full warning inside a narrow bubble ($name)', (tester) async {
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
                  Text("I've set a reminder to submit the report at 19:00 today."),
                  UnverifiedClaimNote(),
                ],
              ),
            ),
          ),
        ),
      ));

      expect(tester.takeException(), isNull);
      expect(find.text(UnverifiedClaimNote.message), findsOneWidget);
      expect(tester.getSize(find.byType(UnverifiedClaimNote)).width, lessThanOrEqualTo(320));
      expect(UnverifiedClaimNote.message, contains('nothing was saved'));
    });
  }
}
