# Implementation Plan: TBHStats — помощник по статистике Task Bar Hero

**Branch**: `001-tbh-stats-helper` | **Date**: 2026-05-31 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-tbh-stats-helper/spec.md`

## Summary

Десктоп-виджет для Windows 11, который наблюдает за окном запущенной игры Task Bar Hero, **визуально считывает** игровые показатели (золото, опыт, время этапа, класс/уровень/урон героя, текущий этап, сундуки по типам), вычисляет темпы (золото/час, опыт/час, сундуки/час), накапливает историю по 60 этапам (3 акта × 2 сложности × 10) и рекомендует оптимальный этап для фарма по выбранной пользователем цели (золото/час или опыт/час).

**Технический подход**: нативное приложение **.NET 8 / C#**. Захват — **Windows.Graphics.Capture** по HWND окна игры (терпим к перекрытию, следует за окном); распознавание — встроенный **Windows.Media.Ocr**; области считывания (ROI) задаются декларативно и нормализуются относительно клиентской области окна (доли, не пиксели) → независимость от положения/размера/масштаба/монитора. Хранение — **SQLite через EF Core**. UI-виджет — **WinUI 3** (WPF как fallback для оверлей-оболочки). Игровые механики (типы сундуков, классы, акты/сложности/этапы) описаны конфигурацией для лёгкого расширения. Заложен модуль `TBHStats.Remote` (будущий локальный API для Android-клиента на .NET MAUI).

## Technical Context

**Language/Version**: C# 12 / .NET 8 (LTS)
**Primary Dependencies**:
- Windows App SDK (WinUI 3) — UI-виджет
- `Microsoft.Windows.CsWinRT` / Windows SDK projections — доступ к Windows.Graphics.Capture и Windows.Media.Ocr
- `Microsoft.EntityFrameworkCore.Sqlite` — персистентность
- Charts: **LiveCharts2** (`LiveChartsCore.SkiaSharpView.WinUI`) — основной кандидат; ScottPlot как альтернатива (решено в research.md)
- `CommunityToolkit.Mvvm` — MVVM (ObservableObject, RelayCommand)
**Storage**: локальный файл SQLite (`%LOCALAPPDATA%\TBHStats\tbhstats.db`)
**Testing**: xUnit + FluentAssertions (Core/Data логика); фикстуры-скриншоты для OCR (Capture). **UI-тесты живой игры** — отдельный проект `TBHStats.UiTests` на **FlaUI + визуальная локализация (OCR, переиспользуя Capture) + SendInput** (human-like ввод), по сценариям `ui-test-scenarios.md` (FR-022…FR-027). НЕ Playwright (браузерный). Харнесс не входит в поставку (observe-only продукта сохранён через carve-out конституции v2.2.0)
**Target Platform**: Windows 11 (x64/arm64), один локальный пользователь
**Project Type**: single (десктоп) — мультипроектное .NET-решение (Core / Capture / Data / App; +future Remote, MAUI)
**Performance Goals**:
- интервал опроса захвата ~1–3 c; кадр+OCR одной ROI < ~150 мс
- живые темпы видны ≤10 c после старта (SC-001)
- возобновление после перекрытия ≤5 c (SC-008)
- idle-нагрузка CPU низкая (захват по требованию, не непрерывный видеопоток)
**Constraints**:
- observe-only: никаких записей в память игры и инъекций ввода
- захват терпим к перекрытию/сворачиванию (ожидание, не ошибка) — FR-005/005b
- ROI привязаны к окну, а не к экрану — FR-005b
- данные только локально, без внешней передачи (v1) — FR-012
**Scale/Scope**: 60 этапов (3 акта × 2 сложности × 10), 3 типа сундуков (расширяемо), отряд из 3 героев (просматривается выбранный), 9 разделов игры (источники данных — Hero/Status/Portal); история — порядки тысяч записей забегов (тривиально для SQLite).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Оценка против `.specify/memory/constitution.md` (v2.2.0, адаптирована под .NET 2026-05-31):

| Принцип | Статус | Комментарий |
|---------|--------|-------------|
| I. Context-First | ✅ PASS | Спека + research перед реализацией; библиотечно-первый подход. |
| II. Single Source of Truth | ✅ PASS | Доменные модели, enum'ы, game-mechanics config — в `TBHStats.Core`, импортируются остальными. |
| III. Library-First | ✅ PASS | Захват/OCR/БД/графики — платформенные API и зрелые библиотеки, не самопис. Кастом — только доменная логика оптимизации и ROI-маппинг. |
| IV. Code Reuse / DRY | ✅ PASS | Core переиспользуется App'ом и будущим MAUI; нет дублирования. |
| V. Strict Type Safety (NON-NEG) | ✅ PASS | Nullable refs on, `dynamic` запрещён, analyzers clean, build обязателен до коммита. (Re-targeted с TS на C# — см. конституцию.) |
| VI. Atomic Task Execution | ✅ PASS | Разбивка на атомарные задачи — на этапе `/speckit.tasks`. |
| VII. Quality Gates (NON-NEG) | ✅ PASS | `dotnet build` + тесты Core/Data до коммита; нет хардкод-секретов (их и нет в v1). |
| VIII. Progressive Specification | ✅ PASS | spec → plan (этот) → tasks → implement. |
| IX. Error Handling | ✅ PASS | Типизированные ошибки захвата/OCR; «окно недоступно» = состояние ожидания, не throw (FR-005). |
| X. Observability | ✅ PASS | Структурное логирование (Microsoft.Extensions.Logging), метрики уверенности OCR; без секретов в логах. |
| XI. Accessibility (RECOMMENDED) | ✅ PASS | Клавиатура для основных действий, контраст, Light/Dark, тип сундука не только цветом. |
| Security: observe-only + QA carve-out (v2.2.0) | ✅ PASS | Поставляемый продукт строго observe-only (без инъекций ввода). QA-харнесс `TBHStats.UiTests` инжектит ввод только в тест-режиме, не входит в поставку, с Safety-Guard (запрет необратимых действий) — соответствует поправке конституции v2.2.0. |

**Violations requiring justification**: нет. Конфликт «конституция требовала TypeScript» снят легитимной поправкой конституции (одобрено пользователем 2026-05-31), а не обходом гейта. Complexity Tracking не требуется.

## Project Structure

### Documentation (this feature)

```text
specs/001-tbh-stats-helper/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (service + future remote API contracts)
├── checklists/
│   └── requirements.md  # Spec quality checklist (done)
└── tasks.md             # Phase 2 (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── TBHStats.Core/            # Домен: модели (Stage, StageRun, HeroSnapshot, ChestType, ...),
│   │                         #   логика оптимизации/ранжирования, game-mechanics config, парсинг чисел.
│   │                         #   Без UI и без прямых WinRT-зависимостей → переиспользуемо в MAUI.
│   ├── Models/
│   ├── Optimization/
│   ├── Mechanics/            # GameMechanicsConfig (типы сундуков, классы, акты/сложности/этапы)
│   └── Parsing/              # сокращённые числа (1.2K / 3.4M), время этапа
├── TBHStats.Capture/         # Windows-only: WGC по HWND, цикл захвата, ROI-маппинг к окну,
│   │                         #   обёртка Windows.Media.Ocr, детекция активной вкладки, confidence-фильтр, «ожидание».
│   ├── WindowTracking/       # поиск окна TBH по HWND, отслеживание move/resize
│   ├── Wgc/                  # GraphicsCaptureItem из HWND, кадры
│   ├── Ocr/                  # OcrEngine wrapper + нормализация результата
│   ├── Tabs/                 # детекция активной вкладки по названию (FR-002a), фильтр полей по источнику (FR-002b)
│   └── Roi/                  # нормализованные области (доли клиентской области)
├── TBHStats.Data/            # EF Core, SQLite, репозитории, миграции, агрегаты этапов.
│   ├── Entities/
│   ├── Repositories/
│   └── Migrations/
├── TBHStats.App/             # WinUI 3 виджет: оверлей-оболочка, ViewModels (MVVM),
│   │                         #   живые показатели, экран сравнения, графики, настройки/калибровка.
│   ├── Views/
│   ├── ViewModels/
│   └── Services/             # композиция: capture loop → compute → persist → bind
└── TBHStats.Remote/          # (FUTURE, вне объёма v1) локальный API для Android-клиента.

tests/
├── TBHStats.Core.Tests/      # оптимизация, парсинг чисел, конфиг механик
├── TBHStats.Capture.Tests/   # ROI-маппинг, confidence-фильтр, парсинг OCR-вывода (на фикстурах-изображениях)
├── TBHStats.Data.Tests/      # репозитории/агрегаты на in-memory/temp SQLite (реальный SQLite, не мок)
└── TBHStats.UiTests/         # E2E-аудит живой игры: FlaUI + визуальная локализация (OCR) + SendInput (human-like),
                              #   Safety-Guard (запрет мутаций), сценарии ui-test-scenarios.md. Не входит в поставку.

TBHStats.sln
```

**Structure Decision**: Мультипроектное .NET-решение с чистым разделением: доменное ядро (`Core`, без платформенных зависимостей — ключ к будущему переиспользованию в MAUI), изолированный Windows-слой захвата (`Capture`), персистентность (`Data`) и UI (`App`). `Remote` — пустой задел под будущий Android (FR-020), в v1 не реализуется. Такое разбиение прямо отражает границы спеки (захват / вычисление / хранение / отображение / расширяемость) и обеспечивает атомарность будущих задач.

## Complexity Tracking

> Не требуется — нарушений конституции нет.
