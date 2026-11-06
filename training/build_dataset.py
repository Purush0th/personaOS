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

    python -I training/build_dataset.py data/training/dataset data/training/traces.jsonl [more traces…]
"""

import json
import os
import random
import re
import sys

PROPOSAL_ONLY = "Here is the change I would make — confirm it below if it looks right."
WRITES = {
    "create_task", "create_reminder", "add_planner_item", "create_goal", "create_note", "create_memory",
    "move_task", "update_task", "cancel_reminder", "delete_goal",
}
# Saved at once rather than proposed: the turn shows them as reads that ran, not as cards.
SAVED_AT_ONCE = {"create_note", "create_memory"}
# Tools whose calls are not used as labels. Was {"create_goal"} until the traces were re-collected
# with GoalPeriodGuard in place (2026-11-05); before it, goal periods were picked at random.
NO_TOOL_LABELS: set[str] = set()
# How an honest "there is none" reads.
NEGATIVE = re.compile(r"(?i)\b(no|not|don't|do not|couldn't|could not|can't|cannot|isn't|haven't|nothing|none)\b")
# A reply saying a change was made. Only applied where nothing was proposed or saved, so it is a
# false claim there: "GOAL-10 was created" after the app refused the goal (2026-11-05). Broader than
# the app's ActionClaimDetector on purpose: a lost example costs little, a lying label teaches lying.
CLAIM = re.compile(
    r"(?i)\b(i(?:'ve| have| just)?|has been|have been|was|were|is now|are now)\s+(?:\w+\s+){0,2}?"
    r"(set|created|added|scheduled|saved|booked|moved|updated|marked|cancell?ed|deleted|removed)\b")
NOT_A_CLAIM = re.compile(r"(?i)(\bnot\b|n't\b|\bnever\b|\bpropos\w*|\bconfirm\w*|\bbelow\b|\bwould\b|\bcould\b|\bcan\b|\bmay\b|\bmight\b|\bif you\b)")
# A line of a list read back from a tool ("- NoteId 836: …") is the user's data, not the reply's claim.
LIST_ITEM = re.compile(r"^\s*(?:[-*•]|\d+[.)])\s")


def claims(text: str) -> bool:
    """Whether any sentence of the reply says a change was made."""
    return any(CLAIM.search(s) and not NOT_A_CLAIM.search(s)
               for line in text.splitlines() if not LIST_ITEM.match(line)
               for s in re.split(r"(?<=[.!?])\s+", line))


def accepted(prompt: dict) -> set[str]:
    """The tools a prompt may end in: one, or any of a list for a read."""
    tool = prompt["tool"]
    return set(tool) if isinstance(tool, list) else {tool} if tool else set()


def has_words(value: object, words: list[str]) -> bool:
    text = json.dumps(value, ensure_ascii=False).lower()
    return all(w.lower() in text for w in words)


def passed(trace: dict) -> bool:
    """Whether the turn ended where its prompt says it should."""
    prompt = trace["prompt"]
    tool, words = prompt["tool"], prompt["words"]
    text = (trace["text"] or "").strip()
    if prompt.get("ask"):  # something was missing: no change, and a question back
        return not trace["proposals"] and "?" in text and not claims(text)
    if prompt.get("refuse"):  # nothing can be made: no change, and an honest answer (checking first is fine)
        return not trace["proposals"] and bool(text) and not claims(text)
    if isinstance(tool, list):
        tool = next((t for t in tool if t in trace["reads"]), tool[0])
    if tool in WRITES:
        # The stored input can be resolved to an id (cancel_reminder keeps only reminderId), so the
        # words may sit in the card's summary instead.
        made = [p for p in trace["proposals"]
                if p["tool"] == tool and has_words([p["input"], p.get("summary")], words)]
        saved = tool in SAVED_AT_ONCE and tool in trace["reads"] and has_words(ran_input(trace, tool), words)
        others = [p for p in trace["proposals"] if p["tool"] != tool]
        return (bool(made) or saved) and not others
    if tool:  # a read; one that should find nothing must say so
        found = tool in trace["reads"] and not trace["proposals"] and not claims(text)
        # A read that should find something must say it (words), one that should find nothing must say so.
        return (found and (not words or has_words([text], words))
                and (not prompt.get("negative") or bool(NEGATIVE.search(text))))
    return not trace["proposals"] and not trace["reads"] and bool(text) and not claims(text)


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

    if not passed(trace) or accepted(prompt) & NO_TOOL_LABELS:
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
        if ran is None or ran["tool_calls"][0]["function"]["name"] not in accepted(prompt):
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
        # A question back is the right answer whatever the model first tried (the app's guards
        # turned a guessed time or title into the question), so it is taught directly. Anything
        # else is kept only when the model answered without a tool.
        if text and (prompt.get("ask") or not first["response"]["tool_calls"]):
            out.append({"kind": "answer", "messages": base + [{"role": "assistant", "content": text}], "tools": tools})
    return out


def main() -> None:
    target, sources = sys.argv[1], sys.argv[2:]
    traces = [json.loads(line) for source in sources for line in open(source, encoding="utf-8")]
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
