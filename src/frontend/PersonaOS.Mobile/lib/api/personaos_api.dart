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
      );

  final String type; // start | delta | done | error
  final String? text;
  final int? conversationId;
  final String? error;
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
}
