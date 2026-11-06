"""Renders a conversation exactly as Ollama renders qwen2.5's template, so training sees what inference sees.

A port of Ollama 0.40.14's template code (template/template.go) for the TEMPLATE in
`ollama show qwen2.5:3b-instruct --template`. Details that matter, all checked against Ollama's
own token count by `check_template.py`:

- Each tool is written with Go's default struct printing, not as JSON: Ollama hands the template
  a `templateToolFunction` that has no String method, so `{{ .Function }}` prints
  `{name description {object <nil> <nil> [required...] {properties as JSON}}}`. Only the
  properties map (and tool-call arguments) are JSON: compact, keys sorted, HTML characters escaped.
- Consecutive messages with the same role are merged with a blank line (Ollama's `collate`).
- The last assistant turn has no <|im_end|> in Ollama's template; `render` adds one so the model
  learns to stop, `render_prompt` leaves the prompt open as Ollama does.

Run `python -I training/check_template.py` after any Ollama upgrade: a model fine-tuned on a prompt
that differs from the real one breaks in small ways (run 2026-11-05 dropped quotes in tool calls).
"""

import json

# Ollama's api.ToolProperty fields, in declaration order.
_PROPERTY_FIELDS = ("anyOf", "type", "items", "description", "enum", "properties", "required")


def _go_json(value: object) -> str:
    """json.Marshal of a Go map or slice: compact, map keys sorted, HTML characters escaped."""
    text = json.dumps(value, ensure_ascii=False, separators=(",", ":"), sort_keys=True)
    return (text.replace("<", r"<").replace(">", r">").replace("&", r"&")
            .replace(" ", r" ").replace(" ", r" "))


def _property(schema: dict) -> str:
    """One api.ToolProperty: struct fields in order, empty ones omitted, nested properties sorted."""
    parts = []
    for key in _PROPERTY_FIELDS:
        value = schema.get(key)
        if value in (None, "", [], {}):
            continue
        if key == "anyOf":
            parts.append('"anyOf":[' + ",".join(_property(x) for x in value) + "]")
        elif key == "properties":
            parts.append('"properties":' + _properties(value))
        elif key == "type" and isinstance(value, list) and len(value) == 1:
            parts.append('"type":' + _go_json(value[0]))  # PropertyType marshals one type as a string
        else:
            parts.append(f'"{key}":' + _go_json(value))
    return "{" + ",".join(parts) + "}"


def _properties(properties: dict | None) -> str:
    if not properties:
        return "{}"
    return "{" + ",".join(_go_json(name) + ":" + _property(properties[name]) for name in sorted(properties)) + "}"


def _go_value(value: object) -> str:
    """fmt's %v of an `any` field: <nil> when unset."""
    return "<nil>" if value is None else str(value)


def _function(function: dict) -> str:
    """fmt's %v of Ollama's templateToolFunction{Name, Description, Parameters{Type, Defs, Items, Required, Properties}}."""
    parameters = function.get("parameters") or {}
    inner = " ".join([
        parameters.get("type", ""),
        _go_value(parameters.get("$defs")),
        _go_value(parameters.get("items")),
        "[" + " ".join(parameters.get("required") or []) + "]",
        _properties(parameters.get("properties")),
    ])
    return "{" + function["name"] + " " + (function.get("description") or "") + " {" + inner + "}}"


def _arguments(arguments: object) -> str:
    if isinstance(arguments, str):
        arguments = json.loads(arguments) if arguments.strip() else {}
    return _go_json(arguments) if arguments else "{}"


def _collate(messages: list[dict]) -> list[dict]:
    """Ollama merges consecutive messages of the same role (tool results excepted)."""
    out: list[dict] = []
    for message in messages:
        if out and out[-1]["role"] == message["role"] and message["role"] != "tool":
            out[-1] = dict(out[-1], content=(out[-1].get("content") or "") + "\n\n" + (message.get("content") or ""))
        else:
            out.append(dict(message))
    return out


def render_prompt(messages: list[dict], tools: list[dict] | None, close_last: bool = False) -> str:
    """The prompt text Ollama builds for /api/chat with these messages and tools."""
    system = "\n\n".join(m.get("content") or "" for m in messages if m["role"] == "system")
    out = []
    if system or tools:
        out.append("<|im_start|>system")
        if system:
            out.append("\n" + system)
        if tools:
            out.append(
                "\n\n# Tools\n\nYou may call one or more functions to assist with the user query.\n\n"
                "You are provided with function signatures within <tools></tools> XML tags:\n<tools>")
            for tool in tools:
                out.append('\n{"type": "function", "function": ' + _function(tool["function"]) + "}")
            out.append(
                "\n</tools>\n\nFor each function call, return a json object with function name and arguments "
                "within <tool_call></tool_call> XML tags:\n<tool_call>\n"
                '{"name": <function-name>, "arguments": <args-json-object>}\n</tool_call>')
        out.append("<|im_end|>\n")

    collated = _collate(messages)
    for i, message in enumerate(collated):
        last = i == len(collated) - 1
        role, content = message["role"], message.get("content") or ""
        if role == "user":
            out.append(f"<|im_start|>user\n{content}<|im_end|>\n")
        elif role == "assistant":
            out.append("<|im_start|>assistant\n")
            if content:
                out.append(content)
            elif message.get("tool_calls"):
                out.append("<tool_call>\n")
                for call in message["tool_calls"]:
                    function = call["function"]
                    out.append(f'{{"name": "{function["name"]}", "arguments": {_arguments(function.get("arguments"))}}}\n')
                out.append("</tool_call>")
            if not last or close_last:
                out.append("<|im_end|>\n")
        elif role == "tool":
            out.append(f"<|im_start|>user\n<tool_response>\n{content}\n</tool_response><|im_end|>\n")
        if role != "assistant" and last:
            out.append("<|im_start|>assistant\n")
    return "".join(out)


def render(messages: list[dict], tools: list[dict] | None) -> str:
    """A training example: the conversation with its last assistant turn closed, so the model learns to stop."""
    return render_prompt(messages, tools, close_last=True)
