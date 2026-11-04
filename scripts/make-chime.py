"""Writes PersonaOS's notification chime: two soft bell notes rising a fifth, 0.7 s, mono.

Made from scratch here, so the sound is the project's own under its licence, with nothing to
credit. Run it again to change the sound:

    python scripts/make-chime.py src/frontend/PersonaOS.Mobile/android/app/src/main/res/raw/personaos_chime.wav

Standard library only.
"""

import math
import struct
import sys
import wave

RATE = 44100
LENGTH = 0.7

# (start in seconds, frequency in Hz, loudness): A5 then E6, a rising fifth that reads as "for you".
NOTES = [(0.0, 880.0, 0.55), (0.11, 1318.5, 0.6)]

# A bell's partials above its note, with their loudness: a little brightness, nothing sharp.
PARTIALS = [(1.0, 1.0), (2.0, 0.18), (3.0, 0.06)]


def sample(t: float) -> float:
    value = 0.0
    for start, freq, loud in NOTES:
        x = t - start
        if x < 0:
            continue
        attack = min(1.0, x / 0.006)  # 6 ms, so it does not click
        for ratio, amp in PARTIALS:
            # Higher partials fade faster, as on a real bell.
            decay = math.exp(-x * (5.0 + 3.0 * (ratio - 1.0)))
            value += loud * amp * attack * decay * math.sin(2 * math.pi * freq * ratio * x)
    # The last 60 ms fade to silence.
    tail = LENGTH - t
    if tail < 0.06:
        value *= max(0.0, tail / 0.06)
    return value


def main(path: str) -> None:
    frames = [sample(i / RATE) for i in range(int(RATE * LENGTH))]
    peak = max(abs(f) for f in frames)
    scale = 0.5 / peak  # about -6 dBFS: pleasant, not loud
    with wave.open(path, "wb") as out:
        out.setnchannels(1)
        out.setsampwidth(2)
        out.setframerate(RATE)
        out.writeframes(b"".join(struct.pack("<h", int(f * scale * 32767)) for f in frames))


if __name__ == "__main__":
    main(sys.argv[1])
