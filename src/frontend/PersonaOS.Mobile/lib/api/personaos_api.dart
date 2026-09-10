import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

/// One event from the chat SSE stream.
class ChatEvent {
  ChatEvent({required this.type, this.text, this.conversationId, this.error});

  factory ChatEvent.fromJson(Map<String, dynamic> json) => ChatEvent(
        type: json['type'] as String,
        text: json['text'] as String?,
        conversationId: json['conversationId'] as int?,
        error: json['error'] as String?,
        actions: (json['actions'] as List<dynamic>?)
            ?.map((a) => ToolReceipt.fromJson(a as Map<String, dynamic>))
            .toList(),
      );

  final String type; // start | delta | tool | done | error
  final String? text;
  final int? conversationId;
  final String? error;

  /// On `done`: what the tools actually did this turn. Null when none ran.
  final List<ToolReceipt>? actions;
}

/// What a tool actually did, recorded by the server from the tool's own result
/// rather than from anything the model said. Shown under a reply so a claim like
/// "I set your reminder for 6pm" can be checked against what was stored.
class ToolReceipt {
  ToolReceipt({required this.tool, required this.ok, this.summary});

  factory ToolReceipt.fromJson(Map<String, dynamic> json) => ToolReceipt(
        tool: json['tool'] as String,
        ok: json['ok'] as bool? ?? true,
        summary: json['summary'] as String?,
      );

  final String tool;
  final bool ok;
  final String? summary;
}

/// A failed API call carrying a user-presentable message. The server returns
/// `{ "error": "..." }` on validation failures; we surface that where present.
class ApiException implements Exception {
  ApiException(this.message);
  final String message;

  @override
  String toString() => message;
}

/// A goal with its computed rollup progress and nested children.
class GoalNode {
  GoalNode({
    required this.id,
    required this.title,
    required this.parentGoalId,
    required this.periodType,
    required this.status,
    required this.progress,
    required this.effectiveProgress,
    required this.children,
  });

  factory GoalNode.fromJson(Map<String, dynamic> json) => GoalNode(
        id: json['id'] as int,
        title: json['title'] as String,
        parentGoalId: json['parentGoalId'] as int?,
        periodType: json['periodType'] as String,
        status: json['status'] as String,
        progress: json['progress'] as int,
        effectiveProgress: json['effectiveProgress'] as int,
        children: (json['children'] as List<dynamic>)
            .map((e) => GoalNode.fromJson(e as Map<String, dynamic>))
            .toList(),
      );

  final int id;
  final String title;
  final int? parentGoalId;
  final String periodType; // year | quarter | month
  final String status; // active | completed | dropped
  final int progress;
  final int effectiveProgress;
  final List<GoalNode> children;
}

/// A goal with the tree depth it sits at, for an indented flat list.
class GoalRow {
  GoalRow(this.goal, this.depth);
  final GoalNode goal;
  final int depth;
}

/// Flattens a goal forest into depth-tagged rows, parents before children.
List<GoalRow> flattenGoals(List<GoalNode> roots, [int depth = 0]) {
  final rows = <GoalRow>[];
  for (final goal in roots) {
    rows.add(GoalRow(goal, depth));
    rows.addAll(flattenGoals(goal.children, depth + 1));
  }
  return rows;
}

/// A single daily-planner task.
class PlannerItem {
  PlannerItem({
    required this.id,
    required this.title,
    required this.date,
    required this.scheduledTime,
    required this.status,
    required this.goalTitle,
  });

  factory PlannerItem.fromJson(Map<String, dynamic> json) => PlannerItem(
        id: json['id'] as int,
        title: json['title'] as String,
        date: json['date'] as String,
        scheduledTime: json['scheduledTime'] as String?,
        status: json['status'] as String,
        goalTitle: json['goalTitle'] as String?,
      );

  final int id;
  final String title;
  final String date;
  final String? scheduledTime; // "HH:mm:ss" or null
  final String status; // planned | done | skipped
  final String? goalTitle;
}

/// A reminder with its due time resolved to the install's zone for display.
class Reminder {
  Reminder({
    required this.id,
    required this.message,
    required this.dueAtLocal,
    required this.status,
    required this.goalTitle,
    required this.plannerItemTitle,
  });

  factory Reminder.fromJson(Map<String, dynamic> json) => Reminder(
        id: json['id'] as int,
        message: json['message'] as String,
        dueAtLocal: DateTime.parse(json['dueAtLocal'] as String),
        status: json['status'] as String,
        goalTitle: json['goalTitle'] as String?,
        plannerItemTitle: json['plannerItemTitle'] as String?,
      );

  final int id;
  final String message;
  final DateTime dueAtLocal;
  final String status; // pending | delivered | cancelled | failed
  final String? goalTitle;
  final String? plannerItemTitle;
}

/// Thin client for the PersonaOS server API.
class PersonaOsApi {
  PersonaOsApi({required this.serverUrl});

  final String serverUrl;
  String? _token;

  bool get isLoggedIn => _token != null;

  Future<String?> login(String username, String password) async {
    final response = await http
        .post(
          Uri.parse('$serverUrl/api/auth/login'),
          headers: {'Content-Type': 'application/json'},
          body: jsonEncode({'username': username, 'password': password}),
        )
        .timeout(const Duration(seconds: 10));

    if (response.statusCode != 200) {
      final body = jsonDecode(response.body) as Map<String, dynamic>;
      return (body['error'] as String?) ?? 'Login failed (${response.statusCode}).';
    }
    final body = jsonDecode(response.body) as Map<String, dynamic>;
    _token = body['accessToken'] as String;
    return null; // success
  }

  // --- Goals ---------------------------------------------------------------

  Future<List<GoalNode>> getGoals({bool includeDropped = false}) async {
    final data = await _get('/api/goals?includeDropped=$includeDropped') as List<dynamic>;
    return data.map((e) => GoalNode.fromJson(e as Map<String, dynamic>)).toList();
  }

  Future<void> createGoal({
    required String title,
    required String periodType,
    required String periodStart, // yyyy-MM-dd
    int? parentGoalId,
  }) async {
    await _post('/api/goals', {
      'title': title,
      'periodType': periodType,
      'periodStart': periodStart,
      'parentGoalId': ?parentGoalId,
    });
  }

  Future<void> setGoalProgress(int id, int progress) =>
      _put('/api/goals/$id', {'progress': progress});

  Future<void> setGoalStatus(int id, String status) =>
      _put('/api/goals/$id/status', {'status': status});

  Future<void> deleteGoal(int id) => _delete('/api/goals/$id');

  // --- Planner -------------------------------------------------------------

  Future<List<PlannerItem>> getPlannerDay(String date) async {
    final data = await _get('/api/planner?date=$date') as Map<String, dynamic>;
    return (data['items'] as List<dynamic>)
        .map((e) => PlannerItem.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<void> addPlannerItem({
    required String title,
    required String date, // yyyy-MM-dd
    String? scheduledTime, // "HH:mm" or null
  }) async {
    await _post('/api/planner/items', {
      'title': title,
      'date': date,
      'scheduledTime': ?scheduledTime,
    });
  }

  Future<void> setPlannerStatus(int id, String status) =>
      _put('/api/planner/items/$id/status', {'status': status});

  Future<void> movePlannerItem(int id, String date) =>
      _put('/api/planner/items/$id/date', {'date': date});

  Future<void> deletePlannerItem(int id) => _delete('/api/planner/items/$id');

  // --- Reminders -----------------------------------------------------------

  Future<List<Reminder>> getReminders({bool includeCompleted = false}) async {
    final data =
        await _get('/api/reminders?includeCompleted=$includeCompleted') as List<dynamic>;
    return data.map((e) => Reminder.fromJson(e as Map<String, dynamic>)).toList();
  }

  /// [dueAtLocal] is a wall-clock "yyyy-MM-ddTHH:mm" read in the install's zone.
  Future<void> createReminder(String message, String dueAtLocal) =>
      _post('/api/reminders', {'message': message, 'dueAtLocal': dueAtLocal});

  Future<void> cancelReminder(int id) => _post('/api/reminders/$id/cancel', const {});

  Future<void> deleteReminder(int id) => _delete('/api/reminders/$id');

  // --- Chat (SSE) ----------------------------------------------------------

  /// Sends a chat message and yields SSE events as they stream in.
  Stream<ChatEvent> streamChat(String message, {int? conversationId}) async* {
    final client = http.Client();
    try {
      final request = http.Request('POST', Uri.parse('$serverUrl/api/chat'))
        ..headers['Content-Type'] = 'application/json'
        ..headers['Authorization'] = 'Bearer $_token'
        ..body = jsonEncode({
          'message': message,
          'conversationId': ?conversationId,
        });

      final response = await client.send(request);
      if (response.statusCode == 401) {
        _token = null;
        yield ChatEvent(type: 'error', error: 'Session expired. Log in again.');
        return;
      }

      // Parse the SSE wire format: "event: x" / "data: {...}" / blank line.
      final lines = response.stream
          .transform(utf8.decoder)
          .transform(const LineSplitter());
      await for (final line in lines) {
        if (line.startsWith('data: ')) {
          final payload = line.substring(6);
          yield ChatEvent.fromJson(jsonDecode(payload) as Map<String, dynamic>);
        }
      }
    } finally {
      client.close();
    }
  }

  // --- Authed request helpers ---------------------------------------------

  Map<String, String> get _headers => {
        'Content-Type': 'application/json',
        if (_token != null) 'Authorization': 'Bearer $_token',
      };

  Future<dynamic> _get(String path) => _send(
      () => http.get(Uri.parse('$serverUrl$path'), headers: _headers));

  Future<dynamic> _post(String path, Object body) => _send(() => http.post(
      Uri.parse('$serverUrl$path'),
      headers: _headers,
      body: jsonEncode(body)));

  Future<dynamic> _put(String path, Object body) => _send(() => http.put(
      Uri.parse('$serverUrl$path'),
      headers: _headers,
      body: jsonEncode(body)));

  Future<dynamic> _delete(String path) => _send(
      () => http.delete(Uri.parse('$serverUrl$path'), headers: _headers));

  /// Runs a request, maps 401 to a cleared session, and turns any non-2xx into
  /// an [ApiException] carrying the server's `error` message when present.
  Future<dynamic> _send(Future<http.Response> Function() request) async {
    final http.Response response;
    try {
      response = await request().timeout(const Duration(seconds: 15));
    } on TimeoutException {
      throw ApiException('The server did not respond.');
    }

    if (response.statusCode == 401) {
      _token = null;
      throw ApiException('Session expired. Log in again.');
    }
    if (response.statusCode < 200 || response.statusCode >= 300) {
      throw ApiException(_errorFrom(response));
    }
    if (response.body.isEmpty) return null;
    return jsonDecode(response.body);
  }

  String _errorFrom(http.Response response) {
    try {
      final body = jsonDecode(response.body) as Map<String, dynamic>;
      final message = body['error'];
      if (message is String && message.isNotEmpty) return message;
    } catch (_) {
      // Non-JSON body — fall through to a generic message.
    }
    return 'Request failed (${response.statusCode}).';
  }
}
