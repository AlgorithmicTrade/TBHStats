---
description: "Task list for TBHStats — помощник по статистике Task Bar Hero"
---

# Tasks: TBHStats — помощник по статистике Task Bar Hero

**Input**: Design documents from `/specs/001-tbh-stats-helper/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅
**Stack**: C# 12 / .NET 8 / WinUI 3 · WGC + Windows.Media.Ocr · SQLite/EF Core · LiveCharts2
**Tests**: ВКЛЮЧЕНЫ — требуются конституцией (Принцип VII, Quality Gates) и research.md (unit-тесты Core/Data/Capture; реальный SQLite, фикстуры-скриншоты для OCR — без моков).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: можно выполнять параллельно (разные файлы, нет зависимостей)
- **[Story]**: к какой пользовательской истории относится (US1/US2/US3)
- Указаны точные пути файлов

## Path Conventions (из plan.md «Structure Decision»)

```
src/TBHStats.Core   src/TBHStats.Capture   src/TBHStats.Data   src/TBHStats.App   src/TBHStats.Remote(future)
tests/TBHStats.Core.Tests   tests/TBHStats.Capture.Tests   tests/TBHStats.Data.Tests
```

---

## Phase 0: Planning (Executor Assignment)

**Purpose**: подготовка к реализации — анализ, создание агентов, назначение исполнителей.

- [X] P001 Проанализировать все задачи и определить нужные типы агентов. Кандидаты (в реестре нет C#/.NET-агентов — почти всё FUTURE): `dotnet-winui-developer` (WinUI3/MVVM/App), `windows-capture-ocr-specialist` (WGC+Windows.Media.Ocr+ROI+детекция вкладки), `efcore-sqlite-specialist` (EF Core/миграции/репозитории), `dotnet-test-writer` (xUnit/FluentAssertions; существующий `test-writer` заточен под Vitest — не подходит), `dotnet-uiautomation-specialist` (FlaUI + SendInput + визуальная локализация для Phase 7 QA-харнесса). Тривиальные — MAIN.
- [X] P002 Создать недостающих агентов через команду `/create agent` (внутри — `meta-agent-v3` + `prompt-guidance-audit`), затем попросить пользователя перезапустить claude-code. **Создано 5/5, все PASS**: `dotnet-winui-developer`, `windows-capture-ocr-specialist`, `efcore-sqlite-specialist` (development/workers); `dotnet-test-writer`, `dotnet-uiautomation-specialist` (testing/workers). ⏳ Требуется рестарт claude-code для регистрации.
- [X] P003 Назначить исполнителей всем задачам (см. карту ниже).
- [X] P004 Разрешить research-задачи: открытых нет. Единственный эмпирический риск (точность OCR на шрифте TBH) проверяется в T049 на реальных скриншотах — отдельного research-промпта не требует.

**Rules**: MAIN — только тривиальные правки; новые агенты создаются через `/create agent`; после P002 обязателен рестарт.

### Карта исполнителей (P003)

Сокращения: **WINUI** = `dotnet-winui-developer` (Core-домен + App/WinUI), **CAP** = `windows-capture-ocr-specialist` (TBHStats.Capture), **DATA** = `efcore-sqlite-specialist` (TBHStats.Data), **TEST** = `dotnet-test-writer` (xUnit/FluentAssertions), **UIA** = `dotnet-uiautomation-specialist` (TBHStats.UiTests), **MAIN** = главная сессия (тривиальное).

| Задача | Executor | Параллельность |
|--------|----------|----------------|
| T001 | WINUI | SEQUENTIAL (блокирует Setup) |
| T002, T004 | WINUI | [P] после T001 |
| T003 | MAIN (config) | [P] после T001 |
| T005, T006, T007, T008 | WINUI (Core) | T005/T006/T008 [P]; T007 после T006 |
| T009→T010 | DATA | SEQUENTIAL |
| T011 | DATA | [P] |
| T012→T013 | CAP | T013 после T012 |
| T014, T015 | CAP | [P] |
| T016, T017, T018 | TEST | [P] |
| T019, T020, T021 | TEST | [P] (до реализации US1) |
| T022 | CAP | [P] |
| T023 | CAP | после T022/T014/T015 |
| T024, T025 | WINUI (Core) | T024 после T008 |
| T026 | WINUI (App) | после T013/T023/T024/T025 |
| T027 | DATA | после T011 |
| T028, T029, T030, T031 | WINUI (App) | по зависимостям |
| T032, T033, T034 | TEST | [P] |
| T035 | CAP | после T023 |
| T036 | WINUI (App) | после T035/T026 |
| T037, T038 | DATA | после T011/T009 |
| T039 | WINUI (Core) | — |
| T040, T041, T042 | WINUI (App) | по зависимостям |
| T043 | TEST | [P] |
| T044 | DATA | после T037 |
| T045, T046 | WINUI (App) | после T002/T044 |
| T047, T048, T052, T053 | WINUI | [P] |
| T049, T054 | TEST | [P] |
| T050 | MAIN (docs) | [P] |
| T051 | MAIN + WINUI | acceptance-smoke (нужна игра+SDK) |
| T055, T056 | UIA | [P] |
| T057 | UIA | после T055/T014/T022 |
| T058 | UIA | после T057 |
| T059 | UIA | после T056/T057/T058 |
| T060 | UIA | после T059/T026 |
| T061 | UIA | после T060 |

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: инициализация .NET-решения и базовой инфраструктуры.

- [X] T001 Создать решение `TBHStats.sln` и скелет проектов (Core, Capture, Data, App + tests/*) с TFM `net8.0-windows10.0.22621.0`, `<Nullable>enable</Nullable>`, включёнными анализаторами — в корне репозитория `G:\Project-X\TBHStats`. Проект `TBHStats.Remote` — future-заглушка, в v1 НЕ создаётся (см. plan.md «Structure», FR-020)
- [X] T002 [P] Подключить NuGet-зависимости по проектам: `Microsoft.EntityFrameworkCore.Sqlite` (Data), `CommunityToolkit.Mvvm` + `Microsoft.WindowsAppSDK` + `LiveChartsCore.SkiaSharpView.WinUI` (App), CsWinRT/`Microsoft.Windows.SDK.Net.Ref` (Capture), `xunit` + `FluentAssertions` (tests/*) — в соответствующих `.csproj`
- [X] T003 [P] Настроить `.editorconfig` + анализаторы (nullability warnings-as-errors для Core/Data), запрет `dynamic`, стиль — в корне репозитория
- [X] T004 [P] Настроить DI-контейнер и `Microsoft.Extensions.Logging` (composition root) в `src/TBHStats.App/Services/Composition.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: общая инфраструктура, обязательная ДО любой пользовательской истории (домен, конфиг механик, БД, примитивы захвата).

**⚠️ CRITICAL**: ни одна US не начинается до завершения Phase 2.

- [X] T005 [P] Енумы и value-объекты (`OptimizationMetric`, `CaptureState`, `WindowVisibility`, `StageRef`; `FieldSource` перенесён в T006 к config-моделям) в `src/TBHStats.Core/Models/` → Artifacts: OptimizationMetric.cs, CaptureState.cs, WindowVisibility.cs, StageRef.cs
- [X] T006 [P] Доменные модели справочников (`Stage`, `Act`, `Difficulty`, `ChestType`, `HeroClass`, `Tab`, `HeroSnapshot`) + енумы `FieldSource`/`OcrEngine`/`Theme` + config/state-модели (`RoiCalibration`, `WidgetSettings`, `OptimizationProfile`) в `src/TBHStats.Core/Models/` → Artifacts: ChestType.cs, HeroClass.cs, Tab.cs, Act.cs, Difficulty.cs, Stage.cs, HeroSnapshot.cs, FieldSource.cs, OcrEngine.cs, Theme.cs, RoiCalibration.cs, WidgetSettings.cs, OptimizationProfile.cs
- [X] T007 `GameMechanicsConfig` + дефолтный сид (9 разделов; 3 типа сундуков brown/blue/red; 3 акта × 2 сложности Normal/Nightmare × 10 = 60 этапов; дефолтные Field Source Bindings gold→Hero, stats→Status, stage→Portal, MainZone) + `IGameMechanics`/`GameMechanics` в `src/TBHStats.Core/Mechanics/` → Artifacts: GameMechanicsConfig.cs, FieldSourceBinding.cs, IGameMechanics.cs, GameMechanics.cs
- [X] T008 [P] `IValueParser` + парсер сокращённых чисел (K/M/B/T), времени этапа, идентификатора этапа (act-stage) в `src/TBHStats.Core/Parsing/ValueParser.cs` → Artifacts: IValueParser.cs, ValueParser.cs
- [X] T009 EF Core `TbhStatsDbContext` + конфигурации сущностей (15 IEntityTypeConfiguration; owned HeroSnapshot, StageRef? через ValueConverter→string, составные PK, уник. индексы) в `src/TBHStats.Data/TbhStatsDbContext.cs` + `src/TBHStats.Data/Entities/` → Artifacts: TbhStatsDbContext.cs, Entities/*Configuration.cs (15)
- [X] T010 Первичная миграция EF Core `InitialCreate` + bootstrap SQLite (`%LOCALAPPDATA%\TBHStats\tbhstats.db`, авто-применение миграций) + идемпотентный сидинг механик из GameMechanicsConfig.CreateDefault() + DesignTimeDbContextFactory в `src/TBHStats.Data/` → Artifacts: Migrations/20260531133324_InitialCreate.cs(+Designer+Snapshot), DatabaseInitializer.cs, DesignTimeDbContextFactory.cs
- [X] T011 [P] Интерфейсы и скелеты репозиториев (`IRunRepository`, `IStageAggregateRepository`, `ISettingsRepository`) + `DateRange` в `src/TBHStats.Data/Repositories/` (скелеты бросают NotImplementedException со ссылками на T027/T037/T038) → Artifacts: IRunRepository.cs, IStageAggregateRepository.cs, ISettingsRepository.cs, RunRepository.cs, StageAggregateRepository.cs, SettingsRepository.cs, DateRange.cs
- [X] T012 [P] `IGameWindowTracker` — поиск окна TBH по HWND, отслеживание move/resize, видимость в `src/TBHStats.Capture/WindowTracking/GameWindowTracker.cs` → Artifacts: IGameWindowTracker.cs, GameWindowTracker.cs, GameWindowHandle.cs, SizePx.cs, GameWindowTrackerOptions.cs
- [ ] T013 `ICaptureSession` — WGC `GraphicsCaptureItem` из HWND (захват перекрытого окна), кадр, машина состояний NotFound/Capturing/Waiting в `src/TBHStats.Capture/Wgc/CaptureSession.cs` (depends on T012)
- [X] T006c (доп. к T006/T009) Доменные history-модели в `src/TBHStats.Core/Models/`: `StageRun`, `StageRunChest`, `MetricSample`, `MetricSampleChest`, `StageAggregate`, `StageAggregateChestRate` (пробел гранулярности — нужны для маппинга T009; помещены в Core под Android-reuse R10) → Artifacts: StageRun.cs, StageRunChest.cs, MetricSample.cs, MetricSampleChest.cs, StageAggregate.cs, StageAggregateChestRate.cs
- [X] T014 [P] `IOcrReader` — обёртка Windows.Media.Ocr (`SoftwareBitmap`, confidence-эвристика по покрытию bbox, кроп ROI через BitmapDecoder+Bounds) + общий тип `CapturedFrame` в `src/TBHStats.Capture/Ocr/` + `src/TBHStats.Capture/CapturedFrame.cs` → Artifacts: CapturedFrame.cs, IOcrReader.cs, OcrReader.cs, OcrResult.cs
- [X] T015 [P] ROI-маппер — нормализованные доли клиентской области ↔ пиксели кадра в `src/TBHStats.Capture/Roi/RoiMapper.cs` → Artifacts: IRoiMapper.cs, RoiMapper.cs, RoiPixelRect.cs
- [X] T016 [P] Тесты парсера (реальные строки K/M/B, время, stage-id) в `tests/TBHStats.Core.Tests/ValueParserTests.cs` — 63 теста, все PASS, багов нет → Artifacts: ValueParserTests.cs
- [X] T017 [P] Тесты ROI-маппера (инвариантность к размеру/масштабу) в `tests/TBHStats.Capture.Tests/RoiMapperTests.cs` — 30 тестов, все PASS, багов нет → Artifacts: RoiMapperTests.cs
- [X] T018 [P] Тесты слоя данных на реальном временном SQLite (CRUD, миграции, сидинг, owned-типы, StageRef-конвертер, составные PK/FK/уник.индексы) в `tests/TBHStats.Data.Tests/DataLayerTests.cs` — 27 тестов, все PASS, багов нет → Artifacts: DataLayerTests.cs

**Checkpoint**: фундамент готов — домен, конфиг, БД, примитивы захвата (окно/кадр/OCR/ROI) на месте.

---

## Phase 3: User Story 1 — Живая статистика текущего забега (Priority: P1) 🎯 MVP

**Goal**: рядом с игрой виджет в реальном времени показывает золото/час, опыт/час, сундуки/час, класс/уровень/урон, текущий этап — на основе визуального захвата.

**Independent Test**: запустить виджет у работающей игры на известном этапе → после калибровки в течение ~10с видны корректные живые темпы; перекрытие окна не вызывает ошибки.

### Tests for User Story 1 ⚠️ (написать ДО реализации, убедиться что падают)

- [ ] T019 [P] [US1] Тесты детекции активной вкладки (матч названия с конфигом, fuzzy) в `tests/TBHStats.Capture.Tests/TabDetectorTests.cs`
- [ ] T020 [P] [US1] Тесты вычисления темпов (золото/час, опыт/час с учётом level-up, сундуки по дельтам, периоды недоступности не занижают) в `tests/TBHStats.Core.Tests/MetricsCalculatorTests.cs`
- [ ] T021 [P] [US1] Тесты валидации наблюдений (confidence-порог, монотонность золота, EXP-reset, транзиентные точки сундуков) в `tests/TBHStats.Core.Tests/ObservationValidatorTests.cs`

### Implementation for User Story 1

- [ ] T022 [P] [US1] `ITabDetector` — OCR названия активной вкладки + матч с `GameMechanicsConfig` (9 разделов) в `src/TBHStats.Capture/Tabs/TabDetector.cs`
- [ ] T023 [US1] `IFieldExtractor` — кадр + активная вкладка + ROIs → `RawObservation` (только доступные при активной вкладке поля; MainZone всегда) в `src/TBHStats.Capture/FieldExtractor.cs` (depends on T022, T014, T015)
- [ ] T024 [US1] Валидатор наблюдений: sanity/confidence + монотонность золота + EXP-reset + дельты точек сундуков → надёжный `MetricSample` в `src/TBHStats.Core/Parsing/ObservationValidator.cs` (depends on T008)
- [ ] T025 [US1] `IMetricsCalculator` — живые темпы (золото/час, опыт/час, сундуки/час) по надёжным интервалам в `src/TBHStats.Core/Optimization/MetricsCalculator.cs`
- [ ] T026 [US1] `IStatsOrchestrator` — фоновая петля (окно → кадр/ожидание → вкладка → извлечение → валидация → sample → темпы → биндинг) в `src/TBHStats.App/Services/StatsOrchestrator.cs` (depends on T013, T023, T024, T025)
- [ ] T027 [US1] `ISettingsRepository` impl — персистентность `WidgetSettings` и `RoiCalibration` в `src/TBHStats.Data/Repositories/SettingsRepository.cs` (depends on T011)
- [ ] T028 [US1] Оболочка виджета WinUI3 (перемещаемое окно, опц. topmost, сохранение позиции/размера) в `src/TBHStats.App/Views/WidgetWindow.xaml(.cs)` (depends on T004, T027)
- [ ] T029 [US1] ViewModel живых показателей + биндинг (золото/час, опыт/час, сундуки/час, герой, этап, статус) в `src/TBHStats.App/ViewModels/LiveStatsViewModel.cs` (depends on T026)
- [ ] T030 [US1] UI калибровки ROI: разметка областей поверх захваченного кадра, выбор источника (MainZone/вкладка), сохранение в долях в `src/TBHStats.App/Views/CalibrationView.xaml(.cs)` (depends on T015, T027)
- [ ] T031 [US1] Состояния «игра не найдена» / «ожидание» (окно свёрнуто/перекрыто) + метка устаревания в UI в `src/TBHStats.App/ViewModels/LiveStatsViewModel.cs`

**Checkpoint**: US1 полностью функциональна — MVP, тестируется независимо.

---

## Phase 4: User Story 2 — История этапов и выбор оптимального для фарма (Priority: P2)

**Goal**: накопление истории по 60 этапам, сравнение и рекомендация оптимального этапа по выбранной цели (золото/час ↔ опыт/час).

**Independent Test**: сыграть на нескольких этапах → открыть сравнение → этапы ранжированы по выбранной метрике, рекомендованный имеет лучший показатель; история переживает перезапуск.

### Tests for User Story 2 ⚠️

- [ ] T032 [P] [US2] Тесты ранжирования/рекомендации (по золото/час и опыт/час, переключение цели) в `tests/TBHStats.Core.Tests/OptimizationServiceTests.cs`
- [ ] T033 [P] [US2] Тесты агрегатов этапа (avg/best золото/час, опыт/час, время, темп сундуков; исключение partial) в `tests/TBHStats.Core.Tests/StageAggregateTests.cs`
- [ ] T034 [P] [US2] Тесты записи забегов на реальном SQLite (StageRun + сундуки, переживание перезапуска) в `tests/TBHStats.Data.Tests/RunRecordingTests.cs`

### Implementation for User Story 2

- [ ] T035 [US2] Детектор завершения этапа (прогрессбар/босс в MainZone) → закрытие забега в `src/TBHStats.Capture/StageCompletionDetector.cs` (depends on T023)
- [ ] T036 [US2] Сборка и запись `StageRun` + `StageRunChest` (дельты золота/опыта/сундуков за забег, флаг partial) в `src/TBHStats.App/Services/RunRecorder.cs` (depends on T035, T026)
- [ ] T037 [US2] Полная реализация `IRunRepository` (AddRun, GetRuns, GetSamples, AppendSample) в `src/TBHStats.Data/Repositories/RunRepository.cs` (depends on T011)
- [ ] T038 [US2] Пересчёт `StageAggregate` (avg/best золото/час, опыт/час, время, темп сундуков) в `src/TBHStats.Data/Repositories/StageAggregateRepository.cs` (depends on T009)
- [ ] T039 [US2] `IOptimizationService` — ранжирование этапов и рекомендация по `OptimizationMetric` в `src/TBHStats.Core/Optimization/OptimizationService.cs`
- [ ] T040 [US2] Персистентность и переключатель `OptimizationProfile` (золото/час ↔ опыт/час) в `src/TBHStats.App/Services/` + `SettingsRepository` (depends on T027)
- [ ] T041 [US2] UI сравнения этапов (таблица с сортировкой по метрике, отметка рекомендованного, переключатель цели) в `src/TBHStats.App/Views/CompareView.xaml(.cs)` (depends on T039)
- [ ] T042 [US2] ViewModel сравнения в `src/TBHStats.App/ViewModels/CompareViewModel.cs` (depends on T038, T039, T040)

**Checkpoint**: US1 и US2 работают независимо; история сохраняется.

---

## Phase 5: User Story 3 — Визуализация трендов графиками (Priority: P3)

**Goal**: графики динамики золото/час, опыт/час, времени прохождения по этапу во времени.

**Independent Test**: имея историю по этапу, открыть график → линии отражают реальные записи, тултипы по точкам показывают значения/время.

### Tests for User Story 3 ⚠️

- [ ] T043 [P] [US3] Тесты выборки сэмплов для трендов (диапазон времени, по этапу) в `tests/TBHStats.Data.Tests/SampleQueryTests.cs`

### Implementation for User Story 3

- [ ] T044 [US3] Выборка/ретенция `MetricSample` для трендов по этапу в `src/TBHStats.Data/Repositories/RunRepository.cs` (depends on T037)
- [ ] T045 [US3] Экран графиков (LiveCharts2): тренды золото/час, опыт/час, время; тултипы по точкам в `src/TBHStats.App/Views/ChartsView.xaml(.cs)` (depends on T002)
- [ ] T046 [US3] ViewModel графиков в `src/TBHStats.App/ViewModels/ChartsViewModel.cs` (depends on T044)

**Checkpoint**: все три истории независимо функциональны.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: улучшения, затрагивающие несколько историй.

- [ ] T047 [P] Сквозная обработка ошибок и структурное логирование (типизированные ошибки, «ожидание» вместо throw, без секретов в логах) во всех слоях
- [ ] T048 [P] Конфигурация поставки MSIX (packaged) + проверка TFM/WinRT-доступа + опция unpackaged в `src/TBHStats.App/`
- [ ] T049 Харнесс проверки точности OCR на реальных скриншотах игры (фикстуры) в `tests/TBHStats.Capture.Tests/OcrFixturesTests.cs`
- [ ] T050 [P] Обновить документацию `docs/` под реализацию (структура, запуск)
- [ ] T051 Прогон acceptance-smoke из `quickstart.md` (перекрытие/сворачивание/перемещение окна, завершение этапа, перезапуск, добавление механики). Включить замеры: **SC-005** (найти лучший этап в экране сравнения <30с), **SC-006** (виджет ≤15% площади экрана в компактном состоянии), **FR-012** (отсутствие исходящих сетевых соединений — данные только локально)
- [ ] T052 [P] Проверка производительности (idle CPU, латентность кадр+OCR одной ROI < ~150 мс, возобновление ≤5с)
- [ ] T053 [P] Accessibility-проход (конституция §XI, RECOMMENDED): клавиатурная операбельность основных действий (открыть сравнение, переключить цель оптимизации), достаточный контраст живых показателей, поддержка Light/Dark темы Windows, тип сундука различается не только цветом (иконка/подпись) — в `src/TBHStats.App/`
- [ ] T054 [P] Тест расширяемости механик (SC-010): добавление нового типа сундука/класса/вкладки через `GameMechanicsConfig` без изменения схемы; ранее накопленная история остаётся валидной — в `tests/TBHStats.Core.Tests/MechanicsExtensibilityTests.cs` и `tests/TBHStats.Data.Tests/HistoryValidityTests.cs`

---

## Phase 7: UI Test Harness — аудит живой игры (QA, FR-022…FR-027)

> **Порядок**: номер фазы — последовательная метка, а НЕ строгий порядок. Phase 7 зависит только от US1 (T022/T026) и может разрабатываться параллельно с US2/US3/Polish, не дожидаясь их завершения.

**Purpose**: «нехрупкие» детерминированные E2E-тесты поверх реально запущенной игры; харнесс сам навигирует по интерфейсу человекоподобными кликами и аудирует детекцию разделов/чтение значений. **Не входит в поставку** (observe-only продукта сохранён, carve-out конституции v2.2.0). Инструмент — FlaUI + визуальная локализация (OCR, переиспользуя Capture) + SendInput. **НЕ Playwright** (браузерный).

- [ ] T055 [P] Создать проект `tests/TBHStats.UiTests/` (xUnit) + подключить FlaUI (`FlaUI.Core`, `FlaUI.UIA3`) + ссылку на `TBHStats.Capture`
- [ ] T056 [P] Человекоподобный инжектор ввода (SendInput через FlaUI): клик в случайную точку в границах элемента, случайная задержка в диапазоне, easing курсора в `tests/TBHStats.UiTests/Input/HumanLikeInput.cs` (FR-024)
- [ ] T057 Визуальный локатор элементов: OCR названий разделов/кнопок через `TBHStats.Capture` → bounding box; режим UIA если доступен; Auto-выбор в `tests/TBHStats.UiTests/Locator/ElementLocator.cs` (FR-023) (depends on T055, T014, T022)
- [ ] T058 Safety-Guard: запрет кликов по запрещённым элементам (Runes upgrade, Cube craft/recycle, Stash move, Trade ship, любые sell/spend) + лог попыток в `tests/TBHStats.UiTests/Safety/SafetyGuard.cs` (FR-026) (depends on T057)
- [ ] T059 Раннер сценариев: poll-with-timeout ожидания, ассерты по распознанному состоянию, повтор N прогонов (детерминизм) в `tests/TBHStats.UiTests/ScenarioRunner.cs` (FR-025) (depends on T056, T057, T058)
- [ ] T060 Реализовать **активные** сценарии TS-00, TS-02…TS-10 из `ui-test-scenarios.md` (источники данных, следование за окном, перекрытие, human-like аудит, safety, детерминизм). **TS-01 (section sweep) ОТКЛЮЧЁН** — неверная формулировка, будет переписан позже, в этой задаче не реализуется. В `tests/TBHStats.UiTests/Scenarios/` (FR-022, FR-027) (depends on T059, T026)
- [ ] T061 [P] Отчётность/вердикты харнесса: стабильность серии (SC-012), покрытие 9 разделов + 0 мутаций (SC-013), человекоподобность кликов (SC-014) в `tests/TBHStats.UiTests/Reporting/` (depends on T060)

**Checkpoint**: QA-харнесс аудирует весь интерфейс живой игры, детерминированно и безопасно.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0 (Planning)** → создаёт агентов/исполнителей; блокирует реальную реализацию
- **Phase 1 (Setup)** → без зависимостей (после Phase 0)
- **Phase 2 (Foundational)** → зависит от Setup; **БЛОКИРУЕТ все US**
- **US1 (Phase 3)** → после Foundational; MVP
- **US2 (Phase 4)** → после Foundational; переиспользует пайплайн захвата US1 (T023/T026) для детекции завершения этапа
- **US3 (Phase 5)** → после Foundational; использует репозиторий сэмплов US2 (T037)
- **Polish (Phase 6)** → после нужных US
- **UI Test Harness (Phase 7)** → после US1 (T022/T026 — есть что аудировать); полное покрытие — после US2 (разделы Portal/Status задействованы в записи забегов). Может разрабатываться параллельно с US2/US3.

### Cross-story notes

- US2 зависит от пайплайна захвата/наблюдений US1 (T023, T026) — инкрементальная доставка поверх MVP.
- US3 зависит от записи сэмплов (T037 из US2). Если нужна полная независимость US3 — сначала довести T037.

### Within Each User Story

- Тесты пишутся и падают ДО реализации
- Модели → сервисы → UI; ядро → интеграция

### Parallel Opportunities

- Setup: T002, T003, T004 — параллельно после T001
- Foundational: T005, T006, T008, T011, T012, T014, T015 — параллельно; T016/T017/T018 — параллельно (тесты). T013 после T012; T009→T010 последовательно
- US1 тесты T019/T020/T021 — параллельно; T022 параллельно; далее по зависимостям
- US2 тесты T032/T033/T034 — параллельно
- Polish: T047, T048, T050, T052, T053, T054 — параллельно

---

## Parallel Example: Foundational (Phase 2)

```text
# В одном сообщении (разные файлы, без взаимозависимостей):
Task: "T005 Енумы/value-объекты в src/TBHStats.Core/Models/"
Task: "T006 Доменные модели в src/TBHStats.Core/Models/"
Task: "T008 IValueParser в src/TBHStats.Core/Parsing/"
Task: "T012 IGameWindowTracker в src/TBHStats.Capture/WindowTracking/"
Task: "T014 IOcrReader в src/TBHStats.Capture/Ocr/"
Task: "T015 RoiMapper в src/TBHStats.Capture/Roi/"
```

## Parallel Example: User Story 1 tests

```text
Task: "T019 Тесты детекции вкладки в tests/TBHStats.Capture.Tests/TabDetectorTests.cs"
Task: "T020 Тесты темпов в tests/TBHStats.Core.Tests/MetricsCalculatorTests.cs"
Task: "T021 Тесты валидации наблюдений в tests/TBHStats.Core.Tests/ObservationValidatorTests.cs"
```

---

## Implementation Strategy

### MVP First (только User Story 1)

1. Phase 1: Setup
2. Phase 2: Foundational (КРИТИЧНО — блокирует все истории)
3. Phase 3: User Story 1
4. **STOP & VALIDATE**: проверить живые показатели на реальной игре (калибровка → темпы → перекрытие окна)
5. Демо MVP

### Incremental Delivery

1. Setup + Foundational → фундамент
2. US1 → независимый тест → демо (MVP: живая статистика)
3. US2 → независимый тест → демо (история + рекомендация)
4. US3 → независимый тест → демо (графики)

### Critical-path риск

- Слой захвата (WGC по HWND + OCR + детекция вкладки) — самый рискованный; точность OCR на шрифте TBH проверяется рано (T049/калибровка). Если Windows.Media.Ocr недостаточен → fallback Tesseract (per-ROI, research R2) без смены архитектуры.

---

## Notes

- [P] = разные файлы, нет зависимостей
- [Story] связывает задачу с US для трассируемости
- Тесты — на реальном SQLite и реальных скриншотах (без моков БД/OCR), per quality-rules
- Коммит после каждой задачи/логической группы (`/push patch`)
- `dotnet build` + тесты должны проходить до коммита (Quality Gates, Принцип VII)
- Остановка на любом checkpoint для независимой проверки истории

---

## Summary

- **Всего задач**: 61 (T001–T061) + 4 планирования (P001–P004)
- **По историям**: Setup 4 · Foundational 14 (T005–T018) · US1 13 (T019–T031) · US2 11 (T032–T042) · US3 4 (T043–T046) · Polish 8 (T047–T054) · UI Test Harness 7 (T055–T061)
- **Тестов**: 12 unit/integration (T016–T021, T032–T034, T043, T049, T054) + E2E-харнесс (T055–T061, сценарии TS-00…TS-10)
- **MVP**: Phase 1 + Phase 2 + US1 (T001–T031). QA-харнесс (Phase 7) — после MVP, не входит в поставку
- **Параллельных групп**: см. Parallel Opportunities (макс. выигрыш в Foundational и блоках тестов)
