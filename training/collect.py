"""Runs the training prompts through a real PersonaOS instance and records each turn.

For every prompt: a fresh conversation, the chat call, every model call the proxy logged for it,
and what the turn ended in (the proposals as stored, with their inputs, and the reads that ran).
Each proposal is discarded and the conversation deleted afterwards. The instance's AI base URL is
pointed at the proxy for the run and put back at the end.

    PERSONAOS_USER=… PERSONAOS_PASSWORD=… python -I training/collect.py \
        --proxy-log data/training/calls.jsonl --out data/training/traces.jsonl

Point it at a development instance: the prompts create (and then discard) proposals of their own.
"""

import argparse
import datetime
import json
import os
import sqlite3
import sys
import time
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import prompts  # noqa: E402


def call(server: str, method: str, path: str, token: str | None = None, body: dict | None = None):
    request = urllib.request.Request(
        server + path, method=method, data=None if body is None else json.dumps(body).encode(),
        headers={"Content-Type": "application/json", **({"Authorization": f"Bearer {token}"} if token else {})})
    with urllib.request.urlopen(request, timeout=600) as response:
        raw = response.read().decode("utf-8")
    return json.loads(raw) if raw.strip().startswith(("{", "[")) else raw


def fixtures(server: str, token: str, db: sqlite3.Connection) -> dict[str, str]:
    """Makes sure the training-only task, reminder and goal fixtures exist, and returns their keys
    as placeholder values ("{task:0}" -> "TASK-41"). Proposals are always discarded, so the
    fixtures stay as set up here from run to run."""
    keys: dict[str, str] = {}
    sprint = db.execute("select Number from Sprints where Status = 'active'").fetchone()
    sprint_key = f"SPRINT-{sprint[0]}" if sprint else None
    for n, title in enumerate(prompts.FIXTURE_TASKS):
        row = db.execute("select Number from BoardTasks where Title = ? order by Id desc", (title,)).fetchone()
        key = f"TASK-{row[0]}" if row else call(server, "POST", "/api/board/tasks", token,
                                                {"title": title, "sprintKey": sprint_key,
                                                 "acknowledgeScopeChange": True})["key"]
        # A task asked to go back to "to do" starts in progress; the others start in to do.
        column = "in_progress" if list(prompts.MOVE_FORMS)[n % 3] == "todo" else "todo"
        call(server, "PUT", f"/api/board/tasks/{key}/move", token,
             {"column": column, "sprintKey": sprint_key, "acknowledgeScopeChange": True})
        keys[f"{{task:{n}}}"] = key
    due = (datetime.datetime.now() + datetime.timedelta(days=9)).replace(hour=10, minute=0, second=0, microsecond=0)
    for message in prompts.FIXTURE_REMINDERS:
        if not db.execute("select 1 from Reminders where Message = ? and Status = 'pending'", (message,)).fetchone():
            call(server, "POST", "/api/reminders", token, {"message": message, "dueAtLocal": due.isoformat()})
    for n, (title, period) in enumerate(prompts.FIXTURE_GOALS):
        row = db.execute("select Number from Goals where Title = ? and Status = 'active'", (title,)).fetchone()
        if row:
            keys[f"{{goal:{n}}}"] = f"GOAL-{row[0]}"
            continue
        year = datetime.date.today().year + (1 if period == "year" else 0)
        goal = call(server, "POST", "/api/goals", token, {
            "title": title, "description": None, "periodType": period, "year": year,
            **({"month": 12} if period == "month" else {})})
        keys[f"{{goal:{n}}}"] = goal["key"]
    for text in prompts.FIXTURE_NOTES:
        if not db.execute("select 1 from Notes where Text = ?", (text,)).fetchone():
            call(server, "POST", "/api/notes", token, {"text": text})
    return keys


def add_routine(server: str, token: str) -> list[int]:
    """Creates the planner capability's daily routine and returns the new items' ids, to delete after."""
    today = datetime.date.today().isoformat()
    ids = []
    for title, start, end in prompts.FIXTURE_ROUTINE:
        item = call(server, "POST", "/api/planner/items", token, {
            "title": title, "date": today, "scheduledTime": f"{start}:00",
            "endTime": f"{end}:00" if end else None, "recurrence": "FREQ=DAILY"})
        ids.append(item["id"])
    return ids


def probe_planner(server: str, token: str, proxy_log: str, tries: int = 3) -> dict | None:
    """A get_planner call and its result, as the model saw them, for the generated labels
    (build_dataset.py): the planner prompts' labels are built on what this read returned."""
    for _ in range(tries):
        seen = sum(1 for _ in open(proxy_log, encoding="utf-8")) if os.path.exists(proxy_log) else 0
        done = chat(server, token, prompts.PLANNER_PROBE)
        time.sleep(0.5)
        with open(proxy_log, encoding="utf-8") as log:
            calls = [json.loads(line) for line in log.readlines()[seen:]]
        if done.get("conversationId") is not None:
            call(server, "DELETE", f"/api/chat/conversations/{done['conversationId']}", token)
        for logged in calls:
            messages = logged["request"]["messages"]
            for i, message in enumerate(messages):
                if message.get("role") == "tool" and message.get("tool_name") == "get_planner" and i > 0:
                    return {"call": messages[i - 1], "result": message}
    return None


def fill(prompt: dict, keys: dict[str, str]) -> dict:
    """The prompt with its fixture placeholders replaced by the fixtures' keys."""
    def sub(text: str) -> str:
        for placeholder, key in keys.items():
            text = text.replace(placeholder, key)
        return text
    out = dict(prompt, text=sub(prompt["text"]), words=[sub(w) for w in prompt["words"]])
    if prompt.get("before"):
        out["before"] = [sub(t) for t in prompt["before"]]
    return out


def chat(server: str, token: str, message: str, conversation_id: int | None = None) -> dict:
    raw = call(server, "POST", "/api/chat", token, {"message": message, "conversationId": conversation_id})
    done, streamed, conversation = None, [], None
    for line in raw.splitlines():
        if not line.startswith("data:"):
            continue
        event = json.loads(line[5:])
        if event.get("type") == "start":
            conversation = event.get("conversationId")
        elif event.get("type") == "delta":
            streamed.append(event.get("text") or "")
        elif event.get("type") == "done":
            done = event
        elif event.get("type") == "error":
            raise RuntimeError(event.get("error"))
    if done is None:
        raise RuntimeError("no done event")
    done.setdefault("conversationId", conversation)
    if not done.get("text"):
        done["text"] = "".join(streamed)
    return done


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--server", default="http://localhost:5080")
    parser.add_argument("--proxy", default="http://localhost:11500/v1")
    parser.add_argument("--proxy-log", required=True)
    parser.add_argument("--db", default="src/backend/PersonaOS.Api/data/personaos.db")
    parser.add_argument("--out", required=True)
    parser.add_argument("--start", type=int, default=0)
    parser.add_argument("--limit", type=int, default=10_000)
    parser.add_argument("--skip-done", action="store_true", help="skip prompts already in --out")
    parser.add_argument("--only", nargs="*", help="capabilities to run (default: all)")
    args = parser.parse_args()

    token = call(args.server, "POST", "/api/auth/login", body={
        "username": os.environ["PERSONAOS_USER"], "password": os.environ["PERSONAOS_PASSWORD"]})["accessToken"]
    original = call(args.server, "GET", "/api/setup", token)["aiBaseUrl"]
    if original == args.proxy:
        # A run that died left the proxy in place; restoring it would leave the instance pointing at
        # a proxy that is no longer running (it did on 2026-11-03, and a whole eval run failed).
        sys.exit(f"aiBaseUrl is already the proxy ({original}); set it back to Ollama's address first")
    db = sqlite3.connect(f"file:{args.db}?mode=ro", uri=True)

    keys = fixtures(args.server, token, db)
    print("fixtures:", keys, flush=True)
    todo = [fill(p, keys) for p in prompts.build(only=args.only)[args.start:args.start + args.limit]]
    done_texts = set()
    if args.skip_done and os.path.exists(args.out):
        done_texts = {json.loads(line)["prompt"]["text"] for line in open(args.out, encoding="utf-8")}
    routine: list[int] = []
    planner_read = None
    try:
        call(args.server, "PUT", "/api/setup", token, {"aiBaseUrl": args.proxy})
        if any(p.get("generate") for p in todo):
            # The planner prompts' routine, and one real read of it for their labels.
            routine = add_routine(args.server, token)
            planner_read = probe_planner(args.server, token, args.proxy_log)
            print("planner probe:", "read" if planner_read else "NO READ (planner labels will be skipped)", flush=True)
        failures_in_a_row = 0
        for n, prompt in enumerate(todo, start=args.start):
            if prompt["text"] in done_texts and not prompt.get("before"):
                continue
            started = time.time()
            last_note = db.execute("select coalesce(max(Id), 0) from Notes").fetchone()[0]
            last_memory = db.execute("select coalesce(max(Id), 0) from Memories").fetchone()[0]
            earlier: list[dict] = []
            try:
                # A follow-up: the earlier turns first, in the same conversation. Only the last turn's
                # model calls and proposals are recorded; the earlier ones are its context.
                conversation_id = None
                for turn in prompt.get("before") or []:
                    first = chat(args.server, token, turn, conversation_id)
                    conversation_id = first.get("conversationId")
                    earlier.append({"text": turn, "reply": first.get("text"), "pending": first.get("pending") or []})
                seen = sum(1 for _ in open(args.proxy_log, encoding="utf-8")) if os.path.exists(args.proxy_log) else 0
                last_pending = db.execute("select coalesce(max(Id), 0) from PendingActions").fetchone()[0]
                done = chat(args.server, token, prompt["text"], conversation_id)
            except Exception as error:  # one bad turn should not end the run
                print(f"{n} error: {error}", flush=True)
                failures_in_a_row += 1
                if failures_in_a_row >= 5:  # but a dead Ollama or API should, not skip every prompt
                    sys.exit(f"stopping: {failures_in_a_row} turns in a row failed")
                continue
            failures_in_a_row = 0
            time.sleep(0.5)  # the proxy writes its record just after the stream ends
            with open(args.proxy_log, encoding="utf-8") as log:
                calls = [json.loads(line) for line in log.readlines()[seen:]]

            conversation = done.get("conversationId")
            proposals = [
                {"tool": tool, "input": json.loads(data), "summary": summary}
                for tool, data, summary in db.execute(
                    "select ToolName, InputJson, Summary from PendingActions where ConversationId = ? and Id > ? order by Id",
                    (conversation, last_pending))]
            for pending in (done.get("pending") or []) + [p for turn in earlier for p in turn["pending"]]:
                try:
                    call(args.server, "POST", f"/api/chat/actions/{pending['id']}/discard", token)
                except Exception:
                    pass
            # Notes and memories are saved at once, not proposed: take back the ones this turn made.
            notes = db.execute("select Id, Text from Notes where Id > ?", (last_note,)).fetchall()
            memories = db.execute("select Id, Content from Memories where Id > ?", (last_memory,)).fetchall()
            for path, rows in (("notes", notes), ("memories", memories)):
                for row_id, _ in rows:
                    try:
                        call(args.server, "DELETE", f"/api/{path}/{row_id}", token)
                    except Exception:
                        pass
            if conversation is not None:
                try:
                    call(args.server, "DELETE", f"/api/chat/conversations/{conversation}", token)
                except Exception:
                    pass

            trace = {
                "prompt": prompt, "earlier": earlier, "calls": calls, "proposals": proposals,
                "reads": [a.get("tool") for a in done.get("actions") or []],
                "notes": [text for _, text in notes],
                "memories": [text for _, text in memories],
                "text": done.get("text"), "seconds": round(time.time() - started, 1),
            }
            if prompt.get("generate"):
                trace["planner_read"] = planner_read
            with open(args.out, "a", encoding="utf-8") as out:
                out.write(json.dumps(trace, ensure_ascii=False) + "\n")
            got = ",".join(p["tool"] for p in proposals) or ",".join(trace["reads"]) or "-"
            print(f"{n} {trace['seconds']}s {prompt['tool'] or prompt['intent']} -> {got}", flush=True)
    finally:
        for item_id in routine:
            try:
                call(args.server, "DELETE", f"/api/planner/items/{item_id}", token)
            except Exception:
                pass
        call(args.server, "PUT", "/api/setup", token, {"aiBaseUrl": original})
        print(f"restored aiBaseUrl {original}", flush=True)


if __name__ == "__main__":
    main()
