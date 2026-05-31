---
# Project Architecture (TBHStats)
---

> Инварианты слоистой .NET-архитектуры TBHStats (.NET 8 / C# 12). Цель: не размывать границы слоёв, держать домен переиспользуемым (задел под Android/MAUI) и не делать выводов о слое без чтения его кода.
> Первоисточник решений — `specs/001-tbh-stats-helper/` + `.specify/memory/constitution.md`; `docs/project/*` — производный навигационный слой (см. `.claude/rules/docs-navigation.md`).

## When to apply

При любой правке/добавлении/удалении кода в `src/**` или `tests/**`, при ответах на вопросы об устройстве слоёв, при выборе «куда положить новый тип/логику». Срабатывает по intent, не по literal-фразе. Исключения — как в `docs-navigation.md` (тривиальные правки, работа только с `.claude/`/`.specify/`/`claude-code-orchestrator-kit/`).

## Структура решения (источник: ARCHITECTURE.md §8, ADR-002)

```
src/
├── TBHStats.Core/      # ДОМЕН: Models/, Mechanics/, Parsing/, Optimization/ — без WinRT/UI/EF (net8.0)
├── TBHStats.Capture/   # Windows-only: WindowTracking/, Wgc/, Ocr/, Roi/, Tabs/, CapturedFrame (net8.0-windows10.0.22621.0)
├── TBHStats.Data/      # EF Core/SQLite: TbhStatsDbContext, Entities/(конфигурации), Migrations/, Repositories/, DatabaseInitializer (net8.0)
├── TBHStats.App/       # WinUI 3 MVVM: Views/, ViewModels/, Services/(Composition, StatsOrchestrator)
└── TBHStats.Remote/    # FUTURE — в v1 НЕ создаётся (FR-020, ADR-010)
tests/
├── TBHStats.Core.Tests/  TBHStats.Capture.Tests/  TBHStats.Data.Tests/  TBHStats.UiTests/(FlaUI E2E, не в поставке)
```

Направление зависимостей (однонаправленно): `Core` ← `Capture`, `Core` ← `Data`, `{Core,Capture,Data}` ← `App`. **Core не зависит ни от одного внутреннего проекта.** Обратные зависимости (Core → Capture/Data/App, Capture ↔ Data) ЗАПРЕЩЕНЫ.

## Rule

- **Куда класть логику.** Биржа-агностичный аналог для TBHStats — слой:
  - Доменная логика (парсинг значений, расчёт темпов, ранжирование/оптимизация, интерпретация фактов игры, справочники механик) → **`TBHStats.Core`** (без WinRT/UI/EF). Подпапки: `Models/`, `Mechanics/`, `Parsing/`, `Optimization/`.
  - Захват экрана и распознавание (WGC, OCR, ROI-маппинг, детекция вкладки, извлечение полей) → **`TBHStats.Capture`** (единственный Windows/WinRT-слой). Подпапки по назначению: `WindowTracking/`, `Wgc/`, `Ocr/`, `Roi/`, `Tabs/`.
  - Персистентность (DbContext, конфигурации EF, миграции, репозитории, bootstrap/сидинг) → **`TBHStats.Data`**.
  - UI/виджет/графики/оркестрация петли → **`TBHStats.App`**.
- **Single Source of Truth (Принцип II).** Доменные типы, енумы, value-объекты, config/state- и history-модели определяются ОДИН раз в `TBHStats.Core/Models`; конфиг механик — в `TBHStats.Core/Mechanics`. Data/Capture/App их ПЕРЕИСПОЛЬЗУЮТ, не дублируют. EF маппит сами Core-модели через `IEntityTypeConfiguration<>` в `TBHStats.Data/Entities/` — параллельных «Data.Entities»-двойников доменных типов быть НЕ должно.
- **Граница Core ↔ WinRT/UI (ADR-002, ADR-010, задел под MAUI/Android R10).** `TBHStats.Core` НЕ ссылается на WinRT (`Windows.*`), WinUI, EF Core. Если доменной логике нужны данные из захвата — они приходят как чистые DTO/модели Core (напр. `RawObservation`/`MetricSample`), а не как `SoftwareBitmap`/`GraphicsCaptureItem`. Утечка WinRT/UI-типов в Core — bug.
- **Config-driven механики (ADR-009, FR-021, SC-010).** Новый тип сундука / класс героя / вкладка / этап = запись в `GameMechanicsConfig` (+ ROI в калибровке), а НЕ новый enum-в-коде и НЕ правка схемы БД. История ссылается на id справочников → остаётся валидной после расширений (FR-013). Менеджеры/сервисы не содержат `switch`/`if (chestType == "...")`-веток под конкретную механику — поведение управляется данными конфига.
- **Строгая типобезопасность (Принцип V).** `TBHStats.Core` и `TBHStats.Data` собираются с `TreatWarningsAsErrors=true` (`Directory.Build.props`) — правка ОБЯЗАНА держать **0 предупреждений**. `dynamic` запрещён. Nullable on solution-wide.
- **Тесты на реальных данных (quality.md, research R5/R2).** Тесты Data — на реальном временном файловом SQLite (НЕ in-memory-мок); тесты OCR — на фикстурах-скриншотах; парсер/маппер — на реальных строках/размерах. Моки БД/OCR запрещены.
- **Навигация и обновление docs.** Перед правкой слоя — прочитай релевантный раздел `docs/project/ARCHITECTURE.md` (§2 захват, §3 OCR, §4 ROI, §5 детекция вкладки, §6 хранилище, §7 графики, §8 структура, §9–§10 поток/состояния, §11 контракты, §12 расширяемость, §13 Android-задел, §14 поставка) + соответствующий ADR из `docs/project/DECISIONS.md`. После — обнови их (Step 3 `docs-navigation.md`).
- **Никаких выводов о слое без чтения кода (quality.md).** Утверждение о поведении захвата/OCR/EF-маппинга/парсинга без чтения конкретного файла в правимом слое — `unverified`. Напр.: вывод об OCR-точности без чтения `src/TBHStats.Capture/Ocr/OcrReader.cs` и ADR-005 — недопустим.
- **WGC/OCR runtime.** Корректность WGC-захвата и Windows.Media.Ocr эмпирична и проверяется на живой игре/фикстурах (T049/T051), а не headless. Критерий приёмки правок захвата без игры: чистая сборка + соответствие актуальному API (Microsoft Learn, Принцип III) + верная логика машины состояний (`NotFound`/`Capturing`/`Waiting`, ADR-004).

## Anti-examples (что НЕ делать)

- ❌ Определить `ChestType`/`Stage`/`MetricSample` отдельно в `TBHStats.Data` рядом с Core-копией — нарушение Single Source of Truth; EF маппит Core-типы.
- ❌ Добавить `using Windows.Graphics.Capture;` или ссылку на WinUI/EF в `TBHStats.Core` — ломает границу под Android (ADR-010).
- ❌ Ввести `enum ChestKind { Brown, Blue, Red }` в коде и `switch` по нему — вместо записей в `GameMechanicsConfig` (нарушает ADR-009/FR-021).
- ❌ Закоммитить правку Core/Data с предупреждениями компилятора — там `warnings-as-errors`.
- ❌ «OCR читает золото из MainZone» без чтения `FieldExtractor`/`GameMechanicsConfig` (gold привязан к вкладке Hero, FR-002b) — `unverified` вывод.
- ❌ Тест Data на `UseInMemoryDatabase` — прячет реальную SQL-семантику/миграции (research R5).

## TL;DR

Домен → `Core` (без WinRT/UI/EF, переиспользуем в MAUI). Захват/OCR → `Capture`. БД → `Data` (маппит Core-типы, не дублирует). UI → `App`. Зависимости только внутрь к Core. Механики — данными в `GameMechanicsConfig`, не enum/schema. Core+Data — 0 warnings. Тесты — на реальных SQLite/скриншотах. Перед правкой слоя читай ARCHITECTURE.md §/ADR, после — обновляй (`docs-navigation.md`); выводы о слое — только по прочитанному коду.
