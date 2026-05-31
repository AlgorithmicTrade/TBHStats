<!--
Sync Impact Report
==================
Version Change: 2.0.0 → 2.1.0 → 2.2.0
Date: 2026-05-31
v2.2.0: Security/Compliance — added carve-out allowing a SEPARATE QA UI-test harness to inject human-like input into the running game for interface auditing (FR-022…FR-027); shipped product remains strictly observe-only.
Modified Principles: V. Strict Type Safety — re-targeted from TypeScript to C#/.NET (nullable refs, no `dynamic`, analyzers clean)
Concretized Sections: Technology Standards (filled placeholders → .NET 8 / WinUI 3 / WGC / Windows.Media.Ocr / SQLite+EF Core), Accessibility (RECOMMENDED, desktop widget), Security (local single-user, observe-only, no PII), File Organization (src/ .NET solution layout)
Project: TBHStats — desktop stats helper for Task Bar Hero (feature 001-tbh-stats-helper)

Templates Requiring Updates:
- plan-template.md - Constitution Check evaluated in specs/001-tbh-stats-helper/plan.md
- spec-template.md - No change
- tasks-template.md - No change
- CLAUDE.md (kit consumer contract) - Not affected (operational, language-agnostic)

Follow-up TODOs: none (placeholders resolved for TBHStats)
-->

# TBHStats Constitution

> **Authority**: This constitution supersedes all other development practices. Runtime guidance in `CLAUDE.md` implements these principles operationally.

## Core Principles

### I. Context-First Development (NON-NEGOTIABLE)

Before any implementation or delegation, gather comprehensive context:
- Read existing code in related files
- Search for similar patterns and implementations
- Review specs, ADRs, and recent commits
- Understand dependencies and integration points

**Rationale**: Prevents duplicate work, ensures consistency, avoids conflicting approaches.

### II. Single Source of Truth

Types, constants, enums, schemas, and shared logic MUST be defined in designated central locations (e.g., `{{SHARED_TYPES_PATH}}`). Duplication is forbidden — consumers must import from the source.

**Rationale**: Eliminates drift between duplicate definitions, simplifies refactoring.

### III. Library-First Development

Before implementing custom solutions (>20 lines):
1. Search for existing libraries (npm, PyPI, etc.)
2. Evaluate: maintenance status, security, bundle size, TypeScript support
3. Use library if it covers >70% of requirements

**Context7 Rule**: Before writing code that uses ANY library, MUST fetch up-to-date documentation via Context7. This ensures correct API usage and avoids deprecated patterns. For Windows/WinRT APIs (Windows.Graphics.Capture, Windows.Media.Ocr) consult current Microsoft Learn docs.

**Rationale**: Reduces maintenance burden, leverages community standards and security fixes. Prefer built-in platform APIs (WinRT) over third-party deps where they cover the need.

### IV. Code Reuse & DRY

Before creating new components, utilities, or logic:
1. Search existing codebase for reusable implementations
2. Prefer adapting and extending over duplicating
3. Document why reuse was not possible if creating new

**Rationale**: Reduces codebase size, ensures consistent behavior, lowers cognitive load.

### V. Strict Type Safety (NON-NEGOTIABLE)

- C# **nullable reference types** enabled (`<Nullable>enable</Nullable>`) solution-wide
- `dynamic` is prohibited — use proper types or generics; avoid `object` where a concrete type fits
- Warnings-as-errors for nullability (`CS86xx`) on Core/Domain projects; explicit return types (no implicit `var` for public API surface)
- `dotnet build` with analyzers must pass clean before commit
- DTOs/contracts validated explicitly (e.g. data annotations / guard clauses); no silent coercion

**Rationale**: Catches errors at compile time, enables safe refactoring, improves IDE support. (Stack adapted from TypeScript to C#/.NET on 2026-05-31 — see Technology Standards.)

### VI. Atomic Task Execution

Each task must be independently completable, testable, and committable:
- Mark task `in_progress` before starting
- Verify implementation (read files + run checks)
- Mark `completed` after validation only
- Commit after EACH task, not in batches

**Atomic Delegation Rule**: One agent invocation = one task. Never batch multiple tasks into a single agent call.
- Parallel work: Launch N agents in single message, each with exactly one task
- Sequential work: Complete one agent call, then start next
- Same agent type can run multiple times in parallel — each instance handles one task

**Rationale**: Enables easy rollback, clear progress tracking, better code review. Atomic delegation ensures predictable agent behavior and simplifies error handling.

### VII. Quality Gates (NON-NEGOTIABLE)

Before any commit:
- [ ] Type-check passes
- [ ] Build succeeds
- [ ] No hardcoded credentials
- [ ] No `TODO` without issue reference

**Rationale**: Prevents broken code in main branch, maintains codebase health.

### VIII. Progressive Specification

Features progress through mandatory phases:
1. **Spec** → User stories and requirements
2. **Plan** → Technical approach and decisions
3. **Tasks** → Atomic, ordered implementation steps
4. **Implement** → Execute with validation

No phase can be skipped. Each output validated before proceeding.

**Rationale**: Reduces rework, validates approach before expensive implementation.

## Operational Excellence

### IX. Error Handling

- All errors must be typed (custom Error classes or union types)
- User-facing errors: localized, actionable, no stack traces
- Internal errors: structured logging with context
- Never swallow errors silently

### X. Observability

- Structured logging (JSON) in production
- Correlation IDs for distributed operations
- Performance metrics for critical paths
- Audit logs for sensitive operations

### XI. Accessibility (RECOMMENDED)

<!-- Desktop overlay widget: full WCAG not strictly applicable, but usability is -->
- Keyboard operability for primary actions (open compare, switch optimization target)
- Sufficient color contrast for live metrics over varied desktop backgrounds
- Respect Windows Light/Dark theme; verify widget readability in both
- Do not rely on color alone to distinguish chest types — use labels/icons too

## Security Requirements

### Data Protection

- No hardcoded credentials or secrets in source
- Authentication: **N/A for v1** — single local user, no accounts. When the remote (Android) feature lands, the local↔remote channel MUST be authenticated (paired token) and bound to localhost/LAN by default.
- Data isolation: all captured data is local-only (per-machine), no external transmission until remote access is explicitly enabled by the user
- Input validation: validate OCR-parsed values (range/sanity checks, confidence threshold) before persisting — never trust a raw recognition result
- No telemetry/data leaves the machine without explicit user opt-in

### Compliance

- No regulated/PII data is processed (gameplay metrics only). No GDPR/PCI scope in v1.
- **Shipped product is observe-only**: must not automate or modify the game; no memory writes, no input injection in the delivered runtime.
- **Carve-out for QA UI-test harness** (added 2026-05-31, with user consent): a SEPARATE, developer-only test harness MAY inject input (clicks/navigation) into the running game strictly for automated interface auditing (FR-022…FR-027). It MUST: be excluded from the shipped product; run only in an explicit test mode; emulate human-like input (random point within element bounds, randomized bounded delays); and AVOID any action that irreversibly mutates game state (Runes upgrades, Cube craft/recycle, Stash moves, Trade ship). This carve-out applies ONLY to the test harness, never to the product runtime.

## Technology Standards

### Core Stack

| Layer | Technology |
|-------|------------|
| Language | C# 12 / .NET 8 |
| App Framework | WinUI 3 (Windows App SDK) desktop widget — WPF fallback for overlay shell if transparency/topmost needs it |
| Window Capture | Windows.Graphics.Capture (HWND target, occlusion-tolerant) via CsWinRT |
| OCR | Windows.Media.Ocr (built-in); Tesseract only as fallback if accuracy insufficient |
| Database | SQLite via EF Core (local file) |
| Charts | LiveCharts2 or ScottPlot (decided in research.md) |
| Auth | N/A v1 (local single-user); paired token for future remote |
| Hosting | Local desktop app; future remote = local ASP.NET minimal API on LAN |
| Future mobile | .NET MAUI (Android), reusing Core/Domain projects |

### File Organization (source)

```
src/
├── TBHStats.Core/        # Domain models, optimization logic, game-mechanics config (no UI/WinRT)
├── TBHStats.Capture/     # WGC capture + OCR + ROI mapping (Windows-only)
├── TBHStats.Data/        # EF Core, SQLite persistence, repositories
├── TBHStats.App/         # WinUI 3 widget shell, viewmodels, charts
└── TBHStats.Remote/      # (future) local API exposing stats to Android client
tests/
├── TBHStats.Core.Tests/
├── TBHStats.Capture.Tests/
├── TBHStats.Data.Tests/
└── TBHStats.UiTests/        # QA E2E против живой игры (FlaUI + OCR + SendInput); not shipped; see Security carve-out
```

Kit assets (`.claude/`, `.specify/`, `docs/reports/`, `.tmp/current/`) remain as provided by the orchestrator kit.

## Governance

### Amendment Procedure

Constitution changes require:
1. Documented rationale
2. Impact analysis on templates/workflows
3. Version bump (MAJOR: breaking, MINOR: additive, PATCH: clarification)
4. Sync Impact Report in header
5. Update dependent documentation

### Exception Process

Principle violations require justification in plan.md:
- Why violation is necessary
- Alternatives considered and rejected
- Mitigation strategies

### Document Hierarchy

1. **Constitution** (this file) — Principles and laws
2. **CLAUDE.md** — Operational procedures implementing principles
3. **Spec/Plan/Tasks** — Feature-specific guidance

---

**Version**: 2.2.0 | **Ratified**: 2026-05-31 | **Last Amended**: 2026-05-31
