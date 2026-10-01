import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Light, dark, or following the phone: this phone's choice, as the web's dark-mode switch is
/// that browser's. The app rebuilds its theme whenever it changes.
final themeChoice = ValueNotifier<ThemeMode>(ThemeMode.system);

const _themeKey = 'appearance.theme';

/// Reads the stored choice; anything unknown follows the phone.
Future<void> loadThemeChoice() async {
  final prefs = await SharedPreferences.getInstance();
  themeChoice.value = ThemeMode.values.asNameMap()[prefs.getString(_themeKey)] ?? ThemeMode.system;
}

Future<void> saveThemeChoice(ThemeMode mode) async {
  themeChoice.value = mode;
  final prefs = await SharedPreferences.getInstance();
  await prefs.setString(_themeKey, mode.name);
}
