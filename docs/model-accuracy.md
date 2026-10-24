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