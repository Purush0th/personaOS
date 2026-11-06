"""Everyday phrasings the fine-tuned model got wrong in run 2026-11-05 (A11, 2026-11-06).

The trained model lost scenarios the stock model passed: "don't let me forget …" saved as a note
instead of a reminder, "am I free on <weekday>" answered without reading the planner, a calendar
entry with a title answered with "what should it be called?", a fact to remember and "what notes
do I have" ending in an empty reply. The core set says these things one way; this set says them
the many other ways people do, so the model learns the intent and not the wording.

The note reads need notes to find: collect.py creates FIXTURE_NOTES first (and leaves them).
"""

import random

from capabilities.core import TIMES, _title_words

FIXTURE_NOTES = [
    "Boiler service is due in March",
    "Locker combination at the gym is 2291",
    "Sanjay's birthday present: a travel mug",
]

DAYS = ["tomorrow", "on Wednesday", "on Friday", "on Monday", "the day after tomorrow", "on Thursday", "on Saturday"]
WEEKDAYS = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"]

# Reminders said without "remind": the app's reminder, not a note.
REMINDER_THINGS = [
    "ring the electrician", "put the bins out", "book the train tickets", "pay the water bill",
    "water the tomatoes", "take the chicken out of the freezer", "text Marco back", "renew my library card",
]
REMINDER_FORMS = [
    "Don't let me forget to {x} {d} at {t}", "Make sure I {x} {d} at {t}", "Give me a shout {d} at {t} to {x}",
    "Alert me {d} at {t}: {x}", "I must {x} {d} at {t}, don't let it slip", "{d} at {t} I need to {x}, remind me",
    "Buzz me {d} at {t} so I {x}", "Can you make sure I don't forget to {x} at {t} {d}?",
]

# Free/busy on a named day: always read the planner first.
FREE_FORMS = [
    "Am I free on {w}?", "Is {w} open?", "Have I got anything on {w}?", "Is my {w} afternoon free?",
    "What does {w} look like for me?", "Can I fit a meeting in on {w} morning?", "Anything booked for {w} evening?",
    "How's my {w} looking?",
]

# Calendar entries that name the thing: make the item, do not ask for a title.
DAY_EVENTS = [
    ("pick up the kids", "Pick up the kids"), ("a dentist check-up", "Dentist check-up"),
    ("team lunch", "Team lunch"), ("the gas meter reading", "Gas meter reading"),
    ("a video call with Hana", "Video call with Hana"), ("swimming", "Swimming"),
    ("the book club", "Book club"), ("a walk with Dev", "Walk with Dev"),
]
DAY_FORMS = [
    "Add {e} to today's plan at {t}", "Put {e} in my day at {t}", "{e} today at {t}, add it",
    "Schedule {e} for today at {t}", "I've got {e} at {t} today, put it in", "Plan {e} today at {t}",
    "Block out {t} today for {e}", "Add {e} to my calendar for today at {t}",
]

# Facts about the user, said the many ways people say "remember this".
FACTS = [
    "I'm vegetarian", "my car is a blue Honda", "I'm left-handed", "my landlord is called Mr Osei",
    "I'm learning the cello", "I take the 8:10 train", "my sister lives in Leeds", "I can't eat gluten",
]
MEMORY_FORMS = [
    "{x}, please remember that", "Keep in mind that {x}", "{x} - store that", "Don't forget that {x}",
    "For future reference, {x}", "Make a mental note: {x}", "Remember for me that {x}", "Good to know for later: {x}",
]

NOTE_READS = [
    ("What notes do I have?", None), ("List my notes", None), ("Read me my notes", None),
    ("What have I jotted down?", None), ("Any notes saved?", None),
    ("Did I write anything about the boiler?", ["boiler"]), ("What's my gym locker code?", ["2291"]),
    ("What did I note for Sanjay's birthday?", ["mug"]), ("Check my notes for the boiler", ["march"]),
]
NOTE_ABSENT = [
    "Did I note anything about the window fitter?", "Have I got a note on the car loan?",
    "Anything in my notes about Lisbon?", "Is there a note about the vaccination dates?",
]


def build(seed: int = 7) -> list[dict]:
    rng = random.Random(f"phrasings:{seed}")
    out: list[dict] = []

    def add(text: str, intent: str, tool: str | None, words: list[str] | None = None, **more) -> None:
        out.append({"text": text, "intent": intent, "tool": tool, "words": words or [], **more})

    for x in REMINDER_THINGS:
        for form in rng.sample(REMINDER_FORMS, 3):
            add(form.format(x=x, d=rng.choice(DAYS), t=rng.choice(TIMES)), "create_reminder", "create_reminder", _title_words(x))
    for form in FREE_FORMS:
        for w in rng.sample(WEEKDAYS, 2):
            add(form.format(w=w), "question", "get_planner")
    for spoken, title in DAY_EVENTS:
        for form in rng.sample(DAY_FORMS, 3):
            add(form.format(e=spoken, t=rng.choice(TIMES)), "add_planner_item", "add_planner_item", _title_words(title))
    for x in FACTS:
        for form in rng.sample(MEMORY_FORMS, 3):
            add(form.format(x=x), "create_memory", "create_memory", _title_words(x)[:1])
    for text, words in NOTE_READS:
        add(text, "question", "get_notes", words)
    for text in NOTE_ABSENT:
        add(text, "question", "get_notes", negative=True)

    rng.shuffle(out)
    return out
