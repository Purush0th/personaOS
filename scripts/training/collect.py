"""Runs the training prompts through a real PersonaOS instance and records each turn.

For every prompt: a fresh conversation, the chat call, every model call the proxy logged for it,
and what the turn ended in (the proposals as stored, with their inputs, and the reads that ran).
Each proposal is discarded and the conversation deleted afterwards. The instance's AI base URL is
pointed at the proxy for the run and put back at the end.

    PERSONAOS_USER=… PERSONAOS_PASSWORD=… python -I scripts/training/collect.py \
        --proxy-log data/training/calls.jsonl --out data/training/traces.jsonl

Point it at a development instance: the prompts create (and then discard) proposals of their own.
"""

import argparse
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


def chat(server: str, token: str, message: str) -> dict:
    raw = call(server, "POST", "/api/chat", token, {"message": message})
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
    args = parser.parse_args()

    token = call(args.server, "POST", "/api/auth/login", body={
        "username": os.environ["PERSONAOS_USER"], "password": os.environ["PERSONAOS_PASSWORD"]})["accessToken"]
    original = call(args.server, "GET", "/api/setup", token)["aiBaseUrl"]
    if original == args.proxy:
        # A run that died left the proxy in place; restoring it would leave the instance pointing at
        # a proxy that is no longer running (it did on 2026-11-03, and a whole eval run failed).
        sys.exit(f"aiBaseUrl is already the proxy ({original}); set it back to Ollama's address first")
    db = sqlite3.connect(f"file:{args.db}?mode=ro", uri=True)

    todo = prompts.build()[args.start:args.start + args.limit]
    done_texts = set()
    if args.skip_done and os.path.exists(args.out):
        done_texts = {json.loads(line)["prompt"]["text"] for line in open(args.out, encoding="utf-8")}
    try:
        call(args.server, "PUT", "/api/setup", token, {"aiBaseUrl": args.proxy})
        for n, prompt in enumerate(todo, start=args.start):
            if prompt["text"] in done_texts:
                continue
            seen = sum(1 for _ in open(args.proxy_log, encoding="utf-8")) if os.path.exists(args.proxy_log) else 0
            started = time.time()
            last_note = db.execute("select coalesce(max(Id), 0) from Notes").fetchone()[0]
            last_memory = db.execute("select coalesce(max(Id), 0) from Memories").fetchone()[0]
            try:
                done = chat(args.server, token, prompt["text"])
            except Exception as error:  # one bad turn should not end the run
                print(f"{n} error: {error}", flush=True)
                continue
            time.sleep(0.5)  # the proxy writes its record just after the stream ends
            with open(args.proxy_log, encoding="utf-8") as log:
                calls = [json.loads(line) for line in log.readlines()[seen:]]

            conversation = done.get("conversationId")
            proposals = [
                {"tool": tool, "input": json.loads(data), "summary": summary}
                for tool, data, summary in db.execute(
                    "select ToolName, InputJson, Summary from PendingActions where ConversationId = ? order by Id",
                    (conversation,))]
            for pending in done.get("pending") or []:
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
                "prompt": prompt, "calls": calls, "proposals": proposals,
                "reads": [a.get("tool") for a in done.get("actions") or []],
                "notes": [text for _, text in notes],
                "memories": [text for _, text in memories],
                "text": done.get("text"), "seconds": round(time.time() - started, 1),
            }
            with open(args.out, "a", encoding="utf-8") as out:
                out.write(json.dumps(trace, ensure_ascii=False) + "\n")
            got = ",".join(p["tool"] for p in proposals) or ",".join(trace["reads"]) or "-"
            print(f"{n} {trace['seconds']}s {prompt['tool'] or prompt['intent']} -> {got}", flush=True)
    finally:
        call(args.server, "PUT", "/api/setup", token, {"aiBaseUrl": original})
        print(f"restored aiBaseUrl {original}", flush=True)


if __name__ == "__main__":
    main()
