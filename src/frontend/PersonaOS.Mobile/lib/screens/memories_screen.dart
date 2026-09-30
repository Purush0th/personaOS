import 'package:flutter/material.dart';

import '../api/personaos_api.dart';
import '../layout.dart';
import '../date_utils.dart';

/// What the assistant remembers across conversations: read, search, add, edit
/// and delete each memory, and choose whether the assistant saves them without
/// asking. The server is the only store; this screen keeps nothing of its own.
class MemoriesScreen extends StatefulWidget {
  const MemoriesScreen({super.key, required this.api});

  final PersonaOsApi api;

  @override
  State<MemoriesScreen> createState() => _MemoriesScreenState();
}

class _MemoriesScreenState extends State<MemoriesScreen> {
  final _search = TextEditingController();
  late Future<List<MemoryDto>> _memories;
  bool? _autoSave;
  String? _category;

  @override
  void initState() {
    super.initState();
    _memories = widget.api.getMemories();
    widget.api.getMemoryAutoSave().then((on) {
      if (mounted) setState(() => _autoSave = on);
    }).catchError((_) {});
  }

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  Future<void> _refresh() async {
    final reloaded = widget.api.getMemories();
    setState(() { _memories = reloaded; });
    await reloaded;
  }

  void _show(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));
  }

  Future<void> _setAutoSave(bool on) async {
    setState(() => _autoSave = on);
    try {
      final saved = await widget.api.setMemoryAutoSave(on);
      if (mounted) setState(() => _autoSave = saved);
    } on ApiException catch (e) {
      if (mounted) setState(() => _autoSave = !on);
      _show(e.message);
    }
  }

  /// Opens the editor for a new memory, or [memory] when given; saves on "Save".
  Future<void> _edit([MemoryDto? memory]) async {
    final result = await showDialog<(String, String)>(
      context: context,
      builder: (context) => _MemoryEditor(memory: memory),
    );
    if (result == null) return;

    final (content, category) = result;
    try {
      if (memory == null) {
        await widget.api.createMemory(content, category);
      } else {
        await widget.api.updateMemory(memory.id, content, category);
      }
      await _refresh();
    } on ApiException catch (e) {
      _show(e.message);
    }
  }

  Future<void> _delete(MemoryDto memory) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Delete memory?'),
        content: Text('“${memory.content}” will be forgotten.'),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(context).pop(false),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () => Navigator.of(context).pop(true),
            child: const Text('Delete'),
          ),
        ],
      ),
    );
    if (confirmed != true) return;

    try {
      await widget.api.deleteMemory(memory.id);
      await _refresh();
    } on ApiException catch (e) {
      _show(e.message);
    }
  }

  /// Every word typed must appear; the list is small, so it filters as you type.
  List<MemoryDto> _filter(List<MemoryDto> all) {
    final words = _search.text.toLowerCase().split(RegExp(r'\s+')).where((w) => w.isNotEmpty);
    return all
        .where((m) =>
            (_category == null || m.category == _category) &&
            words.every((w) => m.content.toLowerCase().contains(w)))
        .toList();
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final autoSave = _autoSave;

    return Scaffold(
      appBar: AppBar(title: const Text('Memories')),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _edit(),
        icon: const Icon(Icons.add),
        label: const Text('Add'),
      ),
      body: ReadableWidth(child: Column(
        children: [
          if (autoSave != null)
            SwitchListTile(
              title: const Text('Save memories without asking'),
              subtitle: Text(autoSave
                  ? 'The assistant saves one when you tell it something worth keeping, and says so.'
                  : 'Each memory comes as a card to confirm first.'),
              value: autoSave,
              onChanged: _setAutoSave,
            ),
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 4, 16, 4),
            child: TextField(
              controller: _search,
              textInputAction: TextInputAction.search,
              decoration: InputDecoration(
                hintText: 'Search memories',
                prefixIcon: const Icon(Icons.search),
                suffixIcon: _search.text.isEmpty
                    ? null
                    : IconButton(
                        icon: const Icon(Icons.clear),
                        tooltip: 'Clear',
                        onPressed: () => setState(_search.clear),
                      ),
              ),
              onChanged: (_) => setState(() {}),
            ),
          ),
          SizedBox(
            height: 48,
            child: ListView(
              scrollDirection: Axis.horizontal,
              padding: const EdgeInsets.symmetric(horizontal: 16),
              children: [
                for (final category in MemoryDto.categories)
                  Padding(
                    padding: const EdgeInsets.only(right: 8),
                    child: FilterChip(
                      label: Text(MemoryDto.label(category)),
                      selected: _category == category,
                      onSelected: (on) => setState(() => _category = on ? category : null),
                    ),
                  ),
              ],
            ),
          ),
          Expanded(
            child: FutureBuilder<List<MemoryDto>>(
              future: _memories,
              builder: (context, snapshot) {
                if (snapshot.connectionState != ConnectionState.done) {
                  return const Center(child: CircularProgressIndicator());
                }
                if (snapshot.hasError) {
                  return Center(child: Text('${snapshot.error}'));
                }

                final all = snapshot.data ?? const [];
                final shown = _filter(all);
                if (shown.isEmpty) {
                  return Center(
                    child: Padding(
                      padding: const EdgeInsets.all(32),
                      child: Column(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Icon(Icons.psychology_outlined, size: 44, color: scheme.outline),
                          const SizedBox(height: 14),
                          Text(all.isEmpty ? 'No memories yet' : 'No memory matches',
                              style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w600)),
                          if (all.isEmpty) ...[
                            const SizedBox(height: 6),
                            Text(
                              'Tell the assistant something worth keeping, like "I prefer morning workouts", or add one here.',
                              textAlign: TextAlign.center,
                              style: TextStyle(color: scheme.onSurfaceVariant),
                            ),
                          ],
                        ],
                      ),
                    ),
                  );
                }

                return RefreshIndicator(
                  onRefresh: _refresh,
                  child: ListView.separated(
                    padding: const EdgeInsets.fromLTRB(16, 4, 16, 96),
                    itemCount: shown.length,
                    separatorBuilder: (_, _) => const SizedBox(height: 8),
                    itemBuilder: (context, index) {
                      final memory = shown[index];
                      return Card(
                        child: ListTile(
                          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(18)),
                          title: Text(memory.content),
                          subtitle: Text([
                            MemoryDto.label(memory.category),
                            if (memory.sourceConversationTitle?.isNotEmpty ?? false)
                              'from “${memory.sourceConversationTitle}”',
                            relativeTime(memory.updatedAtUtc),
                          ].join(' · ')),
                          trailing: PopupMenuButton<String>(
                            onSelected: (choice) => choice == 'edit' ? _edit(memory) : _delete(memory),
                            itemBuilder: (context) => const [
                              PopupMenuItem(value: 'edit', child: Text('Edit')),
                              PopupMenuItem(value: 'delete', child: Text('Delete')),
                            ],
                          ),
                          onTap: () => _edit(memory),
                        ),
                      );
                    },
                  ),
                );
              },
            ),
          ),
        ],
      )),
    );
  }
}

/// The add/edit dialog: the memory's text and its category.
class _MemoryEditor extends StatefulWidget {
  const _MemoryEditor({this.memory});

  final MemoryDto? memory;

  @override
  State<_MemoryEditor> createState() => _MemoryEditorState();
}

class _MemoryEditorState extends State<_MemoryEditor> {
  late final _content = TextEditingController(text: widget.memory?.content ?? '');
  late String _category = widget.memory?.category ?? 'fact';

  @override
  void dispose() {
    _content.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: Text(widget.memory == null ? 'New memory' : 'Edit memory'),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          TextField(
            controller: _content,
            autofocus: true,
            minLines: 1,
            maxLines: 4,
            maxLength: MemoryDto.maxLength,
            decoration: const InputDecoration(hintText: 'e.g. Prefers morning workouts'),
            onChanged: (_) => setState(() {}),
          ),
          DropdownButtonFormField<String>(
            initialValue: _category,
            decoration: const InputDecoration(labelText: 'Category'),
            items: [
              for (final c in MemoryDto.categories)
                DropdownMenuItem(value: c, child: Text(MemoryDto.label(c))),
            ],
            onChanged: (value) => setState(() => _category = value ?? _category),
          ),
        ],
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('Cancel'),
        ),
        FilledButton(
          onPressed: _content.text.trim().isEmpty
              ? null
              : () => Navigator.of(context).pop((_content.text.trim(), _category)),
          child: const Text('Save'),
        ),
      ],
    );
  }
}
