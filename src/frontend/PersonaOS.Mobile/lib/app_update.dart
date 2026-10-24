import 'dart:io';

import 'package:flutter/services.dart';
import 'package:http/http.dart' as http;
import 'package:path_provider/path_provider.dart';

/// Updating the app from inside it (owner, 2026-10-24: "update from the app itself, without
/// manual download and installation"). The release's APK is downloaded into the app's own cache
/// and handed to Android's installer. Android still shows its "Update this app?" screen: only a
/// device-owner app may install silently. The first time, Android also asks to allow installing
/// apps from PersonaOS. Android installs the update only if it is signed with the same key as the
/// installed app, which the release workflow sees to.
class AppUpdater {
  AppUpdater({http.Client? client, MethodChannel? channel, Future<Directory> Function()? cache})
    : _client = client ?? http.Client(),
      _channel = channel ?? const MethodChannel('personaos/installer'),
      _cache = cache ?? getTemporaryDirectory;

  final http.Client _client;
  final MethodChannel _channel;
  final Future<Directory> Function() _cache;

  /// Whether Android lets this app start an install. False until the user allows it once.
  Future<bool> canInstall() async => await _channel.invokeMethod<bool>('canInstall') ?? false;

  /// Opens the system page where the user allows PersonaOS to install apps.
  Future<void> openInstallSettings() => _channel.invokeMethod<void>('openInstallSettings');

  /// Downloads [url] (a PersonaOS release's APK on GitHub) and starts Android's installer.
  /// [onProgress] gets 0..1 when the size is known, null otherwise.
  Future<void> downloadAndInstall(
    String url, {
    required String repository,
    void Function(double? done)? onProgress,
  }) async {
    if (!isReleaseApk(url, repository)) throw AppUpdateException('Not a PersonaOS release: $url');

    final folder = Directory('${(await _cache()).path}/updates');
    if (folder.existsSync()) folder.deleteSync(recursive: true); // Only ever the one being installed.
    folder.createSync(recursive: true);
    final apk = File('${folder.path}/personaos-update.apk');

    final response = await _client.send(http.Request('GET', Uri.parse(url)));
    if (response.statusCode != 200) throw AppUpdateException('The download failed (${response.statusCode}).');
    final total = response.contentLength;
    var received = 0;
    final sink = apk.openWrite();
    try {
      await for (final chunk in response.stream) {
        sink.add(chunk);
        received += chunk.length;
        onProgress?.call(total == null || total == 0 ? null : received / total);
      }
    } finally {
      await sink.close();
    }
    if (total != null && received != total) throw AppUpdateException('The download was cut short.');

    await _channel.invokeMethod<void>('install', {'path': apk.path});
  }

  /// Only an APK attached to a release of [repository] on GitHub: the URL the release lists.
  static bool isReleaseApk(String url, String repository) {
    final uri = Uri.tryParse(url);
    return uri != null &&
        uri.scheme == 'https' &&
        uri.host == 'github.com' &&
        uri.path.toLowerCase().startsWith('/${repository.toLowerCase()}/releases/download/') &&
        uri.path.toLowerCase().endsWith('.apk');
  }
}

class AppUpdateException implements Exception {
  AppUpdateException(this.message);

  final String message;

  @override
  String toString() => message;
}
