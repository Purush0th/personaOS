import 'dart:async';
import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter_markdown_plus/flutter_markdown_plus.dart';

import '../api/personaos_api.dart';
import '../item_links.dart';
import '../layout.dart';
import '../push_service.dart';
import '../server_speech.dart';
import '../voice_service.dart';
import 'conversations_screen.dart';
import 'goal_view.dart';
import 'task_views.dart';

part 'chat_messages.dart';

class _Bubble {
  _Bubble({required this.isUser, required this.text});

  final bool isUser;
  String text;
  bool isError = false;

  /// What the tools actually did during this turn, shown beneath the reply.
  List<ToolReceipt>? actions;

  /// Changes awaiting the user's confirmation. Nothing has been written yet.
  List<PendingAction>? pending;

  /// The reply says a change was made, but no tool made one — nothing was saved.
  bool unverifiedClaim = false;

  /// Item keys the reply names that do not exist.
  List<String> unknownItems = const [];

  /// The notes shown (and, hands-free, spoken) under the reply, in order.
  List<String> get notes => [
        if (unverifiedClaim) UnverifiedClaimNote.message,
        ?UnknownItemsNote.messageFor(unknownItems),
      ];
}

/// Streaming chat with the assistant. Deltas append to the live bubble.
class ChatScreen extends StatefulWidget {
  const ChatScreen({
    super.key,
    required this.api,
    required this.assistantNickname,
    this.voiceEnabled = false,
    this.voice,
  });

  final PersonaOsApi api;
  final String assistantNickname;

  /// Whether the instance has the voice module enabled (mic + read-back).
  final bool voiceEnabled;

  /// Speech in and out. The real one talks to platform plugins, so tests pass their own.
  final VoiceService? voice;

  @override
  State<ChatScreen> createState() => _ChatScreenState();
}

class _ChatScreenState extends State<ChatScreen> {
  final _input = TextEditingController();
  final _scroll = ScrollController();
  final List<_Bubble> _bubbles = [];

  /// Action ids with a confirm/discard in flight, so a card can't be double-tapped.
  final Set<String> _resolving = {};
  StreamSubscription<ChatEvent>? _subscription;
  int? _conversationId;
  bool _streaming = false;

  /// How the assistant works here; sent with each message and kept with the conversation.
  String _mode = 'chat';

  late final VoiceService _voice = widget.voice ?? VoiceService();
  bool _listening = false;
  bool _readBack = false;

  /// Hands-free: keep the conversation going without touching the phone.
  ///
  /// Push-to-talk needs a tap per turn, which defeats the point when the phone
  /// is across the room. In this mode the loop is: listen, send on the pause
  /// that ends an utterance, speak the reply, then listen again. The two halves
  /// never overlap — the microphone stays shut while the phone is talking, or
  /// the assistant hears itself and answers its own words.
  bool _handsFree = false;

  /// True while the hands-free loop is speaking, so the mic button reflects it.
  bool _speaking = false;

  /// Set when a confirm card stopped hands-free. Resolving the last card starts it
  /// again: the user put the phone down, and a decision they have now made should
  /// not end the conversation.
  bool _handsFreeAwaitsCard = false;

  /// True while an old conversation's messages are being fetched.
  bool _loadingHistory = false;

  @override
  void initState() {
    super.initState();
    if (widget.voiceEnabled && widget.voice == null) unawaited(_useServerSpeechIfChosen());
  }

  /// Hands the voice service the server's speech engines the user chose in
  /// Settings, for each direction the server actually has.
  Future<void> _useServerSpeechIfChosen() async {
    try {
      final prefs = await VoicePrefs.load();
      if (!prefs.serverStt && !prefs.serverTts) return;
      final status = await widget.api.getSpeechStatus();
      _voice
        ..server = ServerSpeech(widget.api)
        ..useServerStt = prefs.serverStt && status.speechToText
        ..useServerTts = prefs.serverTts && status.textToSpeech;
    } catch (_) {
      // The phone's own engines keep working.
    }
  }

  @override
  void dispose() {
    unawaited(_subscription?.cancel());
    _voice.dispose();
    _input.dispose();
    _scroll.dispose();
    super.dispose();
  }

  /// Turns hands-free mode on or off. On, it starts the listen/answer/speak loop.
  Future<void> _toggleHandsFree() async {
    // Either way this is the user's own decision, so no card is holding the loop.
    _handsFreeAwaitsCard = false;
    if (_handsFree) {
      setState(() => _handsFree = false);
      await _voice.stopListening();
      await _voice.stopSpeaking();
      if (mounted) {
        setState(() {
          _listening = false;
          _speaking = false;
        });
      }
      return;
    }

    if (!await _prepareMic()) return;
    setState(() => _handsFree = true);
    await _listenOnce();
  }

  /// Opens the microphone for one utterance. In hands-free mode the reply
  /// speaking, not this method, is what schedules the next one.
  Future<void> _listenOnce() async {
    if (!mounted || _streaming) return;

    setState(() => _listening = true);
    await _voice.listen(
      onResult: (words) => setState(() => _input.text = words),
      onFinal: (words) {
        setState(() => _listening = false);
        if (words.trim().isNotEmpty) {
          _send();
        } else if (_handsFree) {
          // Silence rather than speech. Reopen instead of ending the session,
          // otherwise a pause while thinking silently drops out of hands-free.
          unawaited(_listenOnce());
        }
      },
      // Wait out a natural pause before deciding the turn is over. The default
      // is short enough to cut people off mid-sentence.
      pauseFor: _handsFree ? const Duration(seconds: 3) : null,
      listenFor: _handsFree ? const Duration(seconds: 60) : null,
    );
  }

  Future<void> _toggleMic() async {
    if (_listening) {
      await _voice.stopListening();
      if (mounted) setState(() => _listening = false);
      return;
    }
    if (!await _prepareMic()) return;
    await _listenOnce();
  }

  /// Initializes speech recognition, reporting anything that stops it working.
  /// Returns whether the microphone is usable.
  Future<bool> _prepareMic() async {
    final ready = await _voice.ensureStt(
      onStatus: (status) {
        if (status == 'notListening' || status == 'done') {
          if (mounted) setState(() => _listening = false);
        }
      },
      // Recognition can fail after the microphone is already live — no language
      // pack for the locale, no network for the online recognizer. Without this
      // the mic stays lit and the user waits for a transcript that never comes.
      onError: (error) {
        if (!mounted) return;
        // Stop the loop as well as the mic: retrying a failing recognizer
        // forever would spin silently.
        setState(() {
          _listening = false;
          _handsFree = false;
        });
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Could not hear you: $error')),
        );
      },
    );
    if (!ready && mounted) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(
        content: Text('Microphone or speech recognition is unavailable.'),
      ));
    }
    return ready;
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
        .streamChat(text, conversationId: _conversationId, mode: _mode)
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
            assistantBubble.pending = event.pending;
            assistantBubble.unverifiedClaim = event.unverifiedClaim;
            assistantBubble.unknownItems = event.unknownItems;
            unawaited(_afterReply(assistantBubble));
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

  /// Speaks a finished reply when asked to, then reopens the microphone if the
  /// conversation is hands-free.
  ///
  /// A reply that proposes a change stops the loop on purpose: the next step is
  /// a decision on the confirm card, and the user has to see it. Carrying on
  /// listening would invite them to answer a question the assistant has not
  /// really asked, while an unconfirmed write sits waiting off screen.
  Future<void> _afterReply(_Bubble bubble) async {
    final proposals = bubble.pending?.where((p) => p.isPending).isNotEmpty ?? false;
    final speakIt = _readBack || _handsFree;

    if (speakIt && !bubble.isError) {
      setState(() => _speaking = true);
      // Hands-free users may never look at the screen, so the warnings are spoken too.
      await _voice.speak([bubble.text, ...bubble.notes].join('\n\n'));
      if (!mounted) return;
      setState(() => _speaking = false);
    }

    if (!_handsFree || bubble.isError || proposals) {
      if (proposals && _handsFree && mounted) {
        setState(() {
          _handsFree = false;
          _handsFreeAwaitsCard = true;
        });
        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(
          content: Text('Hands-free paused — confirm or discard the change to carry on.'),
        ));
      }
      return;
    }

    await _listenOnce();
  }

  /// Replaces the screen with a stored conversation, or clears it for a new one.
  Future<void> _openHistory() async {
    final picked = await Navigator.of(context).push<Object?>(
      MaterialPageRoute<Object?>(
        builder: (_) => ConversationsScreen(
          api: widget.api,
          assistantNickname: widget.assistantNickname,
        ),
      ),
    );
    if (picked == null || !mounted) return;

    if (picked is! ConversationSummary) {
      _startNewConversation();
      return;
    }

    setState(() {
      _bubbles.clear();
      _loadingHistory = true;
      _handsFreeAwaitsCard = false;
    });

    try {
      final detail = await widget.api.getConversation(
        picked.publicId.isEmpty ? '${picked.id}' : picked.publicId,
      );
      if (!mounted) return;
      setState(() {
        _conversationId = detail.id;
        _mode = detail.mode;
        _bubbles
          ..clear()
          ..addAll(detail.messages.map((m) => _Bubble(
                isUser: m.role == 'user',
                text: m.content,
              )
                ..actions = m.toolActions
                ..pending = m.pendingActions
                ..unverifiedClaim = m.unverifiedClaim
                ..unknownItems = m.unknownItems));
        _loadingHistory = false;
      });
      _scrollToBottom();
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() => _loadingHistory = false);
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text(e.message)));
    }
  }

  void _startNewConversation() {
    setState(() {
      _bubbles.clear();
      _conversationId = null;
      _mode = 'chat';
      // The cards that paused hands-free are gone with the thread.
      _handsFreeAwaitsCard = false;
    });
  }

  /// Confirms or discards a proposed change, replacing the card with its outcome.
  /// Nothing has touched the database until confirm returns.
  /// A task or goal named in a reply opens its quick view over the chat.
  Future<void> _openItem(String key) async {
    if (key.startsWith('TASK-')) {
      await showTaskQuickView(context, widget.api, key);
    } else {
      await showGoalQuickView(context, widget.api, key);
    }
  }

  Future<void> _resolveAction(PendingAction action, bool confirm) async {
    if (!action.isPending || _resolving.contains(action.id)) return;

    setState(() => _resolving.add(action.id));
    try {
      final updated = confirm
          ? await widget.api.confirmAction(action.id)
          : await widget.api.discardAction(action.id);

      if (!mounted) return;
      setState(() {
        for (final bubble in _bubbles) {
          final list = bubble.pending;
          if (list == null) continue;
          for (var i = 0; i < list.length; i++) {
            if (list[i].id == action.id) list[i] = updated;
          }
        }
      });
      // A confirmed reminder card sets (or clears) the alarm on this phone straight away.
      if (confirm && updated.tool.contains('reminder')) {
        unawaited(PushService.instance.resyncAlarms(widget.api));
      }
      await _resumeHandsFreeIfWaiting();
    } catch (_) {
      // Leave the card pending so it can be retried rather than lost.
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Could not reach the server. Try again.')),
        );
      }
    } finally {
      if (mounted) setState(() => _resolving.remove(action.id));
    }
  }

  /// Starts hands-free again once the cards that paused it are all resolved.
  ///
  /// Without this the loop ended for good at the first proposal: the user confirmed
  /// the change and then talked to a phone that had stopped listening.
  Future<void> _resumeHandsFreeIfWaiting() async {
    if (!_handsFreeAwaitsCard || _handsFree || !mounted) return;
    final stillWaiting = _bubbles.any(
      (b) => b.pending?.any((p) => p.isPending) ?? false,
    );
    if (stillWaiting) return;

    setState(() {
      _handsFreeAwaitsCard = false;
      _handsFree = true;
    });
    ScaffoldMessenger.of(context).showSnackBar(const SnackBar(
      content: Text('Hands-free resumed — listening.'),
    ));
    await _listenOnce();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text(widget.assistantNickname),
        actions: [
          IconButton(
            tooltip: 'History',
            icon: const Icon(Icons.history),
            onPressed: _streaming ? null : _openHistory,
          ),
          IconButton(
            tooltip: 'New chat',
            icon: const Icon(Icons.add_comment_outlined),
            onPressed: _streaming || _bubbles.isEmpty ? null : _startNewConversation,
          ),
          if (widget.voiceEnabled)
            IconButton(
              tooltip: _readBack ? 'Read-back on' : 'Read-back off',
              icon: Icon(_readBack ? Icons.volume_up : Icons.volume_off),
              onPressed: () {
                setState(() => _readBack = !_readBack);
                if (!_readBack) unawaited(_voice.stopSpeaking());
              },
            ),
        ],
      ),
      body: ReadableWidth(maxWidth: 960, child: Column(
        children: [
          if (_handsFree) _HandsFreeBanner(listening: _listening, speaking: _speaking),
          Expanded(
            child: _loadingHistory
                ? const Center(child: CircularProgressIndicator())
                : _bubbles.isEmpty
                ? _EmptyChat(nickname: widget.assistantNickname)
                : ListView.builder(
                    controller: _scroll,
                    padding: const EdgeInsets.all(12),
                    itemCount: _bubbles.length,
                    itemBuilder: (context, i) =>
                        _BubbleView(bubble: _bubbles[i], onResolve: _resolveAction, onOpenItem: _openItem),
                  ),
          ),
          _ModeSwitch(
            mode: _mode,
            onChanged: _streaming ? null : (mode) => setState(() => _mode = mode),
          ),
          SafeArea(
            top: false,
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
                      tooltip: _listening ? 'Stop' : 'Speak once',
                      onPressed: _streaming || _handsFree ? null : _toggleMic,
                      color: _listening
                          ? Theme.of(context).colorScheme.error
                          : null,
                      icon: Icon(_listening ? Icons.mic : Icons.mic_none),
                    ),
                    IconButton(
                      tooltip: _handsFree ? 'End hands-free' : 'Hands-free',
                      onPressed: _toggleHandsFree,
                      color: _handsFree
                          ? Theme.of(context).colorScheme.primary
                          : null,
                      icon: Icon(_handsFree
                          ? Icons.record_voice_over
                          : Icons.graphic_eq),
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
      )),
    );
  }
}

/// The mode switch above the composer: one chip per mode, scrolling sideways on a
/// narrow phone, with a line saying what the chosen mode does.
class _ModeSwitch extends StatelessWidget {
  const _ModeSwitch({required this.mode, required this.onChanged});

  final String mode;

  /// Null while a reply streams: the mode applies per message, so it is fixed until it ends.
  final ValueChanged<String>? onChanged;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SizedBox(
          height: 40,
          child: ListView(
            scrollDirection: Axis.horizontal,
            padding: const EdgeInsets.symmetric(horizontal: 12),
            children: [
              for (final m in ChatMode.all)
                Padding(
                  padding: const EdgeInsets.only(right: 6),
                  child: ChoiceChip(
                    label: Text(m.label),
                    selected: mode == m.id,
                    visualDensity: VisualDensity.compact,
                    onSelected: onChanged == null ? null : (_) => onChanged!(m.id),
                  ),
                ),
            ],
          ),
        ),
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 2, 16, 0),
          child: Text(
            ChatMode.of(mode).hint,
            style: TextStyle(fontSize: 12, color: scheme.onSurfaceVariant),
          ),
        ),
      ],
    );
  }
}

/// Says what hands-free mode is doing right now.
///
/// Without it the mode is invisible: the phone is silent between turns either
/// because it is waiting for speech or because the loop has stopped, and those
/// look identical.
class _HandsFreeBanner extends StatelessWidget {
  const _HandsFreeBanner({required this.listening, required this.speaking});

  final bool listening;
  final bool speaking;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final (label, icon) = switch ((listening, speaking)) {
      (true, _) => ('Listening…', Icons.mic),
      (_, true) => ('Speaking…', Icons.volume_up),
      _ => ('Thinking…', Icons.more_horiz),
    };

    return Container(
      width: double.infinity,
      color: scheme.primaryContainer,
      padding: const EdgeInsets.symmetric(vertical: 10, horizontal: 16),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(icon, size: 18, color: scheme.onPrimaryContainer),
          const SizedBox(width: 8),
          Text(
            'Hands-free · $label',
            style: TextStyle(
              color: scheme.onPrimaryContainer,
              fontWeight: FontWeight.w600,
            ),
          ),
        ],
      ),
    );
  }
}

class _EmptyChat extends StatelessWidget {
  const _EmptyChat({required this.nickname});

  final String nickname;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            CircleAvatar(
              radius: 34,
              backgroundColor: scheme.primaryContainer,
              foregroundColor: scheme.onPrimaryContainer,
              child: const Icon(Icons.auto_awesome, size: 30),
            ),
            const SizedBox(height: 16),
            Text(
              'Say hello to $nickname',
              style: const TextStyle(fontSize: 20, fontWeight: FontWeight.w600),
            ),
            const SizedBox(height: 6),
            Text(
              'Ask a question, or start hands-free and just talk.',
              textAlign: TextAlign.center,
              style: TextStyle(color: scheme.onSurfaceVariant),
            ),
          ],
        ),
      ),
    );
  }
}
