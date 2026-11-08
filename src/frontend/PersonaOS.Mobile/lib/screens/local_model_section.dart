import 'dart:async';

import 'package:flutter/material.dart';

import '../api/personaos_api.dart';
import '../phosphor_icons.dart';

/// The recommended local model, offered when Ollama is the provider. It looks for the owner's
/// Ollama through the server, says what the model is (size, licence) before anything downloads,
/// and downloads it into that Ollama only when asked. Installing Ollama itself stays the owner's job.
class LocalModelSection extends StatefulWidget {
  const LocalModelSection({super.key, required this.api, required this.address, required this.onChosen});

  final PersonaOsApi api;

  /// The base URL typed so far; blank lets the server look in the usual places.
  final String Function() address;

  /// Called with where Ollama is and the model's name, to fill in the provider fields.
  final void Function(String address, String model) onChosen;

  @override
  State<LocalModelSection> createState() => _LocalModelSectionState();
}

class _LocalModelSectionState extends State<LocalModelSection> {
  LocalModelStatus? _status;
  bool _looking = false;
  bool _pulling = false;
  String _step = '';
  double? _fraction;
  String? _error;
  StreamSubscription<ModelPullLine>? _download;

  @override
  void dispose() {
    unawaited(_download?.cancel());
    super.dispose();
  }

  Future<void> _look() async {
    setState(() {
      _looking = true;
      _error = null;
    });
    try {
      final status = await widget.api.getLocalModel(address: widget.address());
      if (mounted) setState(() => _status = status);
    } on Object catch (e) {
      if (mounted) setState(() => _error = e is ApiException ? e.message : 'Could not ask the server about Ollama.');
    } finally {
      if (mounted) setState(() => _looking = false);
    }
  }

  void _start() {
    final address = _status?.address;
    if (address == null) return;
    setState(() {
      _error = null;
      _pulling = true;
      _step = 'Starting the download…';
      _fraction = null;
    });
    _download = widget.api
        .pullLocalModel(address)
        .listen(
          _show,
          onError: (Object e) =>
              _finish(e is ApiException ? e.message : 'The download stopped. Try again: it resumes where it left off.'),
          onDone: () => _finish(null),
          cancelOnError: true,
        );
  }

  void _show(ModelPullLine line) {
    if (line.error != null) {
      unawaited(_download?.cancel());
      _finish(line.error);
      return;
    }
    if (line.done || !mounted) return;
    setState(() {
      _step = line.status ?? '';
      _fraction = line.fraction;
    });
  }

  void _cancel() {
    unawaited(_download?.cancel());
    _finish('Download cancelled. Starting again resumes it.');
  }

  void _finish(String? error) {
    _download = null;
    if (!mounted || !_pulling) return;
    setState(() {
      _pulling = false;
      _error = error;
    });
    if (error == null) unawaited(_look());
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final status = _status;
    final small = theme.textTheme.bodySmall?.copyWith(color: scheme.onSurfaceVariant);

    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(color: scheme.surfaceContainer, borderRadius: BorderRadius.circular(12)),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        spacing: 8,
        children: [
          Text(status?.displayName ?? 'PersonaOS model', style: theme.textTheme.titleSmall),
          if (status != null) ...[
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              spacing: 6,
              children: [
                Icon(
                  status.found ? PhosphorIconsRegular.checkCircle : PhosphorIconsRegular.warningCircle,
                  size: 18,
                  color: status.found ? scheme.primary : scheme.error,
                ),
                Expanded(child: Text(status.message)),
              ],
            ),
            Text(
              '${(status.sizeBytes / 1e9).toStringAsFixed(1)} GB download · licence ${status.license}. ${status.notice}',
              style: small,
            ),
          ],
          if (_pulling) ...[
            LinearProgressIndicator(value: _fraction),
            Text(_fraction == null ? _step : '$_step · ${(_fraction! * 100).toStringAsFixed(0)}%', style: small),
          ],
          if (_error != null) Text(_error!, style: TextStyle(color: scheme.error)),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              if (_pulling)
                TextButton(onPressed: _cancel, child: const Text('Cancel download'))
              else if (status?.installed == true)
                FilledButton.icon(
                  onPressed: () => widget.onChosen(status!.address!, status.model),
                  icon: const Icon(PhosphorIconsRegular.checkCircle),
                  label: const Text('Use this model'),
                )
              else if (status?.found == true)
                FilledButton.icon(
                  onPressed: _start,
                  icon: const Icon(PhosphorIconsRegular.downloadSimple),
                  label: const Text('Download'),
                ),
              if (!_pulling)
                TextButton.icon(
                  onPressed: _looking ? null : _look,
                  icon: const Icon(PhosphorIconsRegular.magnifyingGlass),
                  label: Text(status == null ? 'Find Ollama' : 'Look again'),
                ),
            ],
          ),
        ],
      ),
    );
  }
}
