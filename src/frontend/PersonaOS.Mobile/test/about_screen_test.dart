import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:personaos_mobile/screens/about_screen.dart';
import 'package:personaos_mobile/update_check.dart';
import 'package:shared_preferences/shared_preferences.dart';

const _apk = 'https://github.com/o/r/releases/download/mobile-v0.1.0-alpha.12/personaos-app-v0.1.0-alpha.12.apk';

/// The server's branding and GitHub's release list, as About reads them: the newest server
/// release ("v…") and the newest app release ("mobile-v…"), both 0.1.0-alpha.12.
MockClient _client({String? server = '0.1.0-alpha.12', String? minApp, bool Function()? serverDown}) {
  Map<String, dynamic> release(String tag, {bool apk = false}) => {
    'tag_name': tag,
    'name': 'PersonaOS $tag',
    'html_url': 'https://github.com/o/r/releases/tag/$tag',
    'prerelease': true,
    'assets': [
      if (apk) {'name': 'personaos-app-$tag.apk', 'browser_download_url': _apk},
    ],
  };
  return MockClient((request) async {
    if (request.url.host == 'api.github.com') {
      return http.Response(jsonEncode([release('v0.1.0-alpha.12'), release('mobile-v0.1.0-alpha.12', apk: true)]), 200);
    }
    if (serverDown?.call() ?? false) throw http.ClientException('No route to host');
    return http.Response(jsonEncode({'apiVersion': server, 'repository': 'o/r', 'minSupportedClient': minApp}), 200);
  });
}

Future<void> _open(WidgetTester tester, {required String app, required http.Client client}) async {
  await tester.pumpWidget(
    MaterialApp(
      home: AboutScreen(serverUrl: 'http://server:8080', app: app, client: client),
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  setUp(() => SharedPreferences.setMockInitialValues({}));

  testWidgets('shows both versions and offers a newer app with its Update button', (tester) async {
    await _open(tester, app: '0.1.0-alpha.11', client: _client());

    expect(find.text('0.1.0-alpha.11'), findsOneWidget);
    expect(find.text('0.1.0-alpha.12'), findsOneWidget);
    expect(find.text('App 0.1.0-alpha.12 is available'), findsOneWidget);
    expect(find.byKey(const Key('update-install')), findsOneWidget);
    expect(find.text('The server is up to date'), findsOneWidget);
  });

  testWidgets('a version dismissed on the home screen is still offered here', (tester) async {
    // The home notice hides a dismissed version; About is where to find it again.
    await dismissUpdate(
      AvailableUpdate(version: '0.1.0-alpha.12', name: 'x', notesUrl: 'x', forApp: true, appDownloadUrl: _apk),
    );

    await _open(tester, app: '0.1.0-alpha.11', client: _client());

    expect(find.byKey(const Key('update-install')), findsOneWidget);
  });

  testWidgets('says both are up to date, with no Update button', (tester) async {
    await _open(tester, app: '0.1.0-alpha.12', client: _client());

    expect(find.text('This app is up to date'), findsOneWidget);
    expect(find.text('The server is up to date'), findsOneWidget);
    expect(find.byKey(const Key('update-install')), findsNothing);
  });

  testWidgets('a server behind the release is told where it updates', (tester) async {
    await _open(
      tester,
      app: '0.1.0-alpha.12',
      client: _client(server: '0.1.0-alpha.11'),
    );

    expect(find.text('Server PersonaOS v0.1.0-alpha.12 is available'), findsOneWidget);
    expect(find.byKey(const Key('update-install')), findsNothing);
  });

  testWidgets('an unreachable server is reported, and Check for updates tries again', (tester) async {
    var down = true;
    final client = _client(serverDown: () => down);

    await _open(tester, app: '0.1.0-alpha.11', client: client);
    expect(find.text('Not reachable'), findsOneWidget);
    expect(find.byKey(const Key('about-check-error')), findsOneWidget);

    down = false;
    await tester.ensureVisible(find.byKey(const Key('about-check')));
    await tester.tap(find.byKey(const Key('about-check')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('about-check-error')), findsNothing);
    expect(find.byKey(const Key('update-install')), findsOneWidget);
  });

  testWidgets('says when this app is older than the server supports', (tester) async {
    await _open(
      tester,
      app: '0.1.0-alpha.11',
      client: _client(minApp: '0.1.0-alpha.12'),
    );

    expect(find.text('0.1.0-alpha.11, too old for this server: it needs 0.1.0-alpha.12 or newer'), findsOneWidget);
    expect(find.byKey(const Key('update-install')), findsOneWidget);
  });

  testWidgets('a development build offers no app update', (tester) async {
    await _open(tester, app: '0.0.0-dev', client: _client());

    expect(find.text('Development build'), findsOneWidget);
    expect(find.byKey(const Key('update-install')), findsNothing);
  });
}
