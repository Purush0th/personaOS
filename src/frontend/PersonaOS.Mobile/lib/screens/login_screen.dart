import 'package:flutter/material.dart';

import '../api/personaos_api.dart';
import '../auth_vault.dart';

/// Admin login before opening the chat.
///
/// Once the user opts in, the fingerprint prompt appears on open and the
/// password form is only a fallback — for a phone reached many times a day,
/// retyping a password each time is what stops the app being used.
class LoginScreen extends StatefulWidget {
  const LoginScreen({
    super.key,
    required this.api,
    required this.onLoggedIn,
    this.vault,
  });

  final PersonaOsApi api;
  final VoidCallback onLoggedIn;

  /// Injectable so a test can drive the biometric path without a device.
  final AuthVault? vault;

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _username = TextEditingController();
  final _password = TextEditingController();

  late final AuthVault _vault = widget.vault ?? AuthVault();

  String? _error;
  bool _busy = false;

  /// Whether this device can save credentials behind a fingerprint at all.
  bool _biometricAvailable = false;

  /// Whether credentials are already saved, so the fingerprint button shows.
  bool _hasSaved = false;

  /// Opt-in for the next successful password login.
  bool _rememberMe = true;

  @override
  void initState() {
    super.initState();
    _prepare();
  }

  @override
  void dispose() {
    _username.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _prepare() async {
    final available = await _vault.isBiometricAvailable;
    final saved = await _vault.hasSavedCredentials;
    if (!mounted) return;

    setState(() {
      _biometricAvailable = available;
      _hasSaved = saved;
    });

    // Offer the fingerprint straight away rather than making the user tap past
    // a password form they have already opted out of using.
    if (available && saved) await _unlock();
  }

  Future<void> _unlock() async {
    final credentials = await _vault.unlock('Unlock PersonaOS');
    if (credentials == null || !mounted) return;

    setState(() {
      _busy = true;
      _error = null;
    });

    try {
      final error = await widget.api
          .login(credentials.username, credentials.password);
      if (!mounted) return;

      if (error == null) {
        widget.onLoggedIn();
        return;
      }

      // The stored password no longer works — it was changed on the server, or
      // the account is gone. Drop it rather than prompting a fingerprint that
      // can never succeed again.
      await _vault.clear();
      setState(() {
        _busy = false;
        _hasSaved = false;
        _username.text = credentials.username;
        _error = 'Saved sign-in no longer works. Enter your password.';
      });
    } catch (_) {
      // A server that is merely unreachable must NOT wipe the credentials.
      if (mounted) {
        setState(() {
          _busy = false;
          _error = 'Could not reach the server.';
        });
      }
    }
  }

  Future<void> _login() async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final error =
          await widget.api.login(_username.text.trim(), _password.text);
      if (!mounted) return;
      if (error != null) {
        setState(() {
          _busy = false;
          _error = error;
        });
        return;
      }

      if (_biometricAvailable && _rememberMe) {
        await _vault.save(_username.text.trim(), _password.text);
      }
      widget.onLoggedIn();
    } catch (_) {
      if (mounted) {
        setState(() {
          _busy = false;
          _error = 'Could not reach the server.';
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('PersonaOS')),
      body: ListView(
        padding: const EdgeInsets.all(24),
        children: [
          const Text('Log in',
              style: TextStyle(fontSize: 22, fontWeight: FontWeight.bold)),
          const SizedBox(height: 16),
          TextField(
            controller: _username,
            autocorrect: false,
            enableSuggestions: false,
            decoration: const InputDecoration(labelText: 'Username'),
          ),
          const SizedBox(height: 12),
          TextField(
            controller: _password,
            obscureText: true,
            decoration: InputDecoration(
              labelText: 'Password',
              errorText: _error,
            ),
            onSubmitted: (_) => _login(),
          ),
          if (_biometricAvailable && !_hasSaved) ...[
            const SizedBox(height: 4),
            CheckboxListTile(
              value: _rememberMe,
              onChanged: (on) => setState(() => _rememberMe = on ?? false),
              contentPadding: EdgeInsets.zero,
              controlAffinity: ListTileControlAffinity.leading,
              title: const Text('Unlock with fingerprint next time'),
              subtitle: const Text(
                'Your sign-in is kept in the phone’s secure keystore.',
              ),
            ),
          ],
          const SizedBox(height: 16),
          FilledButton(
            onPressed: _busy ? null : _login,
            child: Text(_busy ? 'Signing in…' : 'Log in'),
          ),
          if (_biometricAvailable && _hasSaved) ...[
            const SizedBox(height: 12),
            OutlinedButton.icon(
              onPressed: _busy ? null : _unlock,
              icon: const Icon(Icons.fingerprint),
              label: const Text('Use fingerprint'),
            ),
          ],
        ],
      ),
    );
  }
}
