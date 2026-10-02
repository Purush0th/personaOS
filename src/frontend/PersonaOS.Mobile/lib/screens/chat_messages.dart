part of 'chat_screen.dart';

// What a chat shows for each message: the bubble, an assistant reply as Markdown with item
// keys as links, the notes under a reply the server could not stand behind, the cards waiting
// for Confirm, the receipts of what ran, and the typing dots.

/// An assistant reply, rendered as Markdown.
///
/// Models format their answers — bold, bullets, numbered lists — and shown as
/// plain text that arrives as literal `**Learn Rust**`. The styles are tightened
/// from the defaults, which assume a full-width document rather than a chat
/// bubble and leave far too much space around headings and lists.
class _MarkdownReply extends StatelessWidget {
  const _MarkdownReply({
    required this.text,
    required this.foreground,
    this.unknownItems = const [],
    this.onOpenItem,
  });

  final String text;
  final Color foreground;

  /// Keys the server could not find: shown as written, not as links.
  final List<String> unknownItems;

  /// A task or goal key in the reply is a link to its quick view.
  final ValueChanged<String>? onOpenItem;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final body = TextStyle(color: foreground, fontSize: 15, height: 1.35);

    return MarkdownBody(
      data: onOpenItem == null ? text : linkItemKeys(text, unknown: unknownItems),
      selectable: true,
      onTapLink: (_, href, _) {
        final key = itemKeyFromLink(href);
        if (key != null) onOpenItem?.call(key);
      },
      styleSheet: MarkdownStyleSheet.fromTheme(theme).copyWith(
        p: body,
        a: body.copyWith(
          color: theme.colorScheme.primary,
          fontWeight: FontWeight.w600,
          decoration: TextDecoration.underline,
          decorationColor: theme.colorScheme.primary,
        ),
        listBullet: body,
        strong: body.copyWith(fontWeight: FontWeight.w700),
        em: body.copyWith(fontStyle: FontStyle.italic),
        h1: body.copyWith(fontSize: 19, fontWeight: FontWeight.w700),
        h2: body.copyWith(fontSize: 17, fontWeight: FontWeight.w700),
        h3: body.copyWith(fontSize: 16, fontWeight: FontWeight.w700),
        code: body.copyWith(
          fontFamily: 'monospace',
          fontSize: 13,
          backgroundColor: theme.colorScheme.surfaceContainerHigh,
        ),
        codeblockDecoration: BoxDecoration(
          color: theme.colorScheme.surfaceContainerHigh,
          borderRadius: BorderRadius.circular(10),
        ),
        blockquoteDecoration: BoxDecoration(
          color: theme.colorScheme.surfaceContainerHigh,
          borderRadius: BorderRadius.circular(10),
        ),
        blockSpacing: 8,
        listIndent: 18,
      ),
    );
  }
}

class _BubbleView extends StatelessWidget {
  const _BubbleView({required this.bubble, required this.onResolve, this.onOpenItem});

  final _Bubble bubble;

  /// Opens a task or goal whose key the reply names.
  final ValueChanged<String>? onOpenItem;

  /// Confirm (true) or discard (false) a proposed change.
  final Future<void> Function(PendingAction action, bool confirm) onResolve;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    // Taken from the scheme, not hardcoded: the old fixed navy was a light-theme
    // colour and all but vanished against a dark background.
    final background = bubble.isError
        ? theme.colorScheme.errorContainer
        : bubble.isUser
            ? theme.colorScheme.primary
            : theme.colorScheme.surfaceContainerHighest;
    final foreground = bubble.isError
        ? theme.colorScheme.onErrorContainer
        : bubble.isUser
            ? theme.colorScheme.onPrimary
            : theme.colorScheme.onSurface;

    return Align(
      alignment: bubble.isUser ? Alignment.centerRight : Alignment.centerLeft,
      child: Container(
        margin: const EdgeInsets.symmetric(vertical: 4),
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
        // Most of a phone's width, and no wider than reads comfortably on a tablet.
        constraints: BoxConstraints(maxWidth: math.min(MediaQuery.sizeOf(context).width * 0.82, 600)),
        decoration: BoxDecoration(
          color: background,
          borderRadius: BorderRadius.circular(14),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            if (bubble.text.isEmpty && !bubble.isUser)
              TypingDots(color: foreground)
            else if (bubble.isUser || bubble.isError)
              // Your own words and server errors are literal — rendering them
              // as Markdown would silently eat characters you actually typed.
              SelectableText(bubble.text, style: TextStyle(color: foreground))
            else
              _MarkdownReply(
                text: bubble.text,
                foreground: foreground,
                unknownItems: bubble.unknownItems,
                onOpenItem: onOpenItem,
              ),
            if (bubble.unverifiedClaim && !bubble.isUser) const UnverifiedClaimNote(),
            if (!bubble.isUser && bubble.unknownItems.isNotEmpty) UnknownItemsNote(keys: bubble.unknownItems),
            if (bubble.pending?.isNotEmpty ?? false)
              _ProposalList(actions: bubble.pending!, onResolve: onResolve),
            if (bubble.actions?.isNotEmpty ?? false)
              _ReceiptList(actions: bubble.actions!, foreground: foreground),
          ],
        ),
      ),
    );
  }
}

/// Shown under a reply that says a change was made when no tool made one, even after the
/// server asked the model to correct itself. The sentence above it is not a receipt.
class UnverifiedClaimNote extends StatelessWidget {
  const UnverifiedClaimNote({super.key});

  static const message =
      'This reply says a change was made, but nothing was saved. Ask again if you want it done.';

  @override
  Widget build(BuildContext context) =>
      const _ReplyNote(icon: Icons.warning_amber_rounded, message: message);
}

/// Shown under a reply that names items which do not exist. Small models invent keys, and an
/// invented TASK-6 reads as confidently as a real one. The server checked, not guessed.
class UnknownItemsNote extends StatelessWidget {
  const UnknownItemsNote({super.key, required this.keys});

  final List<String> keys;

  /// The note's wording, or null when every item the reply named exists. Matches the web app.
  static String? messageFor(List<String> keys) {
    if (keys.isEmpty) return null;
    final list = keys.length == 1
        ? keys.single
        : '${keys.sublist(0, keys.length - 1).join(', ')} and ${keys.last}';
    return 'This reply mentions $list, which ${keys.length == 1 ? 'does' : 'do'} not exist. '
        'Check before relying on it.';
  }

  @override
  Widget build(BuildContext context) =>
      _ReplyNote(icon: Icons.help_outline, message: messageFor(keys) ?? '');
}

/// A caution under a reply: tinted from the theme, so it reads in light and dark mode alike.
class _ReplyNote extends StatelessWidget {
  const _ReplyNote({required this.icon, required this.message});

  final IconData icon;
  final String message;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = theme.colorScheme;

    return Container(
      margin: const EdgeInsets.only(top: 8),
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
      decoration: BoxDecoration(
        color: colors.tertiaryContainer,
        border: Border(left: BorderSide(color: colors.tertiary, width: 3)),
        borderRadius: BorderRadius.circular(4),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          Padding(
            padding: const EdgeInsets.only(right: 6, top: 1),
            child: Icon(icon, size: 16, color: colors.onTertiaryContainer),
          ),
          Flexible(
            child: Text(
              message,
              style: theme.textTheme.bodySmall?.copyWith(color: colors.onTertiaryContainer),
            ),
          ),
        ],
      ),
    );
  }
}

/// Changes the assistant wants to make, with Confirm / Discard.
///
/// Louder than a receipt on purpose: this is the last thing standing between a model's
/// whim and the user's data, and without it the assistant cannot write at all on mobile.
class _ProposalList extends StatelessWidget {
  const _ProposalList({required this.actions, required this.onResolve});

  final List<PendingAction> actions;
  final Future<void> Function(PendingAction action, bool confirm) onResolve;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.only(top: 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          for (final action in actions)
            Container(
              margin: const EdgeInsets.only(top: 4),
              padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
              decoration: BoxDecoration(
                color: action.isPending ? const Color(0xFFFFF8E6) : Colors.transparent,
                border: Border.all(
                  color: action.isPending
                      ? const Color(0xFFD9C48A)
                      : theme.dividerColor,
                ),
                borderRadius: BorderRadius.circular(8),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    _label(action),
                    style: theme.textTheme.bodySmall?.copyWith(
                      color: action.isPending ? const Color(0xFF5A4A1F) : null,
                    ),
                  ),
                  if (action.isPending)
                    // Wrap, not Row: on a narrow phone the two buttons overflow the
                    // card by a few pixels and Confirm gets clipped.
                    Wrap(
                      alignment: WrapAlignment.end,
                      spacing: 4,
                      children: [
                        TextButton(
                          onPressed: () => onResolve(action, false),
                          child: const Text('Discard'),
                        ),
                        FilledButton(
                          onPressed: () => onResolve(action, true),
                          child: const Text('Confirm'),
                        ),
                      ],
                    ),
                ],
              ),
            ),
        ],
      ),
    );
  }

  static String _label(PendingAction action) => switch (action.status) {
        'confirmed' =>
          '${action.resultOk == false ? '✕' : '✓'} ${action.resultSummary ?? action.summary}',
        'discarded' => '— Discarded: ${action.summary}',
        _ => action.summary,
      };
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
                          ? action.label
                          : '${action.label}  ${action.summary}',
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

/// Three dots that rise and brighten one after another while a reply is on its way. It was a
/// still "…", which looked the same as a reply that had stopped. With animations turned off in
/// the phone's settings the dots stay still.
class TypingDots extends StatefulWidget {
  const TypingDots({super.key, required this.color});

  final Color color;

  @override
  State<TypingDots> createState() => _TypingDotsState();
}

class _TypingDotsState extends State<TypingDots> with SingleTickerProviderStateMixin {
  late final AnimationController _controller =
      AnimationController(vsync: this, duration: const Duration(milliseconds: 1200));

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (MediaQuery.of(context).disableAnimations) {
      _controller.stop();
    } else if (!_controller.isAnimating) {
      _controller.repeat();
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Semantics(
      label: 'Thinking',
      child: SizedBox(
        height: 20,
        child: AnimatedBuilder(
          animation: _controller,
          builder: (context, _) => Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              for (var i = 0; i < 3; i++) _dot(i),
            ],
          ),
        ),
      ),
    );
  }

  /// Each dot peaks an eighth of the cycle after the one before it, and rests half the cycle.
  Widget _dot(int index) {
    final phase = (_controller.value - index * 0.125) % 1;
    final lift = phase < 0.5 ? (phase < 0.25 ? phase / 0.25 : (0.5 - phase) / 0.25) : 0.0;
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 2),
      child: Transform.translate(
        offset: Offset(0, -3 * lift),
        child: Container(
          width: 6,
          height: 6,
          decoration: BoxDecoration(
            color: widget.color.withValues(alpha: 0.3 + 0.7 * lift),
            shape: BoxShape.circle,
          ),
        ),
      ),
    );
  }
}
