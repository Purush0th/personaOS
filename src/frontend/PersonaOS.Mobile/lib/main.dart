import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;
import 'package:shared_preferences/shared_preferences.dart';

import 'api/personaos_api.dart';
import 'screens/chat_screen.dart';
import 'screens/goals_screen.dart';
import 'screens/login_screen.dart';
import 'screens/planner_screen.dart';
import 'screens/reminders_screen.dart';

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

  /// Opens [builder]'s screen, first routing through login when needed.
  void _openAfterLogin(WidgetBuilder builder) {
    if (_api.isLoggedIn) {
      Navigator.of(context)
          .push(MaterialPageRoute<void>(builder: builder));
      return;
    }
    Navigator.of(context).push(MaterialPageRoute<void>(
      builder: (context) => LoginScreen(
        api: _api,
        onLoggedIn: () {
          Navigator.of(context)
              .pushReplacement(MaterialPageRoute<void>(builder: builder));
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
          final nickname = branding.assistantNickname;
          if (!branding.isConfigured) {
            return const Padding(
              padding: EdgeInsets.all(24),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('Server needs setup',
                      style: TextStyle(fontSize: 24, fontWeight: FontWeight.bold)),
                  SizedBox(height: 12),
                  Text('Open the web app on your server to run the Setup Wizard.'),
                ],
              ),
            );
          }
          bool enabled(String feature) =>
              branding.enabledFeatures.contains(feature);
          return ListView(
            padding: const EdgeInsets.all(16),
            children: [
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 8, horizontal: 8),
                child: Text('$nickname is ready',
                    style: const TextStyle(
                        fontSize: 24, fontWeight: FontWeight.bold)),
              ),
              _ModuleCard(
                icon: Icons.chat_bubble_outline,
                title: 'Chat with $nickname',
                onTap: () => _openAfterLogin(
                    (_) => ChatScreen(api: _api, assistantNickname: nickname)),
              ),
              if (enabled('goals'))
                _ModuleCard(
                  icon: Icons.flag_outlined,
                  title: 'Goals',
                  onTap: () => _openAfterLogin((_) => GoalsScreen(api: _api)),
                ),
              if (enabled('planner'))
                _ModuleCard(
                  icon: Icons.event_note_outlined,
                  title: 'Planner',
                  onTap: () => _openAfterLogin((_) => PlannerScreen(api: _api)),
                ),
              if (enabled('reminders'))
                _ModuleCard(
                  icon: Icons.alarm,
                  title: 'Reminders',
                  onTap: () => _openAfterLogin((_) => RemindersScreen(api: _api)),
                ),
            ],
          );
        },
      ),
    );
  }
}

/// A tappable card linking to one module from the home screen.
class _ModuleCard extends StatelessWidget {
  const _ModuleCard({
    required this.icon,
    required this.title,
    required this.onTap,
  });

  final IconData icon;
  final String title;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Card(
      margin: const EdgeInsets.symmetric(vertical: 6),
      child: ListTile(
        leading: Icon(icon),
        title: Text(title),
        trailing: const Icon(Icons.chevron_right),
        onTap: onTap,
      ),
    );
  }
}
