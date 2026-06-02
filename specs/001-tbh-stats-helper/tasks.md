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

Сокращения: **WINUI** = `dotnet-winui-developer` (Core-домен + App/WinUI), **CAP** = `windows-capture-ocr-specialist` (TBHStats.Capture), **DATA** = `efcore-sqlite-specialist` (TBHStats.Data), **TEST** = `dotnet-test-writer` (xUnit/FluentAssertions), **MAIN** = главная сессия (тривиальное).

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
- [X] T013 `ICaptureSession` — WGC `GraphicsCaptureItem` из HWND (захват перекрытого окна), кадр, машина состояний NotFound/Capturing/Waiting в `src/TBHStats.Capture/Wgc/CaptureSession.cs` (depends on T012) → Artifacts: ICaptureSession.cs, CaptureSession.cs, Direct3D11Interop.cs, GraphicsCaptureItemInterop.cs
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

- [X] T019 [P] [US1] Тесты детекции активной вкладки (матч названия с конфигом, fuzzy) в `tests/TBHStats.Capture.Tests/TabDetectorTests.cs` — 20 тестов, RED (стаб `TabNameMatcher` бросает NotImplementedException, зелёные после T022). Доп. контракт: `ITabNameMatcher`/`TabNameMatcher`(stub), `ITabDetector`/`TabDetector`(stub) в `src/TBHStats.Capture/Tabs/` → Artifacts: TabDetectorTests.cs, ITabNameMatcher.cs, TabNameMatcher.cs, ITabDetector.cs, TabDetector.cs
- [X] T020 [P] [US1] Тесты вычисления темпов (золото/час, опыт/час с учётом level-up, сундуки по дельтам, периоды недоступности не занижают) в `tests/TBHStats.Core.Tests/MetricsCalculatorTests.cs` — 18 тестов, RED (стаб `MetricsCalculator`, зелёные после T025). Доп. контракт: `IMetricsCalculator`/`MetricsCalculator`(stub) + `LiveRates` в `src/TBHStats.Core/Optimization/` → Artifacts: MetricsCalculatorTests.cs, IMetricsCalculator.cs, MetricsCalculator.cs, LiveRates.cs
- [X] T021 [P] [US1] Тесты валидации наблюдений (confidence-порог, монотонность золота, EXP-reset, транзиентные точки сундуков) в `tests/TBHStats.Core.Tests/ObservationValidatorTests.cs` — 34 теста, RED (стаб `ObservationValidator`, зелёные после T024). Доп. контракт: `IObservationValidator`/`ObservationValidator`(stub) + `RawObservation` + `TabRef` в Core → Artifacts: ObservationValidatorTests.cs, IObservationValidator.cs, ObservationValidator.cs, RawObservation.cs, TabRef.cs

### Implementation for User Story 1

- [X] T022 [P] [US1] `ITabDetector` — OCR названия активной вкладки + матч с `GameMechanicsConfig` (9 разделов) в `src/TBHStats.Capture/Tabs/TabDetector.cs`. Реализован `TabNameMatcher` (Левенштейн, нормализация) + `TabDetector` (OCR activeTab ROI → матч); сигнатура `DetectActiveTabAsync` уточнена параметром `RoiCalibration activeTabRoi`. 20 тестов T019 GREEN (Capture 50/50) → Artifacts: TabNameMatcher.cs, TabDetector.cs, ITabDetector.cs, ARCHITECTURE.md §5
- [X] T023 [US1] `IFieldExtractor` — кадр + активная вкладка + ROIs → `RawObservation` (только доступные при активной вкладке поля; MainZone всегда) в `src/TBHStats.Capture/FieldExtractor.cs` (depends on T022, T014, T015). Сигнатура уточнена параметром `GameMechanicsConfig cfg` (маппинг `chest:<key>`→ChestType.Id). Визуальные `stageProgress`/`bossPresent` → null (T035). Сборка чистая (эмпирический OCR — фикстуры в T049) → Artifacts: IFieldExtractor.cs, FieldExtractor.cs, ARCHITECTURE.md §11
- [X] T024 [US1] Валидатор наблюдений: sanity/confidence + монотонность золота + EXP-reset + дельты точек сундуков → надёжный `MetricSample` в `src/TBHStats.Core/Parsing/ObservationValidator.cs` (depends on T008). 34 теста T021 GREEN → Artifacts: ObservationValidator.cs
- [X] T025 [US1] `IMetricsCalculator` — живые темпы (золото/час, опыт/час, сундуки/час) по надёжным интервалам в `src/TBHStats.Core/Optimization/MetricsCalculator.cs`. 18 тестов T020 GREEN (Core 115/115) → Artifacts: MetricsCalculator.cs
- [X] T026 [US1] `IStatsOrchestrator` — фоновая петля (окно → кадр/ожидание → вкладка → извлечение → валидация → sample → темпы → биндинг) в `src/TBHStats.App/Services/StatsOrchestrator.cs` (depends on T013, T023, T024, T025). Реализованы `IStatsOrchestrator`, `StatsOrchestrator` (петля, машина состояний по `session.State`, IsStale-снимки, буфер ≤200, ConfidenceThreshold=0.6), `LiveStatsSnapshot`, `ScopedSettingsRepositoryProxy` (scoped→singleton); composition root полностью наполнен. Solution 7/7 build clean → Artifacts: IStatsOrchestrator.cs, StatsOrchestrator.cs, LiveStatsSnapshot.cs, ScopedSettingsRepositoryProxy.cs, Composition.cs, ARCHITECTURE.md §6/§9/§11
- [X] T027 [US1] `ISettingsRepository` impl — персистентность `WidgetSettings` и `RoiCalibration` в `src/TBHStats.Data/Repositories/SettingsRepository.cs` (depends on T011). Реализованы все 6 методов (singleton-upsert для WidgetSettings/OptimizationProfile, replace-all для RoiCalibration через ExecuteDeleteAsync, get-or-default). Data.Tests 27/27 PASS → Artifacts: SettingsRepository.cs
- [X] T028 [US1] Оболочка виджета WinUI3 (перемещаемое окно, опц. topmost, сохранение позиции/размера) в `src/TBHStats.App/Views/WidgetWindow.xaml(.cs)` (depends on T004, T027). Компактное окно 320×220 (SC-006), биндинг к LiveStatsViewModel, AppWindow Move/Resize/IsAlwaysOnTop из WidgetSettings (FR-015/016), кнопка «Калибровка» → CalibrationHostWindow. App.xaml.cs: init БД (`DatabaseInitializer.InitializeAsync`) → старт оркестратора → показ виджета; StopAsync при закрытии. Solution 7/7 build clean → Artifacts: WidgetWindow.xaml(.cs), CalibrationHostWindow.xaml(.cs), App.xaml.cs, Composition.cs, ARCHITECTURE.md §7/§14
- [X] T029 [US1] ViewModel живых показателей + биндинг (золото/час, опыт/час, сундуки/час, герой, этап, статус) в `src/TBHStats.App/ViewModels/LiveStatsViewModel.cs` (depends on T026). `ObservableObject` + подписка на `IStatsOrchestrator.SnapshotUpdated`, маршалинг через DispatcherQueue, observable-свойства + текстовые форматтеры → Artifacts: LiveStatsViewModel.cs
- [X] T030 [US1] UI калибровки ROI: разметка областей поверх захваченного кадра, выбор источника (MainZone/вкладка), сохранение в долях в `src/TBHStats.App/Views/CalibrationView.xaml(.cs)` (depends on T015, T027). CalibrationViewModel (Load/AddRoi/RemoveRoi/Save через ISettingsRepository), RoiCalibrationItem (clamp [0..1]), Image+Canvas-оверлей, редактор полей; конвертеры. FrameImage — точка расширения для T051 → Artifacts: CalibrationView.xaml(.cs), CalibrationViewModel.cs, RoiCalibrationItem.cs, Views/Converters/*
- [X] T031 [US1] Состояния «игра не найдена» / «ожидание» (окно свёрнуто/перекрыто) + метка устаревания в UI в `src/TBHStats.App/ViewModels/LiveStatsViewModel.cs`. Реализовано в составе T029: `StatusText`/`IsGameFound`/`IsWaiting`/`IsStale`/`LastUpdateText`; визуальные плашки в WidgetWindow (T028) → Artifacts: LiveStatsViewModel.cs, WidgetWindow.xaml

**Checkpoint**: US1 полностью функциональна — MVP, тестируется независимо.

---

## Phase 4: User Story 2 — История этапов и выбор оптимального для фарма (Priority: P2)

**Goal**: накопление истории по 60 этапам, сравнение и рекомендация оптимального этапа по выбранной цели (золото/час ↔ опыт/час) **при текущей силе отряда**.

**Independent Test**: сыграть на нескольких этапах → открыть сравнение → этапы ранжированы по выбранной метрике (по свежим забегам при текущей силе), рекомендованный имеет лучший показатель; история переживает перезапуск.

> **Уточнение (2026-05-31): добыча зависит не только от этапа, но и от силы отряда** — уровней героев, надетых предметов, выставленной сложности (Normal/Nightmare — уже измерение `Stage`) и купленных рун. Сила растёт со временем → усреднение по **всей** истории смешивает забеги слабого и сильного отряда и даёт обманчивую рекомендацию. Решения пользователя:
> - **Recency-aware агрегация**: `StageAggregate` хранит И all-time, И «**свежее окно**» (последние `OptimizationProfile.RecentWindowSize` non-partial забегов, дефолт 10). Ранжирование и рекомендация — **по свежему окну** (отражает текущую силу); all-time остаётся для справки/трендов (US3). Дефолт scope ранжирования = `Recent`.
> - **Прокси силы по выбранному герою + документированное ограничение**: контекст силы фиксируется уже существующим `HeroSnapshot` (класс/уровень/урон выбранного героя) в каждом `StageRun`. Предметы, руны (раздел Runes) и герои 2–3 отряда в v1 **не считываются** (границы v1 — data-model «Game UI Map»); `Attack Damage` — единственный доступный прокси силы по одному герою. `StageAggregate` дополнительно хранит диапазон силы своих окон (min/max уровень и урон) → экран сравнения показывает, при какой силе измерены числа, и помечает «устаревшие» (низкая сила относительно текущей). Ограничение зафиксировано в spec.md Assumptions и data-model.md.

### Tests for User Story 2 ⚠️

- [X] T032 [P] [US2] Тесты **recency-aware** ранжирования/рекомендации (по золото/час и опыт/час; scope `Recent` vs `AllTime`; рекомендация по свежему окну при текущей силе; переключение цели; tie-break recent best; пустая история → нет рекомендации) в `tests/TBHStats.Core.Tests/OptimizationServiceTests.cs` — 18 тестов, RED (stub `OptimizationService` бросает NotImplementedException, зелёные после T039) → Artifacts: OptimizationServiceTests.cs
- [X] T033 [P] [US2] Тесты агрегатов этапа: all-time **и** свежее окно (avg/best золото/час, опыт/час, время, темп сундуков; размер окна; диапазон силы окна min/max уровень/урон; исключение partial) в `tests/TBHStats.Core.Tests/StageAggregateTests.cs` — 18 тестов, RED (stub `StageAggregateCalculator`, зелёные после T038) → Artifacts: StageAggregateTests.cs
- [X] T034 [P] [US2] Тесты записи забегов на реальном SQLite (StageRun + сундуки + **HeroSnapshot-контекст силы**, переживание перезапуска) в `tests/TBHStats.Data.Tests/RunRecordingTests.cs` — 10 тестов, RED (skeleton `RunRepository`, зелёные после T037); DataLayerTests 27/27 целы → Artifacts: RunRecordingTests.cs

### Implementation for User Story 2

- [X] T035 [US2] Детектор завершения этапа (прогрессбар/босс в MainZone) → закрытие забега в `src/TBHStats.Capture/StageCompletionDetector.cs` (depends on T023). Чистая машина состояний InProgress/BossEngaged над `RawObservation`: переход в BossEngaged по BossPresent==true ИЛИ StageProgress≥0.99; событие завершения по BossPresent true→false ИЛИ появлению StageTimeSeconds; null = «нет данных» (не триггерит). Capture build clean → Artifacts: StageCompletionEvent.cs, IStageCompletionDetector.cs, StageCompletionDetector.cs, ARCHITECTURE.md §11
- [X] T036 [US2] Сборка и запись `StageRun` + `StageRunChest` (дельты золота/опыта/сундуков за забег, флаг partial) **с контекстом силы `HeroSnapshot` (класс/уровень/урон выбранного героя на момент забега)** в `src/TBHStats.App/Services/RunRecorder.cs` (depends on T035, T026). Stateful singleton: накопление gold/xp (компенсация level-up)/chest-дельт между стартом и завершением (по `IStageCompletionDetector`); HeroSnapshot из последних надёжных значений + резолв класса по GameMechanicsConfig; IsPartial при отсутствии baseline/нерезолвленном классе/смене этапа; AddRunAsync → RecomputeForStageAsync(RecentWindowSize). Интегрирован в `StatsOrchestrator.ProcessFrameAsync` (9-й опц. параметр), DI через `IServiceScopeFactory`. Ограничение v1: триггер завершения зависит от визуальных полей MainZone (null до T049). Solution build clean, Core 151/Data 44 GREEN → Artifacts: RunRecorder.cs, StatsOrchestrator.cs, Composition.cs
- [X] T037 [US2] Полная реализация `IRunRepository` (AddRun, GetRuns, GetSamples, AppendSample) в `src/TBHStats.Data/Repositories/RunRepository.cs` (depends on T011). AddRun сохраняет граф (owned Hero + Chests); GetRuns с Include(Chests), AsNoTracking, порядок по CompletedAtUtc; GetSamples по stageId+DateRange; AppendSample только при IsReliable. Data.Tests 37/37 (RunRecordingTests 10/10, T034 GREEN) → Artifacts: RunRepository.cs
- [X] T038 [US2] Пересчёт `StageAggregate`: all-time **и свежее окно** (последние N non-partial забегов, N из `OptimizationProfile.RecentWindowSize`) — avg/best золото/час, опыт/час, время, темп сундуков + **диапазон силы окна** (min/max уровень/урон из `HeroSnapshot`). Реализован через доменный `IStageAggregateCalculator`; upsert агрегата+ChestRates (RemoveRange/Add, идемпотентно); UpdatedAtUtc ставит репозиторий. Сигнатура `RecomputeForStageAsync(stageId, recentWindowSize, ct)`. EF-миграция полей `StageAggregate`/`OptimizationProfile` выполнена в scaffolding S1b (`AddRecencyAwareAggregation`, аддитивно, FR-013). Data.Tests 44/44 (+7 StageAggregateRecomputeTests) → Artifacts: StageAggregateRepository.cs, IStageAggregateRepository.cs, StageAggregateRecomputeTests.cs, Migrations/*AddRecencyAwareAggregation*
- [X] T039 [US2] `IOptimizationService` — **recency-aware** ранжирование этапов и рекомендация по `OptimizationMetric` и `AggregationScope` (дефолт `Recent` = при текущей силе; `AllTime` для справки); tie-break recent best в `src/TBHStats.Core/Optimization/OptimizationService.cs`. Также реализован доменный `StageAggregateCalculator` (all-time + свежее окно + power-context + chest rates). Core.Tests 151/151 (T032 18/18, T033 18/18 GREEN) → Artifacts: OptimizationService.cs, StageAggregateCalculator.cs
- [X] T040 [US2] Персистентность и переключатель `OptimizationProfile` (золото/час ↔ опыт/час) **+ `RecentWindowSize` (дефолт 10) и `AggregationScope`** в `src/TBHStats.App/Services/OptimizationProfileService.cs` + `SettingsRepository` (EF-миграция полей профиля выполнена в S1b). Singleton через `IServiceScopeFactory`; `GetAsync`/`SaveAsync`/`SetMetricAsync`/`SetScopeAsync`/`SetRecentWindowSizeAsync` (copy-with-change immutable профиля, валидация ≥1). DI зарегистрирован. Solution build clean → Artifacts: OptimizationProfileService.cs, Composition.cs
- [X] T042 [US2] ViewModel сравнения (recency-aware: рекомендация по свежему окну, индикатор силы, переключатели цели и scope) в `src/TBHStats.App/ViewModels/CompareViewModel.cs` (depends on T038, T039, T040). Загрузка агрегатов (`IStageAggregateRepository` через scope) + метки этапов (join Stage/Act/Difficulty) + ранжирование (`IOptimizationService`) по профилю; `CompareStageRow` (метрики по scope, PowerText, IsRecommended, IsStalePower по текущей силе из `IStatsOrchestrator.Current`); команды Load/SetMetric/SetScope/SetWindowSize. Solution build clean → Artifacts: CompareViewModel.cs, CompareStageRow.cs, Composition.cs
- [X] T041 [US2] UI сравнения этапов (таблица с сортировкой по метрике, отметка рекомендованного, переключатель цели) **+ показ контекста силы (диапазон уровня/урона окна) и пометка «устаревших» забегов при низкой силе относительно текущей; переключатель Recent/All-time** в `src/TBHStats.App/Views/CompareView.xaml(.cs)` (depends on T039). Page+CompareHostWindow (Frame.Navigate), RadioButtons метрика/scope, ListView с шапкой, ★-маркер рекомендованного + «⚠ устар.» (не только цветом, A11y), пустое состояние; кнопка «Сравнение» в виджете. Solution build clean → Artifacts: CompareView.xaml(.cs), CompareHostWindow.xaml(.cs), WidgetWindow.xaml(.cs)

**Checkpoint**: US1 и US2 работают независимо; история сохраняется; рекомендация отражает текущую силу отряда (свежее окно), ограничение по непрочитанным факторам (руны/предметы/герои 2–3) задокументировано. Solution build 0/0, Core 151/151, Data 44/44.

---

## Phase 5: User Story 3 — Визуализация трендов графиками (Priority: P3)

**Goal**: графики динамики золото/час, опыт/час, времени прохождения по этапу во времени.

**Independent Test**: имея историю по этапу, открыть график → линии отражают реальные записи, тултипы по точкам показывают значения/время.

### Tests for User Story 3 ⚠️

- [X] T043 [P] [US3] Тесты выборки сэмплов для трендов (диапазон времени, по этапу) в `tests/TBHStats.Data.Tests/SampleQueryTests.cs`. Реальный temp-SQLite: 6 тестов `GetSamplesTests` (диапазон, граничная включительность, фильтр по этапу, пустой, сундуки через Include, переживание перезапуска) GREEN + 5 тестов `PruneSamplesTests` (удаление старше cutoff строго `<`, изоляция этапа, идемпотентность, сохранность StageRun/агрегатов, удаление ненадёжных) RED до T044. Доп. контракт: `IRunRepository.PruneSamplesAsync` + скелет в RunRepository. Build PASS, Data.Tests 50/55 (5 ожидаемо RED) → Artifacts: SampleQueryTests.cs, IRunRepository.cs, RunRepository.cs

### Implementation for User Story 3

- [X] T044 [US3] Выборка/ретенция `MetricSample` для трендов по этапу в `src/TBHStats.Data/Repositories/RunRepository.cs` (depends on T037). `GetSamplesAsync` (выборка для трендов) подтверждён тестами T043; реализован `PruneSamplesAsync` — двухшаговый bulk `ExecuteDeleteAsync` (сначала зависимые `MetricSampleChest`, затем `MetricSample`; SQLite без `PRAGMA foreign_keys` не каскадирует FK при bulk-delete — обосновано чтением `MetricSampleConfiguration` Cascade + `DatabaseInitializer`), строгий cutoff `<`, возврат числа удалённых, агрегаты/StageRun не затронуты. Data.Tests 55/55 GREEN (T043 PruneSamplesTests → GREEN) → Artifacts: RunRepository.cs
- [X] T045 [US3] Экран графиков (LiveCharts2): тренды золото/час, опыт/час, время; тултипы по точкам в `src/TBHStats.App/Views/ChartsView.xaml(.cs)` (depends on T002). 3 `lvc:CartesianChart` (`TooltipPosition=Top`), `ComboBox` выбора этапа (`StageOptions`/`SelectedStage` TwoWay), пустое состояние, `ScrollViewer`; `ChartsHostWindow` (Frame.Navigate) + кнопка «Графики» в `WidgetWindow`. XAML-namespace `using:LiveChartsCore.SkiaSharpView.WinUI` (Context7-verified). Solution build 0/0 → Artifacts: ChartsView.xaml(.cs), ChartsHostWindow.xaml(.cs), WidgetWindow.xaml(.cs), ARCHITECTURE.md §7
- [X] T046 [US3] ViewModel графиков в `src/TBHStats.App/ViewModels/ChartsViewModel.cs` (depends on T044). `ChartsViewModel` (Transient, scoped-репо через `IServiceScopeFactory`, маршалинг `DispatcherQueue`): загрузка non-partial `StageRun` по этапу → `LineSeries<DateTimePoint>` трендов золото/ч, опыт/ч, время(мин) над `CompletedAtUtc`; ось X `Labeler` dd.MM HH:mm; `StageOptions` (этапы с историей, метка через join Stage+Act+Difficulty); `OnSelectedStageChanged`→`RebuildSeriesAsync`. LiveCharts2 2.0.x API (Context7-verified, без моков). DI в Composition.cs. Solution build 0/0, Core 151/151, Data 55/55 → Artifacts: ChartsViewModel.cs, ChartsStageOption.cs, Composition.cs, ARCHITECTURE.md §7

**Checkpoint**: все три истории независимо функциональны. Solution build 0/0, Core 151/151, Data 55/55. US3: экран трендов (золото/ч, опыт/ч, время) по этапу на реальных записях `StageRun` + ретенция сэмплов (`PruneSamplesAsync`).

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: улучшения, затрагивающие несколько историй.

- [X] T047 [P] Сквозная обработка ошибок и структурное логирование (типизированные ошибки, «ожидание» вместо throw, без секретов в логах) во всех слоях. **РЕЗУЛЬТАТ:** аудит логирования всех слоёв (структурные шаблоны и уровни уже корректны — нарушений интерполяции нет); добавлен persistent локальный файловый синк `FileLoggerProvider`/`FileLogger` (`%LOCALAPPDATA%\TBHStats\logs\tbhstats-YYYY-MM-DD.log`, ротация по дате UTC, потокобезопасно, без сетевых зависимостей FR-012), зарегистрирован в `App.BuildHost`; подтверждено отсутствие PII/секретов в логах (путь к БД/логам не логируется, сырой OCR-текст не логируется); типизированные доменные исключения не понадобились (захват/OCR — «ожидание вместо throw», init БД ловится в `App.InitializeAsync`); ADR-016. Build 0/0, Core 151/Data 55/Capture 87 GREEN. → Artifacts: FileLoggerProvider.cs, FileLogger.cs, App.xaml.cs, ARCHITECTURE.md §9/§14, DECISIONS.md ADR-016
- [X] T048 [P] Конфигурация поставки MSIX (packaged) + проверка TFM/WinRT-доступа + опция unpackaged в `src/TBHStats.App/`. **РЕЗУЛЬТАТ:** `Package.appxmanifest` приведён в порядок (Identity `AlgorithmicTrade.TBHStats` v0.1.4.0, MinVersion=10.0.19041.0, VisualElements на существующие ассеты; **только `runFullTrust`, без сетевых capability — FR-012**); csproj: `<WindowsPackageType>None</WindowsPackageType>` как default (dotnet CLI = unpackaged/`dotnet run`/`dotnet test`), MSIX — через VS/MSBuild Desktop; WinRT-типы (WGC/Ocr/Imaging) доступны в обоих режимах через CsWinRT (подтверждено сборкой + Capture.Tests). Честно задокументировано ограничение: packaged-MSIX из headless dotnet CLI падает (MSB4018, known bug `SDK.BuildTools.MSIX`) → упаковка через VS/MSBuild. ADR-017. Build 0/0. → Artifacts: Package.appxmanifest, TBHStats.App.csproj, ARCHITECTURE.md §14, quickstart.md, DECISIONS.md ADR-017
- [X] T049 [US1] Харнесс проверки точности OCR на **реальных скриншотах игры** (фикстуры из `screenshots/`, см. `docs/project/GAME-FACTS.md` §12 «Справочные скриншоты») в `tests/TBHStats.Capture.Tests/OcrFixturesTests.cs`.
  **Фикстуры-источники v1 (Hero/Status/Portal + MainZone) и ожидаемые значения:**
  - `overall.jpg` — комбинированный кадр (Status+Hero+MainZone): gold `54 678` (вверху Hero); Status: класс `Knight`, Level `23`, Exp `2 285 394 / 4 739 962`, Attack Damage `34`; MainZone: «Cleared Stage 2-1. (92s)», next-location `2-2`.
  - `hero_1.jpg` — Hero/Inventory: gold `57 912`, класс `Ranger`, Lv.`25`.
  - `hero_2.jpg` — Hero/Formation (состав отряда, v1 не считывается): gold `60 655`.
  - `status_1.jpg` — Status (basic): класс `Ranger`, Level `25`, Exp `5 714 975 / 6 266 704`, Attack Damage `63`, Current HP `125 / 125`.
  - `status_2.jpg` — Status/Detailed: класс `Ranger`, Level `25`, Exp `5 715 639 / 6 266 704`, Attack Damage `63`, Attack Speed `2,59`.
  - `portal.jpg` — Portal: Difficulty `Normal`, выбран `Act 2`, текущий этап — нода с зелёным флагом `[2-1]`; формат меток `[act-stage]`.
  - `mainzone.jpg` — MainZone: лог-строка «Obtained Long Staff.», next-location `2-2`.
  **Эмпирические находки (для ассертов и парсинга):**
  - ⚠️ Числа отображаются **ПОЛНОСТЬЮ, с ПРОБЕЛОМ как разделителем разрядов** (`54 678`, `2 285 394`), а **НЕ** в сокращённом виде K/M/B → `ValueParser.TryParseAbbreviatedNumber` (regex `[\d,]*\.?\d+`, verified `src/TBHStats.Core/Parsing/ValueParser.cs:24`) **не парсит пробел-разделитель** — реальный gap. Требуется правка парсера (поддержать пробел и неразрывный пробел ` `/` ` внутри числа); эта правка Core делегируется WINUI (не TEST), харнесс её валидирует.
  - Десятичный разделитель — **запятая** (`502,3`, `2,59`); затрагивает DPS/AttackSpeed (не core-метрики; `heroDamage` = Attack Damage целое).
  - По краям standalone-панелей присутствует **фон рабочего стола** (чёрный/произвольный) — ROI должны покрывать только панель, не фон.
  **Реализация:** загрузка JPG → `SoftwareBitmap` (BitmapDecoder); `CapturedFrame(bitmap, ClientSize = размер изображения, ts)`; per-fixture нормализованные `RoiCalibration` (доли изображения, измерить по скриншоту); реальный `OcrReader.ReadAsync` (Windows.Media.Ocr, **без моков**) → `ValueParser`/`FieldExtractor`; ассерт совпадения распознанных/распарсенных значений с ожидаемыми + проверка порога confidence. Скриншоты подключить как `Content`/`CopyToOutputDirectory` в `.csproj`. Если Windows.Media.Ocr недостаточно точен на TBH-шрифте — задокументировать поля-кандидаты на Tesseract-fallback (research R2).
  **РЕЗУЛЬТАТ:** `OcrFixturesTests.cs` (37 кейсов: 23 OCR-уровня + 14 ParsingGap). Реальный `Windows.Media.Ocr` (без моков) корректно читает gold/класс/Level/Exp/AttackDamage/Portal-Difficulty/Act/MainZone-лог/next-location на всех источниках. Выявленный gap — `ValueParser` не парсил пробел-разделитель разрядов — **исправлен** (нормализация `\p{Zs}` между цифрами в `TryParseAbbreviatedNumber`). Задокументированное ограничение OCR: мелкий пиксельный текст меток нод Portal `[2-1]` не читается Windows.Media.Ocr → кандидат на Tesseract-fallback с масштабированием кропа (ADR-005). Capture.Tests **87/87 GREEN**, Core.Tests **151/151 GREEN** (T016 без регрессий), Solution build 0/0. Скриншоты задокументированы в GAME-FACTS §12. → Artifacts: OcrFixturesTests.cs, TBHStats.Capture.Tests.csproj (Content fixtures), ValueParser.cs (фикс пробела-разделителя), GAME-FACTS.md §12
- [X] T050 [P] Обновить документацию `docs/` под реализацию (структура, запуск). **РЕЗУЛЬТАТ:** устранён устаревший статус «код ещё не написан» — `OVERVIEW.md` (шапка + §8 «Текущий статус»: реализованы US1+US2+US3 + Phase 6, тесты Core 165/Data 64/Capture 90, build 0/0), `README.md` (статус + «следующий шаг» → T051/Phase 7), `ARCHITECTURE.md` §8 (`ValueParser` теперь и полные числа с пробелом-разделителем, T049). Структура §8 и quickstart (обновлён в T048) актуальны; §6/§7/§9/§11/§14 обновлены инкрементально в T026/T028/T035–T046/T047/T048/T053. → Artifacts: OVERVIEW.md, README.md, ARCHITECTURE.md §8
- [ ] T051 Прогон acceptance-smoke из `quickstart.md` (перекрытие/сворачивание/перемещение окна, завершение этапа, перезапуск, добавление механики). Включить замеры: **SC-005** (найти лучший этап в экране сравнения <30с), **SC-006** (виджет ≤15% площади экрана в компактном состоянии), **FR-012** (отсутствие исходящих сетевых соединений — данные только локально). **Чек-лист live-прогона:** [`t051-acceptance-checklist.md`](./t051-acceptance-checklist.md) (пошагово, с полями для замеров SC-005/SC-008/idle CPU). **ПРОГРЕСС (статически проверено без игры):** ✅ **FR-012** — в `.cs`-коде нет сетевых вызовов (grep: нет HttpClient/Socket/TcpClient/UdpClient/WebClient/WebRequest/System.Net.Sockets/Http) + манифест без сетевых capability (T048). ✅ **SC-006** — виджет компактен 320×220 (`WidgetWindow.xaml.cs:36 AppWindow.Resize(320,220)`) = 70 400 px² → 3.4% (1920×1080) / 6.7% (1366×768) / 1.9% (2560×1440) — всюду ≤15%. ✅ **OCR-латентность** (часть SC perf) — median 1.3–4.2 мс (T052). ⏳ **ТРЕБУЕТ ЖИВОЙ ИГРЫ (не headless):** следование за окном/перекрытие/сворачивание/перемещение (WGC), детекция завершения этапа (визуальные поля MainZone), перезапуск-персистентность, добавление механики через конфиг+рестарт, SC-005 (<30с на экране сравнения), idle CPU, возобновление ≤5с. Запускать по `quickstart.md` на реальной TBH.
- [X] T052 [P] Проверка производительности (idle CPU, латентность кадр+OCR одной ROI < ~150 мс, возобновление ≤5с). **РЕЗУЛЬТАТ:** `OcrLatencyTests.cs` (3 кейса) — установившаяся латентность `OcrReader.ReadAsync` одной ROI на реальных фикстурах: **median 1.3–4.2 мс, p95 4.2–6.1 мс** (warmup вне замера, 10 итераций, Stopwatch, кадр в памяти). SC <150 мс выполнен с ~115× запасом. Ассерт — regression-bound median<1000мс (анти-флейки), SC логируется. Capture.Tests **90/90** GREEN. ⏳ **idle CPU и возобновление ≤5с — требуют живого приложения, проверяются в T051** (headless не покрыть). → Artifacts: OcrLatencyTests.cs
- [X] T053 [P] Accessibility-проход (конституция §XI, RECOMMENDED): клавиатурная операбельность основных действий (открыть сравнение, переключить цель оптимизации), достаточный контраст живых показателей, поддержка Light/Dark темы Windows, тип сундука различается не только цветом (иконка/подпись) — в `src/TBHStats.App/`. **РЕЗУЛЬТАТ:** KeyboardAccelerator Alt+G/C/K (Графики/Сравнение/Калибровка); `AutomationProperties.Name`/`AutomationId`/`LabeledBy`/`TabIndex`/`IsTabStop` + `LiveSetting=Polite` на интерактивных элементах и живых метриках во всех views (Widget/Compare/Charts/Calibration); цвета через `{ThemeResource}` (Light/Dark системная тема, без хардкод-#RRGGBB); **тип сундука — текстом `ChestType.DisplayName`** («Базовый/Редкий/Легендарный», источник `IGameMechanics.Current.ChestTypes`, не цвет/Id). Build 0/0, Core 165/Data 64/Capture 87 GREEN. Живой a11y (Narrator/Accessibility Insights/High Contrast) — в T051. → Artifacts: WidgetWindow.xaml(.cs), CompareView.xaml, ChartsView.xaml, CalibrationView.xaml, LiveStatsViewModel.cs, ARCHITECTURE.md §7
- [X] T054 [P] Тест расширяемости механик (SC-010): добавление нового типа сундука/класса/вкладки через `GameMechanicsConfig` без изменения схемы; ранее накопленная история остаётся валидной — в `tests/TBHStats.Core.Tests/MechanicsExtensibilityTests.cs` и `tests/TBHStats.Data.Tests/HistoryValidityTests.cs`. **РЕЗУЛЬТАТ:** Core 14 кейсов (расширенный конфиг через ctor: ChestType green/HeroClass mage/Tab event/Act 4→80 этапов; `StageAggregateCalculator` принимает произвольные chestTypeId без хардкода 1-3; домен verified config-driven). Data 9 кейсов на реальном temp-SQLite: схема не меняется (`GetAppliedMigrationsAsync` идентичен до/после), старая история StageRun/StageRunChest/HeroSnapshot валидна после добавления ChestType Id=4, новый тип round-trip переживает «перезапуск», идемпотентность сидинга. Core **165/165**, Data **64/64** GREEN. **Находка (src не тронут):** `DatabaseInitializer.SeedMechanicsAsync:74` жёстко зовёт `CreateDefault()` без перегрузки под кастомный конфиг — расширение через стандартный сидинг-путь ограничено (для v1 приемлемо: правка `CreateDefault()` = config-data, не enum/schema, ADR-009; внешне-редактируемый конфиг — за рамками v1). → Artifacts: MechanicsExtensibilityTests.cs, HistoryValidityTests.cs

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

- **Всего задач**: 54 (T001–T054) + T006c + 4 планирования (P001–P004). Phase 7 (E2E QA-харнесс, T055–T061) **исключена из объёма** — see ниже.
- **По историям**: Setup 4 · Foundational 14 (T005–T018, +T006c) · US1 13 (T019–T031) · US2 11 (T032–T042) · US3 4 (T043–T046) · Polish 8 (T047–T054)
- **Тестов**: 13 unit/integration (T016–T021, T032–T034, T043, T049, T052, T054) — реальный SQLite + фикстуры-скриншоты, без моков
- **MVP**: Phase 1 + Phase 2 + US1 (T001–T031)
- **Параллельных групп**: см. Parallel Opportunities (макс. выигрыш в Foundational и блоках тестов)
- **Исключено из объёма (2026-06-01)**: Phase 7 — детерминированный E2E UI-харнесс поверх живой игры (FlaUI + SendInput, FR-022…FR-027, сценарии TS-00…TS-10). E2E-тесты признаны ненужными; задачи T055–T061 удалены.

---

## P2 Backlog (отложено / выявлено на живой калибровке 2026-06-01)

> Зафиксировано по итогам живого прогона на реальной игре. Не входит в текущий объём MVP/v1; вернуться после стабилизации live-статистики. Контекст и решения — ADR-019, GAME-FACTS §2/§5–§6, ARCHITECTURE §3/§5/§9.

- [X] **T062 — Визуальный подсчёт сундуков.** «Точки» под иконками сундуков **графические, не текст** → OCR их не считает. Реализовать визуальный детектор: подсчёт заполненных точек под иконкой каждого типа (анализ изображения). Инфраструктура `chest:<тип>@N` + `IChestLayoutResolver` (раскладка/позиция) сохраняется — меняется источник счёта (визуальный вместо OCR). [Story: US1/US2]
  **РЕЗУЛЬТАТ (эволюция по итогам живой калибровки):** визуальный счёт точек реализован, затем дважды доработан под реальную игру:
  - **ADR-021** — базовый детектор `IChestDotCounter`/`ChestDotCounter`: luminance Bgra8 → run-ы тёмных колонок = заполненные точки (тёмный квадрат = заполнено, белый = пусто; число пустых варьируется — ёмкость качается в Rune). Тест `chests.jpg` red=1/blue=1/brown=2.
  - **ADR-022** — идентификация типа по **цвету фона плашки** (`ChestType.PanelColor` якоря red/blue/brown в `CreateDefault`, EF-ignore, без миграции) вместо позиционной `@N`-схемы: исправлен фантом (ключ `chest:red@2` над синей плашкой → blue).
  - **ADR-023 (актуальный)** — **зонный детектор** `IChestZoneAnalyzer`/`ChestZoneAnalyzer`: одна ROI `chestZone` на всю группу → горизонтальная сегментация плашек по цвету → масштабонезависимый счёт точек в нижнем поясе. Устраняет промахи переуплотнения группы и недосчёт −1 от ручной обрезки. `FieldExtractor`: `chestZone` — приоритетный путь, per-ROI `chest:*` — fallback. Калибровка: проверка `chestZone`/`chest:*`-ROI через детектор (не OCR). Верификация на **двух реальных фикстурах**: `chests.jpg` {red:1,blue:1,brown:2}, `main.jpg` {blue:3,brown:3, red отсутствует}. ADR-018/021/022 → Superseded by ADR-023.
  - **Известное ограничение (unverified):** счёт сворачивает ряды точек в один профиль → корректен для ≤5 точек (один ряд); 2-й ряд (6+ сундуков одного типа) не суммируется — нужна фикстура с 6+ для реализации/проверки. Живая точность (DPI/масштаб/тема) — на прогоне T051.
  - **СТАТУС (2026-06-01, решение пользователя): обнаружение сундуков ВРЕМЕННО ОТКЛЮЧЕНО** в рабочем пайплайне и UI (не оправдывает затраты на доработку сейчас; вернуться позже). Гейт `FieldExtractor.ChestDetectionEnabled=false` (все chest-ROI пропускаются → `Chests` пуст); в виджете строки «Сундуки» и «Сундуки/ч» скрыты (`Visibility=Collapsed`). Детекторы (ChestZoneAnalyzer/ChestPanelAnalyzer/ChestDotCounter) и их тесты на фикстурах (chests.jpg 1/1/2, main.jpg 3/3) сохранены как база. Возврат — флаг `true` + раскрытие строк виджета + доработка multi-row (по-рядный счёт; нужна чистая фикстура 6+).
  - Build 0/0; Core **262/262**, Capture **110/110**, Data **64/64**. → Artifacts: Chests/{IChestDotCounter,ChestDotCounter,ChestDotCountResult,IChestPanelAnalyzer,ChestPanelReading,IChestZoneAnalyzer,ChestZoneAnalyzer}.cs, FieldExtractor.cs, Models/PanelColor.cs, ChestType.cs, GameMechanicsConfig.cs, Entities/ChestTypeConfiguration.cs, CalibrationViewModel.cs, Composition.cs, тесты ChestDotCounter/ChestPanelAnalyzer/ChestZoneAnalyzerFixturesTests.cs, DECISIONS.md ADR-021/022/023, ARCHITECTURE.md §3/§4/§7/§9/§11, GAME-FACTS.md §5/§6
- [X] **T063 — Визуальная детекция прогресса этапа (`stageProgress`) + время этапа + статистика.** Прогрессбар идёт справа налево; определять % пройденного пути по изображению. На его основе — **сигнал завершения этапа, расчёт времени этапа и запись статистики** (инфо-лента MainZone редко показывает «(Ns)», OCR-стратегия по `stageTime` ненадёжна). Питает сегментацию забегов (FR-007, ADR-012). [Story: US2]
  **Эмпирика (4 фикстуры `screenshots/progress_*.jpg`, ROI bar fracX≈[0.862..0.985], fracY≈0.84, H≈0.03):** заливка растёт справа-налево; **фиолетовый (165,77,213)** = прогресс пути (full=стрелка слева=0.95, впереди босс), **синий (95,199,255)** = бой с боссом этапа (BossPresent=true, progress=1.0), пустой тёмный трек (~4-23) = начало этапа (0/false). После убийства босса бар сбрасывается в begin → BossPresent true→false = сигнал завершения (ADR-012).
  - **Phase 1 ✅ (2026-06-02)** [`windows-capture-ocr-specialist` + `dotnet-test-writer`]: `IStageProgressReader`/`StageProgressReader` (Capture, ADR-024) → `(double? Progress, bool? BossPresent)` колоночным цветовым анализом; включён в `FieldExtractor` (вместо `null`-заглушки) + флаг `StageProgressDetectionEnabled=true`; DI в Composition. Фикстур-тесты: begin→0.0/false, half→0.49/false, full→0.91/false, boss→1.0/true. Build 0/0; Capture 114/114. → Artifacts: Progress/{IStageProgressReader,StageProgressReader,StageProgressReading}.cs, FieldExtractor.cs, Composition.cs, StageProgressReaderFixturesTests.cs, DECISIONS.md ADR-024, ARCHITECTURE.md §3.
  - **Phase 2 ✅ (2026-06-02)** [`dotnet-winui-developer`]: сегментный таймер этапа в `StatsOrchestrator` (`_stageSegmentStartUtc`/`_stageSegmentRef`, сброс при смене `_lastKnownStage`; `_lastKnownStageProgress`/`_lastKnownBossPresent` держат последнее известное); три новых поля `StageProgress`/`BossPresent`/`StageElapsedSeconds` в `LiveStatsSnapshot` (+`Empty`, оба места публикации); VM-свойства `StageProgress`/`StageProgressText` (босс→«Босс», иначе `P0`, иначе «—»)/`StageElapsedText` (хелпер `FormatElapsed` ч/м/с) в `LiveStatsViewModel`; строки виджета «Прогресс этапа» (% + `ProgressBar` Min0/Max1, Row 9) и «Время этапа» (Row 10), статус-строка → Row 11. Build 0/0 (вся sln, App включительно). → Artifacts: LiveStatsSnapshot.cs, StatsOrchestrator.cs, ViewModels/LiveStatsViewModel.cs, Views/WidgetWindow.xaml, ARCHITECTURE.md §7/§9.
    - **Fix (2026-06-02): таймер синхронизирован с прогрессом.** Раньше таймер сбрасывался по смене `_lastKnownStage` (OCR `NextLocation`, запаздывает) → «Время этапа» рассинхронено с «Прогресс этапа» (шло, когда прогресс уже сброшен). Теперь сброс по **падению прогресса** (`StageProgressDropThreshold=0.10`): любое заметное падение = граница сегмента → таймер с нуля (вариант 2). Падение **после босса** (`BossPresent==true` или прогресс ≥`BossProgressThreshold=0.99`) = этап пройден → длительность сохраняется в новое поле `LastCompletedStageSeconds` и показывается в скобках для сравнения («1м 23с (2м 05с)»); падение без босса (рестарт) длительность не обновляет. `_stageSegmentRef` удалён; сегмент сбрасывается при потере окна (без ложного «завершения по боссу»). `FormatElapsed`: 0→«0с». Build 0/0. → Artifacts: StatsOrchestrator.cs, LiveStatsSnapshot.cs (+`LastCompletedStageSeconds`), LiveStatsViewModel.cs (`BuildStageElapsedText`), WidgetWindow.xaml (tooltip), ARCHITECTURE.md §9.
    - **Fix 2 (2026-06-02): время прохождения только для полностью наблюдаемого этапа.** Баг: виджет запущен на фазе босса → сегмент стартован «с середины» → сохранялось 30 с вместо реальных 6+ мин. Введён флаг `_segmentStartedFromReset`: `_lastCompletedStageSeconds` сохраняется ТОЛЬКО если завершаемый сегмент был начат от наблюдаемого падения прогресса (начало этапа увидено). Сегмент, стартованный fallback-ом при первом кадре (`_stageSegmentStartUtc is null`) или после возврата окна — `false` → его длительность при завершении по боссу не сохраняется; следующий сегмент (от реального сброса) учитывается корректно. Сброс флага в блоке потери окна. Build 0/0. → Artifacts: StatsOrchestrator.cs, ARCHITECTURE.md §9.
  - **Phase 3 ✅ (2026-06-02)** [`dotnet-winui-developer` + `efcore-sqlite-specialist`]: резолв `StageId` из `NextLocation.Previous()` (та же `StageRef`, что и «Этап» виджета) через config-driven `GameMechanicsConfig.ResolveStageId(StageRef)` (Acts по Number / Difficulties по Key OrdinalIgnoreCase / Stages по ActId+DifficultyId+Number → `Stage.Id`, null если нет); `StatsOrchestrator` проставляет `sample.StageId` после `Validate` до `RunRecorder.OnFrameAsync` (разблокирует запись забегов; `ObservationValidator` оставлен без изменений — у него нет конфига by design). Сброс `RunRecorder` при потере окна: guard `_windowLostHandled`, `Reset()` один раз на эпизод при `State==NotFound` (Waiting≠резет — игра жива). Верификация Charts/Compare: FK-выравнивание config↔БД доказано (config `Stage.Id` 1..60 == засеянные `DatabaseInitializer` Id → резолвнутый StageId — валидный FK для `StageRun`); путь Charts (`IRunRepository.GetRunsAsync` non-partial) / Compare (`IStageAggregateRepository.GetAllAsync`) наполняется после резолва. **Known-limitation:** запись `StageRun` идёт только по `StageCompletionEvent` (BossPresent/StageProgress) → требует калибровки ROI `stageProgress` на живой игре (T049/T051); механизм готов и покрыт тестами. Build 0/0; Core **271/271** (+9 резолвер), Data **74/74** (+10 FK/round-trip). → Artifacts: Mechanics/GameMechanicsConfig.cs (ResolveStageId), Services/StatsOrchestrator.cs, Core.Tests/GameMechanicsConfigResolveStageIdTests.cs, Data.Tests/ResolverFkAlignmentTests.cs, ARCHITECTURE.md §6/§9.
- [X] **T064 — `stageId`: текущий этап по зелёному флагу карты Portal.** Текущий этап отмечен нодой с зелёным флагом, позиция меняется. Нужна визуальная локализация маркера во всей зоне Portal + OCR его метки («[2-1]»), вместо OCR всей карты. До реализации текущий этап выводится как `nextLocation − 1` (MainZone). [Story: US2]
  - **ЗАКРЫТО (2026-06-02, решение пользователя):** текущая локация корректно определяется через ROI `nextLocation` (MainZone) → текущий этап = `nextLocation.Previous()` (см. T063 Phase 3, `GameMechanicsConfig.ResolveStageId`). Визуальная локализация зелёного флага на карте Portal не требуется — fallback достаточен. Возврат к маркеру Portal — при необходимости более точного позиционирования.
- [ ] **T065 — (опц.) Слот-зависимая детекция разделов по заголовкам вкладок (anti-contamination).** Сейчас (ADR-019) все поля читаются каждый кадр без gating; если в слоте открыт другой раздел (Stash/Rune вместо Status), область поля может дать чужое значение (снижается парсером/sanity). Усиление: добавить отдельные ROI на **название каждой вкладки** → определять, какие разделы сейчас открыты, и принимать поле только если в слоте нужный раздел. MainZone исключена — отображается всегда и не перекрывается, маркер ей не нужен. [Story: US1]
- [X] **T066 — Надёжность мелких полей OCR (heroLevel).** Очень мелкие зоны (heroLevel «26/27») распознаются нестабильно даже после апскейла `MinOcrDimension=96`. Рассмотреть доп. предобработку (бинаризация/контраст) или Tesseract-fallback per-ROI (ADR-005). [minor]
  - **ЗАКРЫТО (2026-06-02, решение пользователя):** проверено в предыдущей сессии — неактуально. Текущая стратегия OCR (апскейл `MinOcrDimension=96`) достаточна для рабочего пайплайна; доп. предобработка/Tesseract-fallback не оправданы. Возврат — при появлении реальной проблемы на живой игре.
- [X] **T067 — Cleanup: убрать временный `[Diag]`-блок из `StatsOrchestrator`.** Диагностический OCR-лог (сырой текст activeTab/gold/xp/heroLevel + размеры кадра) добавлялся для отладки live-распознавания; удалить после стабилизации (вместе с инъекцией `IOcrReader` в оркестратор, если она больше не нужна). [tech-debt]
  - **ВЫПОЛНЕНО (2026-06-02):** удалён временный блок «ВРЕМЕННАЯ ДИАГНОСТИКА … КОНЕЦ ВРЕМЕННОЙ ДИАГНОСТИКИ» (per-ROI сырой OCR activeTab/gold/xp/heroLevel + размеры кадра); постоянный throttled health-лог («Захват активен, но достоверных данных нет…») сохранён. `IOcrReader` в `StatsOrchestrator` использовался только диаг-блоком → удалена инъекция (поле `_ocrReader`, параметр ctor, `ArgumentNullException`, присваивание, неиспользуемый `using TBHStats.Capture.Ocr`) и аргумент `ocrReader:` из фабрики в `Composition`. Глобальная регистрация `IOcrReader` сохранена (нужна `FieldExtractor`/`TabDetector`). Build 0/0 (вся sln). → Artifacts: Services/StatsOrchestrator.cs, Services/Composition.cs
- [X] **T068 — Дизайн виджета в стиле игры + чекбокс «поверх окон» + кнопка увода окна игры за экран.** Три связанных направления оформления/поведения виджета (`TBHStats.App`, FR-014…FR-016). **Scope переопределён пользователем 2026-06-02:** пункты «Примагничивание (snap/docking)» и «Адаптивная ширина» исключены; вместо них — чекбокс topmost и кнопка скрытия окна игры (ниже). **ЗАКРЫТО (2026-06-02, релиз v0.2.0):** все фазы (1A/1B/2/4/4b/5) + доработки дизайна + перевод UI на английский + персист геометрии окна «Сравнение» + фикс Gold/Exp в сравнении выполнены и зарелизены (tag v0.2.0, коммиты d5bf96b/7c346b9). Сборка sln 0/0, Data-тесты 97/97. Детали — в под-пунктах ниже.
  - **Визуальный стиль.** Оформить весь внешний вид виджета — палитра, шрифты, иконки, рамки, фон, отступы — в визуальном стиле вкладки **STATUS** игры Task Bar Hero (зона статов героя; референс — `screenshots/status_1.jpg`, `docs/project/GAME-FACTS.md` §12). Палитра: тёмный фон, бордовый заголовок, пергаментная панель с коричневой рамкой, золотые лейблы / кремовые значения. Шрифт — похожий пиксельный (бандл OFL-`.ttf`, напр. Pixelify Sans/VT323/Silkscreen). **Единый игровой скин** (фиксированная палитра, НЕ адаптив Light/Dark — отступление от T053 зафиксировано в ADR-026); ресурсы стиля — через словарь ресурсов (`Themes/GameWidgetStyles.xaml`), без хардкод-цветов в разметке `WidgetWindow.xaml`.
  - **Чекбокс «поверх всех окон» (FR-015).** Вернуть и сделать управляемым режим topmost: чекбокс в виджете переключает `OverlappedPresenter.IsAlwaysOnTop`; значение персистится в `WidgetSettings.AlwaysOnTop` (поле уже есть) и восстанавливается при запуске. Снимает текущее жёсткое отключение topmost в `WidgetWindow.xaml.cs:ApplyWidgetSettings`.
  - **Кнопка увода окна игры за экран (без потери статистики).** Кнопка-тоггл уводит окно TBH за пределы видимой области (`SetWindowPos` off-screen) и возвращает на исходную позицию. Окно остаётся `Visible` → DWM компонует → WGC-захват и сбор статистики НЕ прерываются (verified: `CaptureSession`/`GameWindowTracker` — minimized→Waiting ломает захват, off-screen-move сохраняет). Сворачивание/скрытие (`SW_MINIMIZE`/`SW_HIDE`) НЕ используются (останавливают захват). Манипуляция окном игры — window-management (`SetWindowPos`), НЕ инъекция ввода; observe-only по вводу сохраняется (зафиксировано в ADR-026). Реализация — новый `IGameWindowController` в `TBHStats.Capture/WindowTracking/`. [Story: US1/UI]
  - **Персист геометрии окон между перезапусками (FR-016, добавлено пользователем 2026-06-02).** Окно виджета УЖЕ сохраняет позицию/размер (`WidgetWindow.xaml.cs` `OnAppWindowChanged`→`SaveWidgetSettingsAsync` + `ApplyWidgetSettings`, через `WidgetSettings`). Дополнительно окно **«Сравнение»** (`CompareHostWindow`) ДОЛЖНО сохранять и восстанавливать позицию/размер (сейчас — жёстко `820×600`, без персиста). Реализация — обобщённый механизм `WindowPlacement` (Core-модель `PosX/PosY/Width/Height` + ключ окна; Data-таблица + миграция + методы репозитория `Get/SaveWindowPlacementAsync(key)`), переиспользуемый и для будущих окон (Charts/Calibration). `WidgetSettings`-персист виджета НЕ трогаем (работает). [Story: US1/UI, FR-016]
  - **Phase 1 ВЫПОЛНЕНО (2026-06-02):** (1A) `IGameWindowController`/`GameWindowController` в `TBHStats.Capture/WindowTracking/` — off-screen увод/возврат через `SetWindowPos` (флаги `SWP_NOSIZE|NOZORDER|NOACTIVATE`, координаты −32000,−32000), запоминание исходного rect, идемпотентность, `false` без исключений на закрытом окне. (1B) бандл `Assets/Fonts/PixelifySans.ttf` (SIL OFL, +`OFL.txt`) + словарь `Themes/GameWidgetStyles.xaml` (палитра STATUS, FontFamily `GamePixelFont`, стили `GameHeader/GameLabel/GameValue/GameCaption`-Text, `GamePanel/GameHeader/GameInnerPill`-Border, `GameButtonStyle`, `GameCheckBoxStyle`) + merge в `App.xaml`. Build всей sln 0/0. ARCHITECTURE.md §11 дополнен контрактом. **Phase 2 ВЫПОЛНЕНО (2026-06-02):** рестайл `WidgetWindow.xaml` под игровой скин (4 строки: заголовок-плашка «TBHStats», статус-плашки, пергаментная панель статов, строка кнопок); чекбокс «Поверх окон» (снято принудительное `IsAlwaysOnTop=false`, применяется/персистится `WidgetSettings.AlwaysOnTop`, guard `_applyingSettings`); кнопка-тоггл «Скрыть/Вернуть игру» (через `IGameWindowController`+`IGameWindowTracker`, auto-`Restore` на закрытии виджета); DI-регистрация `IGameWindowController` в `Composition`; ADR-026 + ARCHITECTURE §7 + README. Build sln 0/0. Вся прежняя функциональность сохранена.
  - **Доработки дизайна (запрос пользователя 2026-06-02, Phase 4–5):** (1) пиксельный шрифт Pixelify Sans плохо читает цифры → заменить на более читаемый для цифр (широкий/блочный, напр. Silkscreen/VT323/Jersey); (2) скрыть стандартный Windows-заголовок окна виджета (borderless), сохранив перемещение и закрытие; (3) заголовок виджета — как заголовки вкладок игры (STATUS/HERO/PORTAL): крупнее, широкие буквы (uppercase + большой `FontSize` + `CharacterSpacing`); (4) значения статистики сливаются (светлый текст на светлой пергаментной подложке) — в оригинале STATUS шрифт шире, с тёмной окантовкой символов, подложка темнее → затемнить `GamePanelBackgroundBrush` + тёмная окантовка текста (reusable outlined-text) + более широкий/жирный шрифт; (5) полоса прогресса этапа — зелёная; (6) окно «Сравнение» — стиль совпадает с виджетом (рестайл `CompareHostWindow`/`CompareView` под игровой скин, вместе с персистом геометрии); (7) при закрытии основного окна виджета автоматически закрывать и дочерние окна «Сравнение» (`CompareHostWindow`) и «Калибровка» (`CalibrationHostWindow`) — трекать открытые экземпляры, закрывать в `OnWidgetClosed`. [Story: US1/UI]
  - **Phase 4 ВЫПОЛНЕНО (2026-06-02):** (1) шрифты заменены — `DotGothic16` (заголовок/лейблы, широкий блочный) + `VT323` (значения/цифры, моноширинный, чёткие цифры); Pixelify Sans удалён. (2) Windows title bar скрыт (`OverlappedPresenter.SetBorderAndTitleBar(true,false)`, `IsMaximizable/Minimizable=false`); перемещение — Win32 `WM_NCLBUTTONDOWN`/`HTCAPTION` по `PointerPressed` на header (`[LibraryImport]`); кнопка ✕ закрытия (фикс: `IsWithinButton` обходит визуальное дерево — иначе клик по ✕ запускал бы drag). (3) заголовок «TBHSTATS» uppercase, `FontSize=22`, `CharacterSpacing=150`, окантованный. (4) подложка затемнена (`GamePanelBackgroundBrush=#3D2B1A`), значения через переиспользуемый `OutlinedTextBlock` (8 слоёв окантовки + центр), яркие лейбл/значение-кисти. (5) полоса прогресса зелёная (`GameProgressBarStyle`/`GameProgressBrush=#4BBF40`). (7) дочерние окна Compare/Calibration трекаются (`_compareWindow`/`_calibrationWindow`, Activate-если-жив + подписка на Closed) и авто-закрываются в `OnWidgetClosed`. **Весь видимый UI виджета переведён на английский** (русские лейблы + DotGothic16/VT323 без кириллицы ломали бы рендер; «Опыт»→`Exp`, «Опыт/ч»→`Exp/h`, динамика VM: `/ч`→`/h`, время `ч/м/с`→`h/m/s`, StatusText, «Босс»→`Boss`). Build sln 0/0. ARCHITECTURE §7 обновлён. Внешний словарь `GameWidgetStyles.xaml` подстроен пользователем. **Требует визуальной проверки на живом запуске** (рендер шрифтов, borderless drag/✕, окантовка, зелёный прогресс, авто-закрытие окон). → Artifacts: src/TBHStats.App/Views/Controls/OutlinedTextBlock.cs, Views/WidgetWindow.xaml(.cs), ViewModels/LiveStatsViewModel.cs, Themes/GameWidgetStyles.xaml, Assets/Fonts/{DotGothic16-Regular,VT323-Regular}.ttf
  - **Phase 4b ВЫПОЛНЕНО (2026-06-02):** шрифт заменён на **Baloo 2** (OFL-аналог CookieRun — CookieRun бандлить нельзя, лицензия запрещает в game-related ПО) во всех font-ресурсах словаря; DotGothic16/VT323 удалены (csproj+диск), `OFL.txt`=Baloo 2. Подложка панели осветлена до бежево-орехового (`GamePanelBackgroundBrush=#C8AB78`), лейблы — глубокий янтарный (`#7A4A12`) для контраста, значения кремовые с тёмной окантовкой. Строка статуса унифицирована (лейбл «Status» + `OutlinedTextBlock`-значение, `LastUpdateText` — вторичная подпись). Build sln 0/0.
  - **Phase 5 ВЫПОЛНЕНО (2026-06-02):** обобщённый персист геометрии окон — Core `WindowPlacement` (ключ окна + Pos/Width/Height), Data: `WindowPlacementConfiguration` (PK `WindowKey`), `DbSet`, миграция `20260602174938_AddWindowPlacement`, `ISettingsRepository.Get/SaveWindowPlacementAsync` (upsert), прокси `ScopedSettingsRepositoryProxy` делегирует; 4 теста на реальном SQLite (Data.Tests 93 passed); data-model.md + ARCHITECTURE §6. Окно «Сравнение» (`CompareHostWindow`): персист геометрии (ключ «compare», load на активации + save с debounce 500мс), рестайл под игровой скин (header-плашка «STAGE COMPARISON», Baloo 2, бежевые панели, `GameButtonStyle`/`GameLabelTextStyle`, таблица в игровом стиле), borderless title bar как у виджета (drag через `WM_NCLBUTTONDOWN` + `IsWithinButton`, ✕ `GameCloseButtonStyle`), весь UI переведён на английский (`Stage Comparison`, `Gold/h`, `Exp/h`, `Runs`…). ARCHITECTURE §7. Build sln 0/0. → Artifacts: src/TBHStats.Core/Models/WindowPlacement.cs, src/TBHStats.Data/Entities/WindowPlacementConfiguration.cs, src/TBHStats.Data/Migrations/20260602174938_AddWindowPlacement.cs, src/TBHStats.App/Views/CompareHostWindow.xaml(.cs), Views/CompareView.xaml, ViewModels/CompareViewModel.cs, tests/TBHStats.Data.Tests/WindowPlacementRepositoryTests.cs → Artifacts: src/TBHStats.Capture/WindowTracking/IGameWindowController.cs, src/TBHStats.Capture/WindowTracking/GameWindowController.cs, src/TBHStats.App/Themes/GameWidgetStyles.xaml, src/TBHStats.App/App.xaml
  - **Доработки 2 (запрос пользователя 2026-06-02, выполнено):** (1) текст виджета унифицирован под шрифт/размер/стиль значений Compare (Baloo 2 SemiBold 12, лейблы+значения), виджет компактнее (`RowSpacing 5→2`, паддинги/маржины уменьшены, окантовка `1.2→1.0`, стартовый размер `340×310→320×270`); (2) заголовки таблицы Compare осветлены (`GameTableHeaderTextStyle`, золото вместо тёмного амбера — сливались с бордовой шапкой); (4) полоса прогресса этапа в виджете — бордовая (`GameProgressBrush #4BBF40→#8B2222`, как заголовок). Build sln 0/0.
  - **Баг-фикс Gold/Exp=0 в Compare (выполнено, confirmed по реальной БД пользователя):** среднее золото/опыт за забег показывались 0. Две причины: (а) `StageAggregateRepository.CopyScalarFields` НЕ копировал `AvgGoldGained/AvgXpGained/RecentAvgGoldGained/RecentAvgXpGained` при обновлении агрегата → обнулялись даже при новых забегах; (б) существующие агрегаты не пересчитаны после миграции `AddAvgGained` (колонки default 0). Фикс: (а) `CopyScalarFields` копирует все 4 gained-поля; (б) идемпотентный self-heal `DatabaseInitializer.BackfillStaleAggregatesAsync` из `App.InitializeAsync`. Тесты Data 97 passed (+4 на реальном SQLite). ARCHITECTURE §6. **Эффект — при следующем запуске приложения.** → Artifacts: src/TBHStats.Data/Repositories/StageAggregateRepository.cs, src/TBHStats.Data/DatabaseInitializer.cs, src/TBHStats.App/App.xaml.cs, tests/TBHStats.Data.Tests/AggregateBackfillTests.cs
