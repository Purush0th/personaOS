import 'package:flutter/material.dart';

import '../api/personaos_api.dart';
import '../auth_vault.dart';

/// Module keys the server accepts. Unknown keys are ignored server-side, and
/// this list matches the web app's so the two screens cannot drift apart.
const _modules = <String, String>{
  'goals': 'Goals',
  'board': 'Sprint board',
  'planner': 'Planner',
  'reminders': 'Reminders',
  'docs': 'Documents',
  'voice': 'Voice',
  'proactive': 'Proactive briefs',
};

const _providers = <String, String>{
  'anthropic': 'Anthropic (Claude)',
  'ollama': 'Ollama (local, recommended)',
  'openai_compatible': 'OpenAI-compatible (OpenAI, Groq…)',
};

/// The longest "about you" the server accepts: it is sent with every message.
const _aboutMeMaxLength = 2000;

/// Example address for each provider reached at one; Anthropic has a fixed service.
const _baseUrlHints = <String, String>{
  'ollama': 'http://localhost:11434',
  'openai_compatible': 'https://api.openai.com/v1',
};

/// Instance settings: assistant nickname and persona, AI provider, modules.
///
/// The same settings the web app edits, so the phone is not a read-only client.
/// The provider key is write-only by design — the server never sends it back,
/// so a blank field here means "keep the stored key", never "clear it".
class SettingsScreen extends StatefulWidget {
  const SettingsScreen({super.key, required this.api});

  final PersonaOsApi api;

  @override
  State<SettingsScreen> createState() => _SettingsScreenState();
}

class _SettingsScreenState extends State<SettingsScreen> {
  final _nickname = TextEditingController();
  final _persona = TextEditingController();
  final _aboutMe = TextEditingController();

  /// As loaded, so saving only writes the profile when it changed.
  String _savedAboutMe = '';
  final _model = TextEditingController();
  final _baseUrl = TextEditingController();
  final _timeZone = TextEditingController();
  final _apiKey = TextEditingController();

  String _provider = 'anthropic';
  Map<String, bool> _features = {};
  bool _hasApiKey = false;

  bool _loading = true;
  bool _saving = false;
  bool _testing = false;
  String? _loadError;
  ConnectionTest? _testResult;

  /// Every provider but Anthropic is reached at an address the user gives.
  bool get _needsBaseUrl => _baseUrlHints.containsKey(_provider);

  final _vault = AuthVault();
  bool _hasSavedSignIn = false;

  @override
  void initState() {
    super.initState();
    _load();
    _vault.hasSavedCredentials.then((saved) {
      if (mounted) setState(() => _hasSavedSignIn = saved);
    });
  }

  Future<void> _forgetSignIn() async {
    await _vault.clear();
    if (!mounted) return;
    setState(() => _hasSavedSignIn = false);
    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(content: Text('Saved sign-in removed from this phone.')),
    );
  }

  @override
  void dispose() {
    _nickname.dispose();
    _persona.dispose();
    _aboutMe.dispose();
    _model.dispose();
    _baseUrl.dispose();
    _timeZone.dispose();
    _apiKey.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _loadError = null;
    });
    try {
      final (settings, aboutMe) = await (widget.api.getSettings(), widget.api.getAboutMe()).wait;
      if (!mounted) return;
      setState(() {
        _aboutMe.text = _savedAboutMe = aboutMe;
        _nickname.text = settings.assistantNickname;
        _persona.text = settings.personaTemplate;
        _model.text = settings.aiModel;
        _baseUrl.text = settings.aiBaseUrl;
        _timeZone.text = settings.timeZone;
        _provider = _providers.containsKey(settings.aiProvider)
            ? settings.aiProvider
            : 'anthropic';
        _features = {
          for (final key in _modules.keys) key: settings.features[key] ?? false,
        };
        _hasApiKey = settings.hasApiKey;
        _loading = false;
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _loadError = e.message;
        _loading = false;
      });
    }
  }

  /// Only the fields the server should change. A blank API key is omitted
  /// rather than sent, because the server rejects a blank one and would
  /// otherwise turn "I did not touch that field" into an error.
  Map<String, Object?> _changes() => {
        'assistantNickname': _nickname.text.trim(),
        'personaTemplate': _persona.text.trim(),
        'aiProvider': _provider,
        'aiModel': _model.text.trim(),
        'aiBaseUrl': _needsBaseUrl ? _baseUrl.text.trim() : '',
        'timeZone': _timeZone.text.trim(),
        'features': _features,
        if (_apiKey.text.trim().isNotEmpty) 'anthropicApiKey': _apiKey.text.trim(),
      };

  Future<void> _save() async {
    setState(() => _saving = true);
    try {
      await widget.api.updateSettings(_changes());
      if (_aboutMe.text.trim() != _savedAboutMe) await widget.api.updateAboutMe(_aboutMe.text.trim());
      if (!mounted) return;
      _apiKey.clear();
      ScaffoldMessenger.of(context)
          .showSnackBar(const SnackBar(content: Text('Settings saved.')));
      await _load();
    } on ApiException catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(e.message)));
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  Future<void> _test() async {
    setState(() {
      _testing = true;
      _testResult = null;
    });
    try {
      final result = await widget.api.testConnection({
        'aiProvider': _provider,
        'aiModel': _model.text.trim(),
        'aiBaseUrl': _needsBaseUrl ? _baseUrl.text.trim() : '',
        if (_apiKey.text.trim().isNotEmpty) 'anthropicApiKey': _apiKey.text.trim(),
      });
      if (mounted) setState(() => _testResult = result);
    } on ApiException catch (e) {
      if (mounted) {
        setState(() => _testResult = ConnectionTest(ok: false, message: e.message));
      }
    } finally {
      if (mounted) setState(() => _testing = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    return Scaffold(
      appBar: AppBar(title: const Text('Settings')),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : _loadError != null
              ? Center(
                  child: Padding(
                    padding: const EdgeInsets.all(32),
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Text(_loadError!, textAlign: TextAlign.center),
                        const SizedBox(height: 16),
                        FilledButton(onPressed: _load, child: const Text('Retry')),
                      ],
                    ),
                  ),
                )
              : ListView(
                  padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
                  children: [
                    const _SectionLabel('Assistant'),
                    TextField(
                      controller: _nickname,
                      decoration: const InputDecoration(labelText: 'Nickname'),
                    ),
                    const SizedBox(height: 12),
                    TextField(
                      controller: _persona,
                      minLines: 3,
                      maxLines: 6,
                      decoration: const InputDecoration(
                        labelText: 'Persona / tone',
                        hintText: 'How should the assistant speak to you?',
                      ),
                    ),
                    const SizedBox(height: 12),
                    TextField(
                      controller: _aboutMe,
                      minLines: 3,
                      maxLines: 8,
                      maxLength: _aboutMeMaxLength,
                      decoration: const InputDecoration(
                        labelText: 'About you',
                        hintText: 'What the assistant should always know, e.g. vegetarian, lives in Lisbon.',
                        helperText: 'Sent with every message.',
                      ),
                    ),
                    const SizedBox(height: 12),
                    TextField(
                      controller: _timeZone,
                      decoration: const InputDecoration(
                        labelText: 'Time zone',
                        hintText: 'e.g. Asia/Kolkata',
                      ),
                    ),

                    const _SectionLabel('AI provider'),
                    DropdownButtonFormField<String>(
                      initialValue: _provider,
                      // Without this the longest provider name overflows the
                      // field on a phone-width screen rather than ellipsising.
                      isExpanded: true,
                      decoration: const InputDecoration(labelText: 'Provider'),
                      items: [
                        for (final entry in _providers.entries)
                          DropdownMenuItem(
                            value: entry.key,
                            child: Text(entry.value, overflow: TextOverflow.ellipsis),
                          ),
                      ],
                      onChanged: (value) =>
                          setState(() => _provider = value ?? _provider),
                    ),
                    const SizedBox(height: 12),
                    TextField(
                      controller: _model,
                      decoration: const InputDecoration(
                        labelText: 'Model',
                        hintText: 'e.g. qwen2.5:3b-instruct',
                      ),
                    ),
                    if (_needsBaseUrl) ...[
                      const SizedBox(height: 12),
                      TextField(
                        controller: _baseUrl,
                        keyboardType: TextInputType.url,
                        decoration: InputDecoration(
                          labelText: 'Base URL',
                          hintText: _baseUrlHints[_provider],
                        ),
                      ),
                    ],
                    const SizedBox(height: 12),
                    TextField(
                      controller: _apiKey,
                      obscureText: true,
                      decoration: InputDecoration(
                        labelText: 'API key',
                        helperMaxLines: 2,
                        helperText: _hasApiKey
                            ? 'A key is stored. Leave blank to keep it.'
                            : 'No key stored. Local models may not need one.',
                      ),
                    ),
                    const SizedBox(height: 12),
                    OutlinedButton.icon(
                      onPressed: _testing ? null : _test,
                      icon: _testing
                          ? const SizedBox(
                              width: 16,
                              height: 16,
                              child: CircularProgressIndicator(strokeWidth: 2))
                          : const Icon(Icons.wifi_tethering),
                      label: Text(_testing ? 'Testing…' : 'Test connection'),
                    ),
                    if (_testResult != null) ...[
                      const SizedBox(height: 10),
                      Container(
                        padding: const EdgeInsets.all(14),
                        decoration: BoxDecoration(
                          color: _testResult!.ok
                              ? scheme.primaryContainer
                              : scheme.errorContainer,
                          borderRadius: BorderRadius.circular(14),
                        ),
                        child: Row(
                          children: [
                            Icon(
                              _testResult!.ok ? Icons.check_circle : Icons.error,
                              size: 20,
                              color: _testResult!.ok
                                  ? scheme.onPrimaryContainer
                                  : scheme.onErrorContainer,
                            ),
                            const SizedBox(width: 10),
                            Expanded(
                              child: Text(
                                _testResult!.message,
                                style: TextStyle(
                                  color: _testResult!.ok
                                      ? scheme.onPrimaryContainer
                                      : scheme.onErrorContainer,
                                ),
                              ),
                            ),
                          ],
                        ),
                      ),
                    ],

                    const _SectionLabel('Modules'),
                    Card(
                      child: Column(
                        children: [
                          for (final entry in _modules.entries)
                            SwitchListTile(
                              title: Text(entry.value),
                              value: _features[entry.key] ?? false,
                              onChanged: (on) =>
                                  setState(() => _features[entry.key] = on),
                            ),
                        ],
                      ),
                    ),

                    const SizedBox(height: 24),
                    FilledButton(
                      onPressed: _saving ? null : _save,
                      child: Text(_saving ? 'Saving…' : 'Save settings'),
                    ),

                    if (_hasSavedSignIn) ...[
                      const _SectionLabel('Sign-in'),
                      // The only way to remove a stored password from the
                      // phone. Without it, opting in to fingerprint unlock
                      // would be one-way.
                      OutlinedButton.icon(
                        onPressed: _forgetSignIn,
                        icon: const Icon(Icons.logout),
                        label: const Text('Forget saved sign-in'),
                      ),
                      Padding(
                        padding: const EdgeInsets.only(top: 8, left: 4),
                        child: Text(
                          'Removes the credentials kept for fingerprint unlock. '
                          'You will type your password next time.',
                          style: TextStyle(
                              fontSize: 12, color: scheme.onSurfaceVariant),
                        ),
                      ),
                    ],
                  ],
                ),
    );
  }
}

class _SectionLabel extends StatelessWidget {
  const _SectionLabel(this.text);

  final String text;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(4, 24, 4, 10),
      child: Text(
        text.toUpperCase(),
        style: TextStyle(
          fontSize: 12,
          fontWeight: FontWeight.w700,
          letterSpacing: 0.8,
          color: Theme.of(context).colorScheme.primary,
        ),
      ),
    );
  }
}
