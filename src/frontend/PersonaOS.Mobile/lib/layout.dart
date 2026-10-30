import 'dart:math' as math;

import 'package:flutter/material.dart';

/// Widths the screens adapt at, the same on a phone, a tablet, portrait or landscape: what counts
/// is the width the screen actually has, not the kind of device.
abstract final class Breakpoints {
  /// From here the board shows its columns side by side instead of one page per column. Low enough
  /// for a tablet held upright: many report only 533–600 logical pixels across.
  static const double board = 520;

  /// From here the home screen lays its modules out two abreast.
  static const double twoColumns = 520;

  /// The widest a list of text reads comfortably; wider screens centre it.
  static const double readable = 840;

  /// From here a quick view floats as a dialog over the screen instead of rising as a sheet from
  /// the bottom edge: on a tablet a full-width sheet is mostly empty space.
  static const double dialog = 600;

  /// From here an item's full page puts its details beside its text instead of below it.
  static const double sideBySide = 840;
}

/// The typeface, bundled in assets/fonts and shared with the web app. The theme sets it for text
/// widgets; a style that is not merged with the theme (a button's own textStyle, a TextPainter)
/// has to name it, or it falls back to Roboto.
const String personaOsFont = 'Geist';

/// Geist Mono, for numbers that line up: dates in lists, percentages, keys.
const String personaOsMonoFont = 'Geist Mono';

/// The behind-everything colour of the dark theme (docs/DESIGN.md "Dusk"): a violet-tinted
/// off-black, never pure black.
const Color duskSurface = Color(0xFF0D0B14);

/// The backdrop of the assistant's own screens (home greeting, chat), the same glow as the web
/// app's chat page: a deep violet dusk in the dark theme, a faint lavender dawn in the light one.
class Presence extends StatelessWidget {
  const Presence({super.key, required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    final dark = Theme.of(context).brightness == Brightness.dark;
    final surface = Theme.of(context).colorScheme.surface;
    // The surface is its own layer: a BoxDecoration ignores its colour once it has a gradient, and
    // the page would show through wherever the glow has faded out.
    return ColoredBox(
      color: surface,
      child: DecoratedBox(
        decoration: BoxDecoration(
          gradient: RadialGradient(
            center: dark ? const Alignment(-0.8, -1.1) : const Alignment(0, -1.2),
            radius: dark ? 1.3 : 1.1,
            colors: dark ? const [Color(0x8C3B2A7A), Color(0x003B2A7A)] : const [Color(0xFFE9E4FF), Color(0x00E9E4FF)],
          ),
        ),
        child: dark
            ? DecoratedBox(
                decoration: const BoxDecoration(
                  gradient: RadialGradient(
                    center: Alignment(1.1, -0.5),
                    radius: 0.9,
                    colors: [Color(0x662A1F5C), Color(0x002A1F5C)],
                  ),
                ),
                child: child,
              )
            : child,
      ),
    );
  }
}

/// The Orb: the assistant itself. Three radial blooms on a violet sphere, with a soft glow. The
/// same mark as the web app's `.orb`; it is the avatar beside replies, the send button and the
/// centrepiece of an empty chat. A [child] (an icon) sits on top of it.
class Orb extends StatelessWidget {
  const Orb({super.key, this.size = 24, this.glow = true, this.child});

  final double size;
  final bool glow;
  final Widget? child;

  static const _core = RadialGradient(colors: [Color(0xFF8B7CFF), Color(0xFF3B2A9A)], stops: [0, 0.75]);
  static const _blooms = [
    RadialGradient(center: Alignment(0.44, -0.52), radius: 0.45, colors: [Color(0xFF56C8F5), Color(0x0056C8F5)]),
    RadialGradient(center: Alignment(-0.5, 0.6), radius: 0.55, colors: [Color(0xFFE36BD8), Color(0x00E36BD8)]),
    RadialGradient(center: Alignment(-0.3, -0.4), radius: 0.18, colors: [Color(0xD9FFFFFF), Color(0x00FFFFFF)]),
  ];

  @override
  Widget build(BuildContext context) {
    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        gradient: _core,
        boxShadow: glow ? [BoxShadow(color: const Color(0x668B7CFF), blurRadius: size * 0.5)] : null,
      ),
      child: Stack(
        fit: StackFit.expand,
        children: [
          for (final bloom in _blooms)
            DecoratedBox(
              decoration: BoxDecoration(shape: BoxShape.circle, gradient: bloom),
            ),
          if (child != null) Center(child: child),
        ],
      ),
    );
  }
}

/// The status colours of the workspace screens (docs/DESIGN.md "Status"), the same pairs as the
/// web app's `--status-*` tokens: a text colour and a soft fill for each, per theme. Colour is never
/// the only sign: a pill also carries its words and, for a task's column, a mark.
enum StatusTone {
  todo(Color(0xFF52525B), Color(0xFFF1F1F3), Color(0xFFB4B0C2), Color(0xFF221E2E)),
  progress(Color(0xFF1D4ED8), Color(0xFFE8F0FE), Color(0xFF93B4FF), Color(0xFF1A2340)),
  review(Color(0xFFA15C07), Color(0xFFFDF3E1), Color(0xFFF5C46B), Color(0xFF2E2414)),
  done(Color(0xFF166534), Color(0xFFE6F6EC), Color(0xFF6FD69A), Color(0xFF132A1E)),
  urgent(Color(0xFFC2261D), Color(0xFFFDECEA), Color(0xFFFF8A80), Color(0xFF341716)),
  waiting(Color(0xFF7E22CE), Color(0xFFF4ECFD), Color(0xFFC9A6FF), Color(0xFF2A1A40));

  const StatusTone(this._lightInk, this._lightFill, this._darkInk, this._darkFill);

  final Color _lightInk;
  final Color _lightFill;
  final Color _darkInk;
  final Color _darkFill;

  static bool _dark(BuildContext context) => Theme.of(context).brightness == Brightness.dark;

  /// The text colour, for a word on the page ("running", a warning) or inside the pill.
  Color ink(BuildContext context) => _dark(context) ? _darkInk : _lightInk;

  /// The pill's fill.
  Color fill(BuildContext context) => _dark(context) ? _darkFill : _lightFill;
}

/// The shape a status pill leads with, so a status reads without its colour: a dashed ring for
/// work not started, a half-filled ring for work under way, a filled dot for work done.
enum StatusMark { notStarted, underWay, finished }

/// A status as a small pill: a soft fill, its words, and optionally a [mark] before them.
class StatusPill extends StatelessWidget {
  const StatusPill({super.key, required this.label, required this.tone, this.mark});

  final String label;
  final StatusTone tone;
  final StatusMark? mark;

  @override
  Widget build(BuildContext context) {
    final ink = tone.ink(context);
    return Container(
      height: 24,
      padding: EdgeInsets.only(left: mark == null ? 10 : 8, right: 10),
      decoration: ShapeDecoration(color: tone.fill(context), shape: const StadiumBorder()),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (mark case final mark?) ...[
            CustomPaint(size: const Size.square(10), painter: _StatusMarkPainter(mark, ink)),
            const SizedBox(width: 6),
          ],
          Flexible(
            child: Text(
              label,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(fontSize: 12, height: 1, fontWeight: FontWeight.w600, color: ink),
            ),
          ),
        ],
      ),
    );
  }
}

class _StatusMarkPainter extends CustomPainter {
  _StatusMarkPainter(this.mark, this.color);

  final StatusMark mark;
  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final centre = size.center(Offset.zero);
    final radius = size.width / 2 - 0.75;
    final ring = Paint()
      ..color = color
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1.5;
    final solid = Paint()..color = color;
    final box = Rect.fromCircle(center: centre, radius: radius);
    switch (mark) {
      case StatusMark.notStarted:
        // Eight dashes around the ring.
        for (var i = 0; i < 8; i++) {
          canvas.drawArc(box, i * math.pi / 4, math.pi / 8, false, ring);
        }
      case StatusMark.underWay:
        canvas.drawCircle(centre, radius, ring);
        canvas.drawArc(box, -math.pi / 2, math.pi, true, solid);
      case StatusMark.finished:
        canvas.drawCircle(centre, radius + 0.75, solid);
    }
  }

  @override
  bool shouldRepaint(_StatusMarkPainter oldDelegate) => oldDelegate.mark != mark || oldDelegate.color != color;
}

/// Shows a quick view: a short look at one item, with its common actions and a way to its full
/// page. A sheet from the bottom on a phone; a dialog no wider than [maxWidth] on a wider screen.
/// Either way it scrolls when its content is taller than most of the screen.
Future<T?> showQuickView<T>(BuildContext context, {required WidgetBuilder builder, double maxWidth = 560}) {
  final size = MediaQuery.sizeOf(context);
  if (size.width >= Breakpoints.dialog) {
    return showDialog<T>(
      context: context,
      builder: (context) => Dialog(
        clipBehavior: Clip.antiAlias,
        child: ConstrainedBox(
          constraints: BoxConstraints(maxWidth: maxWidth, maxHeight: size.height * 0.85),
          child: builder(context),
        ),
      ),
    );
  }
  return showModalBottomSheet<T>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    useSafeArea: true,
    builder: (context) => ConstrainedBox(
      constraints: BoxConstraints(maxHeight: size.height * 0.85),
      child: builder(context),
    ),
  );
}

/// Centres [child] and keeps it no wider than [maxWidth], so a list on a tablet or a phone in
/// landscape does not stretch lines across the whole screen. The child gets the full height and an
/// exact width, so on a narrow screen it lays out exactly as it would without this.
class ReadableWidth extends StatelessWidget {
  const ReadableWidth({super.key, required this.child, this.maxWidth = Breakpoints.readable});

  final Widget child;
  final double maxWidth;

  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, constraints) => Align(
      alignment: Alignment.topCenter,
      child: SizedBox(
        width: math.min(constraints.maxWidth, maxWidth),
        height: constraints.hasBoundedHeight ? constraints.maxHeight : null,
        child: child,
      ),
    ),
  );
}
