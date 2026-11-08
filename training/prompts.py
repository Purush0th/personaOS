"""Every training prompt, from every capability in capabilities/.

    python -I training/prompts.py                          # counts per capability and intent
    python -I training/prompts.py scripts/model-check.ps1  # and any prompt too close to a suite message

The wording here is kept apart from scripts/model-check.ps1: the suites must measure the trained
model on things it has not seen. `check_overlap` enforces that.
"""

import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from capabilities import CAPABILITIES  # noqa: E402

# Kept for collect.py, which creates these items before a run.
from capabilities.core import FIXTURE_GOALS, FIXTURE_REMINDERS, FIXTURE_TASKS, MOVE_FORMS  # noqa: E402,F401
from capabilities.phrasings import FIXTURE_NOTES  # noqa: E402,F401
from capabilities.planner import FIXTURE_ROUTINE, PROBE as PLANNER_PROBE  # noqa: E402,F401


SUITE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "scripts", "model-check.ps1")


def build(seed: int = 7, only: list[str] | None = None) -> list[dict]:
    """Every prompt, capability by capability, each tagged with its capability's name. A prompt too
    close to a suite message is left out, so the suites keep measuring unseen wording."""
    out: list[dict] = []
    for name, module in CAPABILITIES.items():
        if only and name not in only:
            continue
        out.extend(dict(p, capability=name) for p in module.build(seed))
    if os.path.exists(SUITE):
        close = set(check_overlap(out, SUITE))
        out = [p for p in out if p["text"] not in close]
    return out


def check_overlap(prompts: list[dict], suite_path: str) -> list[str]:
    """Training prompts too close to a message in the accuracy suite (shared word 4-grams)."""
    suite = open(suite_path, encoding="utf-8-sig").read().lower()
    # Every quoted string in the script: more than the messages, which only makes the check stricter.
    messages = re.findall(r"'((?:[^']|'')+)'|\"([^\"$]+)\"", suite)
    grams = set()
    for pair in messages:
        words = re.findall(r"[a-z0-9]+", (pair[0] or pair[1]).replace("''", "'"))
        grams.update(tuple(words[i:i + 4]) for i in range(len(words) - 3))
    close = []
    for p in prompts:
        words = re.findall(r"[a-z0-9]+", p["text"].lower())
        if any(tuple(words[i:i + 4]) in grams for i in range(len(words) - 3)):
            close.append(p["text"])
    return close


if __name__ == "__main__":
    import collections

    prompts = build()
    print(len(prompts), dict(collections.Counter(p["capability"] for p in prompts)))
    print(dict(collections.Counter(p["intent"] for p in prompts)))
    if len(sys.argv) > 1:
        for text in check_overlap(prompts, sys.argv[1]):
            print("too close to the suite:", text)
