import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;
import 'package:shared_preferences/shared_preferences.dart';

import 'api/personaos_api.dart';
import 'screens/chat_screen.dart';
import 'screens/login_screen.dart';

/// PersonaOS mobile app.
///
/// The PersonaOS product brand and visual identity are fixed. On first launch
/// the user points the app at their self-hosted server; the app then fetches
/// the instance's assistant nickname + enabled features from /api/branding.
void main() {
  runApp(const PersonaOsApp());
}

const _kServerUrlPref = 'server_url';

/// Fixed PersonaOS look — matches the dashboard's identity.
final ThemeData personaOsTheme = ThemeData(
  colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xFF1A1A2E)),
  appBarTheme: const AppBarTheme(
    backgroundColor: Color(0xFF1A1A2E),
    foregroundColor: Colors.white,
  ),
  useMaterial3: true,
);

class PersonaOsApp extends StatelessWidget {
  const PersonaOsApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'PersonaOS',
      theme: personaOsTheme,
      home: const _Bootstrapper(),
    );
  }
}

/// Decides the first screen: server-URL entry on first launch, home otherwise.
class _Bootstrapper extends StatefulWidget {
  const _Bootstrapper();

  @override
  State<_Bootstrapper> createState() => _BootstrapperState();
}

class _BootstrapperState extends State<_Bootstrapper> {
  String? _serverUrl;
  bool _loaded = false;

  @override
  void initState() {
    super.initState();
    SharedPreferences.getInstance().then((prefs) {
      setState(() {
        _serverUrl = prefs.getString(_kServerUrlPref);
        _loaded = true;
      });
    });
  }

  void _onServerChanged(String? url) {
    setState(() => _serverUrl = url);
  }

  @override
  Widget build(BuildContext context) {
    if (!_loaded) {
      return const Scaffold(body: Center(child: CircularProgressIndicator()));
    }
    return _serverUrl == null
        ? ServerUrlScreen(onSaved: (url) => _onServerChanged(url))
        : HomeScreen(
            serverUrl: _serverUrl!,
            onForgetServer: () => _onServerChanged(null),
          );
  }
}

/// First-launch screen: the user enters their self-hosted server URL
/// (e.g. their Tailscale address).
class ServerUrlScreen extends StatefulWidget {
  const ServerUrlScreen({super.key, required this.onSaved});

  final ValueChanged<String> onSaved;

  @override
  State<ServerUrlScreen> createState() => _ServerUrlScreenState();
}

class _ServerUrlScreenState extends State<ServerUrlScreen> {
  final _controller = TextEditingController();
  String? _error;
  bool _checking = false;

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  Future<void> _connect() async {
    final raw = _controller.text.trim();
    if (raw.isEmpty) {
      setState(() => _error = 'Enter your PersonaOS server URL.');
      return;
    }
    final url = raw.endsWith('/') ? raw.substring(0, raw.length - 1) : raw;

    setState(() {
      _checking = true;
      _error = null;
    });

    try {
      final response = await http
          .get(Uri.parse('$url/api/branding'))
          .timeout(const Duration(seconds: 8));
      if (response.statusCode != 200) {
        throw Exception('HTTP ${response.statusCode}');
      }
      final prefs = await SharedPreferences.getInstance();
      await prefs.setString(_kServerUrlPref, url);
      widget.onSaved(url);
    } catch (_) {
      if (mounted) {
        setState(() {
          _checking = false;
          _error = 'Could not reach a PersonaOS server at that address.';
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('PersonaOS')),
      body: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const Text(
              'Connect to your server',
              style: TextStyle(fontSize: 22, fontWeight: FontWeight.bold),
            ),
            const SizedBox(height: 8),
            const Text(
              'Enter the address of your self-hosted PersonaOS server '
              '(for example, your Tailscale address).',
            ),
            const SizedBox(height: 24),
            TextField(
              controller: _controller,
              keyboardType: TextInputType.url,
              autocorrect: false,
              decoration: InputDecoration(
                labelText: 'Server URL',
                hintText: 'http://my-pc.tailnet.ts.net:5080',
                border: const OutlineInputBorder(),
                errorText: _error,
              ),
              onSubmitted: (_) => _connect(),
            ),
            const SizedBox(height: 16),
            FilledButton(
              onPressed: _checking ? null : _connect,
              child: Text(_checking ? 'Connecting…' : 'Connect'),
            ),
          ],
        ),
      ),
    );
  }
}

/// Branding payload from GET /api/branding.
class Branding {
  Branding({
    required this.assistantNickname,
    required this.isConfigured,
    required this.enabledFeatures,
  });

  factory Branding.fromJson(Map<String, dynamic> json) => Branding(
        assistantNickname: json['assistantNickname'] as String,
        isConfigured: json['isConfigured'] as bool,
        enabledFeatures: (json['enabledFeatures'] as List<dynamic>)
            .map((e) => e as String)
            .toList(),
      );

  final String assistantNickname;
  final bool isConfigured;
  final List<String> enabledFeatures;
}

/// Placeholder home: greets via the instance's assistant nickname and lists
/// enabled modules. Replaced by the chat screen in Phase 1.
class HomeScreen extends StatefulWidget {
  const HomeScreen({
    super.key,
    required this.serverUrl,
    required this.onForgetServer,
  });

  final String serverUrl;
  final VoidCallback onForgetServer;

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  late Future<Branding> _branding;
  late final PersonaOsApi _api;

  @override
  void initState() {
    super.initState();
    _api = PersonaOsApi(serverUrl: widget.serverUrl);
    _branding = _fetchBranding();
  }

  void _openChat(String nickname) {
    if (_api.isLoggedIn) {
      Navigator.of(context).push(MaterialPageRoute<void>(
        builder: (_) => ChatScreen(api: _api, assistantNickname: nickname),
      ));
      return;
    }
    Navigator.of(context).push(MaterialPageRoute<void>(
      builder: (context) => LoginScreen(
        api: _api,
        onLoggedIn: () {
          Navigator.of(context).pushReplacement(MaterialPageRoute<void>(
            builder: (_) => ChatScreen(api: _api, assistantNickname: nickname),
          ));
        },
      ),
    ));
  }

  Future<Branding> _fetchBranding() async {
    final response = await http
        .get(Uri.parse('${widget.serverUrl}/api/branding'))
        .timeout(const Duration(seconds: 8));
    return Branding.fromJson(jsonDecode(response.body) as Map<String, dynamic>);
  }

  Future<void> _forgetServer() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(_kServerUrlPref);
    widget.onForgetServer();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('PersonaOS'),
        actions: [
          IconButton(
            icon: const Icon(Icons.link_off),
            tooltip: 'Change server',
            onPressed: _forgetServer,
          ),
        ],
      ),
      body: FutureBuilder<Branding>(
        future: _branding,
        builder: (context, snapshot) {
          if (snapshot.connectionState != ConnectionState.done) {
            return const Center(child: CircularProgressIndicator());
          }
          if (snapshot.hasError) {
            return Center(
              child: Text('Could not reach ${widget.serverUrl}'),
            );
          }
          final branding = snapshot.data!;
          return Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  branding.isConfigured
                      ? '${branding.assistantNickname} is ready'
                      : 'Server needs setup',
                  style: const TextStyle(
                    fontSize: 24,
                    fontWeight: FontWeight.bold,
                  ),
                ),
                const SizedBox(height: 12),
                if (branding.isConfigured) ...[
                  Text(
                    'Enabled modules: ${branding.enabledFeatures.join(', ')}',
                  ),
                  const SizedBox(height: 24),
                  FilledButton.icon(
                    onPressed: () => _openChat(branding.assistantNickname),
                    icon: const Icon(Icons.chat_bubble_outline),
                    label: Text('Chat with ${branding.assistantNickname}'),
                  ),
                ] else
                  const Text(
                    'Open the dashboard on your server to run the Setup Wizard.',
                  ),
              ],
            ),
          );
        },
      ),
    );
  }
}
