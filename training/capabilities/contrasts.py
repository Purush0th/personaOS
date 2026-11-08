"""Reminder or event, and asks that change nothing (training run 2026-11-07-qwen3).

The fine-tuned qwen3:4b, trained on qwen2.5:3b's turns, lost three things the stock model had:

- "In 30 minutes, nudge me to …" became a planner item instead of a reminder;
- an event the user described and then asked to add ("… on Thursday at 11 - add it to my
  calendar") became a card linked to an unrelated task, without the event's name;
- "write a poem about …" was saved as a note.

Each prompt here has one right ending, checked like the core set. The wording stays apart from the
suites (prompts.check_overlap): different verbs, events and people.
"""

import random

from capabilities.core import _title_words

# Things to be reminded of, and the word the reminder must keep.
NUDGE_THINGS = [
    ("take the washing out", "washing"), ("turn the oven off", "oven"), ("ring the garage", "garage"),
    ("water the tomatoes", "tomatoes"), ("collect Zara from football", "zara"), ("top up the parking", "parking"),
    ("send Dad the photos", "photos"), ("check the slow cooker", "cooker"), ("book the vet", "vet"),
    ("put the bins out", "bins"), ("defrost the chicken", "chicken"), ("log my hours", "hours"),
]
OFFSETS = ["in 15 minutes", "in 20 minutes", "in 40 minutes", "in an hour", "in 90 minutes", "in 3 hours"]
NUDGE_FORMS = [
    "{O}, give me a nudge to {x}", "Give me a nudge {o} to {x}", "Prod me {o} to {x}", "{O}, ping me so I {x}",
    "Buzz me {o} to {x}", "Can you poke me {o} to {x}?", "Give me a shout {o} so I {x}",
    "Alert me {o}: {x}", "Tap me {o} to {x}", "Shout at me {o} to {x}",
]
CLOCK_NUDGE_FORMS = [
    "Nudge me at {t} to {x}", "Ping me {d} at {t} so I {x}", "Give me a nudge {d} at {t} to {x}",
    "At {t}, buzz me to {x}", "Prod me {d} at {t} to {x}",
]
TIMES = ["7am", "8:30am", "10am", "12:15pm", "3pm", "4:30pm", "6:45pm", "9pm"]
DAYS = ["tomorrow", "on Wednesday", "on Friday", "on Sunday", "on Monday"]

# Events the user describes, then asks to add. People and events the suites do not use.
STATED_EVENTS = [
    ("drinks with Femi", "Drinks with Femi"), ("a catch-up with Lucia", "Catch-up with Lucia"),
    ("squash with Omar", "Squash with Omar"), ("the physio appointment", "Physio appointment"),
    ("a viewing at the flat", "Viewing at the flat"), ("the kids' sports day", "Sports day"),
    ("a video call with Hiroshi", "Video call with Hiroshi"), ("my nephew's recital", "Recital"),
    ("dinner at Grandma's", "Dinner at Grandma's"), ("the boiler engineer", "Boiler engineer"),
    ("a mentoring session with Ade", "Mentoring session with Ade"), ("the book club", "Book club"),
]
STATED_FORMS = [
    "I've got {e} {d} at {t} - stick it in my planner", "There's {e} {d} at {t}. Can it go in my diary?",
    "{E} {d} at {t}, pop that in my planner", "Heads up: {e} {d} at {t}. Get it on my calendar",
    "{E} is {d} at {t}; please put it in the planner", "So {e} is {d} at {t} - log it on my calendar",
]

# Asks to be reminded of an event: a reminder, not the event itself.
REMIND_OF_EVENT_FORMS = ["Ping me {d} at {t} about {e}", "Give me a nudge {d} at {t} for {e}"]

# Make something up, answer it, change nothing.
MAKE_SOMETHING = [
    "Write a limerick about a lazy cat", "Make up a riddle about clocks", "Give me a tagline for a bakery",
    "Compose a short toast for my brother's birthday", "Suggest three names for a goldfish",
    "Write four lines about Friday evenings", "Write a short poem about the sea", "Make up a tongue twister",
    "Give me a funny caption for a photo of my dog", "Write a two-sentence story about a lost umbrella",
    "Invent a name for a coffee shop", "Write a haiku about the first frost",
]


def build(seed: int = 7) -> list[dict]:
    rng = random.Random(f"contrasts:{seed}")
    out: list[dict] = []

    def add(text: str, intent: str, tool: str | None, words: list[str] | None = None) -> None:
        out.append({"text": text, "intent": intent, "tool": tool, "words": words or []})

    for x, word in NUDGE_THINGS:
        o = rng.choice(OFFSETS)
        form = rng.choice(NUDGE_FORMS)
        add(form.format(o=o, O=o[0].upper() + o[1:], x=x), "create_reminder", "create_reminder", [word])
        form = rng.choice(CLOCK_NUDGE_FORMS)
        add(form.format(t=rng.choice(TIMES), d=rng.choice(DAYS), x=x), "create_reminder", "create_reminder", [word])

    for spoken, title in STATED_EVENTS:
        for form in rng.sample(STATED_FORMS, 2):
            add(form.format(e=spoken, E=spoken[0].upper() + spoken[1:], d=rng.choice(DAYS), t=rng.choice(TIMES)),
                "add_planner_item", "add_planner_item", _title_words(title))
    for spoken, title in rng.sample(STATED_EVENTS, 6):
        add(rng.choice(REMIND_OF_EVENT_FORMS).format(e=spoken, d=rng.choice(DAYS), t=rng.choice(TIMES)),
            "create_reminder", "create_reminder", _title_words(title)[:1])

    for text in MAKE_SOMETHING:
        add(text, "question", None)

    rng.shuffle(out)
    return out
