"""Renders a conversation exactly as Ollama's qwen2.5 template does, so training sees what inference sees.

The Hugging Face chat template for Qwen2.5 differs in small ways (a "# Tools" heading, spaced
JSON), and a model fine-tuned on one and served with the other meets text it was not trained on.
This is a port of the TEMPLATE in `ollama show qwen2.5:3b-instruct --modelfile`. JSON is written
the way Go writes it: compact, map keys sorted, struct fields in declaration order.
"""

import json


def _go_json(value: object) -> str:
    text = json.dumps(value, ensure_ascii=False, separators=(",", ":"), sort_keys=True)
    # Go's json.Marshal escapes these for HTML.
    return text.replace("<", r"\u003c").replace(">", r"\u003e").replace("&", r"\u0026")


def _property(schema: dict) -> str:
    # Ollama's ToolProperty keeps only type, items, description and enum, in that order: a nested
    # object's own properties and required are dropped before the model ever sees them.
    parts = [f'"{key}":{_go_json(schema[key])}' for key in ("type", "items", "description", "enum") if key in schema]
    return "{" + ",".join(parts) + "}"


def _function(function: dict) -> str:
    # Ollama's ToolFunction struct: name, description, parameters, in that order.
    parts = [f'"name":{_go_json(function["name"])}']
    if function.get("description"):
        parts.append(f'"description":{_go_json(function["description"])}')
    parameters = function.get("parameters") or {}
    # Its parameters struct: type, $defs, items, required, properties.
    inner = [f'"type":{_go_json(parameters.get("type", "object"))}']
    for key in ("$defs", "items", "required"):
        if key in parameters:
            inner.append(f'"{key}":{_go_json(parameters[key])}')
    if "properties" in parameters:
        properties = parameters["properties"]
        inner.append('"properties":{' + ",".join(
            f"{_go_json(name)}:{_property(properties[name])}" for name in sorted(properties)) + "}")
    parts.append('"parameters":{' + ",".join(inner) + "}")
    return "{" + ",".join(parts) + "}"


def render(messages: list[dict], tools: list[dict] | None) -> str:
    """The whole conversation, the last assistant turn closed with <|im_end|> so the model learns to stop."""
    system = "\n\n".join(m["content"] for m in messages if m["role"] == "system")
    out = []
    if system or tools:
        out.append("<|im_start|>system")
        if system:
            out.append("\n" + system)
        if tools:
            out.append(
                "\n\n\nYou may call one or more functions to assist with the user query.\n\n"
                "You are provided with function signatures within <tools></tools> XML tags:\n<tools>")
            for tool in tools:
                out.append('\n{"type": "function", "function": ' + _function(tool["function"]) + "}")
            out.append(
                "\n</tools>\n\nFor each function call, return a json object with function name and arguments "
                "within <tool_call></tool_call> XML tags:\n<tool_call>\n"
                '{"name": <function-name>, "arguments": <args-json-object>}\n</tool_call>')
        out.append("<|im_end|>\n")

    for message in (m for m in messages if m["role"] != "system"):
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
                    out.append(f'{{"name": "{function["name"]}", "arguments": {_go_json(function["arguments"])}}}\n')
                out.append("</tool_call>")
            out.append("<|im_end|>\n")
        elif role == "tool":
            out.append(f"<|im_start|>user\n<tool_response>\n{content}\n</tool_response><|im_end|>\n")
    return "".join(out)
