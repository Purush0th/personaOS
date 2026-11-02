"""Turns collected traces into fine-tuning examples (chat messages, with tools where offered).

A turn makes up to three kinds of model call, and each becomes an example only when its label is
known to be right:

- the intent call: labelled from the prompt itself, so it is kept whatever the model said;
- the tool call: labelled with the call as the app passed it on (after its guards), and kept only
  when the turn ended in the expected tool with the expected words in the title;
- the reply after the tool: labelled with the reply the user finally read (after the reply
  guards), from turns that passed. A reply that only repeats the house line for a proposal is
  labelled with that line alone.

Corrective rounds, where the app asked the model to try again, are internal and left out.

    python -I scripts/training/build_dataset.py data/training/traces.jsonl data/training/dataset
"""

import json
import os
import random
import sys

PROPOSAL_ONLY = "Here is the change I would make — confirm it below if it looks right."
WRITES = {"create_task", "create_reminder", "add_planner_item", "create_goal", "create_note", "create_memory"}
# Saved at once rather than proposed: the turn shows them as reads that ran, not as cards.
SAVED_AT_ONCE = {"create_note", "create_memory"}
# A goal's period is not checked by the app yet: with no time in the message the model picks a
# month at random, sometimes one already over (training capture, 2026-11-02). Until the app decides
# the period, goal calls are not labels; the goal intent still is.
NO_TOOL_LABELS = {"create_goal"}


def has_words(value: object, words: list[str]) -> bool:
    text = json.dumps(value, ensure_ascii=False).lower()
    return all(w.lower() in text for w in words)


def passed(trace: dict) -> bool:
    """Whether the turn ended where its prompt says it should."""
    prompt = trace["prompt"]
    tool, words = prompt["tool"], prompt["words"]
    if tool in WRITES:
        made = [p for p in trace["proposals"] if p["tool"] == tool and has_words(p["input"], words)]
        saved = tool in SAVED_AT_ONCE and tool in trace["reads"] and has_words(ran_input(trace, tool), words)
        others = [p for p in trace["proposals"] if p["tool"] != tool]
        return (bool(made) or saved) and not others
    if tool:  # a read
        return tool in trace["reads"] and not trace["proposals"]
    return not trace["proposals"] and not trace["reads"] and bool((trace["text"] or "").strip())


def ran_input(trace: dict, tool: str) -> dict:
    """The arguments of the last call to tool the app ran, from the history it sent back."""
    for call in reversed(trace["calls"]):
        message = tool_message(call["request"])
        if message and message["tool_calls"][0]["function"]["name"] == tool:
            return message["tool_calls"][0]["function"]["arguments"]
    return {}


def tool_message(request: dict) -> dict | None:
    """The last assistant message with tool calls in a request's history: the call as the app ran it."""
    for message in reversed(request["messages"]):
        if message.get("role") == "assistant" and message.get("tool_calls"):
            return message
    return None


def to_hf(message: dict) -> dict:
    """An Ollama chat message as the Hugging Face chat template expects it."""
    out = {"role": message["role"], "content": message.get("content") or ""}
    if message.get("tool_calls"):
        out["tool_calls"] = [
            {"type": "function", "function": {"name": c["function"]["name"], "arguments": c["function"]["arguments"]}}
            for c in message["tool_calls"]]
    if message["role"] == "tool" and message.get("tool_name"):
        out["name"] = message["tool_name"]
    # Until 2026-11-02 the "waiting for confirmation" result ended in a stray "}"; train on the fixed one.
    if message["role"] == "tool" and out["content"].startswith('{"status":"not_executed"') and out["content"].endswith('"}}'):
        out["content"] = out["content"][:-1]
    return out


def examples(trace: dict) -> list[dict]:
    out = []
    calls = trace["calls"]
    prompt = trace["prompt"]

    intent_call = next((c for c in calls if "format" in c["request"]), None)
    if intent_call:
        out.append({"kind": "intent", "messages": [to_hf(m) for m in intent_call["request"]["messages"]]
                    + [{"role": "assistant", "content": json.dumps({"intent": prompt["intent"]})}]})

    if not passed(trace) or prompt["tool"] in NO_TOOL_LABELS:
        return out

    tooled = [c for c in calls if c["request"].get("tools")]
    if not tooled:
        return out
    tools = tooled[0]["request"]["tools"]
    first = tooled[0]
    base = [to_hf(m) for m in first["request"]["messages"]]

    if prompt["tool"]:
        # The next call's history holds the tool call as the app ran it, after its guards.
        later = next((c for c in tooled[1:] if tool_message(c["request"])), None)
        ran = tool_message(later["request"]) if later else None
        if ran is None or ran["tool_calls"][0]["function"]["name"] != prompt["tool"]:
            return out
        assistant = to_hf(ran)
        assistant["content"] = ""
        out.append({"kind": "tool", "messages": base + [assistant], "tools": tools})

        # The reply after the tool result: the first call that ends on it, not a corrective round.
        reply = next((c for c in tooled[1:] if c["request"]["messages"][-1].get("role") == "tool"), None)
        text = (trace["text"] or "").strip()
        if reply and text:
            if text.startswith(PROPOSAL_ONLY):
                text = PROPOSAL_ONLY
            out.append({"kind": "reply", "messages": [to_hf(m) for m in reply["request"]["messages"]]
                        + [{"role": "assistant", "content": text}], "tools": tools})
    else:
        text = (trace["text"] or "").strip()
        if not first["response"]["tool_calls"] and text:
            out.append({"kind": "answer", "messages": base + [{"role": "assistant", "content": text}], "tools": tools})
    return out


def main() -> None:
    source, target = sys.argv[1], sys.argv[2]
    traces = [json.loads(line) for line in open(source, encoding="utf-8")]
    all_examples, passing = [], 0
    for trace in traces:
        passing += passed(trace)
        all_examples.extend(examples(trace))

    random.Random(11).shuffle(all_examples)
    held = max(1, len(all_examples) // 10)
    os.makedirs(target, exist_ok=True)
    for name, part in (("valid.jsonl", all_examples[:held]), ("train.jsonl", all_examples[held:])):
        with open(os.path.join(target, name), "w", encoding="utf-8") as out:
            for example in part:
                out.write(json.dumps(example, ensure_ascii=False) + "\n")

    kinds: dict[str, int] = {}
    for example in all_examples:
        kinds[example["kind"]] = kinds.get(example["kind"], 0) + 1
    print(f"{len(traces)} traces, {passing} passed; {len(all_examples)} examples {kinds}; {held} held out")


if __name__ == "__main__":
    main()
