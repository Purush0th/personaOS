"""PersonaOS updater: the one container that may control Docker. It installs the release the API
asks for, and nothing else.

The API writes <updates>/request.json ({"id", "version"}); this loop picks it up, then:
  1. backs up the data volume to ./backups next to the compose file,
  2. sets PERSONAOS_VERSION in .env,
  3. pulls the images and restarts every service but itself,
  4. waits for the API to answer with the new version,
  5. on any failure puts the old version back and restarts again.
It writes its progress to <updates>/status.json, with a heartbeat the API reads to tell whether an
updater is running at all. A newer updater image is started last, from a short-lived helper
container, so this one is never asked to replace itself while it is still working.

The compose folder is mounted at the same path as on the host (see docker-compose.yml), so the
paths compose resolves inside this container are the host's own.
"""

import json
import os
import re
import socket
import subprocess
import sys
import time
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

UPDATES = Path(os.environ.get("UPDATES", "/updates"))
PROJECT_DIR = Path(os.environ["PROJECT_DIR"])
PROJECT = os.environ.get("COMPOSE_PROJECT_NAME", "personaos")
DOCKER = os.environ.get("DOCKER", "docker").split()
API_URL = os.environ.get("API_URL", "http://api:8080")
POLL_SECONDS = float(os.environ.get("POLL_SECONDS", "10"))
HEALTH_TRIES = int(os.environ.get("HEALTH_TRIES", "60"))
HEALTH_WAIT = float(os.environ.get("HEALTH_WAIT", "5"))
SELF_SERVICE = os.environ.get("SELF_SERVICE", "updater")

REQUEST = UPDATES / "request.json"
STATUS = UPDATES / "status.json"
ENV_FILE = PROJECT_DIR / ".env"
VERSION = re.compile(r"^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$")


def now() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def log(text: str) -> None:
    print(f"{now()} {text}", flush=True)


def write_json(path: Path, value: dict) -> None:
    """Written whole, then moved into place, so the API never reads half a file."""
    temporary = path.with_suffix(".tmp")
    temporary.write_text(json.dumps(value), encoding="utf-8")
    os.replace(temporary, path)


def read_json(path: Path) -> dict | None:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
        return value if isinstance(value, dict) else None
    except (OSError, ValueError):
        return None


class Job:
    """One request being worked on: its progress goes to status.json."""

    def __init__(self, request_id: str | None, version: str | None):
        self.request_id = request_id
        self.version = version

    def status(self, state: str, message: str | None = None, finished: bool = False) -> None:
        value = {"heartbeat": now(), "state": state}
        if self.request_id:
            value.update(requestId=self.request_id, version=self.version)
        if message:
            value["message"] = message
        if finished:
            value["finishedAt"] = now()
        write_json(STATUS, value)
        log(state + (f": {message}" if message else ""))


def beat() -> None:
    """Keeps the last state, with a fresh heartbeat."""
    value = read_json(STATUS) or {"state": "idle"}
    value["heartbeat"] = now()
    write_json(STATUS, value)


def run(*args: str, check: bool = True) -> subprocess.CompletedProcess:
    result = subprocess.run([*DOCKER, *args], cwd=PROJECT_DIR, capture_output=True, text=True)
    if result.returncode != 0:
        log(f"docker {' '.join(args)} failed: {(result.stderr or result.stdout).strip()[-500:]}")
        if check:
            raise RuntimeError(f"docker {args[0]} {args[1] if len(args) > 1 else ''}".strip())
    return result


def current_version() -> str:
    found = re.findall(r"^PERSONAOS_VERSION=(.*)$", ENV_FILE.read_text(encoding="utf-8"), re.MULTILINE)
    return found[-1].strip() if found else "latest"


def set_version(version: str) -> None:
    text = ENV_FILE.read_text(encoding="utf-8")
    if re.search(r"^PERSONAOS_VERSION=", text, re.MULTILINE):
        text = re.sub(r"^PERSONAOS_VERSION=.*$", f"PERSONAOS_VERSION={version}", text, flags=re.MULTILINE)
    else:
        text = text.rstrip("\n") + f"\nPERSONAOS_VERSION={version}\n"
    ENV_FILE.write_text(text, encoding="utf-8")


def services() -> list[str]:
    """Every service of the project but the updater, under the profiles .env turns on."""
    listed = run("compose", "config", "--services").stdout.split()
    return [s for s in listed if s != SELF_SERVICE]


def restart() -> None:
    names = services()
    run("compose", "pull", "--quiet", *names)
    run("compose", "up", "-d", "--no-deps", *names)


def healthy(expected: str) -> bool:
    """The API answers, with `expected` as its version when that is one. Going back to "latest"
    (an .env that never named a version) can only be checked by the API answering at all."""
    for _ in range(HEALTH_TRIES):
        try:
            with urllib.request.urlopen(f"{API_URL}/api/branding", timeout=5) as response:
                answered = json.loads(response.read()).get("apiVersion")
            if answered and (answered == expected or not VERSION.match(expected)):
                return True
        except (OSError, ValueError):
            pass
        time.sleep(HEALTH_WAIT)
    return False


def self_image() -> str:
    if image := os.environ.get("SELF_IMAGE"):
        return image
    return run("inspect", "--format", "{{.Config.Image}}", socket.gethostname()).stdout.strip()


def install(job: Job) -> None:
    old = current_version()

    job.status("backing-up")
    PROJECT_DIR.joinpath("backups").mkdir(exist_ok=True)
    stamp = datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S")
    try:
        # The data volume, read-only, into a tarball next to the compose file; this image has tar.
        run("run", "--rm", "-v", f"{PROJECT}_api-data:/data:ro", "-v", f"{PROJECT_DIR / 'backups'}:/backups",
            "--entrypoint", "tar", self_image(),
            "czf", f"/backups/before-{job.version}-{stamp}.tar.gz", "-C", "/data", "--exclude=./backups", ".")
    except RuntimeError:
        job.status("failed", "Could not back up the data, so nothing was changed.", finished=True)
        return

    job.status("pulling")
    set_version(job.version)
    try:
        run("compose", "pull", "--quiet", *services())
    except RuntimeError:
        set_version(old)
        job.status("failed", f"Could not download {job.version}, so nothing was changed.", finished=True)
        return

    job.status("restarting")
    try:
        run("compose", "up", "-d", "--no-deps", *services())
    except RuntimeError:
        roll_back(job, old, f"{job.version} did not start.")
        return

    job.status("checking")
    if not healthy(job.version):
        roll_back(job, old, f"{job.version} did not answer.")
        return

    job.status("done", f"Now on {job.version}.", finished=True)
    replace_self()


def roll_back(job: Job, old: str, why: str) -> None:
    set_version(old)
    try:
        restart()
        if healthy(old):
            job.status("rolled-back", f"{why} Back on {old}.", finished=True)
            return
    except RuntimeError:
        pass
    job.status("failed", f"{why} Going back to {old} did not work either; see the updater's log.", finished=True)


def replace_self() -> None:
    """A newer updater comes with the release: a helper container starts it once this one is done.
    Not worth failing the update for."""
    if run("compose", "pull", "--quiet", SELF_SERVICE, check=False).returncode != 0:
        return
    helper = run(
        "run", "-d", "--rm", "-v", "/var/run/docker.sock:/var/run/docker.sock", "-v", f"{PROJECT_DIR}:{PROJECT_DIR}",
        "-w", str(PROJECT_DIR), "--entrypoint", "sh", self_image(),
        "-c", f"sleep 5; docker compose up -d --no-deps {SELF_SERVICE}", check=False)
    if helper.returncode != 0:
        log("Could not start the new updater; the old one keeps running.")


def main() -> None:
    log(f"PersonaOS updater watching {UPDATES} for {PROJECT_DIR}")
    handled = (read_json(STATUS) or {}).get("requestId")
    while not ENV_FILE.exists():
        # PERSONAOS_DIR does not point at the compose folder: say so, and do nothing.
        Job(None, None).status("not-set-up", f"No .env in {PROJECT_DIR}: set PERSONAOS_DIR in .env to the folder with docker-compose.yml.")
        if os.environ.get("ONCE") == "1":
            return
        time.sleep(POLL_SECONDS)
    while True:
        request = read_json(REQUEST) or {}
        request_id, version = request.get("id"), request.get("version")
        if request_id and request_id != handled:
            handled = request_id
            job = Job(request_id, version)
            # A version, nothing else: it ends up in .env and in image tags.
            if isinstance(version, str) and VERSION.match(version):
                install(job)
            else:
                job.status("failed", f"Not a version: {version}", finished=True)
        beat()
        if os.environ.get("ONCE") == "1":
            return
        time.sleep(POLL_SECONDS)


if __name__ == "__main__":
    sys.exit(main())
