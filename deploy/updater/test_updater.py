"""Runs the updater once against a stand-in `docker` and a stand-in API.

    python -m unittest deploy/updater/test_updater.py

The stand-in docker records every call and fails the ones FAIL names; the stand-in API answers
/api/branding with the version in its file, so a test decides whether the new release "starts".
"""

import json
import os
import subprocess
import sys
import tempfile
import threading
import unittest
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

HERE = Path(__file__).parent

FAKE_DOCKER = r'''
import json, os, sys
args = sys.argv[1:]
with open(os.environ["CALLS"], "a", encoding="utf-8") as calls:
    calls.write(json.dumps(args) + "\n")
fail = os.environ.get("FAIL", "")
joined = " ".join(args)
if fail and fail in joined and os.environ.get("FAIL_VERSION", "") in open(os.environ["ENVFILE"]).read():
    sys.exit(1)
if args[:3] == ["compose", "config", "--services"]:
    print("api\nweb\nwhisper\nupdater")
elif args[:1] == ["inspect"]:
    print("ghcr.io/purush0th/personaos-updater:0.1.0-alpha.9")
'''


class Api(BaseHTTPRequestHandler):
    version_file: Path

    def do_GET(self):  # noqa: N802 (http.server's name)
        body = json.dumps({"apiVersion": self.version_file.read_text().strip()}).encode()
        self.send_response(200)
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *_):
        pass


class UpdaterTest(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp())
        self.project = self.root / "personaos"
        self.updates = self.root / "updates"
        self.project.mkdir()
        self.updates.mkdir()
        (self.project / ".env").write_text("JWT_SIGNING_KEY=x\nPERSONAOS_VERSION=0.1.0-alpha.9\n")
        (self.root / "docker.py").write_text(FAKE_DOCKER)
        self.calls = self.root / "calls.jsonl"
        self.served = self.root / "served"
        self.served.write_text("0.1.0-alpha.9")

        handler = type("Handler", (Api,), {"version_file": self.served})
        self.server = HTTPServer(("127.0.0.1", 0), handler)
        threading.Thread(target=self.server.serve_forever, daemon=True).start()

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()

    def request(self, version, request_id="r1"):
        (self.updates / "request.json").write_text(json.dumps({"id": request_id, "version": version}))

    def run_once(self, **extra):
        env = {
            **os.environ,
            "UPDATES": str(self.updates),
            "PROJECT_DIR": str(self.project),
            "DOCKER": f"{sys.executable} {self.root / 'docker.py'}",
            "API_URL": f"http://127.0.0.1:{self.server.server_port}",
            "HEALTH_TRIES": "2",
            "HEALTH_WAIT": "0",
            "ONCE": "1",
            "CALLS": str(self.calls),
            "ENVFILE": str(self.project / ".env"),
            **extra,
        }
        subprocess.run([sys.executable, str(HERE / "updater.py")], env=env, check=True, capture_output=True, text=True)
        return json.loads((self.updates / "status.json").read_text())

    def calls_made(self):
        return [json.loads(line) for line in self.calls.read_text().splitlines()] if self.calls.exists() else []

    def version_in_env(self):
        return [line for line in (self.project / ".env").read_text().splitlines() if line.startswith("PERSONAOS_VERSION=")]

    def test_installs_the_requested_release(self):
        self.request("0.1.0-alpha.10")
        self.served.write_text("0.1.0-alpha.10")  # The new release starts and answers.

        status = self.run_once()

        self.assertEqual("done", status["state"])
        self.assertEqual("r1", status["requestId"])
        self.assertEqual(["PERSONAOS_VERSION=0.1.0-alpha.10"], self.version_in_env())
        calls = self.calls_made()
        backup = next(c for c in calls if c[:2] == ["run", "--rm"])
        self.assertIn("personaos_api-data:/data:ro", backup)
        pull = next(c for c in calls if c[:2] == ["compose", "pull"])
        up = next(c for c in calls if c[:2] == ["compose", "up"])
        # Every service but the updater itself, which a helper replaces afterwards.
        self.assertEqual(["api", "web", "whisper"], pull[3:])
        self.assertEqual(["api", "web", "whisper"], up[4:])
        self.assertTrue(any(c[:2] == ["run", "-d"] and "up -d --no-deps updater" in c[-1] for c in calls))

    def test_rolls_back_a_release_that_does_not_answer(self):
        self.request("0.1.0-alpha.10")  # The API keeps answering alpha.9: the new one never came up.

        status = self.run_once()

        self.assertEqual("rolled-back", status["state"])
        self.assertIn("Back on 0.1.0-alpha.9", status["message"])
        self.assertEqual(["PERSONAOS_VERSION=0.1.0-alpha.9"], self.version_in_env())

    def test_a_failed_download_changes_nothing(self):
        self.request("0.1.0-alpha.10")

        status = self.run_once(FAIL="compose pull", FAIL_VERSION="alpha.10")

        self.assertEqual("failed", status["state"])
        self.assertEqual(["PERSONAOS_VERSION=0.1.0-alpha.9"], self.version_in_env())
        self.assertFalse(any(c[:2] == ["compose", "up"] for c in self.calls_made()))

    def test_without_a_backup_nothing_is_changed(self):
        self.request("0.1.0-alpha.10")

        status = self.run_once(FAIL="run --rm")

        self.assertEqual("failed", status["state"])
        self.assertIn("back up", status["message"])
        self.assertEqual(["PERSONAOS_VERSION=0.1.0-alpha.9"], self.version_in_env())

    def test_refuses_anything_but_a_version(self):
        self.request("latest; rm -rf /")

        status = self.run_once()

        self.assertEqual("failed", status["state"])
        self.assertEqual([], self.calls_made())

    def test_a_request_is_handled_once(self):
        self.request("0.1.0-alpha.10")
        self.served.write_text("0.1.0-alpha.10")
        self.run_once()
        calls = len(self.calls_made())

        status = self.run_once()

        self.assertEqual(calls, len(self.calls_made()))
        self.assertEqual("done", status["state"])

    def test_without_the_compose_folder_it_says_so_and_does_nothing(self):
        (self.project / ".env").unlink()
        self.request("0.1.0-alpha.10")

        status = self.run_once()

        self.assertEqual("not-set-up", status["state"])
        self.assertIn("PERSONAOS_DIR", status["message"])
        self.assertEqual([], self.calls_made())

    def test_with_nothing_asked_it_only_reports_that_it_is_alive(self):
        status = self.run_once()

        self.assertEqual("idle", status["state"])
        self.assertIn("heartbeat", status)
        self.assertEqual([], self.calls_made())


if __name__ == "__main__":
    unittest.main()
