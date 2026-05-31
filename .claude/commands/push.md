---
description: "Release TBHStats (.NET) — project-override kit-команды /push: bump <Version> в Directory.Build.props, CHANGELOG/RELEASE_NOTES из conventional-commits, commit + аннотированный тег + push"
argument-hint: "[patch|minor|major] [-Version x.y.z] [-DryRun] [-NoPush]"
---

# /push — project-override для TBHStats (.NET)

> Этот файл **переопределяет** kit/user-команду `/push` (bash/npm-релиз) на действия `/release` (.NET).
> Для TBHStats нет корневого `package.json`; релиз идёт через PowerShell-скрипт `scripts/release.ps1`,
> источник версии — `<Version>` в `Directory.Build.props`.
> Сделано, чтобы `/commit` (Шаг 2 которого вызывает `/push`) работал корректно на .NET-проекте,
> не меняя порядок выполнения команд. См. `.claude/rules/claude-config-source-of-truth.md`.

**Действия (идентичны `/release`):**
- Определяет тип бампа из conventional-commits (`feat` → minor, `fix`/`security` → patch, `type!` / `BREAKING CHANGE` → major) либо берёт явный аргумент.
- Генерирует секцию `CHANGELOG.md` (Keep a Changelog) и `RELEASE_NOTES.md` (user-facing) из коммитов с прошлого тега.
- Бампает `<Version>` в `Directory.Build.props`.
- `git add -A`, создаёт `chore(release): vX.Y.Z`, аннотированный тег `vX.Y.Z`, пушит `--follow-tags`.
- Полный rollback при ошибке. Первый релиз (нет тегов) = `0.1.0`.

**Аргументы:** `$ARGUMENTS` — `patch|minor|major`, `-Version x.y.z`, `-DryRun`, `-NoPush`.

**Выполнить из корня репозитория TBHStats:**

```powershell
pwsh -NoProfile -File scripts/release.ps1 $ARGUMENTS -Yes
```

> Запускать только из корня (где `TBHStats.sln` и `Directory.Build.props`). Скрипт сам проверяет ветку, remote, наличие коммитов и существование тега. Описание изменений для CHANGELOG/RELEASE_NOTES формируется из conventional-commits — поэтому содержательные изменения должны быть оформлены отдельными `feat`/`fix`-коммитами до релиза (Шаг 1 команды `/commit` даёт такой commit message).
