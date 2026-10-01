import 'dart:convert';

import 'package:http/http.dart' as http;
import 'package:shared_preferences/shared_preferences.dart';

/// A newer PersonaOS release than the server runs.
class AvailableUpdate {
  AvailableUpdate({required this.version, required this.name, required this.notesUrl});

  final String version;
  final String name;
  final String notesUrl;
}

const _dismissedKey = 'updates.dismissed';

/// Notify-only update check, as the web app's: compares the server's version (from
/// /api/branding) with the latest GitHub release of its repository. Fail-silent: a private or
/// missing repository, rate limiting or being offline simply means no notice. A version the user
/// dismissed is not offered again.
Future<AvailableUpdate?> checkForUpdate({
  required String? currentVersion,
  required String? repository,
  http.Client? client,
}) async {
  if (currentVersion == null || repository == null) return null;
  try {
    final get = client?.get ?? http.get;
    final response = await get(
      Uri.parse('https://api.github.com/repos/$repository/releases/latest'),
      headers: const {'Accept': 'application/vnd.github+json'},
    ).timeout(const Duration(seconds: 8));
    if (response.statusCode != 200) return null;
    final release = jsonDecode(response.body) as Map<String, dynamic>;
    final latest = (release['tag_name'] as String? ?? '').replaceFirst(RegExp('^v'), '');
    if (latest.isEmpty || !isNewer(latest, currentVersion)) return null;
    final prefs = await SharedPreferences.getInstance();
    if (prefs.getString(_dismissedKey) == latest) return null;
    final name = release['name'] as String?;
    return AvailableUpdate(
      version: latest,
      name: name == null || name.isEmpty ? 'v$latest' : name,
      notesUrl: release['html_url'] as String? ?? 'https://github.com/$repository/releases',
    );
  } catch (_) {
    return null;
  }
}

Future<void> dismissUpdate(AvailableUpdate update) async {
  final prefs = await SharedPreferences.getInstance();
  await prefs.setString(_dismissedKey, update.version);
}

/// True when [candidate] is a strictly higher dotted-numeric version than [current].
bool isNewer(String candidate, String current) {
  List<int> parts(String v) => v.split('.').map((n) => int.tryParse(n) ?? 0).toList();
  final a = parts(candidate);
  final b = parts(current);
  for (var i = 0; i < (a.length > b.length ? a.length : b.length); i++) {
    final left = i < a.length ? a[i] : 0;
    final right = i < b.length ? b[i] : 0;
    if (left != right) return left > right;
  }
  return false;
}
