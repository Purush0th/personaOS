import 'dart:async';

import 'package:flutter/material.dart';

import 'app_update.dart';
import 'update_check.dart';

/// The home screen's notice of a newer release. For the server it says where the server updates;
/// for this app it downloads the release and starts Android's installer, with progress.
class UpdateNotice extends StatefulWidget {
  const UpdateNotice({
    super.key,
    required this.update,
    required this.repository,
    required this.onDismiss,
    this.updater,
  });

  final AvailableUpdate update;
  final String repository;
  final VoidCallback onDismiss;

  /// Downloads and installs; tests pass their own.
  final AppUpdater? updater;

  @override
  State<UpdateNotice> createState() => _UpdateNoticeState();
}

class _UpdateNoticeState extends State<UpdateNotice> {
  late final AppUpdater _updater = widget.updater ?? AppUpdater();

  bool _working = false;
  double? _progress;

  Future<void> _install() async {
    final url = widget.update.appDownloadUrl;
    if (url == null || _working) return;
    final messenger = ScaffoldMessenger.of(context);

    if (!await _updater.canInstall()) {
      // Once only: Android lets an app install updates after the user allows it.
      messenger.showSnackBar(
        const SnackBar(
          content: Text('Allow PersonaOS to install apps, then come back and tap Update again.'),
          duration: Duration(seconds: 6),
        ),
      );
      await _updater.openInstallSettings();
      return;
    }

    setState(() {
      _working = true;
      _progress = null;
    });
    try {
      await _updater.downloadAndInstall(
        url,
        repository: widget.repository,
        onProgress: (done) => mounted ? setState(() => _progress = done) : null,
      );
    } catch (e) {
      messenger.showSnackBar(SnackBar(content: Text('Could not update: $e')));
    } finally {
      if (mounted) setState(() => _working = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final update = widget.update;
    final canInstall = update.forApp && update.appDownloadUrl != null;
    return Card(
      key: const Key('update-notice'),
      margin: const EdgeInsets.only(top: 16),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          ListTile(
            leading: const Icon(Icons.system_update_alt),
            title: Text(
              update.forApp ? 'A new app is available: ${update.name}' : 'PersonaOS ${update.name} is available',
            ),
            subtitle: Text(
              update.forApp
                  ? 'Update installs it over this one; your settings stay.'
                  : 'Update the server from Settings → Updates on the web.',
            ),
            trailing: IconButton(tooltip: 'Dismiss', icon: const Icon(Icons.close), onPressed: widget.onDismiss),
          ),
          if (_working)
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 0, 16, 12),
              child: LinearProgressIndicator(key: const Key('update-progress'), value: _progress),
            ),
          if (canInstall && !_working)
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 0, 16, 12),
              child: Align(
                alignment: Alignment.centerRight,
                child: FilledButton.icon(
                  key: const Key('update-install'),
                  onPressed: () => unawaited(_install()),
                  icon: const Icon(Icons.download),
                  label: const Text('Update'),
                ),
              ),
            ),
        ],
      ),
    );
  }
}
