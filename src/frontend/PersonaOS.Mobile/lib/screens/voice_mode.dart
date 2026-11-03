import 'dart:math' as math;

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/scheduler.dart';

import '../layout.dart';
import '../phosphor_icons.dart';

/// What the hands-free conversation is doing right now.
enum VoicePhase { listening, thinking, speaking }

/// Hands-free as its own screen: the Orb in the middle, moving with the voice that is speaking,
/// what was said beneath it, and a way out.
///
/// A banner over the chat said what the loop was doing but showed nothing happening, so the phone
/// felt dead between turns (owner, 2026-11-03). Here the Orb swells with the microphone while
/// listening, turns slowly while the reply is on its way and pulses while it is read out. Tapping
/// it does what the moment calls for: sends now while listening, cuts the reply short while
/// speaking.
class VoiceModeView extends StatefulWidget {
  const VoiceModeView({
    super.key,
    required this.nickname,
    required this.phase,
    required this.level,
    required this.caption,
    required this.captionIsUser,
    required this.onTapOrb,
    required this.onEnd,
  });

  final String nickname;
  final VoicePhase phase;

  /// The microphone's loudness, 0 to 1, while listening.
  final ValueListenable<double> level;

  /// The words on screen: the user's while they are being heard, the reply once it arrives.
  final String caption;
  final bool captionIsUser;

  final VoidCallback onTapOrb;
  final VoidCallback onEnd;

  @override
  State<VoiceModeView> createState() => _VoiceModeViewState();
}

class _VoiceModeViewState extends State<VoiceModeView> with SingleTickerProviderStateMixin {
  late final Ticker _ticker = createTicker(_tick);
  final _captionScroll = ScrollController();

  /// Seconds since the screen opened, for the motion that is not driven by the microphone.
  double _t = 0;

  /// The microphone level eased over a few frames, so the Orb breathes rather than jitters.
  double _level = 0;

  void _tick(Duration elapsed) {
    setState(() {
      _t = elapsed.inMicroseconds / 1e6;
      final target = widget.phase == VoicePhase.listening ? widget.level.value : 0.0;
      _level += (target - _level) * (target > _level ? 0.35 : 0.12);
    });
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    // With reduced motion the Orb stands still and only the words change.
    final still = MediaQuery.maybeDisableAnimationsOf(context) ?? false;
    if (still && _ticker.isActive) _ticker.stop();
    if (!still && !_ticker.isActive) _ticker.start();
  }

  @override
  void didUpdateWidget(VoiceModeView old) {
    super.didUpdateWidget(old);
    if (old.caption != widget.caption) {
      // The newest words stay in view as the reply streams in.
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (_captionScroll.hasClients) _captionScroll.jumpTo(_captionScroll.position.maxScrollExtent);
      });
    }
  }

  @override
  void dispose() {
    _ticker.dispose();
    _captionScroll.dispose();
    super.dispose();
  }

  /// How much bigger than at rest the Orb is drawn this frame.
  double get _swell => switch (widget.phase) {
    VoicePhase.listening => 0.22 * _level,
    VoicePhase.thinking => 0.03 * math.sin(_t * 2 * math.pi / 1.8),
    // Two waves out of step read as speech rather than a metronome.
    VoicePhase.speaking => 0.07 * (0.5 + 0.5 * math.sin(_t * 7.3)) * (0.6 + 0.4 * math.sin(_t * 2.1)),
  };

  @override
  Widget build(BuildContext context) {
    final (status, hint, action) = switch (widget.phase) {
      VoicePhase.listening => ('Listening', 'Pause when you are done, or tap to send', 'Send now'),
      VoicePhase.thinking => ('Thinking', '', ''),
      VoicePhase.speaking => ('Speaking', 'Tap to interrupt', 'Interrupt'),
    };
    const ink = Colors.white;
    const soft = Color(0xB3FFFFFF);

    return PopScope(
      // Back leaves hands-free rather than the chat behind it.
      canPop: false,
      onPopInvokedWithResult: (didPop, _) {
        if (!didPop) widget.onEnd();
      },
      child: Material(
        color: const Color(0xFF0E0B1A),
        child: DecoratedBox(
          decoration: const BoxDecoration(
            gradient: RadialGradient(
              center: Alignment(0, -0.15),
              radius: 0.9,
              colors: [Color(0x803B2A7A), Color(0x000E0B1A)],
            ),
          ),
          child: SafeArea(
            child: Column(
              children: [
                Padding(
                  padding: const EdgeInsets.fromLTRB(24, 16, 24, 0),
                  child: Text(
                    '${widget.nickname} · hands-free',
                    style: const TextStyle(color: soft, fontSize: 14, fontWeight: FontWeight.w500),
                  ),
                ),
                Expanded(
                  child: Center(
                    child: Semantics(
                      button: action.isNotEmpty,
                      label: action.isEmpty ? status : action,
                      child: GestureDetector(
                        key: const Key('voice-orb'),
                        behavior: HitTestBehavior.opaque,
                        onTap: action.isEmpty ? null : widget.onTapOrb,
                        child: SizedBox.square(
                          dimension: 300,
                          child: CustomPaint(
                            painter: _Ripples(phase: widget.phase, t: _t, level: _level),
                            child: Center(
                              child: Transform.rotate(
                                angle: widget.phase == VoicePhase.thinking ? _t * 1.2 : _t * 0.15,
                                child: Transform.scale(scale: 1 + _swell, child: const Orb(size: 168)),
                              ),
                            ),
                          ),
                        ),
                      ),
                    ),
                  ),
                ),
                Text(
                  status,
                  key: const Key('voice-status'),
                  style: const TextStyle(color: ink, fontSize: 22, fontWeight: FontWeight.w600),
                ),
                const SizedBox(height: 6),
                Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 24),
                  child: SizedBox(
                    height: 20,
                    child: Text(
                      hint,
                      textAlign: TextAlign.center,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(color: soft, fontSize: 14),
                    ),
                  ),
                ),
                const SizedBox(height: 16),
                // The words, newest at the bottom; a long reply scrolls inside this window. Its height is
                // fixed, so the Orb stays where it is as the words come and go.
                SizedBox(
                  height: 156,
                  child: SingleChildScrollView(
                    controller: _captionScroll,
                    padding: const EdgeInsets.symmetric(horizontal: 28),
                    child: SizedBox(
                      width: double.infinity,
                      child: Text(
                        widget.captionIsUser && widget.caption.isNotEmpty ? '“${widget.caption}”' : widget.caption,
                        textAlign: TextAlign.center,
                        style: TextStyle(
                          color: widget.captionIsUser ? soft : ink,
                          fontSize: 18,
                          height: 1.45,
                          fontStyle: widget.captionIsUser ? FontStyle.italic : FontStyle.normal,
                        ),
                      ),
                    ),
                  ),
                ),
                const SizedBox(height: 20),
                Padding(
                  padding: const EdgeInsets.fromLTRB(32, 0, 32, 24),
                  child: Row(
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    children: [
                      _RoundButton(
                        tooltip: action.isEmpty ? status : action,
                        icon: widget.phase == VoicePhase.speaking
                            ? PhosphorIconsRegular.speakerSlash
                            : PhosphorIconsRegular.arrowUp,
                        fill: const Color(0x26FFFFFF),
                        onPressed: action.isEmpty ? null : widget.onTapOrb,
                      ),
                      _RoundButton(
                        tooltip: 'End hands-free',
                        icon: PhosphorIconsRegular.x,
                        fill: const Color(0xFFD9443A),
                        onPressed: widget.onEnd,
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _RoundButton extends StatelessWidget {
  const _RoundButton({required this.tooltip, required this.icon, required this.fill, required this.onPressed});

  final String tooltip;
  final IconData icon;
  final Color fill;
  final VoidCallback? onPressed;

  @override
  Widget build(BuildContext context) {
    return IconButton(
      tooltip: tooltip,
      onPressed: onPressed,
      iconSize: 26,
      style: IconButton.styleFrom(
        fixedSize: const Size.square(64),
        backgroundColor: fill,
        foregroundColor: Colors.white,
        disabledBackgroundColor: const Color(0x0FFFFFFF),
        disabledForegroundColor: const Color(0x4DFFFFFF),
      ),
      icon: Icon(icon),
    );
  }
}

/// Soft rings around the Orb: they spread with the voice while listening and roll outward while
/// speaking. Thinking has none, so the three moments look different at a glance.
class _Ripples extends CustomPainter {
  _Ripples({required this.phase, required this.t, required this.level});

  final VoicePhase phase;
  final double t;
  final double level;

  @override
  void paint(Canvas canvas, Size size) {
    final centre = size.center(Offset.zero);
    const rest = 84.0;
    final paint = Paint()..style = PaintingStyle.stroke;
    switch (phase) {
      case VoicePhase.listening:
        for (var i = 1; i <= 2; i++) {
          paint
            ..strokeWidth = 2
            ..color = Color.fromRGBO(139, 124, 255, (0.45 - i * 0.12) * (0.3 + level));
          canvas.drawCircle(centre, rest + i * (14 + 40 * level), paint);
        }
      case VoicePhase.speaking:
        for (var i = 0; i < 3; i++) {
          final p = ((t / 1.6) + i / 3) % 1;
          paint
            ..strokeWidth = 2.5
            ..color = Color.fromRGBO(86, 200, 245, 0.5 * (1 - p));
          canvas.drawCircle(centre, rest + 8 + p * 62, paint);
        }
      case VoicePhase.thinking:
        break;
    }
  }

  @override
  bool shouldRepaint(_Ripples old) => old.t != t || old.level != level || old.phase != phase;
}
