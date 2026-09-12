import 'package:flutter/material.dart';

import '../api/personaos_api.dart';
import '../date_utils.dart';

/// Past conversations, most recently active first.
///
/// The phone previously had no way to reach anything said before the current
/// launch: the chat screen kept its conversation id in memory only, so every
/// open started a new thread and the history sat on the server unreachable.
/// This is the list the web app has had all along.
class ConversationsScreen extends StatefulWidget {
  const ConversationsScreen({
    super.key,
    required this.api,
    required this.assistantNickname,
  });

  final PersonaOsApi api;
  final String assistantNickname;

  @override
  State<ConversationsScreen> createState() => _ConversationsScreenState();
}

class _ConversationsScreenState extends State<ConversationsScreen> {
  late Future<List<ConversationSummary>> _conversations;

  @override
  void initState() {
    super.initState();
    _conversations = widget.api.getConversations();
  }

  Future<void> _refresh() async {
    final reloaded = widget.api.getConversations();
    setState(() => _conversations = reloaded);
    await reloaded;
  }

  Future<void> _delete(ConversationSummary conversation) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Delete conversation?'),
        content: Text(
          '“${conversation.title}” and every message in it will be permanently '
          'deleted. This cannot be undone.',
        ),
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
      await widget.api.deleteConversation(conversation.publicId.isEmpty
          ? '${conversation.id}'
          : conversation.publicId);
      await _refresh();
    } on ApiException catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(e.message)));
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    return Scaffold(
      appBar: AppBar(
        title: const Text('History'),
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh),
            tooltip: 'Refresh',
            onPressed: _refresh,
          ),
        ],
      ),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => Navigator.of(context).pop(const _OpenNewChat()),
        icon: const Icon(Icons.add),
        label: const Text('New chat'),
      ),
      body: FutureBuilder<List<ConversationSummary>>(
        future: _conversations,
        builder: (context, snapshot) {
          if (snapshot.connectionState != ConnectionState.done) {
            return const Center(child: CircularProgressIndicator());
          }
          if (snapshot.hasError) {
            return _Message(
              icon: Icons.cloud_off,
              title: 'Could not load history',
              detail: '${snapshot.error}',
            );
          }

          final conversations = snapshot.data ?? const [];
          if (conversations.isEmpty) {
            return _Message(
              icon: Icons.forum_outlined,
              title: 'No conversations yet',
              detail: 'Start one with ${widget.assistantNickname}.',
            );
          }

          return RefreshIndicator(
            onRefresh: _refresh,
            child: ListView.separated(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
              itemCount: conversations.length,
              separatorBuilder: (_, _) => const SizedBox(height: 8),
              itemBuilder: (context, index) {
                final conversation = conversations[index];
                return Card(
                  child: ListTile(
                    shape: RoundedRectangleBorder(
                        borderRadius: BorderRadius.circular(18)),
                    leading: CircleAvatar(
                      backgroundColor: scheme.primaryContainer,
                      foregroundColor: scheme.onPrimaryContainer,
                      child: const Icon(Icons.chat_bubble_outline, size: 20),
                    ),
                    title: Text(
                      conversation.title,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(fontWeight: FontWeight.w600),
                    ),
                    subtitle: Text(relativeTime(conversation.updatedAtUtc)),
                    trailing: IconButton(
                      icon: const Icon(Icons.delete_outline),
                      tooltip: 'Delete',
                      onPressed: () => _delete(conversation),
                    ),
                    onTap: () => Navigator.of(context).pop(conversation),
                  ),
                );
              },
            ),
          );
        },
      ),
    );
  }
}

/// Popped when the user asks for a fresh conversation rather than picking one.
class _OpenNewChat {
  const _OpenNewChat();
}

/// The sentinel the chat screen checks for; kept public for that one use.
const Object openNewChat = _OpenNewChat();

class _Message extends StatelessWidget {
  const _Message({required this.icon, required this.title, required this.detail});

  final IconData icon;
  final String title;
  final String detail;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icon, size: 44, color: scheme.outline),
            const SizedBox(height: 14),
            Text(title,
                style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w600)),
            const SizedBox(height: 6),
            Text(
              detail,
              textAlign: TextAlign.center,
              style: TextStyle(color: scheme.onSurfaceVariant),
            ),
          ],
        ),
      ),
    );
  }
}
