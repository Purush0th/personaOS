"""Checks that ollama_template.py renders prompts exactly as the running Ollama does.

    python -I training/check_template.py [--calls data/training/runs/<run>/calls.jsonl] [--model qwen2.5:3b-instruct] [--family qwen2.5|qwen3] [-n 12]

For a sample of recorded model calls (with and without tools, first turns and turns after a tool
result) it compares the token count Ollama reports for the real /api/chat request with the count
for our rendered text sent raw. Any difference means the trainer would teach the model a prompt it
never sees at inference: fix ollama_template.py before training. Run it after every Ollama upgrade.
"""

import argparse
import glob
import json
import os
import sys
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ollama_template import FAMILIES  # noqa: E402


def post(host: str, path: str, body: dict) -> dict:
    request = urllib.request.Request(host + path, data=json.dumps(body).encode(), headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=600) as response:
        return json.loads(response.read())


def sample(calls: list[dict], n: int) -> list[dict]:
    """Spread over the kinds of request: no tools, tools on a first turn, tools after a tool result."""
    kinds: dict[str, list[dict]] = {"no tools": [], "first turn": [], "after a tool": []}
    for call in calls:
        request = call["request"]
        if not request.get("tools"):
            kinds["no tools"].append(call)
        elif request["messages"][-1]["role"] == "tool":
            kinds["after a tool"].append(call)
        else:
            kinds["first turn"].append(call)
    picked = []
    for kind, items in kinds.items():
        step = max(1, len(items) // max(1, n // 3))
        picked += [(kind, c) for c in items[::step][: max(1, n // 3)]]
    return picked


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--calls", help="a proxy log (default: the newest data/training/runs/*/calls.jsonl)")
    parser.add_argument("--host", default="http://localhost:11434")
    parser.add_argument("--model", default="qwen2.5:3b-instruct")
    parser.add_argument("--family", default="qwen2.5", choices=sorted(FAMILIES))
    parser.add_argument("-n", type=int, default=12)
    args = parser.parse_args()
    path = args.calls or max(glob.glob("data/training/runs/*/calls.jsonl"), key=os.path.getmtime)
    calls = [json.loads(line) for line in open(path, encoding="utf-8")]

    bad = 0
    for kind, call in sample(calls, args.n):
        request = call["request"]
        options = {"num_ctx": max(16384, (request.get("options") or {}).get("num_ctx", 0)), "num_predict": 1}
        body = {k: v for k, v in request.items() if k != "stream_options"} | {"model": args.model, "stream": False, "options": options}
        theirs = post(args.host, "/api/chat", body)["prompt_eval_count"]
        prompt = FAMILIES[args.family][0](request["messages"], request.get("tools"))
        ours = post(args.host, "/api/generate", {"model": args.model, "prompt": prompt, "raw": True, "stream": False, "options": options})["prompt_eval_count"]
        ok = ours == theirs
        bad += not ok
        print(f"{'ok  ' if ok else 'DIFF'} {kind:13} ollama {theirs:6}  ours {ours:6}")
    print("all match" if not bad else f"{bad} differ: fix ollama_template.py before training")
    sys.exit(1 if bad else 0)


if __name__ == "__main__":
    main()
