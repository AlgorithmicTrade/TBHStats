# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository layout

The actual project lives in the **`claude-code-orchestrator-kit/`** subdirectory — that folder is the git repository (the `TBHStats` parent dir is not under version control). Run all git, npm, and release commands from inside `claude-code-orchestrator-kit/`.

## What this is

`claude-code-orchestrator-kit` is an **npm package that distributes Claude Code assets** — it is not a runnable application. Installing it copies a `.claude/` tree (agents, skills, commands, schemas, scripts), MCP configs, and docs into a consumer's project. The JS (`index.js`, `postinstall.js`) only exposes version/path helpers and prints a setup banner; there is no build step and no runtime server. `type: module` (ESM), Node >= 18.

### Two CLAUDE.md files — keep their roles distinct

- **This file** (repo root) — for maintainers *developing the kit itself* (editing agents/skills/commands, releasing the package).
- **`claude-code-orchestrator-kit/CLAUDE.md`** — the *consumer behavior contract* shipped to end users. It tells Claude how to act as an orchestrator (delegate-and-verify, "landing the plane" push workflow, Beads). Edit it only when changing how installed kits should *behave*, not to document this repo.

## Commands

Run from `claude-code-orchestrator-kit/`:

```bash
npm test                       # placeholder — echoes "No tests yet", exits 0 (no test suite exists)
npm run postinstall            # validate install integrity + print setup banner
npm run setup                  # bash switch-mcp.sh — pick an MCP configuration

# Release (conventional-commit driven, auto version bump + dual changelog):
./.claude/scripts/release.sh [patch|minor|major]   # omit type → auto-detect from commits
./.claude/scripts/release.sh patch --yes           # skip confirmation (automation)
./.claude/scripts/release.sh --message "..."       # custom message for auto-committed changes
```

`release.sh` auto-syncs `package.json` to the latest git tag, derives the bump from commit types (`feat`→minor, `fix`/`security`→patch, `type!`→major), writes both `CHANGELOG.md` (Keep a Changelog) and user-facing `RELEASE_NOTES.md`, tags `vX.Y.Z`, and supports rollback via file backups. The `/push` slash command wraps this for in-session releases.

Custom quality gates live in `.claude/scripts/gates/` (`check-security.sh`, `check-coverage.sh`, `check-bundle-size.sh`) — standalone shell scripts run by health workflows; `check-security.sh` blocks on high/critical `npm audit` findings.

## Architecture — the asset taxonomy

Everything the kit delivers is under `.claude/`:

- **`agents/{domain}/workers/*.md`** — specialized sub-agents grouped by domain (`health`, `database`, `frontend`, `infrastructure`, `testing`, `research`, `meta`, …). Each is a markdown file with frontmatter (name, description, tools). Invoked via the Task tool with `subagent_type`.
- **`skills/{name}/SKILL.md`** — reusable, stateless utilities (validation, parsing, reporting, senior-expertise playbooks). Loaded via the Skill tool.
- **`commands/*.md`** — slash commands. Three families: `health-*` (automated workflows), `speckit.*` (spec-driven feature dev), and standalone (`push`, `worktree`, `process-logs`, `beads-init`, `ultra-think`, `translate-doc`, `supabase-performance-optimizer`).
- **`schemas/*.schema.json`** — JSON Schemas validating the plan files that orchestrators hand to workers.
- **`docs/Agents Ecosystem/`** — the authoritative architecture spec. Read `AGENT-ORCHESTRATION.md` before touching the orchestration model.

### Two orchestration modes (the core mental model)

1. **Main-session orchestration (~95%)** — the default for feature work. The main Claude session *is* the orchestrator: gather full context → delegate one atomic task per agent (Task tool) → **always verify** results (read files, type-check) → accept/reject loop → commit. Atomicity rule: **1 task = 1 agent invocation**; launch parallel tasks as N Task calls in a single message.

2. **Health-workflow orchestration (~5%)** — `/health-bugs`, `/health-security`, `/health-cleanup`, `/health-deps`, `/health-reuse`. Here agents communicate through **plan files** instead of the Task tool, because orchestrator agents run in isolated context *without* Task access:

   ```
   slash command (main orchestrator)
     → orchestrator agent  → writes .tmp/current/plans/{workflow}-{phase}.json, returns control
     → command reads plan, invokes worker (nextAgent) via Task
     → worker reads plan, executes, writes report to .tmp/current/reports/, logs changes for rollback
     → orchestrator agent validates at quality gate → next plan, or stop (max ~3 iterations)
   ```

   Workers must read the plan first, log every file change to `.tmp/current/changes/` (for `rollback-changes`), self-validate, and emit a standard report (`docs/Agents Ecosystem/REPORT-TEMPLATE-STANDARD.md`). Reports archive to `docs/reports/{domain}/{YYYY-MM}/`; `.tmp/` is git-ignored and auto-pruned after 7 days.

   **Key constraint when editing health agents:** orchestrator agents must NOT use the Task tool and must NOT do implementation work — only plan-file coordination and gate validation.

### Spec-driven development (SpecKit)

`speckit.*` commands + `.specify/` templates implement a spec → plan → tasks → implement pipeline. `speckit.implement` runs a planning phase first: classify each task PARALLEL/SEQUENTIAL, annotate `[EXECUTOR: agent-name]`, and create missing agents via `meta-agent-v3` (one meta-agent run per agent). `speckit.tobeads` exports tasks into Beads.

### Issue tracking (Beads)

Optional, git-backed (`bd` CLI). `.beads-templates/` holds the config/formulas/PRIME template seeded by `/beads-init`. The consumer CLAUDE.md mandates `bd sync` + `git push` as part of session completion.

## Conventions for changing kit assets

- **New agent** → create via `meta-agent-v3` (don't hand-author frontmatter); place under the correct `agents/{domain}/workers/`.
- **New skill** → `skills/{name}/SKILL.md`; keep it stateless and reusable. Use `skill-builder-v2` for the format.
- **New command** → `commands/{name}.md`; follow the existing health/speckit structure and reference plan-file/quality-gate skills rather than re-implementing them.
- Counts referenced in `README.md` / `package.json` description (agents/skills/commands) are maintained by hand — update them when adding or removing assets.
- MCP servers are defined in `.mcp.json` (context7, sequential-thinking, supabase, playwright, shadcn, serena); Supabase uses `${SUPABASE_PROJECT_REF}` / `${SUPABASE_ACCESS_TOKEN}` env vars. `npm run setup` switches between configs.

## Active Technologies
- C# 12 / .NET 8 (LTS) (001-tbh-stats-helper)
- локальный файл SQLite (`%LOCALAPPDATA%\TBHStats\tbhstats.db`) (001-tbh-stats-helper)

## Recent Changes
- 001-tbh-stats-helper: Added C# 12 / .NET 8 (LTS)
