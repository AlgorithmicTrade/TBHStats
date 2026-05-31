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
