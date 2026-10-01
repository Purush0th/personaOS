import 'dart:convert';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';

import '../api/personaos_api.dart';

/// Push notifications for this install: the two Firebase files, uploaded from the phone as the
/// web's Settings does. The server checks they belong to the same project, encrypts the key and
/// never sends it back.
class PushConfigSection extends StatefulWidget {
  const PushConfigSection({super.key, required this.api});

  final PersonaOsApi api;

  @override
  State<PushConfigSection> createState() => _PushConfigSectionState();
}

class _PushConfigSectionState extends State<PushConfigSection> {
  PushStatus? _status;
  bool _busy = false;

  /// The picked files: (name, contents).
  (String, String)? _serviceAccount;
  (String, String)? _googleServices;

  @override
  void initState() {
    super.initState();
    widget.api.getPushStatus().then((s) {
      if (mounted) setState(() => _status = s);
    }, onError: (_) {
      // An older server has no push setup to show; the section then says nothing about its state.
    });
  }

  void _show(String message) {
    if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));
  }

  Future<(String, String)?> _pick() async {
    final picked = await FilePicker.platform.pickFiles(
      type: FileType.custom,
      allowedExtensions: const ['json'],
      withData: true,
    );
    final file = picked?.files.single;
    if (file?.bytes == null) return null;
    try {
      return (file!.name, utf8.decode(file.bytes!));
    } on FormatException {
      _show('${file!.name} is not a text file.');
      return null;
    }
  }

  Future<void> _run(Future<PushStatus?> Function() action, String done) async {
    setState(() => _busy = true);
    try {
      final status = await action();
      if (!mounted) return;
      setState(() {
        if (status != null) _status = status;
        _serviceAccount = null;
        _googleServices = null;
      });
      _show(done);
    } on ApiException catch (e) {
      _show(e.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _upload() => _run(
        () => widget.api.setPushConfig(serviceAccountJson: _serviceAccount!.$2, googleServicesJson: _googleServices!.$2),
        'Push is on.',
      );

  Future<void> _remove() async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Turn off push?'),
        content: const Text('Reminders and briefs wait until push is set up again.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Turn off')),
        ],
      ),
    );
    if (ok != true) return;
    await _run(() async {
      await widget.api.clearPushConfig();
      return PushStatus(configured: false);
    }, 'Push is off.');
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final small = theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.onSurfaceVariant);
    final status = _status;
    final configured = status?.configured ?? false;

    return Card(
      key: const Key('push-config'),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (status != null)
              Text(
                configured
                    ? 'On · Firebase project ${status.projectId ?? ''}'
                    : 'Off · reminders and briefs wait until push is set up',
                style: theme.textTheme.titleSmall,
              ),
            const SizedBox(height: 8),
            Text(
              'PersonaOS uses your own Firebase project, so no key is shared with anyone. Both files '
              'come from the Firebase console: the service account key (Project settings → Service '
              'accounts → Generate new private key) and google-services.json (Project settings → '
              'General → Android app com.personaos.personaos_mobile).',
              style: small,
            ),
            const SizedBox(height: 12),
            _FileRow(
              icon: Icons.key,
              label: 'Service account key',
              picked: _serviceAccount?.$1,
              onPick: _busy ? null : () async {
                final file = await _pick();
                if (file != null) setState(() => _serviceAccount = file);
              },
            ),
            _FileRow(
              icon: Icons.android,
              label: 'google-services.json',
              picked: _googleServices?.$1,
              onPick: _busy ? null : () async {
                final file = await _pick();
                if (file != null) setState(() => _googleServices = file);
              },
            ),
            const SizedBox(height: 8),
            Text('The key is encrypted on the server and never shown again — uploading replaces it.', style: small),
            const SizedBox(height: 12),
            Wrap(
              alignment: WrapAlignment.end,
              spacing: 8,
              runSpacing: 8,
              children: [
                if (configured) TextButton(onPressed: _busy ? null : _remove, child: const Text('Turn off')),
                FilledButton(
                  onPressed: _busy || _serviceAccount == null || _googleServices == null ? null : _upload,
                  child: Text(_busy
                      ? 'Saving…'
                      : configured
                          ? 'Replace Firebase files'
                          : 'Turn on push'),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class _FileRow extends StatelessWidget {
  const _FileRow({required this.icon, required this.label, required this.picked, required this.onPick});

  final IconData icon;
  final String label;
  final String? picked;
  final VoidCallback? onPick;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Wrap(
        spacing: 8,
        crossAxisAlignment: WrapCrossAlignment.center,
        children: [
          OutlinedButton.icon(onPressed: onPick, icon: Icon(icon, size: 18), label: Text(label)),
          if (picked != null) Text(picked!, style: Theme.of(context).textTheme.bodySmall),
        ],
      ),
    );
  }
}
