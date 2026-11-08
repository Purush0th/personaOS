"""Answers to planner questions, worked out from the items a get_planner read returned.

The stock model gets these questions wrong (owner's chat 4nzrb5af, 2026-11-07: "what's on my list
for 8 a.m." answered with the item that ended at 8, an "adjusted schedule" that dropped half the day,
"what did you change?" answered with a change that never happened), so the app's own output cannot
label them. build_dataset.py labels these prompts with the read and the answer written here instead.
Every fact in an answer comes from the items, so a label never teaches a guess.
"""

from __future__ import annotations


def stem(word: str) -> str:
    """The app's WordStem (Application/Common/WordStem.cs): "commuting" and "Commute" meet."""
    w = word.lower()
    if w.endswith("'s"):
        w = w[:-2]
    if len(w) > 4 and w.endswith("ies"):
        return w[:-3] + "y"
    if len(w) > 3 and w.endswith("s") and not w.endswith("ss"):
        w = w[:-1]
    if len(w) > 5 and w.endswith("ing"):
        w = _undouble(w[:-3])
    elif len(w) > 4 and w.endswith("ed") and not w.endswith("eed"):
        w = _undouble(w[:-2])
    if len(w) > 3 and w.endswith("e") and not w.endswith("ee"):
        w = w[:-1]
    return w


def _undouble(w: str) -> str:
    return w[:-1] if len(w) >= 4 and w[-1] == w[-2] and w[-1] not in "lszf" else w


def _words(text: str) -> list[str]:
    word, out = [], []
    for ch in text + " ":
        if ch.isalnum() or ch == "'":
            word.append(ch)
        elif word:
            out.append("".join(word))
            word = []
    return out


def _hm(value: str | None) -> str | None:
    return value[:5] if value else None


def _minutes(hm: str) -> int:
    return int(hm[:2]) * 60 + int(hm[3:5])


def timed(items: list[dict]) -> list[dict]:
    """The day's open items with a start time, earliest first, as {title, start, end}."""
    out = [{"title": i["title"], "start": _hm(i.get("scheduledTime")), "end": _hm(i.get("endTime"))}
           for i in items if i.get("scheduledTime") and i.get("status", "planned") not in ("skipped", "cancelled")]
    return sorted(out, key=lambda i: i["start"])


def _span(i: dict) -> str:
    return f"{i['start']} to {i['end']}" if i["end"] and i["end"] != i["start"] else i["start"]


def _list(parts: list[str]) -> str:
    return parts[0] if len(parts) == 1 else ", ".join(parts[:-1]) + " and " + parts[-1]


def at_time(items: list[dict], hm: str) -> str:
    """What is on at one time: what starts then, else what is running, and what ends then."""
    day = timed(items)
    t = _minutes(hm)
    starting = [i for i in day if _minutes(i["start"]) == t]
    running = [i for i in day if _minutes(i["start"]) < t and i["end"] and _minutes(i["end"]) > t]
    ending = [i for i in day if i["end"] and _minutes(i["end"]) == t and _minutes(i["start"]) < t]
    if starting:
        text = f"At {hm} you have " + _list([f"“{i['title']}” ({_span(i)})" for i in starting]) + "."
        if ending:
            text += " " + _list([f"“{i['title']}”" for i in ending]) + (" ends" if len(ending) == 1 else " end") + " then."
        return text
    if running:
        return f"At {hm} you're in " + _list([f"“{i['title']}” ({_span(i)})" for i in running]) + "."
    after = next((i for i in day if _minutes(i["start"]) > t), None)
    return f"Nothing is planned at {hm} today." + (f" Next is “{after['title']}” at {after['start']}." if after else "")


def count(items: list[dict], word: str, one: str, many: str) -> str:
    """How many of the day's items are about one word, each with its time."""
    key = stem(word)
    found = [i for i in timed(items) if any(stem(w) == key for w in _words(i["title"]))]
    if not found:
        return f"You have no {many} in your planner today."
    listed = _list([f"“{i['title']}” at {i['start']}" for i in found])
    return f"You have {len(found)} {many if len(found) != 1 else one} today: {listed}."


def between(items: list[dict], start: str, end: str) -> str:
    """The items that overlap a stretch of the day."""
    a, b = _minutes(start), _minutes(end)
    found = [i for i in timed(items)
             if _minutes(i["start"]) < b and (_minutes(i["end"]) if i["end"] else _minutes(i["start"]) + 1) > a]
    if not found:
        return f"Nothing is planned between {start} and {end} today."
    return f"Between {start} and {end} you have " + _list([f"“{i['title']}” ({_span(i)})" for i in found]) + "."


def critique(items: list[dict]) -> str:
    """A critique built only from the items: the span of the day, the longest block, the busiest
    stretch and the night's sleep. It suggests; it never presents a changed schedule."""
    day = timed(items)
    if not day:
        return "There's nothing in your planner today to look at."
    first, last = day[0], day[-1]
    blocks = [i for i in day if i["end"]]
    lines = [f"You have {len(day)} items today, from “{first['title']}” at {first['start']} "
             f"to “{last['title']}” at {last['start']}."]
    if blocks:
        longest = max(blocks, key=lambda i: _minutes(i["end"]) - _minutes(i["start"]))
        hours = (_minutes(longest["end"]) - _minutes(longest["start"])) / 60
        lines.append(f"The longest block is “{longest['title']}” ({_span(longest)}, {hours:g} hours); "
                     "a short break inside it would help you keep focus.")
    short = [i for i in blocks if _minutes(i["end"]) - _minutes(i["start"]) <= 15]
    if len(short) >= 3:
        lines.append(f"{len(short)} items are 15 minutes or less ("
                     + _list([f"“{i['title']}”" for i in short[:3]])
                     + (" and others" if len(short) > 3 else "") + "), so any delay there pushes the rest of the day.")
    sleep = (24 * 60 - _minutes(last["start"])) + _minutes(first["start"])
    lines.append(f"From {last['start']} to {first['start']} leaves about {sleep / 60:g} hours for sleep.")
    lines.append("Nothing has been changed. Tell me if you'd like me to move or add anything.")
    return " ".join(lines)


NOTHING_CHANGED = ("I haven't changed anything in your planner. What I wrote were only suggestions; "
                   "tell me which change you want and I'll prepare it for you to confirm.")


def answer(generate: dict, items: list[dict]) -> str:
    kind = generate["kind"]
    if kind == "at":
        return at_time(items, generate["time"])
    if kind == "count":
        return count(items, generate["word"], generate["one"], generate["many"])
    if kind == "between":
        return between(items, generate["from"], generate["to"])
    if kind == "critique":
        return critique(items)
    if kind == "changed":
        return NOTHING_CHANGED
    raise ValueError(f"unknown kind {kind}")
