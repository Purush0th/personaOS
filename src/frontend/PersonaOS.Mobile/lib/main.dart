import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;
import 'package:shared_preferences/shared_preferences.dart';

import 'api/personaos_api.dart';
import 'push_service.dart';
import 'reminder_alarms.dart';
import 'screens/alarm_screen.dart';
import 'screens/board_screen.dart';
import 'screens/chat_screen.dart';
import 'screens/documents_screen.dart';
import 'screens/goals_screen.dart';
import 'screens/login_screen.dart';
import 'screens/planner_screen.dart';
import 'screens/reminders_screen.dart';
import 'screens/settings_screen.dart';

/// PersonaOS mobile app.
///
/// The PersonaOS product brand and visual identity are fixed. On first launch
/// the user points the app at their self-hosted server; the app then fetches
/// the instance's assistant nickname + enabled features from /api/branding.
Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  // Before the first frame, not later: when a reminder rings over the lock screen, Android
  // starts the app to show it, and that launch is only visible while starting up.
  final alarms = ReminderAlarms.instance;
  await alarms.init();
  alarms.onOpen = _showAlarm;

  runApp(const PersonaOsApp());

  final launchedBy = await alarms.launchedFrom();
  if (launchedBy != null) {
    WidgetsBinding.instance.addPostFrameCallback((_) => _showAlarm(launchedBy));
  }
}

/// Lets the alarm screen open from anywhere — including before any other screen exists, and
/// regardless of whether the user is signed in. A ringing alarm must not wait behind a login.
final appNavigatorKey = GlobalKey<NavigatorState>();

void _showAlarm(ReminderAlarm alarm) {
  appNavigatorKey.currentState?.push(MaterialPageRoute<void>(
    fullscreenDialog: true,
    builder: (_) => AlarmScreen(alarm: alarm),
  ));
}

const _kServerUrlPref = 'server_url';

/// Turns a connection failure into something the user can act on.
///
/// "Could not reach the server" alone does not say whether the server is down,
/// the address is wrong, or — the usual cause here — the VPN carrying the
/// connection is switched off.
String connectionFailureReason(Object error) => switch (error) {
      SocketException e when e.osError != null => 'Network error: ${e.osError!.message}.',
      SocketException _ => 'Could not open a connection to that host.',
      TimeoutException _ => 'The server did not respond within 8 seconds.',
      HandshakeException _ => 'TLS failed — if the server is plain HTTP, use http:// not https://.',
      FormatException _ => 'That does not look like a valid URL.',
      _ => '$error',
    };

/// The one brand colour. PersonaOS's identity is fixed, so this is a constant,
/// not a per-install setting — only the assistant's nickname and persona vary.
const Color personaOsSeed = Color(0xFF5B5BD6);

/// Fixed PersonaOS look, in light and dark.
///
/// Both are built from the same seed so the two themes stay in step: colours
/// come from the scheme rather than being written per widget, which is what
/// keeps the dark theme from drifting into an unreadable mix as screens are
/// added. Surfaces are tinted and borderless, and the shared shapes live here
/// rather than being repeated at every call site.
ThemeData _personaOsTheme(Brightness brightness) {
  final scheme = ColorScheme.fromSeed(
    seedColor: personaOsSeed,
    brightness: brightness,
  );

  return ThemeData(
    colorScheme: scheme,
    useMaterial3: true,
    scaffoldBackgroundColor: scheme.surface,
    appBarTheme: AppBarTheme(
      backgroundColor: scheme.surface,
      foregroundColor: scheme.onSurface,
      surfaceTintColor: Colors.transparent,
      elevation: 0,
      scrolledUnderElevation: 0.5,
      centerTitle: false,
      titleTextStyle: TextStyle(
        color: scheme.onSurface,
        fontSize: 20,
        fontWeight: FontWeight.w600,
        letterSpacing: -0.2,
      ),
    ),
    cardTheme: CardThemeData(
      color: scheme.surfaceContainerLow,
      surfaceTintColor: Colors.transparent,
      elevation: 0,
      margin: EdgeInsets.zero,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(18)),
    ),
    listTileTheme: const ListTileThemeData(
      contentPadding: EdgeInsets.symmetric(horizontal: 18, vertical: 6),
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: scheme.surfaceContainerHigh,
      border: OutlineInputBorder(
        borderRadius: BorderRadius.circular(16),
        borderSide: BorderSide.none,
      ),
      enabledBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(16),
        borderSide: BorderSide.none,
      ),
      focusedBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(16),
        borderSide: BorderSide(color: scheme.primary, width: 1.5),
      ),
      contentPadding: const EdgeInsets.symmetric(horizontal: 18, vertical: 16),
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        // Height only — NOT Size.fromHeight, which also sets the minimum width to infinity.
        // That worked for buttons in a full-width column, but inside a Row (dialog actions, the
        // confirm card) an infinite-width child cannot be laid out, and release builds simply
        // draw nothing: the Confirm button vanished, as did every dialog's primary action.
        // Screens that want a full-width button get it from their layout, not from the theme.
        minimumSize: const Size(64, 52),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
        textStyle: const TextStyle(fontSize: 16, fontWeight: FontWeight.w600),
      ),
    ),
    snackBarTheme: SnackBarThemeData(
      behavior: SnackBarBehavior.floating,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
    ),
    dividerTheme: DividerThemeData(color: scheme.outlineVariant, space: 1),
  );
}

final ThemeData personaOsTheme = _personaOsTheme(Brightness.light);
final ThemeData personaOsDarkTheme = _personaOsTheme(Brightness.dark);

class PersonaOsApp extends StatelessWidget {
  const PersonaOsApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      navigatorKey: appNavigatorKey,
      title: 'PersonaOS',
      theme: personaOsTheme,
      darkTheme: personaOsDarkTheme,
      // Follow the phone. A self-hosted assistant that ignores the system's
      // dark mode looks broken at night, and there is no per-install theming
      // to configure instead.
      themeMode: ThemeMode.system,
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
    } catch (e) {
      if (mounted) {
        setState(() {
          _checking = false;
          // Say why. Swallowing the exception here made a missing INTERNET permission look
          // identical to a wrong address, and left a self-hoster nothing to act on.
          _error = 'Could not reach a PersonaOS server at that address.\n${connectionFailureReason(e)}';
        });
      }
    }
  }

  /// A short, human reason for a failed connection attempt.

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

class _HomeScreenState extends State<HomeScreen> with WidgetsBindingObserver {
  late Future<Branding> _branding;

  /// Titles the reminder alarms this phone schedules. Kept from the last branding fetch.
  String _nickname = 'PersonaOS';
  late final PersonaOsApi _api;

  @override
  void initState() {
    super.initState();
    _api = PersonaOsApi(serverUrl: widget.serverUrl);
    _branding = _fetchBranding();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  /// Coming back to the app re-checks every alarm against the server, so a reminder made on the
  /// web while the phone was idle is set even if its push has not arrived yet.
  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) unawaited(PushService.instance.resyncAlarms(_api));
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
          // Registering needs a signed-in session, so it happens here rather than at launch.
          // Not awaited: push setup can prompt for permission and talk to Firebase, and the
          // user asked to open a screen, not to wait for that.
          unawaited(PushService.instance.start(_api, assistantNickname: _nickname));
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
    final branding = Branding.fromJson(jsonDecode(response.body) as Map<String, dynamic>);
    _nickname = branding.assistantNickname;
    return branding;
  }

  /// Re-fetches branding after a failed connection.
  ///
  /// The usual cause is the VPN being off, which the user fixes outside the app
  /// and then comes back — so the failure must be recoverable in place. Without
  /// this the screen was a dead end reachable only by force-quitting.
  void _retry() {
    setState(() => _branding = _fetchBranding());
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
            return _Unreachable(
              serverUrl: widget.serverUrl,
              reason: connectionFailureReason(snapshot.error!),
              onRetry: _retry,
              onChangeServer: _forgetServer,
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
          final scheme = Theme.of(context).colorScheme;
          return ListView(
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
            children: [
              _Hero(nickname: nickname),
              const SizedBox(height: 20),
              _ModuleCard(
                icon: Icons.auto_awesome,
                title: 'Chat with $nickname',
                subtitle: 'Ask, plan, or talk hands-free',
                prominent: true,
                onTap: () => _openAfterLogin((_) => ChatScreen(
                      api: _api,
                      assistantNickname: nickname,
                      voiceEnabled: enabled('voice'),
                    )),
              ),
              const SizedBox(height: 20),
              Padding(
                padding: const EdgeInsets.only(left: 4, bottom: 10),
                child: Text(
                  'MODULES',
                  style: TextStyle(
                    fontSize: 12,
                    fontWeight: FontWeight.w700,
                    letterSpacing: 0.8,
                    color: scheme.primary,
                  ),
                ),
              ),
              if (enabled('goals'))
                _ModuleCard(
                  icon: Icons.flag_outlined,
                  title: 'Goals',
                  subtitle: 'What you are working towards',
                  onTap: () => _openAfterLogin((_) => GoalsScreen(api: _api, boardEnabled: enabled('board'))),
                ),
              if (enabled('board'))
                _ModuleCard(
                  icon: Icons.view_kanban_outlined,
                  title: 'Board',
                  subtitle: "This week's sprint, card by card",
                  onTap: () => _openAfterLogin((_) => BoardScreen(api: _api)),
                ),
              if (enabled('planner'))
                _ModuleCard(
                  icon: Icons.event_note_outlined,
                  title: 'Planner',
                  subtitle: 'Your day, item by item',
                  onTap: () => _openAfterLogin((_) => PlannerScreen(api: _api, boardEnabled: enabled('board'))),
                ),
              if (enabled('reminders'))
                _ModuleCard(
                  icon: Icons.alarm,
                  title: 'Reminders',
                  subtitle: 'Nudges at the right time',
                  onTap: () => _openAfterLogin((_) => RemindersScreen(api: _api)),
                ),
              if (enabled('docs'))
                _ModuleCard(
                  icon: Icons.folder_outlined,
                  title: 'Documents',
                  subtitle: 'Files the assistant can read',
                  onTap: () => _openAfterLogin((_) => DocumentsScreen(api: _api)),
                ),
              _ModuleCard(
                icon: Icons.tune,
                title: 'Settings',
                subtitle: 'Persona, provider, modules',
                onTap: () => _openAfterLogin((_) => SettingsScreen(api: _api)),
              ),
            ],
          );
        },
      ),
    );
  }
}

/// Shown when the server cannot be reached, with a way out.
///
/// A self-hosted instance on a tailnet or LAN is unreachable often and normally:
/// the VPN is off, the phone is on mobile data, the machine is asleep. That is
/// an ordinary state to recover from, not an error to be stuck in.
class _Unreachable extends StatelessWidget {
  const _Unreachable({
    required this.serverUrl,
    required this.reason,
    required this.onRetry,
    required this.onChangeServer,
  });

  final String serverUrl;
  final String reason;
  final VoidCallback onRetry;
  final VoidCallback onChangeServer;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    return Center(
      child: SingleChildScrollView(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(Icons.cloud_off, size: 48, color: scheme.outline),
            const SizedBox(height: 16),
            const Text(
              'Can’t reach your server',
              style: TextStyle(fontSize: 20, fontWeight: FontWeight.w600),
            ),
            const SizedBox(height: 8),
            Text(
              serverUrl,
              textAlign: TextAlign.center,
              style: TextStyle(color: scheme.onSurfaceVariant),
            ),
            const SizedBox(height: 8),
            Text(
              reason,
              textAlign: TextAlign.center,
              style: TextStyle(color: scheme.onSurfaceVariant, fontSize: 13),
            ),
            const SizedBox(height: 20),
            // Named rather than generic: this address is only reachable over
            // the tailnet or the home network, and a VPN left off is the most
            // common reason to land here.
            Container(
              padding: const EdgeInsets.all(14),
              decoration: BoxDecoration(
                color: scheme.surfaceContainerHigh,
                borderRadius: BorderRadius.circular(14),
              ),
              child: Text(
                'Check that Tailscale is connected, or that you are on the '
                'same network as your server.',
                textAlign: TextAlign.center,
                style: TextStyle(color: scheme.onSurfaceVariant, fontSize: 13),
              ),
            ),
            const SizedBox(height: 20),
            SizedBox(
              width: double.infinity,
              child: FilledButton.icon(
                onPressed: onRetry,
                icon: const Icon(Icons.refresh),
                label: const Text('Try again'),
              ),
            ),
            const SizedBox(height: 8),
            TextButton(
              onPressed: onChangeServer,
              child: const Text('Change server'),
            ),
          ],
        ),
      ),
    );
  }
}

/// The greeting at the top of the home screen.
class _Hero extends StatelessWidget {
  const _Hero({required this.nickname});

  final String nickname;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(22),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(24),
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [scheme.primaryContainer, scheme.secondaryContainer],
        ),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'PersonaOS',
            style: TextStyle(
              fontSize: 13,
              fontWeight: FontWeight.w700,
              letterSpacing: 1.2,
              color: scheme.onPrimaryContainer.withValues(alpha: 0.7),
            ),
          ),
          const SizedBox(height: 6),
          Text(
            '$nickname is ready',
            style: TextStyle(
              fontSize: 28,
              fontWeight: FontWeight.w700,
              letterSpacing: -0.5,
              color: scheme.onPrimaryContainer,
            ),
          ),
        ],
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
    this.subtitle,
    this.prominent = false,
  });

  final IconData icon;
  final String title;
  final String? subtitle;
  final VoidCallback onTap;

  /// Chat is the reason the app exists, so it gets the filled treatment.
  final bool prominent;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final foreground = prominent ? scheme.onPrimary : scheme.onSurface;

    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: Material(
        color: prominent ? scheme.primary : scheme.surfaceContainerLow,
        borderRadius: BorderRadius.circular(20),
        child: InkWell(
          borderRadius: BorderRadius.circular(20),
          onTap: onTap,
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 18),
            child: Row(
              children: [
                Container(
                  width: 44,
                  height: 44,
                  decoration: BoxDecoration(
                    color: prominent
                        ? scheme.onPrimary.withValues(alpha: 0.18)
                        : scheme.primaryContainer,
                    borderRadius: BorderRadius.circular(14),
                  ),
                  child: Icon(
                    icon,
                    size: 22,
                    color: prominent ? scheme.onPrimary : scheme.onPrimaryContainer,
                  ),
                ),
                const SizedBox(width: 16),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        title,
                        style: TextStyle(
                          fontSize: 16,
                          fontWeight: FontWeight.w600,
                          color: foreground,
                        ),
                      ),
                      if (subtitle != null) ...[
                        const SizedBox(height: 2),
                        Text(
                          subtitle!,
                          style: TextStyle(
                            fontSize: 13,
                            color: prominent
                                ? scheme.onPrimary.withValues(alpha: 0.8)
                                : scheme.onSurfaceVariant,
                          ),
                        ),
                      ],
                    ],
                  ),
                ),
                Icon(Icons.chevron_right,
                    color: prominent
                        ? scheme.onPrimary.withValues(alpha: 0.8)
                        : scheme.outline),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
