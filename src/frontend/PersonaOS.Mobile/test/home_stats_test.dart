import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/main.dart';
import 'package:shared_preferences/shared_preferences.dart';

const _server = 'http://server';

/// A server with one planner event left of two today, a sprint with one of four tasks done, and a
/// reminder due in 25 minutes.
MockClient _fakeServer({bool board = true}) {
  final now = DateTime.now().toUtc();
  final due = now.add(const Duration(minutes: 25)).toIso8601String();
  Map<String, dynamic> task(int id, String column) => {
    'id': id,
    'key': 'TASK-$id',
    'title': 'Task $id',
    'column': column,
  };
  Map<String, dynamic> event(int id, String status) => {
    'id': id,
    'title': 'Event $id',
    'date': '2026-10-30',
    'scheduledTime': null,
    'status': status,
    'goalTitle': null,
  };
  return MockClient((request) async {
    final path = request.url.path;
    if (path == '/api/branding') {
      return http.Response(
        jsonEncode({
          'assistantNickname': 'Friday',
          'isConfigured': true,
          'enabledFeatures': ['planner', 'reminders', if (board) 'board'],
        }),
        200,
      );
    }
    if (path == '/api/auth/login') return http.Response(jsonEncode({'accessToken': 't'}), 200);
    if (path == '/api/planner') {
      return http.Response(
        jsonEncode({
          'items': [event(1, 'done'), event(2, 'planned')],
        }),
        200,
      );
    }
    if (path == '/api/board') {
      return http.Response(
        jsonEncode({
          'sprint': {
            'id': 1,
            'number': 1,
            'status': 'active',
            'startsAtUtc': '2026-10-27T00:00:00',
            'endsAtUtc': '2026-11-02T00:00:00',
          },
          'todo': [task(1, 'todo'), task(2, 'todo')],
          'inProgress': [task(3, 'inProgress')],
          'done': [task(4, 'done')],
        }),
        200,
      );
    }
    if (path == '/api/reminders') {
      return http.Response(
        jsonEncode([
          {'id': 1, 'message': 'Call the bank', 'dueAtLocal': '2026-10-30T10:00', 'dueAtUtc': due, 'status': 'pending'},
        ]),
        200,
      );
    }
    return http.Response('', 404);
  });
}

Future<void> _pumpHome(WidgetTester tester, MockClient client, {required bool signedIn}) async {
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
    for (var i = 0; i < 5; i++) {
      await tester.pump(const Duration(milliseconds: 50));
    }
  }, () => client);
}

void main() {
  testWidgets('home shows today, the sprint and the next reminder once signed in', (tester) async {
    await _pumpHome(tester, _fakeServer(), signedIn: true);

    expect(find.text("What's on your mind?"), findsOneWidget);
    expect(find.bySemanticsLabel(RegExp(r'^Planned today: 1 left\. 2 in all$')), findsOneWidget);
    expect(find.bySemanticsLabel(RegExp(r'^Sprint: 25 %\. 1 of 4 done$')), findsOneWidget);
    expect(find.bySemanticsLabel(RegExp(r'^Next reminder: 2[45] min\. Call the bank$')), findsOneWidget);
  });

  testWidgets('a module that is off leaves its card out', (tester) async {
    await _pumpHome(tester, _fakeServer(board: false), signedIn: true);

    expect(find.byKey(const Key('stat-today')), findsOneWidget);
    expect(find.byKey(const Key('stat-sprint')), findsNothing);
  });

  testWidgets('before sign-in there are no cards, only the way in to the chat', (tester) async {
    await _pumpHome(tester, _fakeServer(), signedIn: false);

    expect(find.byKey(const Key('home-ask')), findsOneWidget);
    expect(find.text('Today'), findsNothing);
  });
}
