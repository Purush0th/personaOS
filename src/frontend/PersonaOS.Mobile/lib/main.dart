import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;
import 'package:shared_preferences/shared_preferences.dart';

import 'api/personaos_api.dart';
import 'date_utils.dart';
import 'layout.dart';
import 'phosphor_icons.dart';
import 'planner_calendar.dart';
import 'push_service.dart';
import 'reminder_alarms.dart';
import 'screens/about_screen.dart';
import 'screens/alarm_screen.dart';
import 'screens/board_screen.dart';
import 'screens/chat_screen.dart';
import 'screens/goals_screen.dart';
import 'screens/login_screen.dart';
import 'screens/memories_screen.dart';
import 'screens/notes_screen.dart';
import 'screens/planner_screen.dart';
import 'screens/reminders_screen.dart';
import 'screens/settings_screen.dart';
import 'theme_choice.dart';
import 'update_check.dart';
import 'update_notice.dart';

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

  await loadThemeChoice();
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
  appNavigatorKey.currentState?.push(
    MaterialPageRoute<void>(fullscreenDialog: true, builder: (_) => AlarmScreen(alarm: alarm)),
  );
}

const _kServerUrlPref = 'server_url';

/// Turns a connection failure into something the user can act on.
///
/// "Could not reach the server" alone does not say whether the server is down,
/// the address is wrong, or — the usual cause here — the VPN carrying the
/// connection is switched off.
String connectionFailureReason(Object error) => switch (error) {
  final SocketException e when e.osError != null => 'Network error: ${e.osError!.message}.',
  SocketException _ => 'Could not open a connection to that host.',
  TimeoutException _ => 'The server did not respond within 8 seconds.',
  HandshakeException _ => 'TLS failed — if the server is plain HTTP, use http:// not https://.',
  FormatException _ => 'That does not look like a valid URL.',
  _ => '$error',
};

/// The one brand colour, the same violet as the web app. PersonaOS's identity is fixed, so this
/// is a constant, not a per-install setting — only the assistant's nickname and persona vary.
/// Violet only ever means the assistant (docs/DESIGN.md).
const Color personaOsSeed = Color(0xFF5B4FE9);

/// The ink of the light theme: text, and the main action's fill.
const Color personaOsInk = Color(0xFF18181B);

/// The scheme, written out to match the web app's tokens (styles.scss, "Dusk") rather than left
/// to the seed: neutral greys and white paper in light, a violet-tinted off-black in dark, and one
/// violet accent.
ColorScheme _personaOsScheme(Brightness brightness) {
  final seeded = ColorScheme.fromSeed(seedColor: personaOsSeed, brightness: brightness);
  if (brightness == Brightness.dark) {
    return seeded.copyWith(
      primary: const Color(0xFF8B7CFF),
      onPrimary: duskSurface,
      primaryContainer: const Color(0xFF231C45),
      onPrimaryContainer: const Color(0xFFDCD6FF),
      secondaryContainer: const Color(0xFF221E2E),
      onSecondaryContainer: const Color(0xFFF2F0F7),
      tertiary: const Color(0xFF8B7CFF),
      tertiaryContainer: const Color(0xFF231C45),
      onTertiaryContainer: const Color(0xFFDCD6FF),
      surface: duskSurface,
      onSurface: const Color(0xFFF2F0F7),
      onSurfaceVariant: const Color(0xFF9A95AB),
      surfaceContainerLowest: const Color(0xFF09080D),
      surfaceContainerLow: const Color(0xFF15121F),
      surfaceContainer: const Color(0xFF1A1625),
      surfaceContainerHigh: const Color(0xFF221E2E),
      surfaceContainerHighest: const Color(0xFF2A2538),
      outline: const Color(0xFF6E6880),
      outlineVariant: const Color(0xFF26222F),
    );
  }
  return seeded.copyWith(
    primary: personaOsSeed,
    onPrimary: Colors.white,
    primaryContainer: const Color(0xFFEEECFD),
    onPrimaryContainer: const Color(0xFF4338CA),
    secondaryContainer: const Color(0xFFEFEFF2),
    onSecondaryContainer: personaOsInk,
    tertiary: personaOsSeed,
    tertiaryContainer: const Color(0xFFEEECFD),
    onTertiaryContainer: const Color(0xFF4338CA),
    surface: Colors.white,
    onSurface: personaOsInk,
    onSurfaceVariant: const Color(0xFF6B6B76),
    surfaceContainerLowest: Colors.white,
    surfaceContainerLow: const Color(0xFFF7F7F8),
    surfaceContainer: const Color(0xFFF2F2F4),
    surfaceContainerHigh: const Color(0xFFEFEFF2),
    surfaceContainerHighest: const Color(0xFFE7E7EB),
    outline: const Color(0xFF8A8A95),
    outlineVariant: const Color(0xFFE7E7EB),
  );
}

/// Fixed PersonaOS look, in light and dark, after the web app's "Dusk" style (docs/DESIGN.md).
///
/// Colours come from the scheme rather than being written per widget, which is what keeps the
/// dark theme from drifting into an unreadable mix as screens are added. Headings are medium and
/// drawn in slightly, corners run 10 (controls) to 14 (cards) to 20 (dialogs), and the main action
/// is ink in light and violet in dark. The shared shapes live here rather than at every call site.
ThemeData _personaOsTheme(Brightness brightness) {
  final scheme = _personaOsScheme(brightness);
  final dark = brightness == Brightness.dark;
  final base = ThemeData(colorScheme: scheme, useMaterial3: true, fontFamily: personaOsFont);
  TextStyle? heading(TextStyle? style) => style?.copyWith(fontWeight: FontWeight.w500, letterSpacing: -0.3);
  final text = base.textTheme;

  return base.copyWith(
    scaffoldBackgroundColor: scheme.surface,
    textTheme: text.copyWith(
      headlineLarge: heading(text.headlineLarge),
      headlineMedium: heading(text.headlineMedium),
      headlineSmall: heading(text.headlineSmall),
      titleLarge: heading(text.titleLarge),
      titleMedium: text.titleMedium?.copyWith(fontWeight: FontWeight.w600),
    ),
    appBarTheme: AppBarTheme(
      backgroundColor: scheme.surface,
      foregroundColor: scheme.onSurface,
      surfaceTintColor: Colors.transparent,
      elevation: 0,
      scrolledUnderElevation: 0.5,
      centerTitle: false,
      titleTextStyle: TextStyle(
        fontFamily: personaOsFont,
        color: scheme.onSurface,
        fontSize: 20,
        fontWeight: FontWeight.w500,
        letterSpacing: -0.3,
      ),
    ),
    cardTheme: CardThemeData(
      color: scheme.surfaceContainerLow,
      surfaceTintColor: Colors.transparent,
      elevation: 0,
      margin: EdgeInsets.zero,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
    ),
    listTileTheme: const ListTileThemeData(contentPadding: EdgeInsets.symmetric(horizontal: 18, vertical: 6)),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: scheme.surfaceContainerLow,
      border: OutlineInputBorder(
        borderRadius: BorderRadius.circular(10),
        borderSide: BorderSide(color: scheme.outlineVariant),
      ),
      enabledBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(10),
        borderSide: BorderSide(color: scheme.outlineVariant),
      ),
      focusedBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(10),
        borderSide: BorderSide(color: scheme.primary, width: 2),
      ),
      contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        // Height only — NOT Size.fromHeight, which also sets the minimum width to infinity.
        // That worked for buttons in a full-width column, but inside a Row (dialog actions, the
        // confirm card) an infinite-width child cannot be laid out, and release builds simply
        // draw nothing: the Confirm button vanished, as did every dialog's primary action.
        // Screens that want a full-width button get it from their layout, not from the theme.
        minimumSize: const Size(64, 48),
        // Ink on paper in light; on dusk the accent, with dark text. Violet in light is kept for
        // the assistant's own controls.
        backgroundColor: dark ? scheme.primary : personaOsInk,
        foregroundColor: dark ? scheme.onPrimary : Colors.white,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
        textStyle: const TextStyle(fontFamily: personaOsFont, fontSize: 15, fontWeight: FontWeight.w600),
      ),
    ),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: OutlinedButton.styleFrom(
        foregroundColor: scheme.onSurface,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
        side: BorderSide(color: dark ? const Color(0x24FFFFFF) : const Color(0xFFD4D4DA)),
      ),
    ),
    textButtonTheme: TextButtonThemeData(style: TextButton.styleFrom(foregroundColor: scheme.onSurface)),
    chipTheme: ChipThemeData(shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8))),
    dialogTheme: DialogThemeData(shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20))),
    snackBarTheme: SnackBarThemeData(
      behavior: SnackBarBehavior.floating,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
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
    return ValueListenableBuilder<ThemeMode>(
      valueListenable: themeChoice,
      builder: (context, mode, _) => MaterialApp(
        navigatorKey: appNavigatorKey,
        title: 'PersonaOS',
        theme: personaOsTheme,
        darkTheme: personaOsDarkTheme,
        // Follows the phone unless this phone chose light or dark in Settings; there is no
        // per-install theming beyond that.
        themeMode: mode,
        home: const _Bootstrapper(),
      ),
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
    unawaited(
      SharedPreferences.getInstance().then((prefs) {
        setState(() {
          _serverUrl = prefs.getString(_kServerUrlPref);
          _loaded = true;
        });
      }),
    );
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
        ? ServerUrlScreen(onSaved: _onServerChanged)
        : HomeScreen(serverUrl: _serverUrl!, onForgetServer: () => _onServerChanged(null));
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
      final response = await http.get(Uri.parse('$url/api/branding')).timeout(const Duration(seconds: 8));
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
            const Text('Connect to your server', style: TextStyle(fontSize: 22, fontWeight: FontWeight.bold)),
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
            FilledButton(onPressed: _checking ? null : _connect, child: Text(_checking ? 'Connecting…' : 'Connect')),
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
    this.apiVersion,
    this.repository,
    this.minSupportedClient,
  });

  factory Branding.fromJson(Map<String, dynamic> json) => Branding(
    assistantNickname: json['assistantNickname'] as String,
    isConfigured: json['isConfigured'] as bool,
    enabledFeatures: (json['enabledFeatures'] as List<dynamic>).map((e) => e as String).toList(),
    apiVersion: json['apiVersion'] as String?,
    repository: json['repository'] as String?,
    minSupportedClient: json['minSupportedClient'] as String?,
  );

  final String assistantNickname;
  final bool isConfigured;
  final List<String> enabledFeatures;

  /// The server's version and its GitHub repository ("owner/name"), for the update notice.
  final String? apiVersion;
  final String? repository;

  /// The oldest app the server works with; an older one says so on the home screen.
  final String? minSupportedClient;
}

/// Placeholder home: greets via the instance's assistant nickname and lists
/// enabled modules. Replaced by the chat screen in Phase 1.
class HomeScreen extends StatefulWidget {
  const HomeScreen({super.key, required this.serverUrl, required this.onForgetServer, this.api});

  final String serverUrl;
  final VoidCallback onForgetServer;

  /// A client to use instead of a new one: a test passes one that is already signed in.
  @visibleForTesting
  final PersonaOsApi? api;

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> with WidgetsBindingObserver {
  late Future<Branding> _branding;

  /// A newer release than the server runs, once the check finds one.
  AvailableUpdate? _update;

  /// Titles the reminder alarms this phone schedules. Kept from the last branding fetch.
  String _nickname = 'PersonaOS';
  late final PersonaOsApi _api;

  /// The modules this server has switched on, from the last branding fetch.
  List<String> _features = const [];

  /// The numbers on the home screen's cards; null until a signed-in session has loaded them.
  _HomeStats? _stats;

  @override
  void initState() {
    super.initState();
    _api = widget.api ?? PersonaOsApi(serverUrl: widget.serverUrl);
    _branding = _fetchBranding();
    WidgetsBinding.instance.addObserver(this);
    // A planner event's notification opens the planner on that day.
    PushService.instance.onOpenPlannerDay = (date) => appNavigatorKey.currentState?.push(
      MaterialPageRoute<void>(
        builder: (_) => PlannerScreen(api: _api, initialDate: parseDay(date), initialView: CalendarView.day),
      ),
    );
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
    if (state == AppLifecycleState.resumed) {
      unawaited(PushService.instance.resyncAlarms(_api));
      unawaited(_recheckUpdate());
      unawaited(_loadStats());
    }
  }

  /// Loads the home screen's cards: what is planned today, how far the sprint is, and the next
  /// reminder. Only with a signed-in session (home itself opens before sign-in), and each number
  /// on its own, so a module that is off or a request that fails only drops its own card.
  Future<void> _loadStats() async {
    if (!_api.isLoggedIn) return;
    Future<T?> quietly<T>(bool on, Future<T> Function() load) async {
      if (!on) return null;
      try {
        return await load();
      } catch (_) {
        return null;
      }
    }

    final now = DateTime.now();
    final planner = quietly(_features.contains('planner'), () => _api.getPlannerDay(localYmd(now)));
    final board = quietly(_features.contains('board'), _api.getBoard);
    final reminders = quietly(_features.contains('reminders'), _api.getReminders);
    final stats = _HomeStats.from(planner: await planner, board: await board, reminders: await reminders, now: now);
    if (mounted) setState(() => _stats = stats);
  }

  /// When the update notice was last worked out; GitHub allows 60 unsigned requests an hour.
  DateTime? _updateCheckedAt;

  /// Works the update notice out again on coming back to the app. The server updates itself, so a
  /// notice about it goes stale; once it is current, the notice becomes the app's own, with its
  /// Update button (owner, 2026-10-29: the phone kept the server's notice and never offered one).
  Future<void> _recheckUpdate() async {
    final last = _updateCheckedAt;
    if (last != null && DateTime.now().difference(last) < const Duration(minutes: 10)) return;
    try {
      final response = await http
          .get(Uri.parse('${widget.serverUrl}/api/branding'))
          .timeout(const Duration(seconds: 8));
      await _checkUpdate(Branding.fromJson(jsonDecode(response.body) as Map<String, dynamic>));
    } catch (_) {
      // Offline or the server is restarting: the notice stays as it was.
    }
  }

  Future<void> _checkUpdate(Branding branding) async {
    _updateCheckedAt = DateTime.now();
    final update = await checkForUpdate(currentVersion: branding.apiVersion, repository: branding.repository);
    if (mounted) setState(() => _update = update);
  }

  /// Opens [builder]'s screen, first routing through login when needed.
  void _openAfterLogin(WidgetBuilder builder) {
    if (_api.isLoggedIn) {
      // Back on home, the cards catch up with whatever was done on the way.
      unawaited(Navigator.of(context).push(MaterialPageRoute<void>(builder: builder)).then((_) => _loadStats()));
      return;
    }
    Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (context) => LoginScreen(
          api: _api,
          onLoggedIn: () {
            // Registering needs a signed-in session, so it happens here rather than at launch.
            // Not awaited: push setup can prompt for permission and talk to Firebase, and the
            // user asked to open a screen, not to wait for that.
            unawaited(PushService.instance.start(_api, assistantNickname: _nickname));
            unawaited(_loadStats());
            Navigator.of(context).pushReplacement(MaterialPageRoute<void>(builder: builder));
          },
        ),
      ),
    );
  }

  Future<Branding> _fetchBranding() async {
    final response = await http.get(Uri.parse('${widget.serverUrl}/api/branding')).timeout(const Duration(seconds: 8));
    final branding = Branding.fromJson(jsonDecode(response.body) as Map<String, dynamic>);
    _nickname = branding.assistantNickname;
    _features = branding.enabledFeatures;
    unawaited(_checkUpdate(branding));
    unawaited(_loadStats());
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
    // Home is the assistant's own screen, so it sits on the dusk glow (docs/DESIGN.md, Presence).
    return Presence(
      child: Scaffold(
        backgroundColor: Colors.transparent,
        appBar: AppBar(
          backgroundColor: Colors.transparent,
          title: const Text('PersonaOS'),
          actions: [
            IconButton(
              key: const Key('open-about'),
              icon: const Icon(PhosphorIconsRegular.info),
              tooltip: 'About and updates',
              onPressed: () => Navigator.of(
                context,
              ).push(MaterialPageRoute<void>(builder: (_) => AboutScreen(serverUrl: widget.serverUrl))),
            ),
            IconButton(icon: const Icon(PhosphorIconsRegular.linkBreak), tooltip: 'Change server', onPressed: _forgetServer),
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
                    Text('Server needs setup', style: TextStyle(fontSize: 24, fontWeight: FontWeight.bold)),
                    SizedBox(height: 12),
                    Text('Open the web app on your server to run the Setup Wizard.'),
                  ],
                ),
              );
            }
            bool enabled(String feature) => branding.enabledFeatures.contains(feature);
            void openChat() => _openAfterLogin(
              (_) => ChatScreen(api: _api, assistantNickname: nickname, voiceEnabled: enabled('voice')),
            );
            final stats = _stats;
            return ReadableWidth(
              maxWidth: 960,
              child: ListView(
                padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
                children: [
                  _Hero(nickname: nickname, onAsk: openChat),
                  if (isTooOld(branding.minSupportedClient))
                    _TooOld(
                      minimum: branding.minSupportedClient!,
                      onOpenAbout: () => Navigator.of(
                        context,
                      ).push(MaterialPageRoute<void>(builder: (_) => AboutScreen(serverUrl: widget.serverUrl))),
                    ),
                  if (_update != null)
                    UpdateNotice(
                      update: _update!,
                      repository: branding.repository ?? '',
                      onDismiss: () {
                        unawaited(dismissUpdate(_update!));
                        setState(() => _update = null);
                      },
                    ),
                  if (stats != null && stats.hasAny) ...[
                    const SizedBox(height: 28),
                    const _SectionLabel('Today'),
                    _StatCards(
                      stats: stats,
                      onPlanner: () => _openAfterLogin((_) => PlannerScreen(api: _api, boardEnabled: enabled('board'))),
                      onBoard: () => _openAfterLogin((_) => BoardScreen(api: _api)),
                      onReminders: () => _openAfterLogin((_) => RemindersScreen(api: _api)),
                    ),
                  ],
                  const SizedBox(height: 28),
                  const _SectionLabel('Your spaces'),
                  // Two abreast when there is room: a tablet, or a phone on its side.
                  _ModuleGrid(
                    children: [
                      if (enabled('goals'))
                        _ModuleCard(
                          icon: PhosphorIconsRegular.flag,
                          title: 'Goals',
                          subtitle: 'What you are working towards',
                          onTap: () => _openAfterLogin((_) => GoalsScreen(api: _api, boardEnabled: enabled('board'))),
                        ),
                      if (enabled('board'))
                        _ModuleCard(
                          icon: PhosphorIconsRegular.kanban,
                          title: 'Board',
                          subtitle: 'The running sprint, card by card',
                          onTap: () => _openAfterLogin((_) => BoardScreen(api: _api)),
                        ),
                      if (enabled('planner'))
                        _ModuleCard(
                          icon: PhosphorIconsRegular.calendarDots,
                          title: 'Planner',
                          subtitle: 'Your day, item by item',
                          onTap: () => _openAfterLogin((_) => PlannerScreen(api: _api, boardEnabled: enabled('board'))),
                        ),
                      if (enabled('reminders'))
                        _ModuleCard(
                          icon: PhosphorIconsRegular.alarm,
                          title: 'Reminders',
                          subtitle: 'Nudges at the right time',
                          onTap: () => _openAfterLogin((_) => RemindersScreen(api: _api)),
                        ),
                      if (enabled('notes'))
                        _ModuleCard(
                          icon: PhosphorIconsRegular.note,
                          title: 'Notes',
                          subtitle: 'Jot things down for later',
                          onTap: () => _openAfterLogin((_) => NotesScreen(api: _api, boardEnabled: enabled('board'))),
                        ),
                      if (enabled('memory'))
                        _ModuleCard(
                          icon: PhosphorIconsRegular.brain,
                          title: 'Memories',
                          subtitle: 'What $nickname remembers about you',
                          onTap: () => _openAfterLogin((_) => MemoriesScreen(api: _api)),
                        ),
                      _ModuleCard(
                        icon: PhosphorIconsRegular.slidersHorizontal,
                        title: 'Settings',
                        subtitle: 'Persona, provider, modules',
                        onTap: () => _openAfterLogin((_) => SettingsScreen(api: _api)),
                      ),
                    ],
                  ),
                ],
              ),
            );
          },
        ),
      ),
    );
  }
}

/// The server needs a newer app than this one. Not dismissible: until the app is updated, some
/// things may not work. About has the Update button.
class _TooOld extends StatelessWidget {
  const _TooOld({required this.minimum, required this.onOpenAbout});

  final String minimum;
  final VoidCallback onOpenAbout;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Card(
      key: const Key('app-too-old'),
      margin: const EdgeInsets.only(top: 16),
      color: scheme.errorContainer,
      child: ListTile(
        leading: Icon(PhosphorIconsRegular.warning, color: scheme.onErrorContainer),
        title: Text('Update this app', style: TextStyle(color: scheme.onErrorContainer)),
        subtitle: Text(
          'Your server needs app $minimum or newer; this is $appVersion. Some things may not work until '
          'you update.',
          style: TextStyle(color: scheme.onErrorContainer),
        ),
        trailing: TextButton(onPressed: onOpenAbout, child: const Text('Update')),
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
            Icon(PhosphorIconsRegular.cloudSlash, size: 48, color: scheme.outline),
            const SizedBox(height: 16),
            const Text('Can’t reach your server', style: TextStyle(fontSize: 20, fontWeight: FontWeight.w600)),
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
              decoration: BoxDecoration(color: scheme.surfaceContainerHigh, borderRadius: BorderRadius.circular(14)),
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
                icon: const Icon(PhosphorIconsRegular.arrowClockwise),
                label: const Text('Try again'),
              ),
            ),
            const SizedBox(height: 8),
            TextButton(onPressed: onChangeServer, child: const Text('Change server')),
          ],
        ),
      ),
    );
  }
}

/// The greeting at the top of the home screen: the Orb, the time of day, one line in the thin
/// display type, and the way in to the chat, shaped like the chat's own composer.
class _Hero extends StatelessWidget {
  const _Hero({required this.nickname, required this.onAsk});

  final String nickname;
  final VoidCallback onAsk;

  static String _partOfDay(DateTime now) => now.hour < 12
      ? 'Good morning'
      : now.hour < 17
      ? 'Good afternoon'
      : 'Good evening';

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final dark = Theme.of(context).brightness == Brightness.dark;
    return Padding(
      padding: const EdgeInsets.only(top: 12),
      child: Column(
        children: [
          const Orb(size: 88),
          const SizedBox(height: 18),
          Text(_partOfDay(DateTime.now()), style: TextStyle(fontSize: 14, color: scheme.onSurfaceVariant)),
          const SizedBox(height: 6),
          Text(
            "What's on your mind?",
            textAlign: TextAlign.center,
            style: TextStyle(
              fontSize: 34,
              height: 1.12,
              fontWeight: FontWeight.w300,
              letterSpacing: -1,
              color: scheme.onSurface,
            ),
          ),
          const SizedBox(height: 22),
          Semantics(
            button: true,
            label: 'Chat with $nickname',
            excludeSemantics: true,
            child: Material(
              key: const Key('home-ask'),
              color: dark ? const Color(0x0DFFFFFF) : scheme.surface,
              shape: StadiumBorder(side: BorderSide(color: dark ? const Color(0x24FFFFFF) : const Color(0xFFD4D4DA))),
              child: InkWell(
                customBorder: const StadiumBorder(),
                onTap: onAsk,
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(20, 6, 6, 6),
                  child: Row(
                    children: [
                      Expanded(
                        child: Text(
                          'Ask $nickname anything…',
                          overflow: TextOverflow.ellipsis,
                          style: TextStyle(fontSize: 16, color: scheme.onSurfaceVariant),
                        ),
                      ),
                      const Orb(size: 40, child: Icon(PhosphorIconsRegular.arrowUp, color: Colors.white, size: 20)),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// A quiet heading over a group on the home screen, in sentence case.
class _SectionLabel extends StatelessWidget {
  const _SectionLabel(this.text);

  final String text;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(left: 4, bottom: 10),
    child: Text(text, style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w600)),
  );
}

/// The numbers the home screen's cards show, worked out from what the server returned.
class _HomeStats {
  const _HomeStats({this.openToday, this.plannedToday, this.sprintDone, this.sprintTotal, this.nextReminder});

  factory _HomeStats.from({
    List<PlannerItem>? planner,
    BoardView? board,
    List<Reminder>? reminders,
    required DateTime now,
  }) {
    final sprint = board?.sprint;
    final tasks = board == null ? 0 : board.columns.values.fold<int>(0, (sum, list) => sum + list.length);
    final upcoming =
        (reminders ?? const <Reminder>[])
            .where((r) => r.status == 'pending' && r.dueAtUtc.isAfter(now.toUtc()))
            .toList()
          ..sort((a, b) => a.dueAtUtc.compareTo(b.dueAtUtc));
    final active = sprint != null && sprint.status == 'active' && tasks > 0;
    return _HomeStats(
      plannedToday: planner?.length,
      openToday: planner?.where((i) => i.status == 'planned').length,
      sprintDone: active ? board!.columns[BoardColumns.done]?.length ?? 0 : null,
      sprintTotal: active ? tasks : null,
      nextReminder: upcoming.isEmpty ? null : upcoming.first,
    );
  }

  final int? openToday;
  final int? plannedToday;
  final int? sprintDone;
  final int? sprintTotal;
  final Reminder? nextReminder;

  bool get hasAny => plannedToday != null || sprintTotal != null || nextReminder != null;
}

/// The row of number cards under the greeting, after the Aura shots: a title, a line under it,
/// and one big thin number. The first card is solid violet, the number that matters most.
class _StatCards extends StatelessWidget {
  const _StatCards({required this.stats, required this.onPlanner, required this.onBoard, required this.onReminders});

  final _HomeStats stats;
  final VoidCallback onPlanner;
  final VoidCallback onBoard;
  final VoidCallback onReminders;

  /// How long until [due], as a number and its unit: "18 min", "3 h", "2 days".
  static (String, String) _until(DateTime due) {
    final left = due.difference(DateTime.now().toUtc());
    if (left.inMinutes < 60) return ('${left.inMinutes < 1 ? 1 : left.inMinutes}', 'min');
    if (left.inHours < 24) return ('${left.inHours}', 'h');
    return ('${left.inDays}', left.inDays == 1 ? 'day' : 'days');
  }

  @override
  Widget build(BuildContext context) {
    final cards = <Widget>[
      if (stats.plannedToday != null)
        _StatCard(
          key: const Key('stat-today'),
          title: 'Planned today',
          subtitle: stats.plannedToday == 0 ? 'A free day' : '${stats.plannedToday} in all',
          value: '${stats.openToday ?? 0}',
          unit: 'left',
          onTap: onPlanner,
        ),
      if (stats.sprintTotal != null)
        _StatCard(
          key: const Key('stat-sprint'),
          title: 'Sprint',
          subtitle: '${stats.sprintDone} of ${stats.sprintTotal} done',
          value: '${(100 * stats.sprintDone! / stats.sprintTotal!).round()}',
          unit: '%',
          onTap: onBoard,
        ),
      if (stats.nextReminder case final next?)
        _StatCard(
          key: const Key('stat-reminder'),
          title: 'Next reminder',
          subtitle: next.message,
          value: _until(next.dueAtUtc).$1,
          unit: _until(next.dueAtUtc).$2,
          onTap: onReminders,
        ),
    ];
    return SizedBox(
      height: 152,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        clipBehavior: Clip.none,
        itemCount: cards.length,
        separatorBuilder: (_, _) => const SizedBox(width: 10),
        itemBuilder: (context, i) => i == 0 ? _Solid(child: cards[i]) : cards[i],
      ),
    );
  }
}

/// Marks the first stat card as the solid one.
class _Solid extends InheritedWidget {
  const _Solid({required super.child});

  static bool of(BuildContext context) => context.dependOnInheritedWidgetOfExactType<_Solid>() != null;

  @override
  bool updateShouldNotify(_Solid oldWidget) => false;
}

class _StatCard extends StatelessWidget {
  const _StatCard({
    super.key,
    required this.title,
    required this.subtitle,
    required this.value,
    required this.unit,
    required this.onTap,
  });

  final String title;
  final String subtitle;
  final String value;
  final String unit;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final dark = Theme.of(context).brightness == Brightness.dark;
    final solid = _Solid.of(context);
    final ink = solid ? scheme.onPrimary : scheme.onSurface;
    final muted = solid ? scheme.onPrimary.withValues(alpha: 0.75) : scheme.onSurfaceVariant;
    return Semantics(
      button: true,
      label: '$title: $value $unit. $subtitle',
      excludeSemantics: true,
      child: Material(
        color: solid ? scheme.primary : (dark ? const Color(0x0AFFFFFF) : scheme.surface),
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(20),
          side: solid ? BorderSide.none : BorderSide(color: dark ? const Color(0x14FFFFFF) : scheme.outlineVariant),
        ),
        child: InkWell(
          borderRadius: BorderRadius.circular(20),
          onTap: onTap,
          child: SizedBox(
            width: 148,
            child: Padding(
              padding: const EdgeInsets.all(14),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    title,
                    style: TextStyle(fontSize: 14, fontWeight: FontWeight.w600, color: ink),
                  ),
                  const SizedBox(height: 2),
                  Text(
                    subtitle,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(fontSize: 12, color: muted),
                  ),
                  const Spacer(),
                  Text.rich(
                    TextSpan(
                      children: [
                        TextSpan(
                          text: value,
                          style: TextStyle(
                            fontSize: 44,
                            height: 1,
                            fontWeight: FontWeight.w300,
                            letterSpacing: -1.6,
                            color: ink,
                            fontFeatures: const [FontFeature.tabularFigures()],
                          ),
                        ),
                        TextSpan(
                          text: ' $unit',
                          style: TextStyle(fontSize: 13, color: muted),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// The module cards: one per row on a narrow screen, two abreast from [Breakpoints.twoColumns].
class _ModuleGrid extends StatelessWidget {
  const _ModuleGrid({required this.children});

  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        if (constraints.maxWidth < Breakpoints.twoColumns) return Column(children: children);
        const gap = 10.0;
        final width = (constraints.maxWidth - gap) / 2;
        return Wrap(
          spacing: gap,
          children: [for (final child in children) SizedBox(width: width, child: child)],
        );
      },
    );
  }
}

/// A tappable card linking to one module from the home screen: paper, a hairline, and an ink
/// icon. Colour is kept for the assistant; the modules are where the work lives.
class _ModuleCard extends StatelessWidget {
  const _ModuleCard({required this.icon, required this.title, required this.onTap, this.subtitle});

  final IconData icon;
  final String title;
  final String? subtitle;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final dark = Theme.of(context).brightness == Brightness.dark;

    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: Material(
        color: dark ? scheme.surfaceContainerLow : scheme.surface,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(14),
          side: BorderSide(color: scheme.outlineVariant),
        ),
        child: InkWell(
          borderRadius: BorderRadius.circular(14),
          onTap: onTap,
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
            child: Row(
              children: [
                Container(
                  width: 40,
                  height: 40,
                  decoration: BoxDecoration(
                    color: scheme.surfaceContainerHigh,
                    borderRadius: BorderRadius.circular(10),
                  ),
                  child: Icon(icon, size: 20, color: scheme.onSurface),
                ),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        title,
                        style: TextStyle(fontSize: 16, fontWeight: FontWeight.w600, color: scheme.onSurface),
                      ),
                      if (subtitle != null) ...[
                        const SizedBox(height: 2),
                        Text(subtitle!, style: TextStyle(fontSize: 13, color: scheme.onSurfaceVariant)),
                      ],
                    ],
                  ),
                ),
                Icon(PhosphorIconsRegular.caretRight, color: scheme.outline),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
