# PersonaOS engineering standard

This is the bar every change is held to, by a person or an AI session. It is short on purpose:
each rule is here because breaking it has cost something, and most rules are checked by the
build, an analyzer or a test rather than by memory. Where a rule is enforced, the enforcing check
is named.

How to work in this repository day to day (commands, the task board, sessions) is in
[CLAUDE.md](../CLAUDE.md); what to build and why is in [PRD.md](PRD.md).

## 1. Principles

1. **Correct before clever.** A personal assistant that is wrong about a date, a task or a
   reminder is worse than none. Prefer the boring construction that is obviously right.
2. **Make wrong states unrepresentable, then make the rest loud.** Types, required parameters,
   validation before a change is proposed, and errors that say what to do next.
3. **One way to do each thing.** One port per capability, one place a rule lives, one helper per
   job. Before writing a helper, search for the one that exists.
4. **Fix the cause at the right depth.** A special case layered on shared code is a sign the fix
   is too shallow. Name the mechanism that failed and change it.
5. **Small, reversible changes**, each leaving the build green, the tests green and the docs true.
6. **Measure what you claim.** A performance or accuracy change states its before and after.

## 2. Architecture

```
Api  →  Application  ←  Infrastructure
              ↓
           Domain
```

| Layer | Holds | Must not reference |
|---|---|---|
| Domain | Entities and pure rules | Anything else |
| Application | Use cases, ports (`Common/Interfaces`), tools, guards, prompts | Infrastructure, EF SQLite, any provider SDK, ASP.NET |
| Infrastructure | Adapters: persistence, AI providers, push, speech, storage | Api |
| Api | Controllers (Application interfaces only), composition root | `AppDbContext` |

*Enforced by* `ArchitectureTests` (assembly references and type usage).

- **A new capability is a port in Application and an adapter in Infrastructure.** A vendor SDK
  imported into Application is a design error.
- **Provider-neutral AI.** Everything goes through `IAiMessageStreamer`; each provider is one
  adapter that translates neutral turns, tools and options. A provider quirk (a model that
  rejects a temperature, a server that needs `num_ctx`) is handled in its adapter or the model
  profile, never in `ChatService`.
- **Modules are gated** with `[RequireFeature(InstanceConfig.Modules.X)]` on endpoints and
  `RequiredFeature` on tools.

### Assistant tools

- A tool is a thin wrapper over an Application service, the same one the REST endpoint uses.
- **A tool takes only identifiers the model has been shown**: keys (`TASK-7`, `GOAL-3`,
  `SPRINT-2`) or ids a read tool returned. Never an internal id it has to guess.
- **Optional parameters say when to leave them out.** Small models fill every field they see.
- Read tools state counts and absences **in words** ("none: it is unestimated"), because models
  misread bare numbers and nulls.
- A tool that changes data is validated **before** its card is shown (`ValidateAsync`): a card
  that fails on Confirm is a bug.

### Guards and prompts

- Prompt text lives in `.prompty` fragments (Prompty format, Mustache body), never in C#.
- **Every guard and every prompt rule names the incident it answers** (chat id or date and what
  the model did) in a comment, and has a test that reproduces it.
- A change to prompts, guards, tools or sampling is judged by `scripts/model-check.ps1` with
  several runs, before and after, and the numbers go in the TODO entry.

## 3. C# (.NET)

*Enforced by* `Directory.Build.props`: nullable reference types, warnings as errors, the .NET
analyzers at the `recommended` level, and code style checked in the build against
`.editorconfig`.

- **Names.** Types, methods, properties: `PascalCase`. Locals and parameters: `camelCase`.
  Private fields: `_camelCase`. Async methods end in `Async`. Test names are sentences
  (`A_time_in_the_past_is_refused_before_the_card`).
- **Records** for DTOs and messages; classes with behaviour for services. Primary constructors
  for dependency injection.
- **Async.** Every I/O method is async, takes a `CancellationToken` as its last parameter, and
  passes it on. Never `.Result`, `.Wait()` or `async void` (except event handlers).
- **Time.** Store UTC. Take a `TimeProvider`, never `DateTime.Now`, in anything a test must pin.
  Convert to the user's zone only at the edges (`UserClock`).
- **Errors.** Validation failures are typed exceptions the API maps to 400 with a message a
  person (or a model) can act on. Catch broadly only at a boundary (a tool call, a request), and
  log what was caught. Never swallow silently.
- **Logging.** Structured message templates (`"Model {Model} got {Count} facts wrong"`), never
  interpolated strings. Never log secrets, tokens or the text of a user's messages.
- **Data.** `AsNoTracking` for reads; no query inside a loop over query results; migrations are
  additive and safe on a live install, and a destructive one says so in the TODO entry with the
  backup taken first.
- **Security.** Secrets are encrypted with Data Protection (purpose string never changes), keys
  are write-only through the API, every endpoint has an explicit authorization attribute.
- **Size.** A file past ~500 lines or a method past ~60 has more than one job: split it along
  its seams.

## 4. Dart (Flutter phone app)

*Enforced by* `analysis_options.yaml` (strict casts, inference and raw types, and the lints
listed there) and `flutter analyze` with no issues.

- `lib/api` talks to the server and holds models; `lib/screens` holds screens and the widgets
  only they use; shared pieces (`layout.dart`, `board_widgets.dart`) are imported, not copied.
- **After every `await` in a widget, check `mounted`** before touching `context` or `setState`.
- **Responsive by width, not device**: `Breakpoints`, `ReadableWidth` for lists and forms,
  `showQuickView` for a short look (sheet on a phone, dialog on a tablet). Lay out for a 360 dp
  phone, a phone on its side, and tablets both ways.
- Colours come from the `ColorScheme`, never literals, so dark mode works. Touch targets are at
  least 48 dp.
- Every screen has a loading, an empty and an error state, and the error state offers a retry.
- Widgets a test needs to find get a `Key`.

## 5. TypeScript (Angular web app)

*Enforced by* `tsconfig.json` (`strict`, strict templates) and the production build.

- Standalone components, signals for state, one service per server area. No `any`.
- Components keep `ChangeDetectionStrategy.Eager` until each is checked for plain fields
  assigned after an `await`.
- "Open full page" opens a new tab; quick views are dialogs over the list.

## 6. Testing

| What | Where | How |
|---|---|---|
| Rules and use cases | `PersonaOS.Tests` | Application layer, `TestDbContext` (EF InMemory) and the fakes in `TestSupport`; no mocking library |
| Provider adapters | `PersonaOS.Tests/Ai` | Stub `HttpMessageHandler`: what is sent, how replies are read |
| Architecture | `PersonaOS.Tests/Architecture` | Dependency rule |
| Phone UI | `PersonaOS.Mobile/test` | Widget tests with fake APIs, at phone and tablet sizes |
| Web UI | `*.spec.ts` | Karma |
| Model behaviour | `scripts/model-check.ps1` | Real model, several runs, pass rate |

- **Every bug fix starts with a test that fails for the bug.**
- A UI change is checked by looking at it (a screenshot or a rendered golden), not only by
  passing tests.
- Tests do not depend on today's date or the machine's time zone.

## 7. Review checklist

- [ ] Does it fix the cause, at the right layer, without a special case on shared code?
- [ ] Layer rules, ports and feature gates respected?
- [ ] Inputs validated; errors say what to do; nothing swallowed?
- [ ] Async with cancellation; no blocking; `mounted` checked after awaits?
- [ ] No secrets or personal data in code, logs or commits?
- [ ] Tests for the behaviour, including the failure it fixes?
- [ ] Phone and tablet, light and dark, looked at?
- [ ] For AI changes: suite numbers before and after?
- [ ] TODO.md updated; PRD/README still true?

## 8. Definition of done

Built with zero warnings, all three test suites green, `flutter analyze` clean, the change looked
at where it shows, the TODO entry marked done with what the next session needs to know, and
nothing committed, pushed or deployed unless the owner asked.
