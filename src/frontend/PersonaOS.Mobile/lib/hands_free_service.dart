import 'package:flutter/services.dart';

/// Keeps hands-free listening with the app in the background or the screen off.
///
/// Android lets an app use the microphone out of sight only while it runs a foreground service,
/// which shows a "Hands-free is on" notification with an End button (HandsFreeService.kt). The
/// conversation stays in the chat screen; this only keeps it allowed to listen and speak. Off
/// Android, or where the service cannot start, hands-free still works with the app open.
class HandsFreeService {
  HandsFreeService._();

  static final instance = HandsFreeService._();

  static const _channel = MethodChannel('personaos/hands_free');

  void Function()? _onEnded;

  /// Starts the service; [onEnded] runs when the notification's End button is pressed.
  Future<void> start({required void Function() onEnded}) async {
    _onEnded = onEnded;
    _channel.setMethodCallHandler((call) async {
      if (call.method == 'ended') _onEnded?.call();
    });
    try {
      await _channel.invokeMethod<bool>('start', {'text': 'Talk to your assistant any time.'});
    } on MissingPluginException {
      // Not Android (or a test): nothing to keep alive.
    } on PlatformException {
      // The app keeps working in the foreground.
    }
  }

  Future<void> stop() async {
    _onEnded = null;
    try {
      await _channel.invokeMethod<void>('stop');
    } on MissingPluginException {
      // Not Android (or a test).
    } on PlatformException {
      // Already gone.
    }
  }
}
