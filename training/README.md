# personaOS training framework

personaOS already decides much of a turn in code: the guards and routers fix dates, titles and the
choice of tool before anything reaches the user. This project turns those checked results into
training data, so the small local model learns to get them right the first time, and measures
whether it did. The app's code teaches the model; the accuracy suite (`scripts/model-check.ps1`)
judges it; the owner decides whether it is adopted.

It talks to personaOS only over its HTTP API (plus a read-only look at the dev database for
fixtures), so it grows with the app's capabilities without being part of the app.

Everything here is for a development machine with an NVIDIA GPU (tested on an RTX 3060, 12 GB).

## Licence

Qwen2.5-3B is released under the Qwen Research Licence: non-commercial use only. Fine-tuning it and
running the result on your own server is fine; publishing the trained weights is not part of this
project. Keep the model on your own machines. The scripts here are Apache-2.0 like the rest.

## Layout

| File | Does |
|---|---|
| `capabilities/` | The training prompts, one module per capability (`core`, `claims`, …). Each prompt carries its own answer: intent, expected tool, words the result must contain, or `ask` / `refuse` / `negative`. |
| `prompts.py` | Gathers every capability; `python -I training/prompts.py scripts/model-check.ps1` counts them and lists any too close to a suite message. |
| `proxy.py` | Passes every Ollama call through and logs it. |
| `collect.py` | Runs prompts through the real API (fixtures first, proposals discarded, notes and memories taken back), recording each turn. |
| `build_dataset.py` | Keeps only turns that ended right (and claim nothing false) and turns them into chat examples. |
| `train.py` | LoRA in the unsloth container; a checkpoint per epoch, resumed after a crash. |
| `ollama_template.py`, `Modelfile` | The exact chat template Ollama serves qwen2.5 with, for training and import. |
| `report.py` | The audit report: verdict, stock vs trained per suite, every scenario that changed, the data. |
| `overnight.ps1` | All of it in one command, resumable. |

## Adding a capability

When the app gains a tool (web search is next), add `capabilities/<name>.py` with
`build(seed) -> list[dict]` and register it in `capabilities/__init__.py`. Collect just that
capability (`-Collect <name>`) and train on it together with the turns already collected. Never
reword or reorder prompts that have been collected: `--skip-done` matches turns by their text.

## The overnight run

From a terminal that outlives any agent session, with Docker Desktop and Ollama running and the GPU
power limit at 120 W (this machine's PSU cut out at full load):

```powershell
$env:PERSONAOS_USER = '…'; $env:PERSONAOS_PASSWORD = '…'
./training/overnight.ps1 -Run 2026-11-05 -Collect claims -Traces data/training/traces3.jsonl
```

Stages, each skipped when its output exists in `data/training/runs/<Run>/` (run the same command
again after any interruption):

1. **Collect** new turns for the named capabilities, through the proxy.
2. **Dataset** from the given earlier traces plus the new ones.
3. **Train** in a detached container (`personaos-train-<Run>`), so it survives the terminal.
4. **Convert** to GGUF and quantize to q4_K_M (llama.cpp b11721), the stock model's quantization.
5. **Import** into Ollama as `personaos-qwen2.5:3b`.
6. **Benchmark** stock and trained on every suite (`-Runs 3`).
7. **Report** to `report.md`.

The trained model is a candidate only when no suite scores lower than stock; the report says so
either way, and nothing points the instance at it until the owner has read the report.

## By hand

The same steps one at a time, from the repository root:

```bash
python -I training/proxy.py --listen 11500 --log data/training/calls.jsonl
PERSONAOS_USER=… PERSONAOS_PASSWORD=… python -I training/collect.py \
    --proxy-log data/training/calls.jsonl --out data/training/traces.jsonl --only claims
python -I training/build_dataset.py data/training/dataset data/training/traces3.jsonl data/training/traces.jsonl
docker run --rm --gpus all -v "$PWD/training:/code" -v "$PWD/data/training:/work" \
    -v personaos-hf-cache:/root/.cache/huggingface -w /code unsloth/unsloth:core-v0.1.908-beta \
    python train.py --data /work/dataset --out /work/model
```

Ollama 0.40 cannot import a Qwen2 safetensors model or quantize a GGUF at import, which is why
llama.cpp converts and quantizes first (see the convert stage in `overnight.ps1`).
