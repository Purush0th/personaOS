import 'dart:convert';

import 'package:http/http.dart' as http;
import 'package:shared_preferences/shared_preferences.dart';

/// This app's version: the release it was built for, passed in by the release build
/// (`--dart-define=APP_VERSION=...`). A build without it is "0.0.0-dev" and offers no app update.
const appVersion = String.fromEnvironment('APP_VERSION', defaultValue: '0.0.0-dev');

/// The tag prefix of this app's own releases ("mobile-v0.1.0-alpha.14"). The server and web are
/// tagged "v…"; each is released when it changes (owner, 2026-10-29).
const appTagPrefix = 'mobile-v';

/// True when this app is older than the oldest one [minSupported] (the server's
/// `minSupportedClient`) works with. A development build is never too old.
bool isTooOld(String? minSupported, {String app = appVersion}) =>
    minSupported != null && minSupported.isNotEmpty && app != '0.0.0-dev' && isNewer(minSupported, app);

/// A newer PersonaOS release than the server, or this app, runs.
class AvailableUpdate {
  AvailableUpdate({
    required this.version,
    required this.name,
    required this.notesUrl,
    this.forApp = false,
    this.appDownloadUrl,
  });

  final String version;
  final String name;
  final String notesUrl;

  /// The update is for this app (the server is up to date); [appDownloadUrl] is where to get it.
  final bool forApp;
  final String? appDownloadUrl;
}

/// Dismissed versions, apart for the server and the app: dismissing the server's notice of a
/// release hid the app's own update to that release too (owner, 2026-10-29).
String _dismissedKey(bool forApp) => forApp ? 'updates.dismissed.app' : 'updates.dismissed';

/// The update to tell the user about, if any. The server comes first: with "Install updates
/// automatically" it catches up by itself, and otherwise it is updated from Settings → Updates on
/// the web. Once it is current, a newer app than this one (an app release, "mobile-v…") is offered
/// for download. Releases are
/// read as a list, not GitHub's "latest", which leaves out prereleases; a server or app on a
/// prerelease follows prereleases. Fail-silent: no repository, rate limiting or being offline
/// simply means no notice, and a version the user dismissed is not offered again.
Future<AvailableUpdate?> checkForUpdate({
  required String? currentVersion,
  required String? repository,
  String app = appVersion,
  http.Client? client,
}) async {
  if (currentVersion == null || repository == null) return null;
  try {
    final found = await findUpdates(currentVersion: currentVersion, repository: repository, app: app, client: client);
    final first = found.server ?? found.app;
    if (first == null) return null;

    final prefs = await SharedPreferences.getInstance();
    return prefs.getString(_dismissedKey(first.forApp)) == first.version ? null : first;
  } catch (_) {
    return null;
  }
}

/// The newest server release and app release, each null when it is current. For About's
/// "Check for updates": dismissed versions are offered again, and failures are thrown, not hidden.
Future<({AvailableUpdate? server, AvailableUpdate? app})> findUpdates({
  required String? currentVersion,
  required String repository,
  String app = appVersion,
  http.Client? client,
}) async {
  final get = client?.get ?? http.get;
  final response = await get(
    Uri.parse('https://api.github.com/repos/$repository/releases?per_page=20'),
    headers: const {'Accept': 'application/vnd.github+json'},
  ).timeout(const Duration(seconds: 8));
  if (response.statusCode != 200) throw http.ClientException('GitHub answered ${response.statusCode}.');
  final releases = (jsonDecode(response.body) as List<dynamic>).cast<Map<String, dynamic>>();

  AvailableUpdate? server;
  if (currentVersion != null) {
    final newest = _newest(releases, followPrereleases: isPrerelease(currentVersion), forApp: false);
    if (newest != null && isNewer(_version(newest), currentVersion)) server = _update(newest, repository);
  }
  AvailableUpdate? forApp;
  if (app != '0.0.0-dev') {
    final newest = _newest(releases, followPrereleases: isPrerelease(app), forApp: true);
    if (newest != null && isNewer(_version(newest), app)) forApp = _update(newest, repository, forApp: true);
  }
  return (server: server, app: forApp);
}

Future<void> dismissUpdate(AvailableUpdate update) async {
  final prefs = await SharedPreferences.getInstance();
  await prefs.setString(_dismissedKey(update.forApp), update.version);
}

String _tag(Map<String, dynamic> release) => release['tag_name'] as String? ?? '';

bool _isAppRelease(Map<String, dynamic> release) => _tag(release).startsWith(appTagPrefix);

/// The tag without its "v" or "mobile-v".
String _version(Map<String, dynamic> release) => _isAppRelease(release)
    ? _tag(release).substring(appTagPrefix.length)
    : _tag(release).replaceFirst(RegExp('^v'), '');

String? _apk(Map<String, dynamic> release) => (release['assets'] as List<dynamic>? ?? [])
    .cast<Map<String, dynamic>>()
    .where((a) => (a['name'] as String? ?? '').toLowerCase().endsWith('.apk'))
    .map((a) => a['browser_download_url'] as String?)
    .firstOrNull;

Map<String, dynamic>? _newest(
  List<Map<String, dynamic>> releases, {
  required bool followPrereleases,
  required bool forApp,
}) {
  Map<String, dynamic>? best;
  for (final release in releases) {
    if (release['draft'] == true || _version(release).isEmpty) continue;
    if (_isAppRelease(release) != forApp) continue;
    if (!followPrereleases && release['prerelease'] == true) continue;
    if (forApp && _apk(release) == null) continue;
    if (best == null || isNewer(_version(release), _version(best))) best = release;
  }
  return best;
}

AvailableUpdate _update(Map<String, dynamic> release, String repository, {bool forApp = false}) {
  final name = release['name'] as String?;
  return AvailableUpdate(
    version: _version(release),
    name: name == null || name.isEmpty ? 'v${_version(release)}' : name,
    notesUrl: release['html_url'] as String? ?? 'https://github.com/$repository/releases',
    forApp: forApp,
    appDownloadUrl: forApp ? _apk(release) : null,
  );
}

bool isPrerelease(String version) => version.contains('-');

/// True when [candidate] is a strictly higher version than [current], as Semantic Versioning orders
/// them: 0.1.0-alpha.2 < 0.1.0-alpha.10 < 0.1.0-beta < 0.1.0. Missing parts count as zero.
bool isNewer(String candidate, String current) => _compare(candidate, current) > 0;

int _compare(String a, String b) {
  (List<int>, List<String>) split(String v) {
    final cleaned = v.replaceFirst(RegExp('^v'), '').split('+').first;
    final dash = cleaned.indexOf('-');
    final core = dash < 0 ? cleaned : cleaned.substring(0, dash);
    final pre = dash < 0 ? <String>[] : cleaned.substring(dash + 1).split('.');
    return (core.split('.').map((n) => int.tryParse(n) ?? 0).toList(), pre);
  }

  final (coreA, preA) = split(a);
  final (coreB, preB) = split(b);
  for (var i = 0; i < 3; i++) {
    final left = i < coreA.length ? coreA[i] : 0;
    final right = i < coreB.length ? coreB[i] : 0;
    if (left != right) return left.compareTo(right);
  }
  // A release outranks its own prereleases.
  if (preA.isEmpty || preB.isEmpty) return preA.isEmpty == preB.isEmpty ? 0 : (preA.isEmpty ? 1 : -1);
  for (var i = 0; i < preA.length && i < preB.length; i++) {
    final x = int.tryParse(preA[i]);
    final y = int.tryParse(preB[i]);
    final step = switch ((x, y)) {
      (final int x, final int y) => x.compareTo(y),
      (int _, null) => -1, // Numbers sort before words.
      (null, int _) => 1,
      _ => preA[i].compareTo(preB[i]),
    };
    if (step != 0) return step;
  }
  return preA.length.compareTo(preB.length);
}
