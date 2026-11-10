# Status
_Last updated: 2026-11-11 - A14 done (4B benchmark, hardware recommendation); A15 done (Ollama reply cap)_

Snapshot for starting a session. The full task board, claims and history stay in
[TODO.md](../TODO.md); when the two disagree, TODO.md wins.

## In progress
- (none)

## Next
- A11. More training examples where the model fails.
- A12. Web search via a self-hosted SearXNG in the compose stack.
- A13. Learn from real use (per-turn quality record; design privacy and opt-in first).
- AI accuracy: the model asks for a task key after being told which tool to call.
- AI accuracy: sprint-level column claims are not checked.
- iOS through the web app first.
- Publish the updated 4B model card (`python publish_hf.py --card-only` in the training-data
  repo) when the owner says so.

## Done (recent)
- 2026-11-11: A15 done. Ollama replies capped at 2,048 tokens (`num_predict`); model-check 225/225
  before and after on the 4B. Not committed or released.
- 2026-11-11: A14 done. README "Hardware for the recommended model": 8 GB GPU, 16 GB RAM, 16k
  context minimum. 4B + voice peaks at 6.1 GB, which explains prod. Report in `output/a14/`.
- 2026-11-08: A8 done (trained Qwen3 4B, 465/465 on every suite, public on Hugging Face);
  A10 done; server and mobile alpha.29 released; phone checks passed on the owner's phone.
- 2026-11-07: guards for planner "no title", nudge-as-reminder, creative asks; planner answers
  in code.
- 2026-11-04/05: logo released (alpha.25 / mobile alpha.27); D1 phone bottom bar.

## Known issues / blockers
- Public release: blocked by the owner until features mature and hallucination is under control.
  Do not suggest cutting it.
- On prod (6 GB card beside Whisper and Kokoro) the 4B left chat loading for minutes. A14: it
  needs 6.1 GB there, more than the card has; runaway replies added to it (capped by A15, not
  released yet). Prod stays on the 3B.
- Rename and the "personaOS" wordmark spelling wait for a final product name.
- Live Anthropic smoke test needs a real API key (kept for the public release).
- Docker Desktop must be running for the speech profile on the dev PC.
