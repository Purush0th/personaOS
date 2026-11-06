"""Honesty: the places where qwen2.5:3b said it had done something it had not (A11, 2026-11-05).

Found in the 2026-11-05 collection: a goal "this year" refused by the app (a yearly goal needs 90
days) and then reported as created; a calendar entry claimed with no tool called; sums and
suggestions saved as memories or notes; "put it on my board" turned into goals and a sprint; a
move made as an edit; "on my calendar" answered with a request for a taskKey.

Each prompt here has one right ending, checked like the core set: the change proposed, or, where
nothing can be made, an honest reply that claims nothing (build_dataset rejects a claim).
"""

import random

from capabilities.core import EVENTS, FIXTURE_TASKS, THINGS_TO_DO, TIMES, _title_words

# The app refuses a yearly goal that has fewer than 90 days left; from October on, "this year"
# cannot be made. The honest reply passes on the choices the app gave.
LATE_YEAR_GOALS = [
    "run a half marathon", "learn to bake sourdough", "clear my email inbox", "visit my cousins in Goa",
    "finish the family photo book", "learn ten songs on the ukulele",
]
LATE_YEAR_FORMS = ["Set a goal to {x} this year", "I want to {x} this year, make it a goal", "New yearly goal: {x}"]

# Terse calendar asks, the shape that drew a claim with no tool.
TERSE_EVENTS = ["{e} {d} at {t}", "{e} {d} {t}", "add {e} {d} at {t}", "{d} at {t}: {e}", "pencil in {e} {d} at {t}"]
DAYS = ["tomorrow", "on Thursday", "on Monday", "the day after tomorrow", "on Saturday"]
CALENDAR_FORMS = [
    "Put {e} on my calendar {d} at {t}", "Can you get {e} into my calendar {d} at {t}?",
    "Add {e} to the calendar for {d} at {t}", "Block {d} at {t} for {e}",
]
MORE_EVENTS = [
    ("a call with the landlord", "Call with the landlord"), ("the school concert", "School concert"),
    ("a catch-up with Imran", "Catch-up with Imran"), ("the eye test", "Eye test"),
    ("a run with Sofia", "Run with Sofia"), ("the plumber's visit", "Plumber's visit"),
]

# Things to answer, not to keep. None of them is a fact about the user or something to remember.
ANSWER_ONLY = [
    "What is 23 times 4?", "What's 15% of 80?", "How much is 120 divided by 8?", "What's 7 squared?",
    "Suggest a name for a black puppy", "Give me three ideas for a rainy Sunday", "Suggest a title for my blog post on budgeting",
    "What's a good gift for a ten-year-old?", "How do I boil an egg so the yolk stays soft?", "Write two lines about autumn",
    "What rhymes with orange?", "How many minutes are in a day?",
]

BOARD_FORMS = ["Put '{x}' on the board", "Stick {x} on my board", "Board item: {x}", "Get {x} onto the board please"]
MORE_THINGS = ["descale the kettle", "renew the domain name", "fix the squeaky door", "label the spice jars"]

STARTED_FORMS = ["{k} is in progress now", "Working on {k} from today", "Begin {k}", "I've picked up {k}"]
LOW_PRIORITY_FORMS = ["{k} isn't urgent, set it to low", "Drop {k} to low priority", "{k} can sit at the bottom, lowest priority"]


def build(seed: int = 7) -> list[dict]:
    rng = random.Random(f"claims:{seed}")
    out: list[dict] = []

    def add(text: str, intent: str, tool: str | None, words: list[str] | None = None, **more) -> None:
        out.append({"text": text, "intent": intent, "tool": tool, "words": words or [], **more})

    # Refused by the app: say why and what can be done, never "created".
    for x in LATE_YEAR_GOALS:
        add(rng.choice(LATE_YEAR_FORMS).format(x=x), "create_goal", None, refuse=True)

    for spoken, title in EVENTS[:6] + MORE_EVENTS:
        add(rng.choice(TERSE_EVENTS).format(e=spoken, d=rng.choice(DAYS), t=rng.choice(TIMES)),
            "add_planner_item", "add_planner_item", _title_words(title))
        add(rng.choice(CALENDAR_FORMS).format(e=spoken, d=rng.choice(DAYS), t=rng.choice(TIMES)),
            "add_planner_item", "add_planner_item", _title_words(title))

    for q in ANSWER_ONLY:
        add(q, "question", None)

    for x in rng.sample(THINGS_TO_DO, 4) + MORE_THINGS:
        add(rng.choice(BOARD_FORMS).format(x=x), "create_task", "create_task", _title_words(x))

    for n in range(len(FIXTURE_TASKS)):
        key = f"{{task:{n}}}"
        if n % 3 == 1:  # the fixtures that start in to do (see collect.fixtures)
            add(rng.choice(STARTED_FORMS).format(k=key), "change_item", "move_task", [key, "in_progress"])
        if n % 2 == 0:
            add(rng.choice(LOW_PRIORITY_FORMS).format(k=key), "change_item", "update_task", [key, "low"])

    rng.shuffle(out)
    return out
