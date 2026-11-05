import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/layout.dart';
import 'package:personaos_mobile/main.dart';
import 'package:personaos_mobile/orb_bar.dart';
import 'package:personaos_mobile/screens/board_screen.dart';
import 'package:personaos_mobile/screens/login_screen.dart';
import 'package:shared_preferences/shared_preferences.dart';

const _server = 'http://server';

MockClient _fakeServer(List<String> features) => MockClient((request) async {
  final path = request.url.path;
  if (path == '/api/branding') {
    return http.Response(
      jsonEncode({'assistantNickname': 'Friday', 'isConfigured': true, 'enabledFeatures': features}),
      200,
    );
  }
  if (path == '/api/auth/login') return http.Response(jsonEncode({'accessToken': 't'}), 200);
  if (path == '/api/board') {
    return http.Response(
      jsonEncode({'sprint': null, 'todo': <Object>[], 'inProgress': <Object>[], 'done': <Object>[]}),
      200,
    );
  }
  if (path == '/api/planner') return http.Response(jsonEncode({'items': <Object>[]}), 200);
  if (path == '/api/reminders') return http.Response('[]', 200);
  return http.Response('[]', 200);
});

Future<void> _pump(
  WidgetTester tester,
  Future<void> Function() body, {
  List<String> features = const ['board', 'planner', 'reminders', 'notes', 'memory', 'voice'],
  bool signedIn = true,
}) async {
  SharedPreferences.setMockInitialValues({});
  final api = PersonaOsApi(serverUrl: _server);
  await http.runWithClient(() async {
    if (signedIn) await api.login('owner', 'secret');
    await tester.pumpWidget(
      MaterialApp(
        theme: personaOsTheme,
        home: HomeScreen(serverUrl: _server, onForgetServer: () {}, api: api),
      ),
    );
    await _settle(tester);
    await body();
  }, () => _fakeServer(features));
}

Future<void> _settle(WidgetTester tester) async {
  for (var i = 0; i < 6; i++) {
    await tester.pump(const Duration(milliseconds: 100));
  }
}

/// Whether the bar marks [key]'s place as the one showing, as a screen reader hears it.
bool _selected(WidgetTester tester, String key) =>
    tester
        .widget<Semantics>(find.descendant(of: find.byKey(Key(key)), matching: find.byType(Semantics)).first)
        .properties
        .selected ==
    true;

void main() {
  testWidgets('the bar holds Home, Board, the Orb, Planner and More', (tester) async {
    await _pump(tester, () async {
      expect(find.byType(OrbBar), findsOneWidget);
      for (final key in ['bar-home', 'bar-board', 'bar-orb', 'bar-planner', 'bar-more']) {
        expect(find.byKey(Key(key)), findsOneWidget, reason: key);
      }
      expect(find.bySemanticsLabel('Chat with Friday'), findsOneWidget);
      // One Orb on home: the bar's, not one over the greeting as well.
      expect(find.byType(Orb), findsOneWidget);
      expect(_selected(tester, 'bar-home'), isTrue);
    });
  });

  testWidgets('a place opens in place, and Back returns home', (tester) async {
    await _pump(tester, () async {
      await tester.tap(find.byKey(const Key('bar-board')));
      await _settle(tester);
      expect(find.byType(BoardScreen), findsOneWidget);
      expect(_selected(tester, 'bar-board'), isTrue);
      // Still the same page under the bar, not a pushed route.
      expect(find.byType(OrbBar), findsOneWidget);

      final popped = await tester.binding.handlePopRoute();
      await _settle(tester);
      expect(popped, isTrue);
      expect(_selected(tester, 'bar-home'), isTrue);
    });
  });

  testWidgets('with Board off, the next module takes its slot', (tester) async {
    await _pump(tester, features: const ['planner', 'reminders', 'notes'], () async {
      expect(find.byKey(const Key('bar-board')), findsNothing);
      expect(find.byKey(const Key('bar-planner')), findsOneWidget);
      expect(find.byKey(const Key('bar-reminders')), findsOneWidget);
    });
  });

  testWidgets('More lists what the bar has no room for', (tester) async {
    await _pump(tester, () async {
      await tester.tap(find.byKey(const Key('bar-more')));
      await _settle(tester);
      final sheet = find.byKey(const Key('more-sheet'));
      expect(sheet, findsOneWidget);
      for (final title in ['Reminders', 'Notes', 'Memories', 'Settings', 'About and updates']) {
        expect(
          find.descendant(of: sheet, matching: find.text(title)),
          findsOneWidget,
          reason: title,
        );
      }
      // Board and Planner are on the bar already.
      expect(find.descendant(of: sheet, matching: find.text('Board')), findsNothing);
    });
  });

  testWidgets('before sign-in, a place asks to sign in first', (tester) async {
    await _pump(tester, signedIn: false, () async {
      await tester.tap(find.byKey(const Key('bar-board')));
      await _settle(tester);
      expect(find.byType(LoginScreen), findsOneWidget);
    });
  });
}
