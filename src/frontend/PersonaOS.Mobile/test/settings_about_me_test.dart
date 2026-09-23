import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/screens/settings_screen.dart';

/// "About you" on the phone: loaded with the settings, and written back only when it changed,
/// through its own endpoint.
void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  // The screen asks the secure store whether a sign-in is saved; there is none in a test.
  setUp(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger.setMockMethodCallHandler(
        const MethodChannel('plugins.it_nomads.com/flutter_secure_storage'), (call) async => null);
  });

  Future<List<http.Request>> run(WidgetTester tester, Future<void> Function() interact) async {
    final requests = <http.Request>[];
    final server = MockClient((request) async {
      requests.add(request);
      final body = switch ((request.method, request.url.path)) {
        ('GET', '/api/setup') => {
            'assistantNickname': 'Juno',
            'personaTemplate': '',
            'aiProvider': 'ollama',
            'aiModel': 'qwen2.5:3b-instruct',
            'aiBaseUrl': 'http://localhost:11434',
            'timeZone': 'Asia/Kolkata',
            'features': <String, bool>{},
            'hasAnthropicApiKey': false,
          },
        ('GET', '/api/profile') => {'aboutMe': 'Vegetarian.', 'updatedAtUtc': '2026-09-23T00:00:00Z'},
        _ => {'message': 'ok'},
      };
      return http.Response(jsonEncode(body), 200, headers: {'content-type': 'application/json'});
    });

    await http.runWithClient(() async {
      await tester.pumpWidget(MaterialApp(home: SettingsScreen(api: PersonaOsApi(serverUrl: 'http://test'))));
      await tester.pumpAndSettle();
      await interact();
    }, () => server);
    return requests;
  }

  Future<void> save(WidgetTester tester) async {
    // The form is a lazily built list: the button exists only once scrolled to.
    final button = find.text('Save settings');
    await tester.scrollUntilVisible(button, 300, scrollable: find.byType(Scrollable).first);
    await tester.tap(button);
    await tester.pumpAndSettle();
  }

  testWidgets('loads what the assistant knows about the user', (tester) async {
    await run(tester, () async {
      expect(find.widgetWithText(TextField, 'Vegetarian.'), findsOneWidget);
    });
  });

  testWidgets('writes it back when it changed', (tester) async {
    final requests = await run(tester, () async {
      await tester.enterText(find.widgetWithText(TextField, 'Vegetarian.'), 'Vegetarian. Lives in Chennai.');
      await save(tester);
    });

    final put = requests.singleWhere((r) => r.method == 'PUT' && r.url.path == '/api/profile');
    expect(jsonDecode(put.body), {'aboutMe': 'Vegetarian. Lives in Chennai.'});
  });

  testWidgets('leaves it alone when only other settings changed', (tester) async {
    final requests = await run(tester, () => save(tester));

    expect(requests.where((r) => r.method == 'PUT' && r.url.path == '/api/setup'), hasLength(1));
    expect(requests.where((r) => r.url.path == '/api/profile' && r.method == 'PUT'), isEmpty);
  });
}
