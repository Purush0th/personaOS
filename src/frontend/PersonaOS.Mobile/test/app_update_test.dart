import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:personaos_mobile/app_update.dart';
import 'package:personaos_mobile/update_check.dart';
import 'package:personaos_mobile/update_notice.dart';

const _url = 'https://github.com/Purush0th/personaOS/releases/download/v0.1.0-alpha.10/personaos-v0.1.0-alpha.10.apk';

/// Answers the installer channel as Android would, and records what it was asked.
class _Installer {
  _Installer({this.allowed = true});

  bool allowed;
  final calls = <MethodCall>[];

  void attach(MethodChannel channel) {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger.setMockMethodCallHandler(channel, (call) async {
      calls.add(call);
      return call.method == 'canInstall' ? allowed : null;
    });
  }
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  const channel = MethodChannel('personaos/installer');
  late Directory cache;

  setUp(() => cache = Directory.systemTemp.createTempSync('update-'));
  tearDown(() => cache.deleteSync(recursive: true));

  AppUpdater updater({List<int> apk = const [1, 2, 3, 4]}) => AppUpdater(
    client: MockClient((_) async => http.Response.bytes(apk, 200)),
    channel: channel,
    cache: () async => cache,
  );

  test('only an APK from a release of this project is accepted', () {
    expect(AppUpdater.isReleaseApk(_url, 'Purush0th/personaOS'), isTrue);
    expect(
      AppUpdater.isReleaseApk(
        'http://github.com/Purush0th/personaOS/releases/download/v1/a.apk',
        'Purush0th/personaOS',
      ),
      isFalse,
    );
    expect(
      AppUpdater.isReleaseApk('https://github.com/someone/else/releases/download/v1/a.apk', 'Purush0th/personaOS'),
      isFalse,
    );
    expect(
      AppUpdater.isReleaseApk(
        'https://evil.example/Purush0th/personaOS/releases/download/v1/a.apk',
        'Purush0th/personaOS',
      ),
      isFalse,
    );
  });

  test('the release is downloaded into the update folder and handed to the installer', () async {
    final installer = _Installer()..attach(channel);
    final progress = <double?>[];

    await updater().downloadAndInstall(_url, repository: 'Purush0th/personaOS', onProgress: progress.add);

    final install = installer.calls.single;
    expect(install.method, 'install');
    final path = (install.arguments as Map)['path'] as String;
    expect(File(path).readAsBytesSync(), [1, 2, 3, 4]);
    expect(File(path).parent.path, endsWith('updates'));
    expect(progress.last, 1.0);
  });

  test('anything else is refused before downloading', () async {
    final installer = _Installer()..attach(channel);

    await expectLater(
      updater().downloadAndInstall('https://evil.example/x.apk', repository: 'Purush0th/personaOS'),
      throwsA(isA<AppUpdateException>()),
    );
    expect(installer.calls, isEmpty);
  });

  AvailableUpdate appUpdate() => AvailableUpdate(
    version: '0.1.0-alpha.10',
    name: 'Alpha 10',
    notesUrl: 'https://github.com/Purush0th/personaOS/releases/tag/v0.1.0-alpha.10',
    forApp: true,
    appDownloadUrl: _url,
  );

  Future<void> pump(WidgetTester tester, AppUpdater updater) => tester.pumpWidget(
    MaterialApp(
      home: Scaffold(
        body: UpdateNotice(update: appUpdate(), repository: 'Purush0th/personaOS', onDismiss: () {}, updater: updater),
      ),
    ),
  );

  testWidgets('Update installs the new app from inside this one', (tester) async {
    // Owner, 2026-10-24: "update from the app itself, without manual download and installation".
    final installer = _Installer()..attach(channel);
    await pump(tester, updater());

    await tester.runAsync(() async {
      await tester.tap(find.byKey(const Key('update-install')));
      await Future<void>.delayed(const Duration(milliseconds: 200));
    });
    await tester.pump();

    expect(installer.calls.map((c) => c.method), ['canInstall', 'install']);
  });

  testWidgets('the first time, Android is asked to allow installing apps from PersonaOS', (tester) async {
    final installer = _Installer(allowed: false)..attach(channel);
    await pump(tester, updater());

    await tester.tap(find.byKey(const Key('update-install')));
    await tester.pumpAndSettle();

    expect(installer.calls.map((c) => c.method), ['canInstall', 'openInstallSettings']);
    expect(find.textContaining('Allow PersonaOS to install apps'), findsOneWidget);
  });

  testWidgets('a server update has no Update button on the phone', (tester) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: UpdateNotice(
            update: AvailableUpdate(version: '0.1.0-alpha.10', name: 'Alpha 10', notesUrl: 'https://x'),
            repository: 'Purush0th/personaOS',
            onDismiss: () {},
            updater: updater(),
          ),
        ),
      ),
    );

    expect(find.byKey(const Key('update-install')), findsNothing);
    expect(find.textContaining('Settings → Updates on the web'), findsOneWidget);
  });
}
