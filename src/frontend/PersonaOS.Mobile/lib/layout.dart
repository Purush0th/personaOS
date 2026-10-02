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
