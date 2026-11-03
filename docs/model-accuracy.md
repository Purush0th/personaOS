# PersonaOS — Intent Routing & Reliable Tool Execution

**Type:** Functional requirement / implementation checklist  
**Priority:** High  
**Scope:** ASP.NET Core backend, C#, Ollama/Qwen, existing domain services and database  
**Goal:** Reliable intent recognition and execution with local LLMs, without requiring cloud AI.

## 1. Problem statement

The current assistant can receive a user request, but the LLM sometimes responds as if it completed an action without invoking the appropriate tool.

For example, these requests express the same underlying intent:

- "Add a task to follow up with Ravi tomorrow."
- "Create a to-do for following up with Ravi."
- "Just note this down: follow up with Ravi tomorrow."
- "I need to remember to follow up with Ravi tomorrow."

The assistant should recognize the user's intention to create a task, invoke the appropriate workflow, and report success only after the backend confirms the operation.

**Important:** Do not require users to use predefined phrases. Natural-language variations must be supported.

## 2. Functional requirements

### FR-01 — Intent classification

- [ ] Introduce or verify an intent-classification stage before action execution.
- [ ] Map different natural-language expressions to a canonical intent.
- [ ] Support existing application capabilities, including task creation, goal management, reminders, planner operations, queries, and general conversation.
- [ ] Reuse existing tools and domain services; do not create duplicate business logic.
- [ ] Return a structured result containing the intent, relevant parameters when available, and an explicit unknown or ambiguous outcome.
- [ ] Use the local LLM for semantic understanding where deterministic rules are insufficient.

Example canonical intent:

`CreateTask`

This intent must be recognized whether the user says "add a task," "make a to-do," or "note this down" when the context indicates a task.

### FR-02 — Deterministic backend routing

- [ ] Add a C# intent router that maps recognized intents to approved application workflows.
- [ ] The router, not free-form model-generated prose, determines which workflow is eligible to execute.
- [ ] Use an explicit mapping between canonical intents and existing domain services or tools.
- [ ] Reject unsupported or invalid intent values.
- [ ] Do not silently interpret an unknown intent as a successful action.
- [ ] Preserve the existing authorization, validation, and confirmation requirements.

### FR-03 — Parameter extraction

- [ ] Extract structured fields required by the selected workflow.
- [ ] For task creation, extract the task title, optional description, due date, priority, and any supported goal association.
- [ ] Resolve relative dates such as "tomorrow" using the application's configured time zone.
- [ ] Validate required fields in C#.
- [ ] Ask the user a clarification question when essential information is missing or ambiguous.
- [ ] Never invent missing values simply to complete a tool call.

### FR-04 — Reliable execution

- [ ] Invoke the existing domain service only after the intent and parameters pass validation.
- [ ] Preserve the application's current confirmation rules for write operations.
- [ ] Treat a model-generated tool call as a proposal, not proof that an operation succeeded.
- [ ] Obtain the result from the actual service or database operation.
- [ ] Return the execution result and created entity identifier, where available.
- [ ] Handle validation failures, exceptions, timeouts, and duplicate requests explicitly.

### FR-05 — Truthful responses

- [ ] Generate success messages from the confirmed execution result.
- [ ] Never accept an LLM claim such as "I've created the task" as evidence of completion.
- [ ] If no write occurred, state that nothing was saved.
- [ ] If execution failed, report the failure accurately.
- [ ] If an operation is awaiting user confirmation, clearly indicate that it has not yet been executed.
- [ ] Preserve the existing reply guards and execution receipts.

### FR-06 — Local-first architecture

- [ ] Keep intent routing, validation, authorization, persistence, and execution in the .NET backend.
- [ ] Keep the LLM provider replaceable.
- [ ] Do not introduce a mandatory cloud LLM dependency.
- [ ] Use structured output or constrained decoding where supported by the selected model and runtime.
- [ ] Log classification results, selected routes, validation outcomes, and execution results without unnecessarily recording sensitive personal data.

## 3. Recommended execution flow

1. Receive the user's natural-language message.
2. Load relevant conversation and application context.
3. Classify the request into a canonical intent.
4. Validate the classification and select the corresponding workflow in C#.
5. Extract and validate the workflow parameters.
6. Ask for clarification if required.
7. Prepare the proposed action and obtain confirmation where required.
8. Execute the existing domain service.
9. Verify the operation's actual result.
10. Return a truthful response based on that result.

**Key architectural rule:** Intent classification decides what the user wants. Backend routing decides which workflow can run. The domain service determines what actually happened.

## 4. Acceptance criteria

### AC-01 — Natural-language equivalence

Given the four task-related phrases in FR-01, when the intended action is unambiguous, the system should classify each as `CreateTask` and route it to the same workflow.

### AC-02 — No false success

Given a message requesting a task, if the model claims success but no task-creation operation succeeds, the system must not report that the task was created.

### AC-03 — Successful creation

Given a valid task request, after any required confirmation, the system must invoke the existing task service and report success only when the service confirms creation.

### AC-04 — Ambiguous requests

Given a phrase such as "I need to remember this," if the intended destination is unclear, the system should ask a clarification question instead of arbitrarily creating a task, reminder, or memory.

### AC-05 — No duplicate writes

Repeated model responses or retried requests must not accidentally create duplicate records. Use idempotency or existing duplicate-call protection where appropriate.

### AC-06 — Regression coverage

Existing goal, task, planner, reminder, memory, and query workflows must continue to work. General conversation and brainstorming must not trigger unintended writes.

### AC-07 — Local execution

The full routing and execution workflow must operate using the configured local model without requiring a cloud LLM.

## 5. Implementation checklist

- [ ] Inspect the current chat orchestration and tool-calling pipeline.
- [ ] Identify whether intent classification and deterministic routing already exist.
- [ ] Identify where the model can produce a success response without a successful service result.
- [ ] Reuse existing guards, confirmation cards, tool receipts, and domain services wherever possible.
- [ ] Implement only the missing routing or validation components.
- [ ] Add automated tests for phrase variations, ambiguous requests, hallucinated success, failures, and duplicate calls.
- [ ] Run regression tests against existing assistant capabilities.
- [ ] Document any remaining limitations of the local model.

**Implementation constraint:** Audit the existing code before making changes. This requirement does not authorize a complete rewrite of the assistant or a second implementation of functionality that already exists.

## 6. Definition of done

The requirement is complete when semantically equivalent user requests consistently reach the correct workflow, invalid or ambiguous requests are handled safely, existing functionality remains intact, and the assistant never claims a write succeeded without evidence from the backend.
## 7. Measured results

`scripts/model-check.ps1 -Suite <suite> -Runs 3` against qwen2.5:3b-instruct on Ollama (RTX 3060),
the production model. Each scenario is a fresh conversation run three times; the score is the share
of passing runs. A holdout set is written before any tuning against it, and its first number is the
honest one: once a failure in it has been fixed, it measures the fix, not generalisation.

| Date | Change | Core + extended | Holdout | Holdout2 | Holdout3 |
|---|---|---|---|---|---|
| 2026-11-02 | Baseline (core 97.2%, extended 66.7%) | — | — | — | — |
| 2026-11-02 | Times read in code, titles from the user's words, grounding | 97.0% | 84.6% (first run) | — | — |
| 2026-11-02 | Calendar words, memory guard, reminder cancel router | 96.2% | 89.7% | — | — |
| 2026-11-02 | Calendar request guard, one-day planner reads | 95.5% | 100% | 79.5% (first run) | — |
| 2026-11-02 | Plain bookings and "I've finished TASK-n" routed in code; month names fixed | 97.7% | 97.4% | 94.9% | 75.6% (first run) |
| 2026-11-02 | Day after tomorrow, other-day answers, task edits and sums in code; weekday end-date fix | 99.2% | 94.9% | 100% | 97.8% |
| 2026-11-03 | To-do phrasings routed, reminder text loses its time, questions never classed as "make" (5 runs) | 99.5% | 98.5% | 98.5% | 98.7% |
| 2026-11-03 | Same code, fine-tuned model (first LoRA, 543 examples); not adopted | 93.6% | 92.3% | 95.4% | 93.3% |
| 2026-11-03 | Goal periods in code; a goal with no period is asked about (A9) | 99.1% | 96.9% | 100% | 98.7% |

What the numbers taught, for the next round:

- **Deterministic beats instructed.** Every large gain came from moving a decision out of the model
  (FR-02): dates and times (`WhenPhrase`, `UserTimeGuard`), the target of a move or cancel
  (`TaskCommand`, `ReminderCommand`, `PlannerCommand`), the kind of item (`CalendarRequestGuard`).
  Prompt wording moved the 3B model a few points at most.
- **A fresh holdout finds what tuning hides.** Holdout2 found "I've finished TASK-11" proposing
  complete_sprint, and "Marco" read as the month of March; neither showed in 171 tuned runs.
- **Holdout3 found real bugs, not model weakness.** Its worst failures were code: "the day after
  tomorrow" read as tomorrow by a guard that then refused the right date, and a filler end date that
  stopped a named weekday from being corrected. Arithmetic, "put it on my calendar" after a
  statement and "thanks" proposing a memory are now handled in code.
- **Still weak on the 3B model:** an answer about another day that paraphrases an item's title (so
  the exact-title check misses it), and a question for a missing task title that has no question
  mark. Write holdout4 before working on these.
- **The training capture is a test set too.** Running 233 fresh prompts to build fine-tuning data
  (`scripts/training/`) found three code bugs before any training: invalid JSON in the "waiting for
  confirmation" result, reminders repeating their time, and "add X to my to-do list" made an event.
- **The intent check can block a guard.** "When is my reminder about the bank?" was classed
  create_reminder at temperature 0, so the read-before-answering retry, which waits for a question,
  never ran (holdout2 fell to 92.3%). A plain question is now never classed as making something.
- **The first fine-tune made the model worse, in exactly the gaps of its data.** Goal calls were
  left out of training (A9), and the tuned model stopped proposing goals (0/5 in three scenarios);
  there were no edit examples, and "start TASK-11" moved it to done; notes outnumbered reminders,
  and "don't let me forget" became a note. Low held-out loss said nothing about this. The next
  dataset needs every intent the suites cover, edits and follow-ups included, before training again.

### Other models from Hugging Face (2026-11-03)

The model had only ever been compared with `qwen2.5:0.5b` and `qwen3:4b` (September). Official
small models published since then, pulled from Hugging Face into Ollama as Q4 GGUF
(`ollama pull hf.co/<repo>:<quant>`), run on the same code at 3 runs per scenario. Every local
model is now sent `think: false`; before this only names containing "qwen3" were, and Granite,
MiniCPM and Gemma reason before every reply. "GPU" is what Ollama loads at the 16k context we use;
prod has 6 GB, of which whisper and Kokoro take about 2.

| Model | Core + extended | Holdout | Holdout2 | Holdout3 | All | s/turn | GPU |
|---|---|---|---|---|---|---|---|
| **qwen2.5:3b-instruct** (current) | 99.3% | 97.4% | 100% | 100% | **99.2%** | 2.0 | 2.7 GB |
| Qwen3-4B-Instruct-2507 (unsloth Q4_K_M) | 92.6% | 100% | 100% | 95.6% | 95.3% | 2.6 | 5.1 GB |
| gemma-4-E2B-it (Google QAT q4_0) | 88.1% | 82.1% | 89.7% | 95.6% | 88.8% | 1.8 | 1.8 GB* |
| MiniCPM5-2B (OpenBMB Q4_K_M) | 85.9% | 87.2% | 87.2% | 88.9% | 86.8% | 1.7 | 2.3 GB |
| granite-4.2-3b (IBM Q4_K_M) | 88.1% | 71.8% | 84.6% | 86.7% | 84.5% | 3.3 | 3.6 GB |

\* Gemma's file is 3.35 GB with its vision and audio parts; Ollama loads 1.8 GB for text.

Not tested: gemma-4-E4B and Nemotron-3.5-Bolt-E4B are about 5 GB at Q4, too big for prod beside
voice; community "distill" and "uncensored" repacks were left out.

- **qwen2.5:3b stays.** Nothing small beat it, and part of that is honest: the code and the core
  suite grew around it, so a new model loses points the guards were written for.
- **Qwen3-4B-Instruct-2507 is the one to watch.** It is the only model that generalised as well
  (100% on two holdouts it never saw), but its misses are systematic, not noise: it saved
  "write a two-line poem" as a note in 3 of 3 runs, said it did not know a task's points it had
  read, asked for points before proposing a task, and twice returned an empty reply for a yearly
  goal. It also needs 5.1 GB at a 16k context, which does not fit prod with voice. Worth trying
  again if prod gets a bigger card or voice moves off the GPU.
- **The 2B models are fast but not accurate enough**: Gemma 4 E2B and MiniCPM5 answer in under
  2 s but miss one turn in eight to ten.
