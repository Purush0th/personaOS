"""Records what PersonaOS sends the model, for building training data.

Sits between the API and Ollama: point the instance's AI base URL at this proxy and every
/api/chat call is passed through unchanged and appended to a JSONL log, with the request body and
the model's whole answer (its text and its tool calls, joined from the stream).

    python -I training/proxy.py --listen 11500 --target http://localhost:11434 --log calls.jsonl

Standard library only. Development use: it listens on localhost and logs conversations in full.
"""

import argparse
import json
import threading
import time
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

_lock = threading.Lock()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--listen", type=int, default=11500)
    parser.add_argument("--target", default="http://localhost:11434")
    parser.add_argument("--log", required=True)
    args = parser.parse_args()

    class Handler(BaseHTTPRequestHandler):
        # HTTP/1.1 keeps the connection open: the API reuses pooled connections, and under the
        # default HTTP/1.0 every other call failed on a socket this side had already closed.
        protocol_version = "HTTP/1.1"

        def do_GET(self) -> None:
            self._forward(None)

        def do_POST(self) -> None:
            length = int(self.headers.get("Content-Length") or 0)
            self._forward(self.rfile.read(length))

        def _forward(self, body: bytes | None) -> None:
            request = urllib.request.Request(
                args.target + self.path,
                data=body,
                method=self.command,
                headers={"Content-Type": self.headers.get("Content-Type", "application/json")},
            )
            try:
                upstream = urllib.request.urlopen(request, timeout=600)
            except urllib.error.HTTPError as error:
                upstream = error
            self.send_response(upstream.status)
            for name, value in upstream.headers.items():
                if name.lower() not in ("transfer-encoding", "connection", "content-length"):
                    self.send_header(name, value)
            self.send_header("Transfer-Encoding", "chunked")
            self.end_headers()

            text, tool_calls, raw = [], [], []
            for line in upstream:
                self.wfile.write(b"%x\r\n%s\r\n" % (len(line), line))
                self.wfile.flush()
                raw.append(line)
                try:
                    chunk = json.loads(line)
                except ValueError:
                    continue
                message = chunk.get("message") or {}
                text.append(message.get("content") or "")
                tool_calls.extend(message.get("tool_calls") or [])
            self.wfile.write(b"0\r\n\r\n")

            if self.path.startswith("/api/chat") and body:
                record = {
                    "time": time.time(),
                    "request": json.loads(body),
                    "response": {"content": "".join(text), "tool_calls": tool_calls},
                }
                with _lock, open(args.log, "a", encoding="utf-8") as log:
                    log.write(json.dumps(record, ensure_ascii=False) + "\n")

        def log_message(self, *_: object) -> None:
            pass

    print(f"proxy :{args.listen} -> {args.target}, logging to {args.log}", flush=True)
    ThreadingHTTPServer(("127.0.0.1", args.listen), Handler).serve_forever()


if __name__ == "__main__":
    main()
