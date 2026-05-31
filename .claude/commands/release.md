---
description: "Релиз TBHStats (.NET): bump <Version> в Directory.Build.props, генерация CHANGELOG/RELEASE_NOTES из conventional-commits, commit + аннотированный тег + push"
argument-hint: "[patch|minor|major] [-Version x.y.z] [-DryRun] [-NoPush]"
---

# /release — релиз .NET-проекта TBHStats

PowerShell-аналог npm-команды `/push` из orchestrator-kit, но под .NET:
источник версии — `<Version>` в `Directory.Build.props`, без `package.json`/Node/bash.

**Что делает:**
- Определяет тип бампа из conventional-commits (`feat` → minor, `fix`/`security` → patch, `type!` / `BREAKING CHANGE` → major) либо берёт явный аргумент.
- Генерирует секцию `CHANGELOG.md` (Keep a Changelog) и `RELEASE_NOTES.md` (user-facing) из коммитов с прошлого тега.
- Бампает `<Version>` в `Directory.Build.props`.
- Создаёт `chore(release): vX.Y.Z`, аннотированный тег `vX.Y.Z`, пушит `--follow-tags`.
- Полный rollback при ошибке (бэкапы файлов, удаление тега, `reset --soft`).
- Первый релиз (нет тегов) = `0.1.0` (параметр `-FirstVersion`).

**Аргументы:** `$ARGUMENTS`
- `patch|minor|major` — принудительный тип бампа (иначе авто-детект).
- `-Version x.y.z` — явная версия.
- `-DryRun` — только превью, без изменений.
- `-NoPush` — commit+tag локально, без push.

**Выполнить из корня репозитория:**

```powershell
pwsh -NoProfile -File scripts/release.ps1 $ARGUMENTS -Yes
```

> Запускать только из корня репозитория TBHStats (где лежит `TBHStats.sln` и `Directory.Build.props`). Скрипт сам проверяет ветку, remote, наличие коммитов и существование тега.
