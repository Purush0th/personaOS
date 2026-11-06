"""Core capability: tasks, reminders, the planner, goals, notes, memories, edits, reads and plain talk.

Each prompt carries its own answer: the intent, the tool the turn should end in, and words the
item's title must contain. A collected turn whose result disagrees is dropped, so the labels are
only ever the app's own checked output.

This was the whole prompt set until 2026-11-05. Its text and order are fixed by the seed, so turns
collected before then still match; add new kinds of prompt as their own capability module instead.
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
    ("What's in my calendar {d}?", "get_planner"), ("Do I have anything planned {d}?", "get_planner"),
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

# ---- Added 2026-11-05 (A8, second dataset): what the first one left out and the trained model then
# got wrong. Edits, cancellations and deletions act on items that must exist, so collect.py creates
# these training-only fixtures first and fills {task:…}, {reminder:…} and {goal:…} with their keys.
FIXTURE_TASKS = [
    "Tidy the garage", "Book the window cleaner", "Compare broadband deals", "Sand the deck",
    "Archive old emails", "Fix the leaking tap",
]
FIXTURE_REMINDERS = ["water the ferns", "renew the parking permit", "call the optician", "pay the childminder"]
FIXTURE_GOALS = [("Learn to juggle", "month"), ("Visit three new cities", "year")]

GOAL_PERIODS = ["this year", "next year", "this quarter", "next month", "in December", "by the end of the year"]
MOVE_FORMS = {
    "done": ["Close {k}, it's finished", "{k} is complete", "I've wrapped up {k}", "Tick off {k} as done"],
    "in_progress": ["I'm starting on {k} now", "Put {k} in progress", "{k} is underway"],
    "todo": ["Send {k} back to to do", "{k} isn't started after all, back to to do"],
}
POINT_FORMS = ["Estimate {k} at {p} points", "{k} should be {p} points", "Make {k} a {p}-pointer"]
RENAMES = [("Clear out the garage", "garage"), ("Get quotes for broadband", "quotes"), ("Oil the deck", "oil")]
RENAME_FORMS = ["Call {k} '{n}' instead", "Change the title of {k} to {n}", "Retitle {k}: {n}"]
PRIORITY_FORMS = [
    ("{k} is urgent, make it highest priority", "highest"), ("Set {k} to high priority", "high"),
    ("{k} can wait, lowest priority", "lowest"), ("Give {k} medium priority", "medium"),
]
CANCEL_FORMS = [
    "Cancel the reminder to {r}", "I don't need the reminder to {r} any more", "Scrap my reminder to {r}",
    "Get rid of the reminder to {r}",
]
GOAL_DELETE_FORMS = ["Remove the goal '{g}'", "Delete my goal {g}", "I'm dropping the goal {g}, delete it"]
# Things the model must not make up: no time, no title, no period. The app asks; so should the model.
ASK_FORMS = [
    ("Remind me to {x}", "create_reminder"), ("Set a reminder to {x}", "create_reminder"),
    ("Can you set me a reminder?", "create_reminder"), ("Add a task", "create_task"),
    ("New task please", "create_task"), ("Could you create a task for me?", "create_task"),
    ("Put something on my calendar", "add_planner_item"),
]
# Follow-ups: the first turn leaves something out, the second gives it.
FOLLOW_UPS = [
    (["Remind me to {x}", "{d} at {t}"], "create_reminder", "create_reminder"),
    (["Add a task", "{x}"], "create_task", "create_task"),
    (["Schedule {e}", "{d} at {t}"], "add_planner_item", "add_planner_item"),
]
# Reads, including the ones whose honest answer is "nothing".
READ_FORMS = [
    ("When does my {r} reminder go off?", "get_reminders", False),
    ("Is there a reminder for {r}?", "get_reminders", False),
    ("Am I free {d} morning?", "get_planner", False),
    ("What have I got on {d} evening?", "get_planner", False),
    ("How many points is {k}?", ["get_task", "get_board"], False),
    ("What's the status of {k}?", ["get_task", "get_board"], False),
    ("Show me my notes", "get_notes", False),
    ("Have I written anything down about {a}?", "get_notes", True),
    ("Do I have a note on {a}?", "get_notes", True),
    ("When is my {a} appointment?", "get_planner", True),
]
ABSENT = ["the chimney sweep", "the accountant", "scuba lessons", "the piano tuner", "the passport photos"]
# Keys that do not exist: the app must say so, not propose a change to nothing.
MISSING = ["Move TASK-8123 to done", "Set TASK-7710 to 5 points", "Delete GOAL-512", "Close TASK-9031"]
MORE_QUESTIONS = [
    "What's 12% of 300?", "How much is 45 divided by 9?", "What is 17 times 6?", "Who painted the Mona Lisa?",
    "How far is the moon, roughly?", "Write a haiku about rain", "Suggest a name for a grey kitten",
    "What day of the week is it today?", "What's the date the day after tomorrow?", "Who are you?",
]


def _title_words(text: str) -> list[str]:
    stop = {"the", "a", "an", "my", "to", "for", "with", "some", "and", "of", "on", "in", "is"}
    return [w for w in re.findall(r"[A-Za-z']+", text.lower()) if w not in stop and len(w) > 2][:2]


def build(seed: int = 7) -> list[dict]:
    """Every prompt, in a fixed order for the seed."""
    rng = random.Random(seed)
    out: list[dict] = []

    def add(text: str, intent: str, tool: str | list[str] | None, words: list[str] | None = None, **more) -> None:
        out.append({"text": text, "intent": intent, "tool": tool, "words": words or [], **more})

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
        # With a period the goal is made; without one the app asks which period (A9).
        for form in rng.sample(GOAL_FORMS, 2):
            period = rng.choice(GOAL_PERIODS)
            text = form.format(x=x)
            text = text if any(p in x for p in ("this year", "every month")) else f"{text} {period}"
            add(text, "create_goal", "create_goal", _title_words(x))
    for x in rng.sample([g for g in GOALS if not any(w in g for w in ("year", "month", "by ", "before"))], 4):
        add(rng.choice(GOAL_FORMS).format(x=x), "create_goal", None, ask=True)

    # Edits on the fixtures. {task:N} is the Nth fixture task's key.
    for n, title in enumerate(FIXTURE_TASKS):
        key = f"{{task:{n}}}"
        column = list(MOVE_FORMS)[n % 3]
        form = rng.choice(MOVE_FORMS[column])
        add(form.format(k=key), "change_item", "move_task", [key, column])
        p = rng.choice([1, 2, 3, 5, 8, 13])
        add(rng.choice(POINT_FORMS).format(k=key, p=p), "change_item", "update_task", [key, str(p)])
        form, level = PRIORITY_FORMS[n % len(PRIORITY_FORMS)]
        add(form.format(k=key), "change_item", "update_task", [key, level])
    for n, (new, word) in enumerate(RENAMES):
        key = f"{{task:{n}}}"
        add(rng.choice(RENAME_FORMS).format(k=key, n=new), "change_item", "update_task", [key, word])
    for n, message in enumerate(FIXTURE_REMINDERS):
        for form in rng.sample(CANCEL_FORMS, 2):
            add(form.format(r=message), "change_item", "cancel_reminder", _title_words(message)[-1:])
    for n, (title, _) in enumerate(FIXTURE_GOALS):
        for form in rng.sample(GOAL_DELETE_FORMS, 2):
            add(form.format(g=title), "change_item", "delete_goal", [f"{{goal:{n}}}"])
    for text in MISSING:
        add(text, "change_item", None, refuse=True)

    for form, intent in ASK_FORMS:
        x = rng.choice(REMINDERS if intent == "create_reminder" else THINGS_TO_DO)
        add(form.format(x=x), intent, None, ask=True)
    for turns, intent, tool in FOLLOW_UPS:
        for _ in range(4):
            x = rng.choice(REMINDERS if tool == "create_reminder" else THINGS_TO_DO)
            spoken, title = rng.choice(EVENTS)
            filled = [t.format(x=x, d=rng.choice(DAYS), t=rng.choice(TIMES), e=spoken) for t in turns]
            words = _title_words(title if tool == "add_planner_item" else x)
            add(filled[-1], intent, tool, words, before=filled[:-1])

    for form, tool, negative in READ_FORMS:
        for _ in range(2):
            text = form.format(r=rng.choice(FIXTURE_REMINDERS), d=rng.choice(DAYS).replace("on ", "on "),
                               k=f"{{task:{rng.randrange(len(FIXTURE_TASKS))}}}", a=rng.choice(ABSENT))
            add(text, "question", tool, negative=negative)
    for q in MORE_QUESTIONS:
        add(q, "question", None)
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

