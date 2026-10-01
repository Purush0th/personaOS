import 'package:flutter/material.dart';

import '../api/personaos_api.dart';
import '../date_utils.dart';

/// The comments on a task or goal: read, add, edit and delete them. The same discussion the web
/// shows on the item's page, so a note written on the phone is there on the desktop too.
class CommentsSection extends StatefulWidget {
  const CommentsSection({
    super.key,
    required this.api,
    required this.itemType,
    required this.itemKey,
    this.assistantName = 'Assistant',
  });

  final PersonaOsApi api;

  /// "task" or "goal".
  final String itemType;
  final String itemKey;
  final String assistantName;

  @override
  State<CommentsSection> createState() => _CommentsSectionState();
}

class _CommentsSectionState extends State<CommentsSection> {
  late Future<List<WorkItemComment>> _comments = widget.api.getComments(widget.itemType, widget.itemKey);
  final _draft = TextEditingController();
  bool _busy = false;

  @override
  void dispose() {
    _draft.dispose();
    super.dispose();
  }

  Future<void> _reload() async {
    final reloaded = widget.api.getComments(widget.itemType, widget.itemKey);
    setState(() {
      _comments = reloaded;
    });
    await reloaded;
  }

  Future<void> _run(Future<void> Function() action) async {
    setState(() => _busy = true);
    try {
      await action();
      await _reload();
    } on ApiException catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _add() async {
    final body = _draft.text.trim();
    if (body.isEmpty) return;
    await _run(() async {
      await widget.api.addComment(widget.itemType, widget.itemKey, body);
      _draft.clear();
    });
  }

  Future<void> _edit(WorkItemComment comment) async {
    final controller = TextEditingController(text: comment.body);
    final body = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Edit comment'),
        content: TextField(controller: controller, autofocus: true, minLines: 2, maxLines: 6),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
          FilledButton(onPressed: () => Navigator.pop(context, controller.text.trim()), child: const Text('Save')),
        ],
      ),
    );
    controller.dispose();
    if (body == null || body.isEmpty || body == comment.body) return;
    await _run(() => widget.api.updateComment(comment.id, body));
  }

  Future<void> _delete(WorkItemComment comment) async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Delete comment?'),
        content: const Text('It will be gone for good.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Delete')),
        ],
      ),
    );
    if (ok == true) await _run(() => widget.api.deleteComment(comment.id));
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return FutureBuilder<List<WorkItemComment>>(
      future: _comments,
      builder: (context, snapshot) {
        final comments = snapshot.data ?? const <WorkItemComment>[];
        return Column(
          key: const Key('comments'),
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(comments.isEmpty ? 'Comments' : 'Comments · ${comments.length}', style: theme.textTheme.titleSmall),
            const SizedBox(height: 6),
            if (snapshot.connectionState != ConnectionState.done)
              const Padding(padding: EdgeInsets.all(8), child: LinearProgressIndicator())
            else if (snapshot.hasError)
              Text('Could not load the comments.', style: theme.textTheme.bodySmall)
            else if (comments.isEmpty)
              Text('No comments yet.', style: theme.textTheme.bodySmall),
            for (final comment in comments)
              Card(
                margin: const EdgeInsets.only(bottom: 6),
                elevation: 0,
                color: theme.colorScheme.surfaceContainerHighest,
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(12, 8, 4, 8),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              [
                                comment.author == 'assistant' ? widget.assistantName : 'You',
                                relativeTime(comment.createdAtUtc),
                                if (comment.edited) 'edited',
                              ].join(' · '),
                              style: theme.textTheme.labelSmall,
                            ),
                            const SizedBox(height: 2),
                            Text(comment.body),
                          ],
                        ),
                      ),
                      PopupMenuButton<String>(
                        tooltip: 'Comment actions',
                        icon: const Icon(Icons.more_vert, size: 18),
                        onSelected: (choice) => choice == 'edit' ? _edit(comment) : _delete(comment),
                        itemBuilder: (context) => const [
                          PopupMenuItem(value: 'edit', child: Text('Edit')),
                          PopupMenuItem(value: 'delete', child: Text('Delete')),
                        ],
                      ),
                    ],
                  ),
                ),
              ),
            const SizedBox(height: 6),
            TextField(
              key: const Key('comment-draft'),
              controller: _draft,
              minLines: 1,
              maxLines: 4,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(hintText: 'Add a comment', border: OutlineInputBorder(), isDense: true),
              onChanged: (_) => setState(() {}),
            ),
            Align(
              alignment: Alignment.centerRight,
              child: TextButton(
                onPressed: _busy || _draft.text.trim().isEmpty ? null : _add,
                child: const Text('Comment'),
              ),
            ),
          ],
        );
      },
    );
  }
}
