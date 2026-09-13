import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:local_auth/local_auth.dart';

/// Remembers the admin credentials behind the device's fingerprint.
///
/// PersonaOS issues a short-lived access token and has no refresh endpoint, so
/// storing the token alone would still force a password roughly once a day.
/// The credentials are kept instead, and the app re-logs in silently once the
/// fingerprint check passes.
///
/// That is a deliberate trade: a password now lives on the phone. It is held in
/// `EncryptedSharedPreferences`, whose key is generated in the Android Keystore
/// and cannot be exported, so reading it means unlocking the device — and it is
/// only ever read after a successful biometric check. It is wiped on logout and
/// whenever the server rejects it, so a changed password cannot linger.
class AuthVault {
  AuthVault({FlutterSecureStorage? storage, LocalAuthentication? auth})
      : _storage = storage ??
            const FlutterSecureStorage(
              aOptions: AndroidOptions(encryptedSharedPreferences: true),
            ),
        _auth = auth ?? LocalAuthentication();

  static const _usernameKey = 'auth_username';
  static const _passwordKey = 'auth_password';

  final FlutterSecureStorage _storage;
  final LocalAuthentication _auth;

  /// Whether this device can actually prompt for a fingerprint or face.
  ///
  /// `isDeviceSupported` alone is not enough: a phone with the hardware but no
  /// enrolled fingerprint reports support and then fails at the prompt.
  Future<bool> get isBiometricAvailable async {
    try {
      if (!await _auth.isDeviceSupported()) return false;
      if (!await _auth.canCheckBiometrics) return false;
      return (await _auth.getAvailableBiometrics()).isNotEmpty;
    } on Exception {
      return false;
    }
  }

  /// Whether credentials have been saved for fingerprint sign-in.
  Future<bool> get hasSavedCredentials async =>
      await _storage.read(key: _usernameKey) != null;

  Future<void> save(String username, String password) async {
    await _storage.write(key: _usernameKey, value: username);
    await _storage.write(key: _passwordKey, value: password);
  }

  Future<void> clear() async {
    await _storage.delete(key: _usernameKey);
    await _storage.delete(key: _passwordKey);
  }

  /// Prompts for a fingerprint and returns the stored credentials, or null when
  /// the user cancels, the check fails, or nothing is saved.
  ///
  /// The prompt is the gate: the credentials are read only after it passes.
  Future<({String username, String password})?> unlock(String reason) async {
    if (!await hasSavedCredentials) return null;

    final bool passed;
    try {
      passed = await _auth.authenticate(
        localizedReason: reason,
        options: const AuthenticationOptions(
          biometricOnly: false, // Let the device PIN work as the fallback.
          stickyAuth: true,
        ),
      );
    } on Exception {
      // No enrolled biometric, hardware unavailable, too many attempts. The
      // caller falls back to the password form rather than locking the user out.
      return null;
    }
    if (!passed) return null;

    final username = await _storage.read(key: _usernameKey);
    final password = await _storage.read(key: _passwordKey);
    if (username == null || password == null) return null;

    return (username: username, password: password);
  }
}
