"""The audit report for one training run: what went in, how training went, and stock vs trained.

    python -I training/report.py data/training/runs/2026-11-05 [--stock qwen2.5:3b-instruct --trained personaos-qwen2.5:3b]

Reads the run folder overnight.ps1 fills (dataset/, train.log, bench/*.json, traces) and writes
report.md next to them. The verdict follows the project's rule: the trained model is recommended
only when no suite scores lower than the stock model. Nothing is adopted by this script.
"""

import argparse
import collections
import glob
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_dataset  # noqa: E402

SUITES = ["all", "holdout", "holdout2", "holdout3"]
SUITE_NAMES = {"all": "core + extended", "holdout": "holdout", "holdout2": "holdout2", "holdout3": "holdout3"}


def tag(model: str) -> str:
    return re.sub(r"[^A-Za-z0-9.-]+", "_", model)


def bench(run: str, model: str, suite: str, folder: str = "bench") -> dict | None:
    path = os.path.join(run, folder, f"{tag(model)}-{suite}.json")
    if not os.path.exists(path):
        return None
    return json.load(open(path, encoding="utf-8-sig"))


def losses(run: str) -> tuple[list[float], dict]:
    path = os.path.join(run, "train.log")
    if not os.path.exists(path):
        return [], {}
    text = open(path, encoding="utf-8", errors="replace").read()
    train = [float(m) for m in re.findall(r"'loss': '?([0-9.]+)", text)]
    evals = {}
    for m in re.finditer(r"'eval_loss': '?([0-9.]+)'?.*?'epoch': '?([0-9.]+)", text):
        evals[round(float(m.group(2)), 2)] = float(m.group(1))
    return train, evals


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("run")
    parser.add_argument("--stock", default="qwen2.5:3b-instruct")
    parser.add_argument("--trained", default="personaos-qwen2.5:3b")
    parser.add_argument("--bench", default="bench", help="the run's folder of benchmark results (bench.ps1 writes others)")
    parser.add_argument("--out", default=None, help="where to write the report (default: the run's report.md)")
    parser.add_argument("--traces", nargs="*", default=None, help="trace files used (default: the run's traces.jsonl plus traces listed in inputs.txt)")
    args = parser.parse_args()
    run = args.run
    out: list[str] = []
    w = out.append

    w(f"# Training run {os.path.basename(os.path.normpath(run))}: audit report\n")
    w(f"Stock model `{args.stock}` against the fine-tuned `{args.trained}`. Nothing has been adopted: "
      "prod keeps the stock model until the owner decides.\n")

    # ---- Verdict and benchmark ----------------------------------------------------------------
    rows, drops, missing = [], [], []
    for suite in SUITES:
        s, t = bench(run, args.stock, suite, args.bench), bench(run, args.trained, suite, args.bench)
        if not s or not t:
            missing.append(suite)
            continue
        delta = round(t["rate"] - s["rate"], 1)
        rows.append((suite, s, t, delta))
        if delta < 0:
            drops.append(suite)
    w("## Verdict\n")
    if missing:
        w(f"**Incomplete:** no benchmark for {', '.join(SUITE_NAMES[x] for x in missing)}. No recommendation.\n")
    elif drops:
        w(f"**Do not adopt.** The trained model scores lower on {', '.join(SUITE_NAMES[x] for x in drops)}.\n")
    else:
        w("**Candidate for adoption.** The trained model scores at least as well as stock on every suite. "
          "Review the scenario changes below before deciding.\n")

    w("## Benchmark (scripts/model-check.ps1)\n")
    w("| Suite | Stock | Trained | Change | Mean seconds (stock → trained) |")
    w("|---|---|---|---|---|")
    for suite, s, t in ((r[0], r[1], r[2]) for r in rows):
        sec = lambda b: round(sum(x["meanSeconds"] for x in b["scenarios"]) / max(1, len(b["scenarios"])), 1)  # noqa: E731
        delta = round(t["rate"] - s["rate"], 1)
        w(f"| {SUITE_NAMES[suite]} | {s['rate']}% ({s['passed']}/{s['possible']}) | {t['rate']}% ({t['passed']}/{t['possible']}) "
          f"| {'+' if delta > 0 else ''}{delta} | {sec(s)} → {sec(t)} |")
    w("")

    w("## Scenarios that changed\n")
    w("Every scenario whose pass count differs, with what the losing model said, for the audit.\n")
    any_change = False
    for suite, s, t, _ in rows:
        stock = {x["scenario"]: x for x in s["scenarios"]}
        for x in t["scenarios"]:
            y = stock.get(x["scenario"])
            if not y or x["passes"] == y["passes"]:
                continue
            any_change = True
            better = x["passes"] > y["passes"]
            loser = y if better else x
            w(f"- **{'better' if better else 'worse'}** · {SUITE_NAMES[suite]} · `{x['scenario']}`: "
              f"stock {y['passes']}/{y['runs']}, trained {x['passes']}/{x['runs']}")
            for f in loser.get("failures", [])[:2]:
                said = (f.get("said") or "").replace("\n", " ")
                w(f"  - {'stock' if better else 'trained'} run {f['run']}: {f['why']}" + (f" — said: “{said[:200]}”" if said else ""))
    if not any_change:
        w("None: both models passed and failed the same scenarios.")
    w("")

    # ---- Training ------------------------------------------------------------------------------
    train, evals = losses(run)
    w("## Training\n")
    if train:
        w(f"- Training loss: {train[0]} at the start, {train[-1]} at the end ({len(train)} logged steps).")
    if evals:
        w("- Held-out loss by epoch: " + ", ".join(f"epoch {e}: {v}" for e, v in sorted(evals.items())))
    params = os.path.join(run, "params.txt")
    if os.path.exists(params):
        w("- Settings: " + open(params, encoding="utf-8").read().strip())
    w("")

    # ---- Data ----------------------------------------------------------------------------------
    w("## Training data\n")
    sources = args.traces
    if sources is None:
        listed = os.path.join(run, "inputs.txt")
        sources = [l.strip() for l in open(listed, encoding="utf-8")] if os.path.exists(listed) else []
        sources += glob.glob(os.path.join(run, "traces.jsonl"))
    traces = [json.loads(l) for src in sources if os.path.exists(src) for l in open(src, encoding="utf-8")]
    by_cap: dict[str, list[int]] = collections.defaultdict(lambda: [0, 0])
    fails = []
    for tr in traces:
        cap = tr["prompt"].get("capability", "core")
        ok = build_dataset.passed(tr)
        by_cap[cap][0] += ok
        by_cap[cap][1] += 1
        if not ok:
            got = ",".join(p["tool"] for p in tr["proposals"]) or ",".join(tr["reads"]) or "no tool"
            fails.append((cap, tr["prompt"]["text"], tr["prompt"]["tool"] or tr["prompt"]["intent"], got, (tr["text"] or "")[:160]))
    w("Collected turns that ended right become examples; the rest are left out (the stock model's mistakes).\n")
    w("| Capability | Turns | Ended right |")
    w("|---|---|---|")
    for cap, (ok, n) in sorted(by_cap.items()):
        w(f"| {cap} | {n} | {ok} ({round(100 * ok / n)}%) |")
    kinds = collections.Counter()
    for name in ("train.jsonl", "valid.jsonl"):
        path = os.path.join(run, "dataset", name)
        if os.path.exists(path):
            for line in open(path, encoding="utf-8"):
                kinds[(name, json.loads(line)["kind"])] += 1
    if kinds:
        tr_n = sum(v for (n, _), v in kinds.items() if n == "train.jsonl")
        va_n = sum(v for (n, _), v in kinds.items() if n == "valid.jsonl")
        by_kind = collections.Counter()
        for (_, k), v in kinds.items():
            by_kind[k] += v
        w(f"\nExamples: {tr_n} for training, {va_n} held out. By kind: "
          + ", ".join(f"{k} {v}" for k, v in sorted(by_kind.items())) + ".")
    if fails:
        w("\n<details><summary>Turns left out of the data (" + str(len(fails)) + ")</summary>\n")
        for cap, text, want, got, said in sorted(fails):
            w(f"- [{cap}] “{text}” — wanted {want}, got {got}: “{said.replace(chr(10), ' ')}”")
        w("\n</details>")
    w("")

    path = args.out or os.path.join(run, "report.md")
    open(path, "w", encoding="utf-8").write("\n".join(out) + "\n")
    print("written:", path)


if __name__ == "__main__":
    main()
