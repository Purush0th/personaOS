import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/main.dart';

/// Guards the shared theme against layouts it silently breaks.
///
/// The filled-button theme once used `Size.fromHeight(52)`, which also sets an infinite minimum
/// width. Buttons in full-width columns looked fine, so it passed every screen that was checked —
/// but inside a Row an infinite-width child cannot be laid out, and a release build draws nothing
/// in its place. The Confirm button on proposal cards vanished, and so did the primary action of
/// every dialog. The analyzer cannot see this; only laying the widgets out does.
void main() {
  for (final (name, theme) in [('light', personaOsTheme), ('dark', personaOsDarkTheme)]) {
    group('$name theme', () {
      testWidgets('a filled button lays out beside another button in a Row', (tester) async {
        await tester.pumpWidget(MaterialApp(
          theme: theme,
          home: Scaffold(
            body: Row(
              mainAxisAlignment: MainAxisAlignment.end,
              children: [
                TextButton(onPressed: () {}, child: const Text('Discard')),
                FilledButton(onPressed: () {}, child: const Text('Confirm')),
              ],
            ),
          ),
        ));

        expect(tester.takeException(), isNull);
        expect(find.text('Confirm'), findsOneWidget);
        expect(tester.getSize(find.byType(FilledButton)).width, lessThan(400));
      });

      testWidgets('a dialog shows its filled primary action', (tester) async {
        await tester.pumpWidget(MaterialApp(
          theme: theme,
          home: Builder(
            builder: (context) => Scaffold(
              body: TextButton(
                onPressed: () => showDialog<void>(
                  context: context,
                  builder: (_) => AlertDialog(
                    title: const Text('Delete conversation?'),
                    actions: [
                      TextButton(onPressed: () {}, child: const Text('Cancel')),
                      FilledButton(onPressed: () {}, child: const Text('Delete')),
                    ],
                  ),
                ),
                child: const Text('open'),
              ),
            ),
          ),
        ));

        await tester.tap(find.text('open'));
        await tester.pumpAndSettle();

        expect(tester.takeException(), isNull);
        expect(find.text('Delete'), findsOneWidget);
      });
    });
  }
}
