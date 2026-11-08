"""Planner reads the model must base on a fresh read (owner's chat 4nzrb5af, 2026-11-07).

The trained qwen2.5:3b answered from what was already in the conversation instead of reading: "what's
on my list for 8 a.m." got the item that ended at 8, "No, check the planner" got the same wrong
answer without a read, "critic my schedule" got an "adjusted schedule" that dropped half the day,
and "what did you change?" got a change that never happened.

These prompts carry `generate`: the stock model fails them too, so the app's output cannot be the
label. build_dataset.py labels them with a get_planner read (captured by collect.py's probe) and the
answer planner_labels.py works out from the items that read returned. "When is my X?", "what's
next?" and "am I free?" are answered by the app in code before the model is asked, so none of the
wording here may match PlannerQuestion's patterns ("when", "what time", "do I have", "any", …).

collect.py creates FIXTURE_ROUTINE as daily items before these prompts and deletes it afterwards,
so the routine never sits in the planner while the accuracy suites run.
"""

import random

# A day with three commutes, meals, long blocks and short ones. Not the owner's routine.
FIXTURE_ROUTINE = [
    ("Wake up and stretch", "06:30", "06:45"),
    ("Read 20 pages", "06:45", "07:15"),
    ("Breakfast", "07:15", "07:45"),
    ("Commute", "07:45", "08:15"),
    ("Focused work", "08:15", "12:30"),
    ("Lunch", "12:30", "13:15"),
    ("Meetings and email", "13:15", "17:30"),
    ("Commute to the pool", "17:30", "18:00"),
    ("Swimming", "18:00", "19:00"),
    ("Commute home", "19:00", "19:30"),
    ("Dinner", "19:30", "20:15"),
    ("Evening reading", "20:15", "21:00"),
    ("Plan tomorrow", "21:00", "21:15"),
    ("Wind down", "21:15", "21:45"),
    ("Lights out", "21:45", None),
]

# Run by collect.py through the proxy before the first prompt here: its get_planner call and result
# are what the generated labels are built on.
PROBE = "Show me my planner for today"

# How people say a time, and the time it is.
TIMES = [
    ("8am", "08:00"), ("8:15", "08:15"), ("7:45 am", "07:45"), ("6 p.m.", "18:00"), ("noon", "12:00"),
    ("9:30 tonight", "21:30"), ("1:15pm", "13:15"), ("half past seven in the evening", "19:30"),
    ("10 at night", "22:00"), ("6:40 in the morning", "06:40"),
]
AT_FORMS = [
    "What's on my list for {t}?", "What am I doing at {t}?", "What's planned at {t} today?",
    "What's at {t} on my plan?", "{t} today, what's that slot for?", "Tell me what I've got at {t}",
]
REREAD_BEFORE = ["What's my plan for the day?", "Show me today's schedule"]
REREAD_FORMS = [
    "No, check the planner. What's at {t}?", "Check again, what's at {t}?",
    "Look at the planner again: what am I doing at {t}?", "That's not right, read my planner. {t}?",
]
COUNTS = [
    ("commute", "commute", "commutes"), ("read", "reading slot", "reading slots"),
    ("lunch", "lunch", "lunches"), ("meeting", "meeting block", "meeting blocks"),
]
COUNT_FORMS = ["How many {many} are in my day?", "Count my {many} for today", "How many {many} today?"]
BETWEENS = [("12:00", "14:00", "noon", "2pm"), ("17:00", "19:30", "5pm", "7:30pm"), ("06:30", "08:00", "6:30", "8")]
BETWEEN_FORMS = ["What's on between {a} and {b}?", "Walk me through {a} to {b} today", "What fills {a} to {b} today?"]
CRITIQUE_FORMS = [
    "Critique my schedule for today", "What do you think of today's plan?", "Give me honest feedback on my day",
    "Review my schedule and tell me what to improve", "critic my schedule", "Is my day too packed?",
]
CHANGED_FORMS = ["What did you change?", "Which items did you move?", "So what's different from my original plan?"]


def build(seed: int = 7) -> list[dict]:
    rng = random.Random(f"planner:{seed}")
    out: list[dict] = []

    def add(text: str, generate: dict, before: list[str] | None = None) -> None:
        prompt = {"text": text, "intent": "question", "tool": None if generate["kind"] == "changed" else "get_planner",
                  "words": [], "generate": generate}
        if before:
            prompt["before"] = before
        out.append(prompt)

    for form in AT_FORMS:
        for spoken, hm in rng.sample(TIMES, 3):
            add(form.format(t=spoken), {"kind": "at", "time": hm})
    for form in REREAD_FORMS:
        for spoken, hm in rng.sample(TIMES, 2):
            add(form.format(t=spoken), {"kind": "at", "time": hm}, [rng.choice(REREAD_BEFORE)])
    for word, one, many in COUNTS:
        for form in rng.sample(COUNT_FORMS, 2):
            add(form.format(many=many), {"kind": "count", "word": word, "one": one, "many": many})
    for start, end, a, b in BETWEENS:
        for form in rng.sample(BETWEEN_FORMS, 2):
            add(form.format(a=a, b=b), {"kind": "between", "from": start, "to": end})
    for form in CRITIQUE_FORMS:
        add(form, {"kind": "critique"})
    for form in CHANGED_FORMS:
        for before in rng.sample(CRITIQUE_FORMS, 2):
            add(form, {"kind": "changed"}, [before])

    rng.shuffle(out)
    return out
