import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/screens/memories_screen.dart';

MemoryDto _memory(int id, String content, String category, {String? from}) => MemoryDto(
      id: id,
      content: content,
      category: category,
      updatedAtUtc: DateTime.utc(2026, 9, 29),
      sourceConversationId: from == null ? null : 'abcd1234',
      sourceConversationTitle: from,
    );

class _FakeApi extends PersonaOsApi {
  _FakeApi() : super(serverUrl: 'http://fake');

  final memories = [
    _memory(1, 'Prefers morning workouts', 'preference', from: 'Workout chat'),
    _memory(2, 'Car insurance renews in March', 'fact'),
  ];
  final created = <(String, String)>[];
  final deleted = <int>[];
  bool autoSave = true;

  @override
  Future<List<MemoryDto>> getMemories() async => List.of(memories);

  @override
  Future<bool> getMemoryAutoSave() async => autoSave;

  @override
  Future<bool> setMemoryAutoSave(bool on) async => autoSave = on;

  @override
  Future<MemoryDto> createMemory(String content, String category) async {
    created.add((content, category));
    final memory = _memory(3, content, category);
    memories.insert(0, memory);
    return memory;
  }

  @override
  Future<void> deleteMemory(int id) async {
    deleted.add(id);
    memories.removeWhere((m) => m.id == id);
  }
}

void main() {
  Future<_FakeApi> open(WidgetTester tester) async {
    final api = _FakeApi();
    await tester.pumpWidget(MaterialApp(home: MemoriesScreen(api: api)));
    await tester.pumpAndSettle();
    return api;
  }

  testWidgets('lists each memory with its category and the chat it came from', (tester) async {
    await open(tester);

    expect(find.text('Prefers morning workouts'), findsOneWidget);
    expect(find.text('Car insurance renews in March'), findsOneWidget);
    expect(find.textContaining('Preference · from “Workout chat”'), findsOneWidget);
    expect(find.textContaining('says so'), findsOneWidget);
  });

  testWidgets('narrows the list by category and by words', (tester) async {
    await open(tester);

    await tester.tap(find.widgetWithText(FilterChip, 'Fact'));
    await tester.pumpAndSettle();
    expect(find.text('Prefers morning workouts'), findsNothing);
    expect(find.text('Car insurance renews in March'), findsOneWidget);

    await tester.tap(find.widgetWithText(FilterChip, 'Fact'));
    await tester.enterText(find.byType(TextField), 'workout');
    await tester.pumpAndSettle();
    expect(find.text('Prefers morning workouts'), findsOneWidget);
    expect(find.text('Car insurance renews in March'), findsNothing);
  });

  testWidgets('adds a memory with its category', (tester) async {
    final api = await open(tester);

    await tester.tap(find.text('Add'));
    await tester.pumpAndSettle();
    await tester.enterText(find.widgetWithText(TextField, 'e.g. Prefers morning workouts'), 'Has two kids');
    await tester.pumpAndSettle();
    await tester.tap(find.text('Save'));
    await tester.pumpAndSettle();

    expect(api.created, [('Has two kids', 'fact')]);
    expect(find.text('Has two kids'), findsOneWidget);
  });

  testWidgets('deletes a memory only after asking', (tester) async {
    final api = await open(tester);

    await tester.tap(find.byType(PopupMenuButton<String>).first);
    await tester.pumpAndSettle();
    await tester.tap(find.text('Delete').last);
    await tester.pumpAndSettle();
    expect(api.deleted, isEmpty);

    await tester.tap(find.widgetWithText(FilledButton, 'Delete'));
    await tester.pumpAndSettle();
    expect(api.deleted, [1]);
    expect(find.text('Prefers morning workouts'), findsNothing);
  });

  testWidgets('switching auto-save off says cards come first', (tester) async {
    final api = await open(tester);

    await tester.tap(find.byType(Switch));
    await tester.pumpAndSettle();

    expect(api.autoSave, isFalse);
    expect(find.text('Each memory comes as a card to confirm first.'), findsOneWidget);
  });
}
