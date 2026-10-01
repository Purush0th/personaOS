import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/layout.dart';
import 'package:personaos_mobile/screens/chat_screen.dart';
import 'package:personaos_mobile/screens/conversations_screen.dart';
import 'package:personaos_mobile/screens/goals_screen.dart';
import 'package:personaos_mobile/screens/memories_screen.dart';
import 'package:personaos_mobile/screens/planner_screen.dart';
import 'package:personaos_mobile/screens/reminders_screen.dart';
import 'package:personaos_mobile/screens/settings_screen.dart';

/// Serves every screen something to show, without a server.
class _Api extends PersonaOsApi {
  _Api() : super(serverUrl: 'http://test');

  @override
  Future<List<Goal>> getGoals() async => [];

  @override
  Future<List<PlannerItem>> getPlannerDay(String date) async => [];

  @override
  Future<List<Reminder>> getReminders({bool includeCompleted = false}) async => [];

  @override
  Future<List<ConversationSummary>> getConversations() async => [];

  @override
  Future<List<MemoryDto>> getMemories() async => [
        MemoryDto(
          id: 1,
          content: 'Prefers morning workouts before work, ideally a run and some strength training',
          category: 'preference',
          updatedAtUtc: DateTime.utc(2026, 9, 29),
          sourceConversationId: 'abcd1234',
          sourceConversationTitle: 'Planning the week of workouts and runs',
        ),
      ];

  @override
  Future<bool> getMemoryAutoSave() async => true;

  @override
  Future<InstanceSettings> getSettings() async => InstanceSettings.fromJson({
        'assistantNickname': 'Juno',
        'personaTemplate': '',
        'aiProvider': 'ollama',
        'aiModel': 'qwen2.5:3b-instruct',
        'aiBaseUrl': 'http://localhost:11434',
        'timeZone': 'Asia/Kolkata',
        'features': <String, bool>{'voice': true, 'memory': true},
        'hasAnthropicApiKey': false,
      });

  @override
  Future<String> getAboutMe() async => 'Vegetarian.';

  @override
  Future<SpeechStatus> getSpeechStatus() async => SpeechStatus(speechToText: true, textToSpeech: false);
}

/// Phone upright and on its side, tablet upright and on its side (logical pixels).
const _sizes = {
  'phone portrait': Size(390, 844),
  'phone landscape': Size(844, 390),
  'tablet portrait': Size(800, 1280),
  '7-inch tablet portrait': Size(533, 853),
  'tablet landscape': Size(1280, 800),
};

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  setUp(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger.setMockMethodCallHandler(
        const MethodChannel('plugins.it_nomads.com/flutter_secure_storage'), (call) async => null);
  });

  final screens = <String, Widget Function(PersonaOsApi api)>{
    'goals': (api) => GoalsScreen(api: api),
    'planner': (api) => PlannerScreen(api: api),
    'reminders': (api) => RemindersScreen(api: api),
    'memories': (api) => MemoriesScreen(api: api),
    'settings': (api) => SettingsScreen(api: api),
    'conversations': (api) => ConversationsScreen(api: api, assistantNickname: 'Juno'),
    'chat': (api) => ChatScreen(api: api, assistantNickname: 'Juno'),
  };

  for (final screen in screens.entries) {
    for (final size in _sizes.entries) {
      testWidgets('${screen.key} lays out without overflow, ${size.key}', (tester) async {
        tester.view.physicalSize = size.value;
        tester.view.devicePixelRatio = 1;
        addTearDown(tester.view.reset);

        await tester.pumpWidget(MaterialApp(home: screen.value(_Api())));
        await tester.pumpAndSettle();

        expect(tester.takeException(), isNull);
      });
    }
  }

  testWidgets('lists stay a readable width on a wide screen, and fill a phone', (tester) async {
    Future<double> widthOf(Size size) async {
      tester.view.physicalSize = size;
      tester.view.devicePixelRatio = 1;
      await tester.pumpWidget(MaterialApp(home: MemoriesScreen(api: _Api())));
      await tester.pumpAndSettle();
      return tester.getSize(find.byType(Card).first).width;
    }
    addTearDown(tester.view.reset);

    expect(await widthOf(const Size(1280, 800)), lessThanOrEqualTo(Breakpoints.readable));
    expect(await widthOf(const Size(390, 844)), greaterThan(340));
  });
}
