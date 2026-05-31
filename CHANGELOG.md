# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.3] - 2026-05-31

### Added
- **US2**: история этапов и recency-aware выбор оптимального для фарма (Phase 4, T032–T042) (b54e008)

## [0.1.2] - 2026-05-31

### Added
- **US1**: живая статистика текущего забега — виджет в реальном времени (золото/час, опыт/час, сундуки/час, класс/уровень/урон героя, текущий этап) на основе визуального захвата (WGC) + OCR (Phase 3, T019–T031) (efd7c2a)
  - **Core/Optimization**: `MetricsCalculator` — живые темпы по надёжным интервалам (level-up через XpToLevel, сундуки по положительным дельтам, периоды недоступности не занижают результат; FR-006/005a) + `LiveRates`.
  - **Core/Parsing**: `ObservationValidator` — confidence-фильтр, монотонность золота, EXP-reset как level-up, транзиентные точки сундуков → надёжный `MetricSample` (FR-005/010).
  - **Core/Models**: общие контракты `RawObservation`, `TabRef` (граница Core↔Capture, Single Source of Truth).
  - **Capture/Tabs**: `TabDetector` + `TabNameMatcher` — fuzzy-матч активной вкладки (Левенштейн) + OCR ROI `activeTab` (FR-002a).
  - **Capture**: `FieldExtractor` — кадр + активная вкладка + ROIs → `RawObservation` с фильтрацией по источнику (MainZone всегда + поля активной вкладки; FR-002b).
  - **App/Services**: `StatsOrchestrator` — фоновая петля и машина состояний NotFound/Capturing/Waiting; `LiveStatsSnapshot`; `ScopedSettingsRepositoryProxy`; composition root.
  - **App/UI**: `WidgetWindow` — компактный перемещаемый виджет (опц. поверх окон, сохранение позиции/размера; FR-015/016); `LiveStatsViewModel` (состояния «игра не найдена»/«ожидание»/устаревание); `CalibrationView` (разметка ROI).
  - **Data**: полная реализация `ISettingsRepository` (WidgetSettings/OptimizationProfile/RoiCalibration) на реальном SQLite.

### Tested
- 72 новых unit-теста (MetricsCalculator 18, ObservationValidator 34, TabDetector 20) на реальных объектах, без моков; всего 192/192 PASS (Core 115, Capture 50, Data 27). Сборка решения — 0 ошибок, 0 предупреждений.

## [0.1.1] - 2026-05-31

### Added
- **Foundational**: завершить Phase 2 — доменные history-модели, EF Core/SQLite, слой захвата WGC/OCR (666a4c4)
- **Capture**: добавить RoiMapper нормализованных долей ↔ пикселей (T015) (0843d7a)
- **Core**: добавить IValueParser для idle-чисел, времени и id этапа (T008) (4d298d8)
- **Capture**: реализовать IGameWindowTracker поиска и видимости окна игры (T012) (e334bb9)
- **Core**: добавить GameMechanicsConfig с дефолтным сидом и IGameMechanics (T007) (f573642)
- **Core**: добавить справочники, config-модели и доменные енумы (T006) (d482477)
- **Core**: добавить доменные енумы и value-объект StageRef (T005) (7455e0b)

## [0.1.0] - 2026-05-31

### Added
- **release**: добавить .NET release-автоматизацию (release.ps1 + /release) (0315409)
- **Setup**: инициализировать .NET 8 решение TBHStats (Phase 1) (cb92934)

### Other
- **spec**: добавить спецификацию и документацию проекта TBHStats (feature 001) (2f1f016)
- @ chore(repo): инициализировать репозиторий TBHStats (fdb253b)

