# Claude Config Source of Truth (kit isolation, TBHStats)

> Фиксирует, откуда резолвятся skills/agents/commands/rules и speckit-конфиг, и почему `claude-code-orchestrator-kit/` НЕ является боевым источником.

## When to apply

При любой работе с Claude-конфигурацией: чтение/правка skills, agents, commands, rules; поиск «где лежит агент X / скилл Y / команда Z»; вопросы про health-воркеры, `/health-*`, `/record-metrics`, `/health-metrics`, `/speckit.*`, `/release`, наши .NET-агенты. Срабатывает по intent, не по literal-фразе.

## Раскладка TBHStats (verified)

- **project `.claude/`** (репозиторий TBHStats, отслеживается git): только `commands/` (сейчас один проектный `/release` — .NET-релиз), `rules/` (это правило, `architecture.md`, `docs-navigation.md`), `settings.json`/`settings.local.json`. **Project-level `agents/` и `skills/` ОТСУТСТВУЮТ** — они резолвятся из user `~/.claude/`.
- **user `~/.claude/`** (`C:\Users\Uchetko\.claude\`): боевые agents/skills/commands/rules для всех проектов.
- **root `.specify/`** (отслеживается git: `constitution.md`, `scripts/`, `templates/`): **активный** источник speckit для TBHStats. `/speckit.*` и `check-prerequisites.sh` работают именно отсюда.
- **`claude-code-orchestrator-kit/`**: вложенный vendored-клон (отдельный репозиторий `maslennikov-ig/claude-code-orchestrator-kit`), **полностью gitignored** (`.gitignore` → `claude-code-orchestrator-kit/`). В рантайм-резолв НЕ входит ничем.

## Rule

- Боевые (runtime) **skills / agents** резолвятся ТОЛЬКО из user `~/.claude/`:
  - health-воркеры → `~/.claude/agents/health/workers/*.md` (bug-hunter, bug-fixer, dead-code-hunter, …);
  - health-inline скиллы → `~/.claude/skills/*-health-inline/` (cleanup/deps/reuse/security);
  - **проектные .NET-агенты TBHStats** → `~/.claude/agents/development/workers/{dotnet-winui-developer,efcore-sqlite-specialist,windows-capture-ocr-specialist}.md` и `~/.claude/agents/testing/workers/{dotnet-test-writer,dotnet-uiautomation-specialist}.md` (созданы в Phase 0 через `/create`).
- Боевые **commands** резолвятся из `~/.claude/commands/` (`/health-*`, `/record-metrics`, `/health-metrics`, `/speckit.*`, …) + project `.claude/commands/`. Project `.claude/commands/` содержит `release.md`, `push.md` (override kit-`/push`) и `commit.md` (override user-`/commit`) — все ведут на `scripts/release.ps1`; kit-bash `release.sh` для TBHStats НЕ применяется (его в проекте нет). Цель override-ов `push.md`/`commit.md` — перехватить вызовы `/push` и `/commit` по голому имени, чтобы релиз шёл через .NET-путь, а не подхватывался kit-bash `release.sh`.
- Боевые **rules** — project `.claude/rules/` + `~/.claude/rules/`.
- Боевой **speckit** (spec/plan/tasks/constitution, шаблоны, скрипты) — **root `.specify/`** (НЕ kit). Изменение требований/принципов — через `/speckit.*` + Amendment Procedure конституции, а не ручной правкой.
- `claude-code-orchestrator-kit/{.claude,.specify,…}` — **мёртвый upstream-референс** (vendored, gitignored, ведёт свою историю в отдельном репо). В рантайм-резолв НЕ входит; версии устарели относительно боевых `~/.claude/` и root `.specify/`.
  > Отличие от FundingBot: там из kit использовался `.specify/`. В **TBHStats `.specify/` лежит в корне репозитория** и самодостаточен — из kit при работе над TBHStats НЕ используется НИЧЕГО.
- ЗАПРЕЩЕНО: читать/править/цитировать `claude-code-orchestrator-kit/**` как источник истины; делать выводы о поведении health-воркеров/скиллов/команд/speckit по kit-копиям. Правки конфигурации — ТОЛЬКО в боевые `~/.claude/`, project `.claude/`, либо root `.specify/` (для speckit, документированным путём).
- При расхождении kit ↔ боевая копия приоритет у боевой; kit игнорируется (или поднимается отдельным upstream-PR, если явно требуется).

## Anti-examples (что НЕ делать)

- ❌ `grep -r claude-code-orchestrator-kit/.claude/agents` чтобы «понять, как работает security-scanner» — устаревшая мёртвая копия.
- ❌ `Edit claude-code-orchestrator-kit/.claude/skills/*/SKILL.md` для изменения поведения `/health-*` — нулевой рантайм-эффект.
- ❌ Искать `dotnet-winui-developer` в project `.claude/agents/` — его там нет; боевой агент в `~/.claude/agents/development/workers/`.
- ❌ Править `claude-code-orchestrator-kit/.specify/memory/constitution.md` — боевая конституция в **root** `.specify/memory/constitution.md`.
- ❌ Делать .NET-релиз TBHStats через kit-`/push` (bash, conventional-commits npm) — для .NET используется `/release` (`scripts/release.ps1`, bump `<Version>` в `Directory.Build.props`).
- ❌ Запускать `bash .claude/scripts/release.sh` или kit-`/push` для TBHStats — для .NET релиз только через `scripts/release.ps1` (обёртки: project `/commit`/`/push`/`/release`).

## TL;DR

skills/agents → только `~/.claude/` (project-level в TBHStats нет). commands → `~/.claude/` + project `.claude/commands/` = `/release` + override `/push` + override `/commit` (все → `release.ps1`). rules → project `.claude/rules/` + `~/.claude/`. speckit → **root `.specify/`**. `claude-code-orchestrator-kit/` — полностью мёртвый gitignored-дубль, не трогать и не цитировать; из него при работе над TBHStats не используется ничего.
