import 'package:flutter/material.dart';

import '../api/personaos_api.dart';

/// The server's optional speech service (speech-to-text and text-to-speech over the
/// OpenAI-style audio API), set up from the phone as on the web's Settings. It has its own key
/// and its own test, and saves apart from "Save settings".
class SpeechConfigSection extends StatefulWidget {
  const SpeechConfigSection({super.key, required this.api, required this.status, required this.onChanged});

  final PersonaOsApi api;
  final SpeechStatus status;

  /// Called with the saved status, so the phone's own voice switches follow it.
  final ValueChanged<SpeechStatus> onChanged;

  @override
  State<SpeechConfigSection> createState() => _SpeechConfigSectionState();
}

class _SpeechConfigSectionState extends State<SpeechConfigSection> {
  final _baseUrl = TextEditingController();
  final _sttModel = TextEditingController();
  final _ttsModel = TextEditingController();
  final _ttsVoice = TextEditingController();

  /// Blank keeps the stored key: it is never sent back to the phone.
  final _apiKey = TextEditingController();

  bool _busy = false;
  bool _testing = false;
  ConnectionTest? _result;

  @override
  void initState() {
    super.initState();
    _fill(widget.status);
  }

  @override
  void dispose() {
    for (final c in [_baseUrl, _sttModel, _ttsModel, _ttsVoice, _apiKey]) {
      c.dispose();
    }
    super.dispose();
  }

  void _fill(SpeechStatus s) {
    _baseUrl.text = s.baseUrl ?? '';
    _sttModel.text = s.sttModel ?? '';
    _ttsModel.text = s.ttsModel ?? '';
    _ttsVoice.text = s.ttsVoice ?? '';
    _apiKey.clear();
  }

  String? get _key => _apiKey.text.trim().isEmpty ? null : _apiKey.text.trim();

  void _show(String message) {
    if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));
  }

  Future<void> _test() async {
    setState(() {
      _busy = true;
      _testing = true;
      _result = null;
    });
    try {
      final result = await widget.api.testSpeech(
        baseUrl: _baseUrl.text.trim(),
        sttModel: _sttModel.text.trim(),
        ttsModel: _ttsModel.text.trim(),
        ttsVoice: _ttsVoice.text.trim(),
        apiKey: _key,
      );
      if (mounted) setState(() => _result = result);
    } on ApiException catch (e) {
      if (mounted) setState(() => _result = ConnectionTest(ok: false, message: e.message));
    } finally {
      if (mounted) {
        setState(() {
          _busy = false;
          _testing = false;
        });
      }
    }
  }

  Future<void> _save({bool forgetKey = false}) async {
    setState(() => _busy = true);
    try {
      final saved = forgetKey
          ? await widget.api.updateSpeech(apiKey: '')
          : await widget.api.updateSpeech(
              baseUrl: _baseUrl.text.trim(),
              sttModel: _sttModel.text.trim(),
              ttsModel: _ttsModel.text.trim(),
              ttsVoice: _ttsVoice.text.trim(),
              apiKey: _key,
            );
      if (!mounted) return;
      setState(() => _fill(saved));
      widget.onChanged(saved);
      _show(forgetKey ? 'Key removed.' : 'Speech settings saved.');
    } on ApiException catch (e) {
      _show(e.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final s = widget.status;
    final on = s.speechToText || s.textToSpeech;
    final what = s.speechToText && s.textToSpeech
        ? 'speech-to-text and text-to-speech'
        : s.speechToText
            ? 'speech-to-text only'
            : 'text-to-speech only';

    return Card(
      key: const Key('speech-config'),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(on ? 'On · $what' : 'Off · the phone uses its own speech engine',
                style: theme.textTheme.titleSmall),
            const SizedBox(height: 8),
            Text(
              'Optional. A speech server with an OpenAI-style audio API (faster-whisper, Kokoro, '
              'OpenAI…) transcribes what you say and reads replies back. The phone sends audio only '
              'to PersonaOS, which calls the service.',
              style: theme.textTheme.bodySmall?.copyWith(color: scheme.onSurfaceVariant),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _baseUrl,
              keyboardType: TextInputType.url,
              decoration: const InputDecoration(
                labelText: 'Address',
                hintText: 'http://localhost:8000/v1',
                helperText: 'Blank turns the speech service off.',
              ),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _sttModel,
              decoration: const InputDecoration(labelText: 'Speech-to-text model', hintText: 'whisper-1'),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _ttsModel,
              decoration: const InputDecoration(labelText: 'Text-to-speech model', hintText: 'tts-1'),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _ttsVoice,
              decoration: const InputDecoration(labelText: 'Voice', hintText: 'alloy'),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _apiKey,
              obscureText: true,
              autocorrect: false,
              enableSuggestions: false,
              decoration: InputDecoration(
                labelText: 'API key',
                helperMaxLines: 2,
                helperText: s.hasApiKey ? 'A key is stored. Leave blank to keep it.' : 'Blank for keyless servers.',
              ),
            ),
            if (_result != null) ...[
              const SizedBox(height: 10),
              Row(
                children: [
                  Icon(_result!.ok ? Icons.check_circle : Icons.error,
                      size: 20, color: _result!.ok ? scheme.primary : scheme.error),
                  const SizedBox(width: 8),
                  Expanded(child: Text(_result!.message)),
                ],
              ),
            ],
            const SizedBox(height: 12),
            Wrap(
              alignment: WrapAlignment.end,
              spacing: 8,
              runSpacing: 8,
              children: [
                if (s.hasApiKey)
                  TextButton(onPressed: _busy ? null : () => _save(forgetKey: true), child: const Text('Remove key')),
                ListenableBuilder(
                  listenable: _baseUrl,
                  builder: (context, _) => OutlinedButton(
                    onPressed: _busy || _baseUrl.text.trim().isEmpty ? null : _test,
                    child: Text(_testing ? 'Testing…' : 'Test'),
                  ),
                ),
                FilledButton(onPressed: _busy ? null : _save, child: const Text('Save speech')),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
