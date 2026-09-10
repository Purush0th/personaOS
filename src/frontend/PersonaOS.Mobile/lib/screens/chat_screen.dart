import 'dart:async';

import 'package:flutter/material.dart';

import '../api/personaos_api.dart';
import '../voice_service.dart';

class _Bubble {
  _Bubble({required this.isUser, required this.text});

  final bool isUser;
  String text;
  bool isError = false;

  /// What the tools actually did during this turn, shown beneath the reply.
  List<ToolReceipt>? actions;
}

/// Streaming chat with the assistant. Deltas append to the live bubble.
class ChatScreen extends StatefulWidget {
  const ChatScreen({
    super.key,
    required this.api,
    required this.assistantNickname,
    this.voiceEnabled = false,
  });

  final PersonaOsApi api;
  final String assistantNickname;

  /// Whether the instance has the voice module enabled (mic + read-back).
  final bool voiceEnabled;

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

  final _voice = VoiceService();
  bool _listening = false;
  bool _readBack = false;

  @override
  void dispose() {
    _subscription?.cancel();
    _voice.dispose();
    _input.dispose();
    _scroll.dispose();
    super.dispose();
  }

  Future<void> _toggleMic() async {
    if (_listening) {
      await _voice.stopListening();
      if (mounted) setState(() => _listening = false);
      return;
    }
    final ready = await _voice.ensureStt(onStatus: (status) {
      if (status == 'notListening' || status == 'done') {
        if (mounted) setState(() => _listening = false);
      }
    });
    if (!ready) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(
          content: Text('Microphone or speech recognition is unavailable.'),
        ));
      }
      return;
    }
    setState(() => _listening = true);
    await _voice.listen(
      onResult: (words) => setState(() => _input.text = words),
      onFinal: (words) {
        setState(() => _listening = false);
        if (words.trim().isNotEmpty) _send();
      },
    );
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
            // Text on 'done' means the server stored something different from the
            // deltas we streamed — it stripped a tool call the model wrote as prose.
            // Replace before speaking, or read-back would say the raw JSON aloud.
            if (event.text != null) assistantBubble.text = event.text!;
            assistantBubble.actions = event.actions;
            if (_readBack) _voice.speak(assistantBubble.text);
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
      appBar: AppBar(
        title: Text(widget.assistantNickname),
        actions: [
          if (widget.voiceEnabled)
            IconButton(
              tooltip: _readBack ? 'Read-back on' : 'Read-back off',
              icon: Icon(_readBack ? Icons.volume_up : Icons.volume_off),
              onPressed: () {
                setState(() => _readBack = !_readBack);
                if (!_readBack) _voice.stopSpeaking();
              },
            ),
        ],
      ),
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
                  if (widget.voiceEnabled) ...[
                    const SizedBox(width: 4),
                    IconButton(
                      tooltip: _listening ? 'Stop' : 'Speak',
                      onPressed: _streaming ? null : _toggleMic,
                      color: _listening
                          ? Theme.of(context).colorScheme.error
                          : null,
                      icon: Icon(_listening ? Icons.mic : Icons.mic_none),
                    ),
                  ],
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
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            if (bubble.text.isEmpty && !bubble.isUser)
              const SizedBox(
                width: 32,
                child: Text('…', textAlign: TextAlign.center),
              )
            else
              SelectableText(bubble.text, style: TextStyle(color: foreground)),
            if (bubble.actions?.isNotEmpty ?? false)
              _ReceiptList(actions: bubble.actions!, foreground: foreground),
          ],
        ),
      ),
    );
  }
}

/// The app's own record of what ran, beneath the reply. Deliberately quieter than
/// the reply text — it is evidence to check against, not the main content.
class _ReceiptList extends StatelessWidget {
  const _ReceiptList({required this.actions, required this.foreground});

  final List<ToolReceipt> actions;
  final Color foreground;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.only(top: 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          Divider(height: 9, color: foreground.withValues(alpha: 0.18)),
          for (final action in actions)
            Padding(
              padding: const EdgeInsets.only(top: 2),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    action.ok ? '✓ ' : '✕ ',
                    style: theme.textTheme.bodySmall?.copyWith(
                      fontWeight: FontWeight.bold,
                      color: action.ok
                          ? const Color(0xFF3D6B45)
                          : theme.colorScheme.error,
                    ),
                  ),
                  Flexible(
                    child: Text(
                      action.summary == null
                          ? action.tool
                          : '${action.tool}  ${action.summary}',
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: action.ok
                            ? const Color(0xFF3D6B45)
                            : theme.colorScheme.error,
                      ),
                    ),
                  ),
                ],
              ),
            ),
        ],
      ),
    );
  }
}
