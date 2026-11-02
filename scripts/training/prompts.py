"""The training prompts: what a user says, and what PersonaOS should do with it.

Each prompt carries its own answer: the intent, the tool the turn should end in, and words the
item's title must contain. A collected turn whose result disagrees is dropped, so the labels are
only ever the app's own checked output.

The wording, names and items here are kept apart from scripts/model-check.ps1: the suites must
measure the trained model on things it has not seen. `check_overlap` enforces that.
"""

import random
import re

THINGS_TO_DO = [
    "fix the garden gate", "renew the car insurance", "sort the tax receipts", "clean the gutters",
    "update my CV", "order new printer ink", "book the boiler service", "return the library books",
    "replace the shower head", "back up the laptop", "plan the team offsite", "write the quarterly summary",
    "repaint the hallway", "file the expense claim", "check the smoke alarms", "sell the old bike",
    "cancel the gym trial", "organise the photo albums", "service the lawnmower", "draft the newsletter",
]
REMINDERS = [
    "call the dentist", "feed the cat", "move the car", "pick up the dry cleaning",
    "check on the bread dough", "send the invoice", "stretch", "drink some water", "collect the parcel",
    "phone grandma", "charge the headphones", "switch off the heater", "take my vitamins", "lock the shed",
]
TIMES = ["6am", "7:30am", "8am", "9:15am", "11am", "noon", "1pm", "2:30pm", "4pm", "5:45pm", "6pm", "8pm", "9:30pm"]
OFFSETS = ["in 10 minutes", "in 25 minutes", "in an hour", "in 2 hours", "in 3 hours", "in half an hour"]
DAYS = ["tomorrow", "on Wednesday", "on Friday", "on Sunday", "the day after tomorrow", "on Tuesday", "on Saturday"]
EVENTS = [
    ("dinner with Priya", "Dinner with Priya"), ("a haircut", "Haircut"), ("the parents' evening", "Parents' evening"),
    ("a call with the bank", "Call with the bank"), ("yoga class", "Yoga class"), ("the car MOT", "Car MOT"),
    ("coffee with Tomasz", "Coffee with Tomasz"), ("a site visit", "Site visit"), ("piano lesson", "Piano lesson"),
    ("the vet appointment", "Vet appointment"), ("a design review", "Design review"), ("brunch with Aiko", "Brunch with Aiko"),
]
GOALS = [
    "learn to swim properly", "read 24 books this year", "save for a new sofa", "get my driving licence",
    "cycle 1000 km by summer", "learn basic Spanish", "lose 5 kg before the wedding", "finish my woodworking course",
    "write a short story every month", "pay off the credit card",
]
NOTES = [
    "the wifi password is on the router", "idea: a shared family calendar", "look into solar panels",
    "Ravi recommended the book Dune", "the plumber's number is in my email", "try the new Thai place",
    "the boiler code was F28", "gift idea for Mum: a silk scarf", "check whether the passport expires soon",
]
FACTS = [
    "I'm allergic to peanuts", "I work night shifts on weekends", "my partner's name is Leila",
    "I prefer meetings in the afternoon", "I don't drink coffee", "my daughter is called Noor",
    "I'm training for a triathlon", "I live in Pune", "I usually start work at 10",
]

TASK_FORMS = [
    "Add a task to {x}", "Put '{x}' on my board", "New task: {x}", "Can you add {x} to my backlog?",
    "add {x} to my to-do list please", "I need a task for {x}", "Create a task: {x}",
]
REMINDER_FORMS = [
    "Remind me to {x} at {t}", "remind me {d} at {t} to {x}", "Ping me {o} to {x}", "Set a reminder to {x} {o}",
    "Can you remind me to {x} {d} at {t}?", "Nudge me {o} about the {y}",
]
EVENT_FORMS = [
    "Put {e} on my calendar {d} at {t}", "Schedule {e} {d} at {t}", "Book {e} for {d} at {t}",
    "Add {e} to my planner {d} at {t}",
]
GOAL_FORMS = ["I want a goal to {x}", "Set a goal: {x}", "New goal - {x}", "Make it a goal to {x}"]
NOTE_FORMS = ["Jot down: {x}", "Note this: {x}", "Make a note that {x}", "Quick note - {x}"]
MEMORY_FORMS = ["{x}, keep that in mind", "Remember that {x}", "Please remember: {x}", "FYI {x}, keep it for later"]
QUESTION_FORMS = [
    ("What's on my calendar {d}?", "get_planner"), ("Do I have anything planned {d}?", "get_planner"),
    ("What tasks are on my board?", "get_board"), ("Which reminders do I have coming up?", "get_reminders"),
    ("What goals am I working on?", "get_goals"), ("How busy am I {d}?", "get_planner"),
]
GENERAL_QUESTIONS = [
    "What's the capital of Portugal?", "How many days are in a leap year?", "What does RSVP stand for?",
    "Give me a tip for falling asleep faster", "What's a good stretch for a stiff back?",
]
CONVERSATION = [
    "Good morning!", "cheers, that's everything", "Thanks a lot", "hi there", "ok great, bye for now",
    "Just thinking out loud about the weekend", "I'm feeling pretty tired today", "nice one",
]


def _title_words(text: str) -> list[str]:
    stop = {"the", "a", "an", "my", "to", "for", "with", "some", "and", "of", "on", "in", "is"}
    return [w for w in re.findall(r"[A-Za-z']+", text.lower()) if w not in stop and len(w) > 2][:2]


def build(seed: int = 7) -> list[dict]:
    """Every prompt, in a fixed order for the seed."""
    rng = random.Random(seed)
    out: list[dict] = []

    def add(text: str, intent: str, tool: str | None, words: list[str] | None = None) -> None:
        out.append({"text": text, "intent": intent, "tool": tool, "words": words or []})

    for x in THINGS_TO_DO:
        for form in rng.sample(TASK_FORMS, 3):
            add(form.format(x=x), "create_task", "create_task", _title_words(x))
    for x in REMINDERS:
        for form in rng.sample(REMINDER_FORMS, 4):
            y = x.split()[-1]
            add(form.format(x=x, t=rng.choice(TIMES), d=rng.choice(DAYS), o=rng.choice(OFFSETS), y=y),
                "create_reminder", "create_reminder", [y] if "{y}" in form else _title_words(x))
    for spoken, title in EVENTS:
        for form in rng.sample(EVENT_FORMS, 3):
            add(form.format(e=spoken, d=rng.choice(DAYS), t=rng.choice(TIMES)), "add_planner_item",
                "add_planner_item", _title_words(title))
    for x in GOALS:
        for form in rng.sample(GOAL_FORMS, 2):
            add(form.format(x=x), "create_goal", "create_goal", _title_words(x))
    for x in NOTES:
        for form in rng.sample(NOTE_FORMS, 2):
            add(form.format(x=x), "create_note", "create_note", _title_words(x)[:1])
    for x in FACTS:
        for form in rng.sample(MEMORY_FORMS, 2):
            add(form.format(x=x), "create_memory", "create_memory", _title_words(x)[:1])
    for form, tool in QUESTION_FORMS:
        for d in rng.sample(DAYS, 3) if "{d}" in form else [""]:
            add(form.format(d=d.replace("on ", "on ")), "question", tool)
    for q in GENERAL_QUESTIONS:
        add(q, "question", None)
    for c in CONVERSATION:
        add(c, "conversation", None)

    rng.shuffle(out)
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
    import sys

    prompts = build()
    print(len(prompts), dict(collections.Counter(p["intent"] for p in prompts)))
    if len(sys.argv) > 1:
        for text in check_overlap(prompts, sys.argv[1]):
            print("too close to the suite:", text)
