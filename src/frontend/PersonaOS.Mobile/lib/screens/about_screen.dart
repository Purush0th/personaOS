import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;

import '../app_update.dart';
import '../layout.dart';
import '../phosphor_icons.dart';
import '../update_check.dart';
import '../update_notice.dart';

/// About: which version this app and the server run, and checking for and installing updates
/// (owner, 2026-10-29). Open without signing in: it only reads the server's public branding and
/// GitHub's release list, and it is where to turn when the app itself needs updating.
class AboutScreen extends StatefulWidget {
  const AboutScreen({super.key, required this.serverUrl, this.app = appVersion, this.client, this.updater});

  final String serverUrl;

  /// This app's version; tests pass their own.
  final String app;
  final http.Client? client;
  final AppUpdater? updater;

  @override
  State<AboutScreen> createState() => _AboutScreenState();
}

class _AboutScreenState extends State<AboutScreen> {
  String? _serverVersion;
  String? _repository;

  /// The oldest app the server works with.
  String? _minApp;
  bool _serverUnreachable = false;

  bool _checking = false;
  String? _checkError;
  DateTime? _checkedAt;
  ({AvailableUpdate? server, AvailableUpdate? app})? _found;

  bool get _devBuild => widget.app == '0.0.0-dev';

  @override
  void initState() {
    super.initState();
    unawaited(_check());
  }

  /// Reads the server's version, then the releases. The server is asked every time: it may have
  /// updated itself since the screen opened.
  Future<void> _check() async {
    if (_checking) return;
    setState(() {
      _checking = true;
      _checkError = null;
    });
    final get = widget.client?.get ?? http.get;
    try {
      final response = await get(Uri.parse('${widget.serverUrl}/api/branding')).timeout(const Duration(seconds: 8));
      final branding = jsonDecode(response.body) as Map<String, dynamic>;
      _serverVersion = branding['apiVersion'] as String?;
      _repository = branding['repository'] as String?;
      _minApp = branding['minSupportedClient'] as String?;
      _serverUnreachable = false;
    } catch (_) {
      _serverUnreachable = true;
    }

    final repository = _repository;
    try {
      if (repository == null || repository.isEmpty) {
        throw StateError(
          _serverUnreachable
              ? 'The server cannot be reached, so where its releases are published is not known.'
              : 'The server does not say where releases are published.',
        );
      }
      _found = await findUpdates(
        currentVersion: _serverVersion,
        repository: repository,
        app: widget.app,
        client: widget.client,
      );
      _checkedAt = DateTime.now();
    } catch (e) {
      _checkError = e is StateError ? e.message : 'Could not check GitHub for releases. Try again in a while.';
    } finally {
      if (mounted) setState(() => _checking = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    return Scaffold(
      appBar: AppBar(title: const Text('About')),
      body: ReadableWidth(
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
          children: [
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 16),
              child: Row(
                children: [
                  CircleAvatar(
                    radius: 28,
                    backgroundColor: scheme.primaryContainer,
                    child: Icon(PhosphorIconsRegular.sparkle, color: scheme.onPrimaryContainer, size: 28),
                  ),
                  const SizedBox(width: 16),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text('PersonaOS', style: theme.textTheme.headlineSmall),
                        Text('Your self-hosted personal assistant', style: theme.textTheme.bodyMedium),
                      ],
                    ),
                  ),
                ],
              ),
            ),
            Card(
              child: Column(
                children: [
                  ListTile(
                    key: const Key('about-app-version'),
                    leading: const Icon(PhosphorIconsRegular.deviceMobile),
                    title: const Text('This app'),
                    subtitle: Text(
                      _devBuild
                          ? 'Development build'
                          : isTooOld(_minApp, app: widget.app)
                          ? '${widget.app}, too old for this server: it needs $_minApp or newer'
                          : widget.app,
                    ),
                  ),
                  ListTile(
                    key: const Key('about-server-version'),
                    leading: const Icon(PhosphorIconsRegular.hardDrives),
                    title: const Text('Server'),
                    subtitle: Text(
                      _serverVersion ?? (_serverUnreachable ? 'Not reachable' : (_checking ? 'Checking…' : 'Unknown')),
                    ),
                  ),
                  ListTile(
                    leading: const Icon(PhosphorIconsRegular.link),
                    title: const Text('Server address'),
                    subtitle: SelectableText(widget.serverUrl),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 16),
            Card(
              child: Padding(
                padding: const EdgeInsets.fromLTRB(16, 12, 16, 16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Text('Updates', style: theme.textTheme.titleMedium),
                    const SizedBox(height: 8),
                    ..._updateStatus(theme),
                    const SizedBox(height: 12),
                    Align(
                      alignment: Alignment.centerLeft,
                      child: OutlinedButton.icon(
                        key: const Key('about-check'),
                        onPressed: _checking ? null : () => unawaited(_check()),
                        icon: _checking
                            ? const SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2))
                            : const Icon(PhosphorIconsRegular.arrowClockwise),
                        label: Text(_checking ? 'Checking…' : 'Check for updates'),
                      ),
                    ),
                  ],
                ),
              ),
            ),
            const SizedBox(height: 16),
            Card(
              child: Column(
                children: [
                  if (_repository != null)
                    ListTile(
                      leading: const Icon(PhosphorIconsRegular.code),
                      title: const Text('Source and releases'),
                      subtitle: SelectableText('github.com/$_repository'),
                    ),
                  ListTile(
                    leading: const Icon(PhosphorIconsRegular.fileText),
                    title: const Text('Open-source licences'),
                    subtitle: const Text('PersonaOS is Apache-2.0'),
                    trailing: const Icon(PhosphorIconsRegular.caretRight),
                    onTap: () => showLicensePage(
                      context: context,
                      applicationName: 'PersonaOS',
                      applicationVersion: _devBuild ? 'Development build' : widget.app,
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  List<Widget> _updateStatus(ThemeData theme) {
    final error = _checkError;
    if (error != null) {
      return [
        Text(
          error,
          key: const Key('about-check-error'),
          style: TextStyle(color: theme.colorScheme.error),
        ),
      ];
    }
    final found = _found;
    if (found == null) return [Text('Checking for updates…', style: theme.textTheme.bodyMedium)];

    final app = found.app;
    final server = found.server;
    final url = app?.appDownloadUrl;
    final checkedAt = _checkedAt;
    return [
      ListTile(
        key: const Key('about-app-status'),
        contentPadding: EdgeInsets.zero,
        leading: Icon(app == null ? PhosphorIconsRegular.checkCircle : PhosphorIconsRegular.downloadSimple),
        title: Text(
          app != null
              ? 'App ${app.version} is available'
              : (_devBuild ? 'Development builds update from Android Studio' : 'This app is up to date'),
        ),
        subtitle: app != null ? const Text('Update installs it over this one; your settings stay.') : null,
      ),
      if (url != null) UpdateInstallButton(url: url, repository: _repository!, updater: widget.updater),
      ListTile(
        key: const Key('about-server-status'),
        contentPadding: EdgeInsets.zero,
        leading: Icon(server == null ? PhosphorIconsRegular.checkCircle : PhosphorIconsRegular.downloadSimple),
        title: Text(
          server != null
              ? 'Server ${server.name} is available'
              : (_serverVersion == null ? 'Server version unknown' : 'The server is up to date'),
        ),
        subtitle: server != null
            ? const Text(
                'It installs by itself when "Install updates automatically" is on; otherwise update it '
                'from Settings → Updates on the web.',
              )
            : null,
      ),
      if (checkedAt != null)
        Text('Last checked at ${TimeOfDay.fromDateTime(checkedAt).format(context)}', style: theme.textTheme.bodySmall),
    ];
  }
}
