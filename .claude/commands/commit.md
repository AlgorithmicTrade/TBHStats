---
description: "Commit & Release для TBHStats (.NET): Шаг 1 — commit message; Шаг 2 — релиз через scripts/release.ps1. Project-override user-/commit (НЕ kit-bash release.sh, НЕ /push)."
argument-hint: "[patch|minor|major]"
allowed-tools: Bash, Read
---

# Commit & Release — TBHStats (.NET) — СТРОГО ПОСЛЕДОВАТЕЛЬНО

> **Project-override** одноимённой user-команды `/commit`. TBHStats — .NET, без корневого `package.json`:
> релиз идёт через PowerShell `scripts/release.ps1` (источник версии — `<Version>` в `Directory.Build.props`),
> а НЕ через kit-bash `.claude/scripts/release.sh` (его в проекте нет) и НЕ через коллизирующий `/push`.
> Шаг 2 НЕ делегирует в `/push` намеренно — чтобы исключить конфликт имён kit↔project.
> См. `.claude/rules/claude-config-source-of-truth.md`.

Команда из ДВУХ ОБЯЗАТЕЛЬНЫХ ШАГОВ. Шаг 2 НЕЛЬЗЯ начинать до полного завершения Шага 1.

## ШАГ 1: Формирование Commit message

Действуй так, как если бы получил сообщение: **«Напиши комментарий к коммиту»**.

1. Выполни `git status` и `git diff` для анализа текущих изменений.
2. Сформируй **Commit message** строго по формату правил проекта (conventional commit на русском с секциями Решение/Изменения/Эффект — `~/.claude/rules/commit_message.md`).
3. **ОБЯЗАТЕЛЬНО ВЫВЕДИ** полный Commit message пользователю в ответе.

**СТОП.** Дождись завершения вывода Commit message. Только после этого переходи к Шагу 2.

## ШАГ 2: Релиз (.NET) — `scripts/release.ps1`

`release.ps1` формирует CHANGELOG.md/RELEASE_NOTES.md из conventional-commits с прошлого тега и сам делает `git add -A` + `chore(release): vX.Y.Z`. Поэтому содержательные изменения нужно закоммитить **ОТДЕЛЬНЫМ feat/fix-коммитом ДО релиза**:

1. **Feature-коммит** рабочего дерева сообщением из Шага 1 (сохрани кириллицу и форматирование через временный файл):
   - запиши Commit message во временный файл, затем `git add -A && git commit -F <файл>`.
2. **Релиз** из корня репозитория (где `TBHStats.sln` и `Directory.Build.props`):
   ```powershell
   pwsh -NoProfile -File scripts/release.ps1 $ARGUMENTS -Yes
   ```
   (bump `<Version>`, генерация CHANGELOG/RELEASE_NOTES, `chore(release): vX.Y.Z` + аннотированный тег + push `--follow-tags`, rollback при ошибке. Без аргумента — авто-детект бампа из коммитов.)
3. После релиза дополни секцию новой версии в `CHANGELOG.md` (технический формат, Keep a Changelog) и `RELEASE_NOTES.md` (user-facing) полным описанием из Шага 1. **Дополнительный коммит для этого делать НЕ нужно.**

> НИКОГДА не запускай `bash .claude/scripts/release.sh` и не вызывай kit-`/push` для TBHStats — это npm/bash-путь, для .NET неприменим. Для релиза используется только `scripts/release.ps1` (его обёртки — project `/commit`, `/push`, `/release`).
