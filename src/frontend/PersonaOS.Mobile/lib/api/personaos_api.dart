import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

import '../date_utils.dart';
import 'board_models.dart';

export 'board_models.dart';

/// One event from the chat SSE stream.
class ChatEvent {
  ChatEvent({
    required this.type,
    this.text,
    this.conversationId,
    this.error,
    this.actions,
    this.pending,
    this.unverifiedClaim = false,
    this.unknownItems = const [],
  });

  factory ChatEvent.fromJson(Map<String, dynamic> json) => ChatEvent(
        type: json['type'] as String,
        text: json['text'] as String?,
        conversationId: json['conversationId'] as int?,
        error: json['error'] as String?,
        actions: (json['actions'] as List<dynamic>?)
            ?.map((a) => ToolReceipt.fromJson(a as Map<String, dynamic>))
            .toList(),
        pending: (json['pending'] as List<dynamic>?)
            ?.map((a) => PendingAction.fromJson(a as Map<String, dynamic>))
            .toList(),
        unverifiedClaim: json['unverifiedClaim'] as bool? ?? false,
        unknownItems: _stringList(json['unknownItems']),
      );

  final String type; // start | delta | tool | done | error
  final String? text;
  final int? conversationId;
  final String? error;

  /// On `done`: what the tools actually did this turn. Null when none ran.
  final List<ToolReceipt>? actions;

  /// On `done`: changes the assistant wants to make, awaiting confirmation. Null when none.
  final List<PendingAction>? pending;

  /// On `done`: the reply says a change was made, but no tool made one — nothing was saved.
  final bool unverifiedClaim;

  /// On `done`: item keys the reply names that do not exist, e.g. `['TASK-6']`.
  final List<String> unknownItems;
}

List<String> _stringList(Object? json) =>
    (json as List<dynamic>?)?.map((e) => e as String).toList() ?? const [];

/// A data-changing action the assistant proposed.
///
/// Nothing has been written: the server refuses to run a mutating tool on the model's say-so,
/// because models have created goals and reminders nobody asked for. It stays here until the
/// user confirms or discards it, so the client must render it — otherwise the assistant simply
/// cannot change anything on this device.
class PendingAction {
  PendingAction({
    required this.id,
    required this.tool,
    required this.summary,
    required this.status,
    this.resultSummary,
    this.resultOk,
  });

  factory PendingAction.fromJson(Map<String, dynamic> json) => PendingAction(
        id: json['id'] as String,
        tool: json['tool'] as String,
        summary: json['summary'] as String? ?? '',
        status: json['status'] as String? ?? 'pending',
        resultSummary: json['resultSummary'] as String?,
        resultOk: json['resultOk'] as bool?,
      );

  final String id;
  final String tool;

  /// What will happen, in the user's terms, e.g. `Create goal “Learn C#” — month · top-level`.
  final String summary;

  /// pending | confirmed | discarded.
  final String status;

  /// Once confirmed: what actually happened, from the tool's own result.
  final String? resultSummary;
  final bool? resultOk;

  bool get isPending => status == 'pending';
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
  ApiException(this.message, {this.code});
  final String message;

  /// The server's machine-readable error code, e.g. [scopeChangeCode].
  final String? code;

  @override
  String toString() => message;
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
    this.goalKey,
    this.taskKey,
  });

  factory PlannerItem.fromJson(Map<String, dynamic> json) => PlannerItem(
        id: json['id'] as int,
        title: json['title'] as String,
        date: json['date'] as String,
        scheduledTime: json['scheduledTime'] as String?,
        status: json['status'] as String,
        goalTitle: json['goalTitle'] as String?,
        goalKey: json['goalKey'] as String?,
        taskKey: json['taskKey'] as String?,
      );

  final int id;
  final String title;
  final String date;
  final String? scheduledTime; // "HH:mm:ss" or null
  final String status; // planned | done | skipped
  final String? goalTitle;

  /// The goal's key, for the chip colour.
  final String? goalKey;

  /// The sprint-board task this item is a day's work on, if picked from the board.
  final String? taskKey;
}

/// A reminder with its due time resolved to the install's zone for display.
class Reminder {
  Reminder({
    required this.id,
    required this.message,
    required this.dueAtLocal,
    required this.dueAtUtc,
    required this.status,
    required this.goalTitle,
    required this.plannerItemTitle,
  });

  factory Reminder.fromJson(Map<String, dynamic> json) => Reminder(
        id: json['id'] as int,
        message: json['message'] as String,
        dueAtLocal: DateTime.parse(json['dueAtLocal'] as String),
        dueAtUtc: parseServerUtc(json['dueAtUtc'] as String),
        status: json['status'] as String,
        goalTitle: json['goalTitle'] as String?,
        plannerItemTitle: json['plannerItemTitle'] as String?,
      );

  final int id;
  final String message;
  final DateTime dueAtLocal;

  /// The actual instant it is due — what an alarm is scheduled against. dueAtLocal is the
  /// server's wall-clock rendering for display, and carries no zone.
  final DateTime dueAtUtc;
  final String status; // pending | delivered | cancelled | failed
  final String? goalTitle;
  final String? plannerItemTitle;
}

/// One conversation in the history list.
class ConversationSummary {
  ConversationSummary({
    required this.id,
    required this.publicId,
    required this.title,
    required this.updatedAtUtc,
  });

  factory ConversationSummary.fromJson(Map<String, dynamic> json) => ConversationSummary(
        id: json['id'] as int,
        publicId: json['publicId'] as String? ?? '',
        title: json['title'] as String? ?? 'Untitled',
        updatedAtUtc: parseServerUtc(json['updatedAtUtc'] as String),
      );

  final int id;
  final String publicId;
  final String title;
  final DateTime updatedAtUtc;
}

/// One stored message, replayed when an old conversation is opened.
class ChatMessageDto {
  ChatMessageDto({
    required this.role,
    required this.content,
    this.toolActions,
    this.pendingActions,
    this.unverifiedClaim = false,
    this.unknownItems = const [],
  });

  factory ChatMessageDto.fromJson(Map<String, dynamic> json) => ChatMessageDto(
        role: json['role'] as String,
        content: json['content'] as String? ?? '',
        toolActions: (json['toolActions'] as List<dynamic>?)
            ?.map((a) => ToolReceipt.fromJson(a as Map<String, dynamic>))
            .toList(),
        pendingActions: (json['pendingActions'] as List<dynamic>?)
            ?.map((a) => PendingAction.fromJson(a as Map<String, dynamic>))
            .toList(),
        unverifiedClaim: json['unverifiedClaim'] as bool? ?? false,
        unknownItems: _stringList(json['unknownItems']),
      );

  final String role; // user | assistant
  final String content;
  final List<ToolReceipt>? toolActions;
  final List<PendingAction>? pendingActions;

  /// The reply says a change was made, but no tool made one — nothing was saved.
  final bool unverifiedClaim;

  /// Item keys the reply names that do not exist.
  final List<String> unknownItems;
}

/// A conversation with its full message history.
class ConversationDetail {
  ConversationDetail({
    required this.id,
    required this.title,
    required this.messages,
  });

  factory ConversationDetail.fromJson(Map<String, dynamic> json) => ConversationDetail(
        id: json['id'] as int,
        title: json['title'] as String? ?? 'Untitled',
        messages: (json['messages'] as List<dynamic>? ?? [])
            .map((m) => ChatMessageDto.fromJson(m as Map<String, dynamic>))
            .toList(),
      );

  final int id;
  final String title;
  final List<ChatMessageDto> messages;
}

/// An uploaded document.
class DocumentDto {
  DocumentDto({
    required this.id,
    required this.fileName,
    required this.sizeBytes,
    required this.createdAtUtc,
    this.description,
  });

  factory DocumentDto.fromJson(Map<String, dynamic> json) => DocumentDto(
        id: json['id'] as int,
        fileName: json['fileName'] as String? ?? '',
        sizeBytes: (json['sizeBytes'] as num?)?.toInt() ?? 0,
        createdAtUtc: parseServerUtc(json['createdAtUtc'] as String),
        description: json['description'] as String?,
      );

  final int id;
  final String fileName;
  final int sizeBytes;
  final DateTime createdAtUtc;
  final String? description;
}

/// Firebase client options for this install's own Firebase project. Identifiers, not secrets.
class FcmClientOptions {
  FcmClientOptions({
    required this.apiKey,
    required this.appId,
    required this.messagingSenderId,
    required this.projectId,
  });

  factory FcmClientOptions.fromJson(Map<String, dynamic> json) => FcmClientOptions(
        apiKey: json['apiKey'] as String,
        appId: json['appId'] as String,
        messagingSenderId: json['messagingSenderId'] as String,
        projectId: json['projectId'] as String,
      );

  final String apiKey;
  final String appId;
  final String messagingSenderId;
  final String projectId;
}

/// Instance settings, as returned for prefilling the settings screen.
///
/// The provider API key is deliberately absent: the server never returns it, only whether
/// one is stored. Sending a blank key back means "leave the stored one alone".
class InstanceSettings {
  InstanceSettings({
    required this.assistantNickname,
    required this.personaTemplate,
    required this.aiProvider,
    required this.aiModel,
    required this.aiBaseUrl,
    required this.timeZone,
    required this.features,
    required this.hasApiKey,
  });

  factory InstanceSettings.fromJson(Map<String, dynamic> json) => InstanceSettings(
        assistantNickname: json['assistantNickname'] as String? ?? '',
        personaTemplate: json['personaTemplate'] as String? ?? '',
        aiProvider: json['aiProvider'] as String? ?? 'anthropic',
        aiModel: json['aiModel'] as String? ?? '',
        aiBaseUrl: json['aiBaseUrl'] as String? ?? '',
        timeZone: json['timeZone'] as String? ?? '',
        features: ((json['features'] as Map<String, dynamic>?) ?? {})
            .map((k, v) => MapEntry(k, v == true)),
        hasApiKey: json['hasAnthropicApiKey'] == true,
      );

  final String assistantNickname;
  final String personaTemplate;
  final String aiProvider;
  final String aiModel;
  final String aiBaseUrl;
  final String timeZone;
  final Map<String, bool> features;
  final bool hasApiKey;
}

/// Result of testing the configured AI provider.
class ConnectionTest {
  ConnectionTest({required this.ok, required this.message});

  factory ConnectionTest.fromJson(Map<String, dynamic> json) => ConnectionTest(
        ok: json['ok'] == true,
        message: json['message'] as String? ?? '',
      );

  final bool ok;
  final String message;
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

  Future<List<Goal>> getGoals({bool includeDropped = false}) async {
    final data = await _get('/api/goals?includeDropped=$includeDropped') as List<dynamic>;
    return data.map((e) => Goal.fromJson(e as Map<String, dynamic>)).toList();
  }

  Future<void> createGoal({
    required String title,
    required String periodType,
    required String periodStart, // yyyy-MM-dd
    String? periodEnd, // yyyy-MM-dd; the server defaults it from the period type when null
  }) async {
    await _post('/api/goals', {
      'title': title,
      'periodType': periodType,
      'periodStart': periodStart,
      'periodEnd': ?periodEnd,
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

  /// Adds an item to a day. With [taskId], the item is a day's work on that board task and
  /// takes its title and goal when [title] is empty.
  Future<void> addPlannerItem({
    required String title,
    required String date, // yyyy-MM-dd
    String? scheduledTime, // "HH:mm" or null
    int? taskId,
  }) async {
    await _post('/api/planner/items', {
      'title': title,
      'date': date,
      'scheduledTime': ?scheduledTime,
      'taskId': ?taskId,
    });
  }

  Future<void> setPlannerStatus(int id, String status) =>
      _put('/api/planner/items/$id/status', {'status': status});

  Future<void> movePlannerItem(int id, String date) =>
      _put('/api/planner/items/$id/date', {'date': date});

  Future<void> deletePlannerItem(int id) => _delete('/api/planner/items/$id');

  // --- Sprint board --------------------------------------------------------

  /// The running sprint's board. Its sprint is null when nothing is running.
  Future<BoardView> getBoard() async =>
      BoardView.fromJson(await _get('/api/board') as Map<String, dynamic>);

  /// The plan: sprints to come and the backlog, for picking where a task goes.
  Future<PlanView> getPlan() async =>
      PlanView.fromJson(await _get('/api/board/plan') as Map<String, dynamic>);

  Future<SprintReport> getSprintReport() async =>
      SprintReport.fromJson(await _get('/api/board/sprints') as Map<String, dynamic>);

  /// [sprintKey] like "SPRINT-2"; omit it to put the task in the backlog.
  Future<void> createTask({
    required String title,
    int? points,
    int? goalId,
    String? sprintKey,
    bool acknowledgeScopeChange = false,
  }) =>
      _post('/api/board/tasks', {
        'title': title,
        'points': ?points,
        'goalId': ?goalId,
        'sprintKey': ?sprintKey,
        'acknowledgeScopeChange': acknowledgeScopeChange,
      });

  Future<void> updateTask(String key, {String? title, int? points, int? goalId}) =>
      _put('/api/board/tasks/$key', {
        'title': ?title,
        'points': ?points,
        'clearPoints': points == null,
        'goalId': ?goalId,
        'clearGoal': goalId == null,
      });

  /// Moves a task to [column]; [sprintKey] names the sprint, and is ignored for the backlog.
  Future<void> moveTask(
    String key, {
    required String column,
    String? sprintKey,
    int? index,
    bool acknowledgeScopeChange = false,
  }) =>
      _put('/api/board/tasks/$key/move', {
        'column': column,
        'sprintKey': ?sprintKey,
        'index': ?index,
        'acknowledgeScopeChange': acknowledgeScopeChange,
      });

  Future<void> deleteTask(String key) => _delete('/api/board/tasks/$key');

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

  // --- Proposed changes ------------------------------------------------------

  /// Runs a change the assistant proposed. The only route by which a model-requested
  /// write reaches the database.
  Future<PendingAction> confirmAction(String id) async {
    final data = await _post('/api/chat/actions/$id/confirm', const {});
    return PendingAction.fromJson(data as Map<String, dynamic>);
  }

  /// Declines a proposed change; nothing is executed.
  Future<PendingAction> discardAction(String id) async {
    final data = await _post('/api/chat/actions/$id/discard', const {});
    return PendingAction.fromJson(data as Map<String, dynamic>);
  }

  // --- Push devices --------------------------------------------------------

  /// The Firebase client options this install's own Firebase project uses, or null when the
  /// admin has not set up push. Fetched rather than compiled in, so the release APK carries no
  /// Firebase config and each self-hosted instance brings its own.
  Future<FcmClientOptions?> getPushConfig() async {
    final data = await _get('/api/devices/push-config');
    return data == null ? null : FcmClientOptions.fromJson(data as Map<String, dynamic>);
  }

  /// Registers this device's push token. Idempotent: re-sending a known token only refreshes it.
  Future<void> registerDevice(String token, {required String platform, String? deviceName}) =>
      _post('/api/devices', {
        'token': token,
        'platform': platform,
        'deviceName': ?deviceName,
      });

  // --- Conversations -------------------------------------------------------

  Future<List<ConversationSummary>> getConversations() async {
    final data = await _get('/api/chat/conversations') as List<dynamic>;
    return data
        .map((e) => ConversationSummary.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<ConversationDetail> getConversation(String idOrPublicId) async {
    final data = await _get('/api/chat/conversations/$idOrPublicId');
    return ConversationDetail.fromJson(data as Map<String, dynamic>);
  }

  Future<void> deleteConversation(String idOrPublicId) =>
      _delete('/api/chat/conversations/$idOrPublicId');

  // --- Documents -----------------------------------------------------------

  Future<List<DocumentDto>> getDocuments({String? search}) async {
    final query = (search == null || search.isEmpty)
        ? ''
        : '?search=${Uri.encodeQueryComponent(search)}';
    final data = await _get('/api/documents$query') as List<dynamic>;
    return data.map((e) => DocumentDto.fromJson(e as Map<String, dynamic>)).toList();
  }

  /// Uploads a file as multipart/form-data, the shape the server's `IFormFile` expects.
  Future<DocumentDto> uploadDocument({
    required String filePath,
    required String fileName,
    String? description,
  }) async {
    final request = http.MultipartRequest(
      'POST',
      Uri.parse('$serverUrl/api/documents'),
    );
    if (_token != null) request.headers['Authorization'] = 'Bearer $_token';
    request.files.add(await http.MultipartFile.fromPath('file', filePath, filename: fileName));
    if (description != null && description.isNotEmpty) {
      request.fields['description'] = description;
    }

    final streamed = await request.send().timeout(const Duration(seconds: 120));
    final response = await http.Response.fromStream(streamed);
    if (response.statusCode == 401) {
      _token = null;
      throw ApiException('Session expired. Log in again.');
    }
    if (response.statusCode < 200 || response.statusCode >= 300) {
      throw ApiException(_errorFrom(response), code: _codeFrom(response));
    }
    return DocumentDto.fromJson(jsonDecode(response.body) as Map<String, dynamic>);
  }

  Future<void> updateDocumentDescription(int id, String? description) =>
      _put('/api/documents/$id/description', {'description': description});

  Future<void> deleteDocument(int id) => _delete('/api/documents/$id');

  // --- Settings ------------------------------------------------------------

  Future<InstanceSettings> getSettings() async {
    final data = await _get('/api/setup');
    return InstanceSettings.fromJson(data as Map<String, dynamic>);
  }

  /// Sends only the fields given; the server leaves anything omitted untouched.
  Future<void> updateSettings(Map<String, Object?> changes) =>
      _put('/api/setup', changes);

  /// What the assistant knows about the user; sent with every message.
  Future<String> getAboutMe() async =>
      ((await _get('/api/profile')) as Map<String, dynamic>)['aboutMe'] as String? ?? '';

  Future<void> updateAboutMe(String aboutMe) => _put('/api/profile', {'aboutMe': aboutMe});

  Future<ConnectionTest> testConnection(Map<String, Object?> settings) async {
    final data = await _post('/api/setup/test', settings);
    return ConnectionTest.fromJson(data as Map<String, dynamic>);
  }

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

  String? _codeFrom(http.Response response) {
    try {
      final code = (jsonDecode(response.body) as Map<String, dynamic>)['code'];
      return code is String ? code : null;
    } catch (_) {
      return null;
    }
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
