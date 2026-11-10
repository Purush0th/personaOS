# Status
_Last updated: 2026-11-11 - project agents set up; A14 (4B benchmark) briefed, not started_

Snapshot for starting a session. The full task board, claims and history stay in
[TODO.md](../TODO.md); when the two disagree, TODO.md wins.

## In progress
- A14. Benchmark `personaos-qwen3:4b` against `personaos-qwen2.5:3b` on the dev PC (RTX 3060
  12 GB) with Whisper and Kokoro loaded: time to first token, per-turn time and VRAM at 8k / 16k /
  32k context. Then write the recommended GPU (8 GB minimum) and RAM into the README and the
  4B model card. Not claimed in TODO.md yet.

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
- 2026-11-08: A8 done (trained Qwen3 4B, 465/465 on every suite, public on Hugging Face);
  A10 done; server and mobile alpha.29 released; phone checks passed on the owner's phone.
- 2026-11-07: guards for planner "no title", nudge-as-reminder, creative asks; planner answers
  in code.
- 2026-11-04/05: logo released (alpha.25 / mobile alpha.27); D1 phone bottom bar.

## Known issues / blockers
- Public release: blocked by the owner until features mature and hallucination is under control.
  Do not suggest cutting it.
- On prod (6 GB card beside Whisper and Kokoro) the 4B left chat loading for minutes; cause not
  found. Prod stays on the 3B.
- README says the 4B "fits a 6 GB GPU"; the owner's recommendation is 8 GB or more (fixed by A14).
- Rename and the "personaOS" wordmark spelling wait for a final product name.
- Live Anthropic smoke test needs a real API key (kept for the public release).
- Docker Desktop must be running for the speech profile on the dev PC.
