# Contributing to PersonaOS

Thanks for taking a look. PersonaOS is a single-user, self-hosted assistant — that constraint
shapes most decisions here, so please read the two ground rules before opening a PR.

## Ground rules

**1. The brand is fixed; the assistant's name is not.**
"PersonaOS" is the product name everywhere — namespaces, images, repo, docs. The *only*
per-install personalization is the assistant's **nickname** and its **persona/tone**, both
stored in the singleton `InstanceConfig` row. No per-install theming, and never hardcode
anything user-specific.

**2. Tools are thin wrappers over domain services.**
The assistant's tools call the same services the REST endpoints do — they don't reimplement
logic. Tool use is **native and provider-neutral, not MCP** (the app is the only consumer): every
provider is an adapter behind `IAiMessageStreamer`. Adding a tool means adding an
`IPersonaTool`, not a new code path.

The full engineering standard (layers, C#, Dart and TypeScript rules, testing, the review
checklist and the definition of done) is [docs/ENGINEERING.md](docs/ENGINEERING.md).

## Architecture

The backend is clean architecture, and the dependency rule is enforced by project references
and by `ArchitectureTests`:

```
Api  →  Application  ←  Infrastructure
              ↓
           Domain
```

- **Domain** — entities and pure rules. No dependencies at all.
- **Application** — use cases plus *ports* (`IAppDbContext`, `IAiMessageStreamer`,
  `IPushSender`, `IDocumentStorage`, …). No EF SQLite provider, no provider SDK, no ASP.NET.
- **Infrastructure** — adapters implementing those ports. `AnthropicMessageStreamer` is the
  only file that touches the Anthropic SDK.
- **Api** — controllers (Application interfaces only, never `AppDbContext`) and the
  composition root.

**Adding a capability = a port in Application + an adapter in Infrastructure.** If you find
yourself importing a vendor SDK into Application, something has gone wrong.

Full conventions, build commands, and gotchas live in [CLAUDE.md](CLAUDE.md).

## Before you open a PR

```bash
dotnet build PersonaOS.slnx                 # warnings are errors (src/backend/Directory.Build.props)
dotnet test src/backend/PersonaOS.Tests     # must be green
```

For the phone app, `flutter analyze` must report no issues and `flutter test` must pass; for
the web app, `npx ng build` and `npx ng test --watch=false --browsers=ChromeHeadless`. A change
to prompts, guards, tools or sampling also runs `scripts/model-check.ps1` before and after.

- **Keep the build at zero warnings.** If a transitive package trips a CVE analyzer, pin a
  patched version rather than suppressing it.
- **Add tests for logic that can silently misbehave** — schedulers, retries, permission gates,
  anything time- or timezone-dependent. Tests live at the Application layer and use the
  scriptable fakes in `PersonaOS.Tests/TestSupport`; extend those rather than adding a mocking
  library. If your code depends on the clock, take a `TimeProvider` so it can be tested.
- **Gate new modules** with `[RequireFeature(InstanceConfig.Modules.X)]` and make the tool
  respect the same toggle.
- **Never log or commit secrets.** The Anthropic key is encrypted at rest; keep it that way.
  Don't change the Data Protection purpose string (`PersonaOS.AnthropicApiKey.v1`) — existing
  installs' ciphertext is bound to it.

## Schema changes

Migrations are applied automatically on startup, so they must be safe on a live install:

```bash
dotnet dotnet-ef migrations add <Name> \
  --project src/backend/PersonaOS.Infrastructure \
  --startup-project src/backend/PersonaOS.Infrastructure \
  -o Persistence/Migrations
```

Prefer additive changes. If you must remove or repurpose a column, say so explicitly in the PR
so upgraders aren't surprised.

## Scope

Some things are deliberately **out of scope** for v1 — RAG over documents, an MCP façade,
wake-word voice, multi-user/multi-tenant, and any licensing or activation gating. If you want
one of them, open an issue to discuss it before building.

## Reporting a security issue

Please don't open a public issue for a vulnerability. Open a private security advisory on the
repository instead.
