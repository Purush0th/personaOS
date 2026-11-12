# Status
_Last updated: 2026-11-12 - B4 phone scope-change fix released as mobile alpha.30_

Snapshot for starting a session. The full task board, claims and history stay in
[TODO.md](../TODO.md); when the two disagree, TODO.md wins.

## In progress
- Nothing.

## Next
- A11. App bug first: the false-claim check does not cover a tool that returned an error
  ("GOAL-10 was created" after a refusal). See `Ai/ActionClaimDetector.cs` and `ClaimCheckGuard`
  in `Ai/Guards/ReplyGuards.cs`; add a failing test, fix, then `model-check.ps1 -Runs 5`. Then the training examples in TODO.md A11.
- Ship A15 to prod with the next server release (`v*` tag, owner's call).
- Local servers on the OpenAI-compatible adapter (`/v1`) still get a 16,000-token reply cap; a cut
  reply shows no "stopped at the limit" note in any adapter. Neither is filed in TODO.md yet.
- A12. Web search via a self-hosted SearXNG in the compose stack.
- A16. Intake-only MCP facade: Claude researches, files goals/tasks into a confirm-first Inbox;
  no read tools. Design the proposal store first (cross-area). See `output/mcp-facade/`.
- A13. Learn from real use (per-turn quality record; design privacy and opt-in first).
- AI accuracy: the model asks for a task key after being told which tool to call.
- AI accuracy: sprint-level column claims are not checked.
- iOS through the web app first.

## Done (recent)
- 2026-11-12: B4 fixed. The phone dropped the server's error `code`, so adding or moving a task
  into the running sprint failed instead of asking "Scope change". Checked live on web and the
  emulator against a throwaway QA instance; released as `mobile-v0.1.0-alpha.30`.
- 2026-11-12: `/agent-router` refresh done: agent files, secret-file `permissions.deny` rules in
  `.claude/settings.local.json` (`.env`, `dp-keys/`, key files), CLAUDE.md marker section,
  `output/logs/`; validation passed. CLAUDE.md change uncommitted.
- 2026-11-11: all git history (171 commits, 43 tags, `dev` and `main`) rewritten to the GitHub
  noreply email and force-pushed; release workflows were off during the push, no releases rebuilt.
  Pre-rewrite mirror at `D:\Projects\personaOS-history-backup-20261111.git`. Other clones must be
  re-cloned or hard-reset to origin.
- 2026-11-11: A16 assessed: MCP facade for external AI clients, propose-only tools into a
  confirm-first Inbox, no read tools. Filed in TODO.md; decision logged. Not started.
- 2026-11-11: A15 done. Ollama replies capped at 2,048 tokens (`num_predict`); model-check 225/225
  before and after on the 4B. Pushed to dev, not released. 4B card with the A14 numbers
  published on Hugging Face.
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
- `output/` is gitignored: the A14 benchmark script (`output/a14/bench.mjs`) and the A16
  assessment (`output/mcp-facade/assessment.md`) exist only on this machine.
