import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/screens/chat_screen.dart';

/// The waiting bubble was a still "…", which looked the same as a reply that had stopped.
void main() {
  List<Offset> dots(WidgetTester tester) =>
      tester.widgetList<Transform>(find.descendant(of: find.byType(TypingDots), matching: find.byType(Transform)))
          .map((t) => Offset(t.transform.getTranslation().x, t.transform.getTranslation().y))
          .toList();

  testWidgets('the dots move while a reply is on its way', (tester) async {
    await tester.pumpWidget(const MaterialApp(home: Scaffold(body: TypingDots(color: Colors.black))));
    await tester.pump(const Duration(milliseconds: 150));
    final first = dots(tester);
    await tester.pump(const Duration(milliseconds: 300));

    expect(dots(tester), isNot(first));
  });

  testWidgets('with animations turned off on the phone they stay still', (tester) async {
    await tester.pumpWidget(const MaterialApp(
      home: MediaQuery(data: MediaQueryData(disableAnimations: true), child: Scaffold(body: TypingDots(color: Colors.black))),
    ));
    final first = dots(tester);
    await tester.pump(const Duration(milliseconds: 300));

    expect(dots(tester), first);
  });
}
