import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:personaos_mobile/api/personaos_api.dart';
import 'package:personaos_mobile/screens/notes_screen.dart';

/// Notes in memory: records what was sent.
class _Api extends PersonaOsApi {
  _Api(this.notes) : super(serverUrl: 'http://test');

  final List<NoteDto> notes;
  final calls = <String>[];
  var _next = 100;

  /// When set, creating a note waits for it, as a slow server would.
  Completer<void>? hold;

  @override
  Future<List<NoteDto>> getNotes() async => [...notes];

  @override
  Future<NoteDto> createNote(String text) async {
    calls.add('create $text');
    await hold?.future;
    return NoteDto(id: _next++, text: text, updatedAtUtc: DateTime.utc(2026, 10, 10));
  }

  @override
  Future<NoteDto> updateNote(int id, String text) async {
    calls.add('update $id $text');
    return NoteDto(id: id, text: text, updatedAtUtc: DateTime.utc(2026, 10, 10));
  }

  @override
  Future<void> deleteNote(int id) async => calls.add('delete $id');

  @override
  Future<String> moveNoteToBacklog(int id) async {
    calls.add('backlog $id');
    return 'TASK-12';
  }
}

NoteDto _note(int id, String text) => NoteDto(id: id, text: text, updatedAtUtc: DateTime.utc(2026, 10, 10));

void main() {
  Future<_Api> open(WidgetTester tester, List<NoteDto> notes, {bool board = true}) async {
    tester.view.physicalSize = const Size(390, 844);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final api = _Api(notes);
    await tester.pumpWidget(MaterialApp(home: NotesScreen(api: api, boardEnabled: board)));
    await tester.pumpAndSettle();
    return api;
  }

  testWidgets('the keyboard key saves a note and keeps the box ready for the next', (tester) async {
    final api = await open(tester, [_note(1, 'Buy batteries')]);

    await tester.enterText(find.byKey(const Key('note-draft')), 'Call the plumber');
    await tester.testTextInput.receiveAction(TextInputAction.send);
    await tester.pumpAndSettle();

    expect(api.calls, ['create Call the plumber']);
    expect(find.text('Call the plumber'), findsOneWidget);
    final box = tester.widget<TextField>(find.byKey(const Key('note-draft')));
    expect(box.controller!.text, '');
    expect(box.focusNode!.hasFocus, isTrue);
  });

  testWidgets('the next note can be typed while one is saving', (tester) async {
    final api = await open(tester, []);
    api.hold = Completer<void>();

    await tester.enterText(find.byKey(const Key('note-draft')), 'Renew car insurance');
    await tester.testTextInput.receiveAction(TextInputAction.send);
    await tester.pump();
    await tester.enterText(find.byKey(const Key('note-draft')), 'Order printer ink');
    api.hold!.complete();
    await tester.pumpAndSettle();

    expect(tester.widget<TextField>(find.byKey(const Key('note-draft'))).controller!.text, 'Order printer ink');
    expect(find.text('Renew car insurance'), findsOneWidget);
  });

  testWidgets('opening a note and going back saves what changed', (tester) async {
    final api = await open(tester, [_note(1, 'Book dentist')]);

    await tester.tap(find.text('Book dentist'));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('note-edit')), 'Book dentist on Monday');
    await tester.tap(find.byType(BackButton));
    await tester.pumpAndSettle();

    expect(api.calls, ['update 1 Book dentist on Monday']);
    expect(find.text('Book dentist on Monday'), findsOneWidget);
  });

  testWidgets('deleting can be undone', (tester) async {
    final api = await open(tester, [_note(1, 'Buy batteries')]);

    await tester.longPress(find.text('Buy batteries'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('note-sheet-delete')));
    await tester.pumpAndSettle();
    expect(find.text('Buy batteries'), findsNothing);

    await tester.tap(find.text('Undo'));
    await tester.pumpAndSettle();

    expect(api.calls, ['delete 1', 'create Buy batteries']);
    expect(find.text('Buy batteries'), findsOneWidget);
  });

  testWidgets('moving to the backlog takes the note away and names the task', (tester) async {
    final api = await open(tester, [_note(1, 'Fix the balcony door\nThe hinge squeaks')]);

    await tester.tap(find.textContaining('Fix the balcony door'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('note-to-backlog')));
    await tester.pumpAndSettle();

    expect(api.calls, ['backlog 1']);
    expect(find.textContaining('Fix the balcony door'), findsNothing);
    expect(find.text('Moved to the backlog as TASK-12'), findsOneWidget);
  });

  testWidgets('without the board there is no move to the backlog', (tester) async {
    await open(tester, [_note(1, 'Buy batteries')], board: false);

    await tester.tap(find.text('Buy batteries'));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('note-to-backlog')), findsNothing);
  });

  testWidgets('long notes and many of them fit a small phone', (tester) async {
    tester.view.physicalSize = const Size(320, 600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    await tester.pumpWidget(MaterialApp(
      home: NotesScreen(
        api: _Api([
          for (var i = 0; i < 12; i++) _note(i, 'Note $i ${'with a fairly long line of words ' * (i % 4 + 1)}'),
          _note(99, 'Supercalifragilisticexpialidocious-and-then-some-unbroken-text-that-will-not-wrap'),
        ]),
        boardEnabled: true,
      ),
    ));
    await tester.pumpAndSettle();

    expect(tester.takeException(), isNull);
  });
}
