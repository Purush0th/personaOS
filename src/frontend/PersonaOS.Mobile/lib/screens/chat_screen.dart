import 'dart:async';

import 'package:flutter/material.dart';

import '../api/personaos_api.dart';

class _Bubble {
  _Bubble({required this.isUser, required this.text});

  final bool isUser;
  String text;
  bool isError = false;
}

/// Streaming chat with the assistant. Deltas append to the live bubble.
class ChatScreen extends StatefulWidget {
  const ChatScreen({
    super.key,
    required this.api,
    required this.assistantNickname,
  });

  final PersonaOsApi api;
  final String assistantNickname;

  @override
  State<ChatScreen> createState() => _ChatScreenState();
}

class _ChatScreenState extends State<ChatScreen> {
  final _input = TextEditingController();
  final _scroll = ScrollController();
  final List<_Bubble> _bubbles = [];
  StreamSubscription<ChatEvent>? _subscription;
  int? _conversationId;
  bool _streaming = false;

  @override
  void dispose() {
    _subscription?.cancel();
    _input.dispose();
    _scroll.dispose();
    super.dispose();
  }

  void _scrollToBottom() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (_scroll.hasClients) {
        _scroll.jumpTo(_scroll.position.maxScrollExtent);
      }
    });
  }

  void _send() {
    final text = _input.text.trim();
    if (text.isEmpty || _streaming) return;

    final assistantBubble = _Bubble(isUser: false, text: '');
    setState(() {
      _bubbles.add(_Bubble(isUser: true, text: text));
      _bubbles.add(assistantBubble);
      _streaming = true;
      _input.clear();
    });
    _scrollToBottom();

    _subscription = widget.api
        .streamChat(text, conversationId: _conversationId)
        .listen((event) {
      setState(() {
        switch (event.type) {
          case 'start':
            _conversationId = event.conversationId ?? _conversationId;
          case 'delta':
            assistantBubble.text += event.text ?? '';
          case 'error':
            assistantBubble.isError = true;
            assistantBubble.text = event.error ?? 'Something went wrong.';
            _streaming = false;
          case 'done':
            _streaming = false;
        }
      });
      _scrollToBottom();
    }, onError: (_) {
      setState(() {
        assistantBubble.isError = true;
        assistantBubble.text = 'Connection lost.';
        _streaming = false;
      });
    }, onDone: () {
      if (mounted && _streaming) setState(() => _streaming = false);
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text(widget.assistantNickname)),
      body: Column(
        children: [
          Expanded(
            child: _bubbles.isEmpty
                ? Center(
                    child: Text(
                      'Say hello to ${widget.assistantNickname}!',
                      style: TextStyle(color: Colors.grey.shade600),
                    ),
                  )
                : ListView.builder(
                    controller: _scroll,
                    padding: const EdgeInsets.all(12),
                    itemCount: _bubbles.length,
                    itemBuilder: (context, i) => _BubbleView(bubble: _bubbles[i]),
                  ),
          ),
          SafeArea(
            child: Padding(
              padding: const EdgeInsets.fromLTRB(12, 4, 12, 12),
              child: Row(
                children: [
                  Expanded(
                    child: TextField(
                      controller: _input,
                      minLines: 1,
                      maxLines: 4,
                      textInputAction: TextInputAction.send,
                      decoration: InputDecoration(
                        hintText: 'Message ${widget.assistantNickname}…',
                        border: const OutlineInputBorder(),
                        isDense: true,
                      ),
                      onSubmitted: (_) => _send(),
                    ),
                  ),
                  const SizedBox(width: 8),
                  IconButton.filled(
                    onPressed: _streaming ? null : _send,
                    icon: _streaming
                        ? const SizedBox(
                            width: 18,
                            height: 18,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(Icons.send),
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _BubbleView extends StatelessWidget {
  const _BubbleView({required this.bubble});

  final _Bubble bubble;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final background = bubble.isError
        ? theme.colorScheme.errorContainer
        : bubble.isUser
            ? const Color(0xFF1A1A2E)
            : theme.colorScheme.surfaceContainerHighest;
    final foreground = bubble.isError
        ? theme.colorScheme.onErrorContainer
        : bubble.isUser
            ? Colors.white
            : theme.colorScheme.onSurface;

    return Align(
      alignment: bubble.isUser ? Alignment.centerRight : Alignment.centerLeft,
      child: Container(
        margin: const EdgeInsets.symmetric(vertical: 4),
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
        constraints: const BoxConstraints(maxWidth: 320),
        decoration: BoxDecoration(
          color: background,
          borderRadius: BorderRadius.circular(14),
        ),
        child: bubble.text.isEmpty && !bubble.isUser
            ? const SizedBox(
                width: 32,
                child: Text('…', textAlign: TextAlign.center),
              )
            : SelectableText(bubble.text, style: TextStyle(color: foreground)),
      ),
    );
  }
}
