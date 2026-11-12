import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:personaos_mobile/api/personaos_api.dart';

/// The server's error code must reach the app: without it a scope change is never asked about,
/// and adding a task to the running sprint just fails.
void main() {
  final scopeChange = jsonEncode({
    'error': 'SPRINT-2 is running, so adding this task is a scope change.',
    'code': 'scope_change_unacknowledged',
  });

  test('creating a task in the running sprint carries the scope-change code', () async {
    final error = await http.runWithClient(() async {
      try {
        await PersonaOsApi(serverUrl: 'http://test').createTask(title: 'Write docs', sprintKey: 'SPRINT-2');
        return null;
      } on ApiException catch (e) {
        return e;
      }
    }, () => MockClient((_) async => http.Response(scopeChange, 400)));
    expect(error?.code, scopeChangeCode);
    expect(error?.message, 'SPRINT-2 is running, so adding this task is a scope change.');
  });

  test('moving a task into the running sprint carries the scope-change code', () async {
    final error = await http.runWithClient(() async {
      try {
        await PersonaOsApi(serverUrl: 'http://test').moveTask('TASK-1', column: 'todo', sprintKey: 'SPRINT-2');
        return null;
      } on ApiException catch (e) {
        return e;
      }
    }, () => MockClient((_) async => http.Response(scopeChange, 400)));
    expect(error?.code, scopeChangeCode);
  });
}
