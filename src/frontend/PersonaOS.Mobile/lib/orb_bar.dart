import 'package:flutter/material.dart';

import 'layout.dart';
import 'phosphor_icons.dart';

/// A module the bottom bar can hold, between Home and More.
class BarPlace {
  const BarPlace({required this.feature, required this.label, required this.icon, required this.activeIcon});

  /// The module's feature name, as `/api/branding` lists it.
  final String feature;
  final String label;
  final IconData icon;

  /// The filled glyph, for the place you are on (docs/DESIGN.md §7).
  final IconData activeIcon;

  static const board = BarPlace(
    feature: 'board',
    label: 'Board',
    icon: PhosphorIconsRegular.kanban,
    activeIcon: PhosphorIconsFill.kanban,
  );
  static const planner = BarPlace(
    feature: 'planner',
    label: 'Planner',
    icon: PhosphorIconsRegular.calendarDots,
    activeIcon: PhosphorIconsFill.calendarDots,
  );
  static const reminders = BarPlace(
    feature: 'reminders',
    label: 'Reminders',
    icon: PhosphorIconsRegular.alarm,
    activeIcon: PhosphorIconsFill.alarm,
  );
  static const notes = BarPlace(
    feature: 'notes',
    label: 'Notes',
    icon: PhosphorIconsRegular.note,
    activeIcon: PhosphorIconsFill.note,
  );
  static const goals = BarPlace(
    feature: 'goals',
    label: 'Goals',
    icon: PhosphorIconsRegular.flag,
    activeIcon: PhosphorIconsFill.flag,
  );

  /// In the order they claim a slot: Board and Planner, and when one of them is switched off, the
  /// next module that is on takes its place.
  static const _order = [board, planner, reminders, notes, goals];

  /// The (at most two) places the bar shows for a server with [features] switched on.
  static List<BarPlace> pick(List<String> features) =>
      _order.where((p) => features.contains(p.feature)).take(2).toList();
}

/// The phone's bottom bar: Home, two places, the Orb raised in the middle, and More
/// (docs/DESIGN.md §5). The Orb is the assistant, so it is not a place: tapping it opens the chat
/// over everything, holding it starts hands-free voice.
///
/// It is drawn taller than the bar by [raise] so the Orb can stand above it; that strip is
/// transparent and lets touches through to the page, so the screen using it sets
/// `Scaffold.extendBody` and keeps its content clear of [barHeight] only.
class OrbBar extends StatelessWidget {
  const OrbBar({
    super.key,
    required this.places,
    required this.selected,
    required this.onSelect,
    required this.onOrb,
    required this.onMore,
    required this.assistantName,
    this.onOrbHold,
  });

  /// The bar itself, without the gesture area under it.
  static const double barHeight = 76;

  /// How far the Orb stands above the bar.
  static const double raise = 34;

  static const double _orb = 60;
  static const double _collar = 5;

  /// What a page must leave clear at the bottom for the bar: the bar and the gesture area.
  static double clearance(BuildContext context) => barHeight + MediaQuery.paddingOf(context).bottom;

  final List<BarPlace> places;

  /// 0 for Home, 1 and on for [places]; anything else when none of them is showing.
  final int selected;
  final ValueChanged<int> onSelect;
  final VoidCallback onOrb;
  final VoidCallback? onOrbHold;
  final VoidCallback onMore;

  /// Names the Orb for screen readers: "Chat with Neo".
  final String assistantName;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final dark = Theme.of(context).brightness == Brightness.dark;
    final bottom = MediaQuery.paddingOf(context).bottom;
    final ground = dark ? scheme.surfaceContainerLow : scheme.surface;

    Widget slot(int index) {
      if (index > places.length) return const Expanded(child: SizedBox());
      final place = places[index - 1];
      return _Tab(
        key: Key('bar-${place.feature}'),
        label: place.label,
        icon: place.icon,
        activeIcon: place.activeIcon,
        on: selected == index,
        onTap: () => onSelect(index),
      );
    }

    return SizedBox(
      height: raise + _collar + barHeight + bottom,
      child: Stack(
        clipBehavior: Clip.none,
        children: [
          Positioned(
            left: 0,
            right: 0,
            bottom: 0,
            height: barHeight + bottom,
            child: DecoratedBox(
              decoration: BoxDecoration(
                color: ground,
                border: Border(top: BorderSide(color: scheme.outlineVariant)),
              ),
              child: Padding(
                padding: EdgeInsets.only(bottom: bottom, top: 9),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    _Tab(
                      key: const Key('bar-home'),
                      label: 'Home',
                      icon: PhosphorIconsRegular.house,
                      activeIcon: PhosphorIconsFill.house,
                      on: selected == 0,
                      onTap: () => onSelect(0),
                    ),
                    slot(1),
                    const SizedBox(width: _orb + 2 * _collar + 14),
                    slot(2),
                    _Tab(
                      key: const Key('bar-more'),
                      label: 'More',
                      icon: PhosphorIconsRegular.dotsThreeOutline,
                      activeIcon: PhosphorIconsFill.dotsThreeOutline,
                      on: false,
                      onTap: onMore,
                    ),
                  ],
                ),
              ),
            ),
          ),
          Positioned(
            top: 0,
            left: 0,
            right: 0,
            child: Center(
              child: Semantics(
                button: true,
                label: 'Chat with $assistantName',
                hint: onOrbHold == null ? null : 'Hold for hands-free voice',
                excludeSemantics: true,
                child: Material(
                  key: const Key('bar-orb'),
                  color: ground,
                  shape: const CircleBorder(),
                  child: InkWell(
                    customBorder: const CircleBorder(),
                    onTap: onOrb,
                    onLongPress: onOrbHold,
                    child: const Padding(
                      padding: EdgeInsets.all(_collar),
                      child: Orb(size: _orb),
                    ),
                  ),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// One place on the bar: its icon over its name, the icon filled and on a soft pill when it is the
/// place you are on.
class _Tab extends StatelessWidget {
  const _Tab({
    super.key,
    required this.label,
    required this.icon,
    required this.activeIcon,
    required this.on,
    required this.onTap,
  });

  final String label;
  final IconData icon;
  final IconData activeIcon;
  final bool on;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final dark = Theme.of(context).brightness == Brightness.dark;
    final ink = on ? scheme.onSurface : scheme.onSurfaceVariant;
    return Expanded(
      child: Semantics(
        button: true,
        selected: on,
        label: label,
        excludeSemantics: true,
        child: InkWell(
          onTap: onTap,
          borderRadius: BorderRadius.circular(14),
          child: Padding(
            padding: const EdgeInsets.symmetric(vertical: 2),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                AnimatedContainer(
                  duration: const Duration(milliseconds: 200),
                  curve: Curves.easeOutCubic,
                  width: 56,
                  height: 28,
                  decoration: BoxDecoration(
                    color: on ? (dark ? const Color(0x14FFFFFF) : scheme.surfaceContainerHigh) : Colors.transparent,
                    borderRadius: BorderRadius.circular(14),
                  ),
                  child: Icon(on ? activeIcon : icon, size: 24, color: ink),
                ),
                const SizedBox(height: 3),
                Text(
                  label,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(fontSize: 12, fontWeight: on ? FontWeight.w600 : FontWeight.w400, color: ink),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
