import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/screens/attachments_section.dart';
import 'package:personaos_mobile/screens/goal_view.dart';
import 'package:personaos_mobile/screens/planner_screen.dart';
import 'package:personaos_mobile/screens/speech_config_section.dart';
import 'package:personaos_mobile/theme_choice.dart';
import 'package:personaos_mobile/update_check.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// What the web app could do and the phone could not: attachments, goal editing, the planner's
/// day picker and links, the speech service setup, the theme choice and the update notice.
Map<String, dynamic> _goalJson({String status = 'active'}) => {
      'id': 4,
      'key': 'GOAL-4',
      'title': 'October',
      'periodType': 'month',
      'slot': 'Oct 2026',
      'periodStart': '2026-10-01',
      'periodEnd': '2026-10-31',
      'status': status,
      'priority': 'high',
      'progress': 0,
      'effectiveProgress': 50,
      'taskCount': 0,
      'doneTaskCount': 0,
      'description': 'Get the paperwork done.',
      'tasks': <Map<String, dynamic>>[],
    };

class ParityApi extends PersonaOsApi {
  ParityApi() : super(serverUrl: 'http://fake');

  final calls = <String>[];
  final attachments = <WorkItemAttachment>[
    WorkItemAttachment(
      id: 9,
      fileName: 'p60.pdf',
      contentType: 'application/pdf',
      sizeBytes: 2048,
      createdAtUtc: DateTime.utc(2026, 9, 30),
    ),
  ];

  @override
  Future<List<WorkItemAttachment>> getAttachments(String itemType, String key) async {
    calls.add('attachments $itemType $key');
    return List.of(attachments);
  }

  @override
  Future<void> deleteAttachment(int id) async {
    calls.add('delete attachment $id');
    attachments.removeWhere((a) => a.id == id);
  }

  @override
  Future<List<WorkItemComment>> getComments(String itemType, String key) async => [];

  @override
  Future<Goal> getGoal(String key) async => Goal.fromJson(_goalJson());

  @override
  Future<void> updateGoal(int id, {String? title, String? description, String? priority}) async =>
      calls.add('goal $id title=${title ?? '-'} description=${description ?? '-'} priority=${priority ?? '-'}');

  @override
  Future<void> setGoalStatus(int id, String status) async => calls.add('goal $id $status');

  final days = <String>[];

  @override
  Future<List<PlannerItem>> getPlannerDay(String date) async {
    days.add(date);
    return [
      PlannerItem(
        id: 1,
        title: 'File taxes',
        date: date,
        scheduledTime: null,
        status: 'planned',
        goalTitle: 'October',
        goalKey: 'GOAL-4',
        taskKey: 'TASK-7',
      ),
    ];
  }

  @override
  Future<SpeechStatus> updateSpeech(
      {String? baseUrl, String? sttModel, String? ttsModel, String? ttsVoice, String? apiKey}) async {
    calls.add('speech $baseUrl $sttModel key=${apiKey ?? '(kept)'}');
    return SpeechStatus(speechToText: true, textToSpeech: false, baseUrl: baseUrl, sttModel: sttModel, hasApiKey: true);
  }

  @override
  Future<ConnectionTest> testSpeech(
      {String? baseUrl, String? sttModel, String? ttsModel, String? ttsVoice, String? apiKey}) async {
    calls.add('test speech $baseUrl');
    return ConnectionTest(ok: true, message: 'Speech-to-text works.');
  }
}

void main() {
  setUp(() => SharedPreferences.setMockInitialValues({}));

  void tall(WidgetTester tester) {
    tester.view.physicalSize = const Size(800, 1400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
  }

  Future<void> pump(WidgetTester tester, Widget child, {bool scrolls = true}) async {
    tall(tester);
    await tester.pumpWidget(MaterialApp(home: Scaffold(body: scrolls ? SingleChildScrollView(child: child) : child)));
    await tester.pumpAndSettle();
  }

  group('attachments', () {
    testWidgets('lists the files and deletes one after asking', (tester) async {
      final api = ParityApi();
      await pump(tester, AttachmentsSection(api: api, itemType: 'task', itemKey: 'TASK-7'));

      expect(find.text('Attachments · 1'), findsOneWidget);
      expect(find.text('p60.pdf'), findsOneWidget);
      expect(find.textContaining('2 KB'), findsOneWidget);

      await tester.tap(find.byTooltip('Delete attachment'));
      await tester.pumpAndSettle();
      await tester.tap(find.widgetWithText(FilledButton, 'Delete'));
      await tester.pumpAndSettle();

      expect(api.calls, contains('delete attachment 9'));
      expect(find.text('No attachments yet.'), findsOneWidget);
    });
  });

  group('goal', () {
    testWidgets('is edited: title, description and priority, only what changed', (tester) async {
      final api = ParityApi();
      await pump(tester, GoalView(api: api, sheet: false, goal: Goal.fromJson(_goalJson())), scrolls: false);

      expect(find.text('Priority: High'), findsOneWidget);
      expect(find.byType(AttachmentsSection), findsOneWidget);

      await tester.tap(find.byKey(const Key('edit-goal')));
      await tester.pumpAndSettle();
      await tester.enterText(find.widgetWithText(TextField, 'Title'), 'October paperwork');
      await tester.tap(find.widgetWithText(FilledButton, 'Save'));
      await tester.pumpAndSettle();

      expect(api.calls, contains('goal 4 title=October paperwork description=- priority=-'));
    });

    testWidgets('completes, and a completed one reopens', (tester) async {
      final api = ParityApi();
      await pump(tester, GoalView(api: api, sheet: false, goal: Goal.fromJson(_goalJson())), scrolls: false);
      await tester.tap(find.text('Complete'));
      await tester.pumpAndSettle();
      expect(api.calls, contains('goal 4 completed'));

      await pump(tester, GoalView(api: api, sheet: false, goal: Goal.fromJson(_goalJson(status: 'completed'))), scrolls: false);
      await tester.tap(find.text('Reopen'));
      await tester.pumpAndSettle();
      expect(api.calls, contains('goal 4 active'));
    });
  });

  group('planner', () {
    testWidgets('jumps to any day from the date picker', (tester) async {
      final api = ParityApi();
      tall(tester);
      await tester.pumpWidget(MaterialApp(home: PlannerScreen(api: api, boardEnabled: true)));
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('planner-day')));
      await tester.pumpAndSettle();
      expect(find.byType(DatePickerDialog), findsOneWidget);
      // Switch to typing the date: the calendar grid depends on today.
      await tester.tap(find.byIcon(Icons.edit_outlined));
      await tester.pumpAndSettle();
      await tester.enterText(find.byType(TextField).last, '12/25/2026');
      await tester.tap(find.text('OK'));
      await tester.pumpAndSettle();

      expect(api.days.last, '2026-12-25');
    });

    testWidgets("an item's task key is a link", (tester) async {
      final api = ParityApi();
      tall(tester);
      await tester.pumpWidget(MaterialApp(home: PlannerScreen(api: api, boardEnabled: true)));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('planner-task-TASK-7')), findsOneWidget);
    });
  });

  group('speech service', () {
    testWidgets('tests and saves, keeping the stored key when the field is blank', (tester) async {
      final api = ParityApi();
      SpeechStatus? saved;
      await pump(
        tester,
        SpeechConfigSection(
          api: api,
          status: SpeechStatus(speechToText: false, textToSpeech: false, hasApiKey: true),
          onChanged: (s) => saved = s,
        ),
      );

      expect(find.text('Off · the phone uses its own speech engine'), findsOneWidget);
      expect(find.text('Remove key'), findsOneWidget);
      await tester.enterText(find.widgetWithText(TextField, 'Address'), 'http://speech:8000/v1');
      await tester.enterText(find.widgetWithText(TextField, 'Speech-to-text model'), 'whisper-1');
      await tester.pump();
      await tester.tap(find.widgetWithText(OutlinedButton, 'Test'));
      await tester.pumpAndSettle();
      expect(find.text('Speech-to-text works.'), findsOneWidget);

      await tester.tap(find.widgetWithText(FilledButton, 'Save speech'));
      await tester.pumpAndSettle();

      expect(api.calls, containsAll(['test speech http://speech:8000/v1', 'speech http://speech:8000/v1 whisper-1 key=(kept)']));
      expect(saved?.speechToText, isTrue);
    });
  });

  group('appearance', () {
    test('the theme choice is kept on the phone', () async {
      await saveThemeChoice(ThemeMode.dark);
      themeChoice.value = ThemeMode.system;
      await loadThemeChoice();
      expect(themeChoice.value, ThemeMode.dark);
    });
  });

  group('update notice', () {
    test('versions compare by number', () {
      expect(isNewer('0.10.0', '0.9.3'), isTrue);
      expect(isNewer('1.0', '1.0.0'), isFalse);
      expect(isNewer('0.9.2', '0.9.3'), isFalse);
    });

    MockClient release(String tag, {int status = 200}) => MockClient((_) async => http.Response(
        jsonEncode({'tag_name': tag, 'name': 'PersonaOS $tag', 'html_url': 'https://github.com/o/r/releases/tag/$tag'}),
        status));

    test('offers a newer release, and not once it is dismissed', () async {
      final update = await checkForUpdate(currentVersion: '0.9.0', repository: 'o/r', client: release('v0.10.0'));
      expect(update?.version, '0.10.0');

      await dismissUpdate(update!);
      expect(await checkForUpdate(currentVersion: '0.9.0', repository: 'o/r', client: release('v0.10.0')), isNull);
    });

    test('stays silent when up to date, without a repository, or when GitHub fails', () async {
      expect(await checkForUpdate(currentVersion: '0.10.0', repository: 'o/r', client: release('v0.10.0')), isNull);
      expect(await checkForUpdate(currentVersion: '0.9.0', repository: null, client: release('v0.10.0')), isNull);
      expect(await checkForUpdate(currentVersion: '0.9.0', repository: 'o/r', client: release('x', status: 404)), isNull);
    });
  });
}
