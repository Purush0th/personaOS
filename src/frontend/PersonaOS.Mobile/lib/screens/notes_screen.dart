import 'dart:async';
import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../api/personaos_api.dart';

/// Notes, after Google Keep: a "Take a note…" box on top and the notes as cards below, the
/// latest edited first, two to a row. A note is a place to park a thought, not to keep it: a
/// card moves to the board's backlog (which removes the note) or is deleted, with an undo.
class NotesScreen extends StatefulWidget {
  const NotesScreen({super.key, required this.api, this.boardEnabled = false});

  final PersonaOsApi api;

  /// Whether notes can be moved to the board's backlog.
  final bool boardEnabled;

  @override
  State<NotesScreen> createState() => _NotesScreenState();
}

class _NotesScreenState extends State<NotesScreen> {
  final _draft = TextEditingController();
  final _draftFocus = FocusNode();
  final _search = TextEditingController();
  List<NoteDto> _notes = [];
  bool _loading = true;
  String? _error;
  bool _searching = false;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void dispose() {
    _draft.dispose();
    _draftFocus.dispose();
    _search.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final notes = await widget.api.getNotes();
      if (mounted) {
        setState(() {
          _notes = notes;
          _error = null;
        });
      }
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  void _show(String message, {SnackBarAction? action}) {
    if (!mounted) return;
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(message), action: action));
  }

  /// The box is cleared at once and keeps the keyboard, so the next thought can follow while this
  /// one saves; one that could not be saved goes back into the box, if it is still empty.
  Future<void> _add() async {
    final text = _draft.text.trim();
    if (text.isEmpty) return;
    _draft.clear();
    _draftFocus.requestFocus();
    setState(() {});
    try {
      final note = await widget.api.createNote(text);
      if (mounted) setState(() => _notes = [note, ..._notes]);
    } on ApiException catch (e) {
      if (_draft.text.trim().isEmpty) _draft.text = text;
      _show(e.message);
    }
  }

  Future<void> _open(NoteDto note) async {
    final result = await Navigator.of(context).push<_EditResult>(
      MaterialPageRoute(builder: (_) => NoteEditorPage(note: note, boardEnabled: widget.boardEnabled)),
    );
    if (result == null || !mounted) return;
    switch (result.action) {
      case _EditAction.delete:
        await _delete(note);
      case _EditAction.backlog:
        await _toBacklog(note);
      case _EditAction.save:
        final text = result.text.trim();
        // Emptying a note is deleting it, as in Keep.
        if (text.isEmpty) return _delete(note);
        if (text == note.text) return;
        try {
          final saved = await widget.api.updateNote(note.id, text);
          if (mounted) setState(() => _notes = [saved, ..._notes.where((n) => n.id != note.id)]);
        } on ApiException catch (e) {
          _show(e.message);
        }
    }
  }

  Future<void> _delete(NoteDto note) async {
    try {
      await widget.api.deleteNote(note.id);
    } on ApiException catch (e) {
      _show(e.message);
      return;
    }
    if (!mounted) return;
    setState(() => _notes = _notes.where((n) => n.id != note.id).toList());
    _show('Note deleted', action: SnackBarAction(label: 'Undo', onPressed: () => unawaited(_restore(note.text))));
  }

  Future<void> _restore(String text) async {
    try {
      final note = await widget.api.createNote(text);
      if (mounted) setState(() => _notes = [note, ..._notes]);
    } on ApiException catch (e) {
      _show(e.message);
    }
  }

  Future<void> _toBacklog(NoteDto note) async {
    try {
      final key = await widget.api.moveNoteToBacklog(note.id);
      if (!mounted) return;
      setState(() => _notes = _notes.where((n) => n.id != note.id).toList());
      _show('Moved to the backlog as $key');
    } on ApiException catch (e) {
      _show(e.message);
    }
  }

  /// A long press offers what the editor's bar does, without opening the note.
  Future<void> _actions(NoteDto note) async {
    final action = await showModalBottomSheet<_EditAction>(
      context: context,
      showDragHandle: true,
      builder: (context) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            if (widget.boardEnabled)
              ListTile(
                key: const Key('note-sheet-backlog'),
                leading: const Icon(Icons.move_to_inbox_outlined),
                title: const Text('Move to backlog'),
                onTap: () => Navigator.pop(context, _EditAction.backlog),
              ),
            ListTile(
              key: const Key('note-sheet-delete'),
              leading: const Icon(Icons.delete_outline),
              title: const Text('Delete'),
              onTap: () => Navigator.pop(context, _EditAction.delete),
            ),
          ],
        ),
      ),
    );
    if (action == _EditAction.backlog) await _toBacklog(note);
    if (action == _EditAction.delete) await _delete(note);
  }

  List<NoteDto> get _shown {
    final words = _search.text.trim().toLowerCase();
    return words.isEmpty ? _notes : _notes.where((n) => n.text.toLowerCase().contains(words)).toList();
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Scaffold(
      appBar: AppBar(
        title: _searching
            ? TextField(
                key: const Key('notes-search'),
                controller: _search,
                autofocus: true,
                decoration: const InputDecoration(hintText: 'Search notes', border: InputBorder.none),
                onChanged: (_) => setState(() {}),
              )
            : const Text('Notes'),
        actions: [
          IconButton(
            tooltip: _searching ? 'Close search' : 'Search',
            icon: Icon(_searching ? Icons.close : Icons.search),
            onPressed: () => setState(() {
              _searching = !_searching;
              if (!_searching) _search.clear();
            }),
          ),
        ],
      ),
      body: Column(
        children: [
          SizedBox(height: 3, child: _loading ? const LinearProgressIndicator() : null),
          if (_error != null)
            MaterialBanner(
              content: Text(_error!),
              actions: [TextButton(onPressed: _load, child: const Text('Try again'))],
            ),
          Padding(
            padding: const EdgeInsets.fromLTRB(12, 8, 12, 4),
            child: Material(
              color: scheme.surfaceContainerHigh,
              borderRadius: BorderRadius.circular(12),
              child: Padding(
                padding: const EdgeInsets.only(left: 16),
                child: Row(
                  children: [
                    Expanded(
                      child: TextField(
                        key: const Key('note-draft'),
                        controller: _draft,
                        focusNode: _draftFocus,
                        minLines: 1,
                        maxLines: 4,
                        maxLength: NoteDto.maxLength,
                        // The keyboard's own key saves and keeps the keyboard up for the next one.
                        textInputAction: TextInputAction.send,
                        textCapitalization: TextCapitalization.sentences,
                        decoration: const InputDecoration(
                          hintText: 'Take a note…',
                          border: InputBorder.none,
                          counterText: '',
                        ),
                        onChanged: (_) => setState(() {}),
                        onSubmitted: (_) => unawaited(_add()),
                      ),
                    ),
                    IconButton(
                      key: const Key('note-send'),
                      tooltip: 'Save note',
                      icon: const Icon(Icons.send),
                      onPressed: _draft.text.trim().isEmpty ? null : () => unawaited(_add()),
                    ),
                  ],
                ),
              ),
            ),
          ),
          Expanded(
            child: RefreshIndicator(
              onRefresh: _load,
              child: _cards(context),
            ),
          ),
        ],
      ),
    );
  }

  Widget _cards(BuildContext context) {
    final notes = _shown;
    if (notes.isEmpty && !_loading) {
      return ListView(
        children: [
          Padding(
            padding: const EdgeInsets.all(32),
            child: Text(
              _search.text.trim().isEmpty
                  ? 'Notes you take appear here. Move them to the backlog when you are ready to work on them.'
                  : 'No notes contain “${_search.text.trim()}”.',
              textAlign: TextAlign.center,
              style: TextStyle(color: Theme.of(context).colorScheme.onSurfaceVariant),
            ),
          ),
        ],
      );
    }
    return LayoutBuilder(
      builder: (context, constraints) {
        // Two to a row on a phone, more on a tablet; the latest first, read left to right.
        final columns = math.max(2, (constraints.maxWidth / 260).floor());
        return SingleChildScrollView(
          physics: const AlwaysScrollableScrollPhysics(),
          padding: const EdgeInsets.fromLTRB(8, 8, 8, 24),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              for (var c = 0; c < columns; c++)
                Expanded(
                  child: Column(
                    children: [
                      for (var i = c; i < notes.length; i += columns) _card(context, notes[i]),
                    ],
                  ),
                ),
            ],
          ),
        );
      },
    );
  }

  Widget _card(BuildContext context, NoteDto note) => Card(
    key: Key('note-${note.id}'),
    margin: const EdgeInsets.all(4),
    elevation: 0,
    shape: RoundedRectangleBorder(
      borderRadius: BorderRadius.circular(12),
      side: BorderSide(color: Theme.of(context).colorScheme.outlineVariant),
    ),
    clipBehavior: Clip.antiAlias,
    child: InkWell(
      onTap: () => unawaited(_open(note)),
      onLongPress: () => unawaited(_actions(note)),
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: SizedBox(
          width: double.infinity,
          child: Text(note.text, maxLines: 10, overflow: TextOverflow.ellipsis),
        ),
      ),
    ),
  );
}

enum _EditAction { save, delete, backlog }

class _EditResult {
  const _EditResult(this.action, [this.text = '']);

  final _EditAction action;
  final String text;
}

/// One note, full screen as in Keep: going back saves it; the bar moves it to the backlog or
/// deletes it.
class NoteEditorPage extends StatefulWidget {
  const NoteEditorPage({super.key, required this.note, this.boardEnabled = false});

  final NoteDto note;
  final bool boardEnabled;

  @override
  State<NoteEditorPage> createState() => _NoteEditorPageState();
}

class _NoteEditorPageState extends State<NoteEditorPage> {
  late final _text = TextEditingController(text: widget.note.text);

  @override
  void dispose() {
    _text.dispose();
    super.dispose();
  }

  void _close(_EditAction action) => Navigator.pop(context, _EditResult(action, _text.text));

  @override
  Widget build(BuildContext context) => PopScope(
    // Back (the arrow or the phone's own) saves, as in Keep.
    canPop: false,
    onPopInvokedWithResult: (didPop, _) {
      if (!didPop) _close(_EditAction.save);
    },
    child: Scaffold(
      appBar: AppBar(
        leading: BackButton(onPressed: () => _close(_EditAction.save)),
        actions: [
          if (widget.boardEnabled)
            IconButton(
              key: const Key('note-to-backlog'),
              tooltip: 'Move to backlog',
              icon: const Icon(Icons.move_to_inbox_outlined),
              onPressed: () => _close(_EditAction.backlog),
            ),
          IconButton(
            key: const Key('note-delete'),
            tooltip: 'Delete',
            icon: const Icon(Icons.delete_outline),
            onPressed: () => _close(_EditAction.delete),
          ),
        ],
      ),
      body: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16),
        child: TextField(
          key: const Key('note-edit'),
          controller: _text,
          autofocus: true,
          maxLines: null,
          expands: true,
          maxLength: NoteDto.maxLength,
          textAlignVertical: TextAlignVertical.top,
          textCapitalization: TextCapitalization.sentences,
          decoration: const InputDecoration(border: InputBorder.none, counterText: ''),
        ),
      ),
    ),
  );
}
