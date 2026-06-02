# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.9] - 2026-06-01

### Added
- **Chests**: визуальный детектор сундуков по цвету плашки + счёт точек (временно отключён) (3109258)
  - **Capture/Chests** (новое): `ChestDotCounter` (счёт точек по яркости), `ChestPanelAnalyzer` (тип по цвету плашки), `ChestZoneAnalyzer` (зонная локализация группы плашек + счёт по рядам); эволюция решения ADR-021 → ADR-022 → ADR-023.
  - **Core**: тип `PanelColor` + якоря цвета плашек (brown/blue/red) в `GameMechanicsConfig` (config-driven, ADR-009); EF-ignore — без миграции, история валидна.
  - **App**: проверка chest-ROI в калибровке через визуальный детектор (тип+точки), а не OCR; строки «Сундуки» и «Сундуки/ч» в виджете скрыты (`Visibility=Collapsed`).
  - **Отключено по решению пользователя**: `FieldExtractor.ChestDetectionEnabled=false` — на живой игре подсчёт требует доработки (многорядность 6+, плотные ряды, масштаб окна); код детекторов и тесты на реальных фикстурах (chests.jpg 1/1/2, main.jpg 3/3) сохранены как база для возврата. Сборка 0/0; тесты Core 262 / Capture 110 / Data 64.

## [0.1.8] - 2026-06-01

### Fixed
- **LiveStats**: устранить скачки опыта/час, заморозку при трате золота и misread урона (de01016)
  - **Core/MetricsCalculator**: structural guard разрыва по переходу `HeroLevel` (легитимно 0 или +1) + магнитудный guard (`xpDelta > XpToLevel`) — разрывные межсэмпловые XP-дельты (смена героя/этапа, misread) исключаются из темпа.
  - **Core/HeroSwitchDetector** (новый): детекция смены героя по падению уровня и смене `XpToLevel` без сигнатуры level-up — надёжный сигнал, сопутствующий опыту (срабатывает даже при null `HeroLevel`/класса на кадре).
  - **Core/RateOutlierDetector** (новый): отброс выброса ставки опыт/ч (`raw > max(EMA×6, 5M)`) — переходные OCR-misread XP не отравляют кумулятив, независимо от триггера.
  - **App/StatsOrchestrator**: сброс окна темпов при смене героя; живой темп по короткому скользящему окну `LiveRateWindowSeconds=90 с` — быстрая сходимость и реакция на смену этапа вместо лага кумулятивного 5-мин буфера.
  - **Core/ObservationValidator**: золото — расходуемый баланс (трата на руны/апгрейды/магазин), монотонность убрана из критерия надёжности — устранено зависание виджета после траты золота (ADR-020).
  - **Capture/OcrReader**: паддинг мелких кропов перед апскейлом (`min(w,h) < 40` → 6px) — починен misread десятичной запятой в уроне («126,9» больше не «12619»).

## [0.1.7] - 2026-06-01

### Added
- **LiveStats**: наладить live-распознавание (мульти-панель, OCR-фиксы) и расширить виджет (fb4aac0)
  - **Capture/FieldExtractor**: снят tab-gating — разделы игры открыты ОДНОВРЕМЕННО (3 слота над MainZone + оверлей Rune), поэтому читаются все калиброванные поля каждый кадр (ADR-019); это и блокировало чтение `gold`/`xp`/`heroLevel`. Добавлены ветки `xpPair`, `chest:<тип>@N`→резолвер, `nextLocation`→`TryParseNextLocation`.
  - **Core/Parsing**: мульти-позиционные ROI сундуков `chest:<тип>@1/@2/@3` + `IChestLayoutResolver`/`ChestLayoutResolver` (выбор раскладки по инварианту `litCount==N`, ADR-018); `TryParseXpPair` (объединённая зона опыта «текущий / до_уровня», деление по «/»); `TryParseNextLocation` (формат «акт-этап»).
  - **Core/Models**: `StageRef.Previous()` — текущий этап = `nextLocation−1` (перенос 10 этапов/акт, ADR-008).
  - **Core/Mechanics**: программная генерация `@N`-ключей сундуков и binding `xpPair` в `GameMechanicsConfig` (config-driven).
  - **App/Калибровка**: живой OCR-предпросмотр выбранной ROI (распознанный текст + уверенность) и кнопка копирования (имя ROI + значение + уверенность).
  - **App/Виджет**: строки текущих `Золото`/`Опыт`/`Сундуки` для контроля OCR; строка `До уровня` (оценка по опыт/ч); `Этап` = `nextLocation−1`.

### Fixed
- **OCR-распознавание мелких полей и производительность** (fb4aac0)
  - **Capture/OCR**: апскейл мелких кропов (`MinOcrDimension=96`) — `Windows.Media.Ocr` не распознаёт слишком маленькие изображения (мелкие `gold`/`xp`/`heroLevel`); кэш пиксель-буфера кадра + `BlockCopy`-нарезка вместо перекодирования всего кадра на каждую ROI — устранена задержка обновления ~5с.
  - **Core/ValueParser**: запятая трактуется как ДЕСЯТИЧНЫЙ разделитель (европейская локаль: «392,8» → 392, а не 3928).
  - **Core/MetricsCalculator**: EMA-сглаживание темпов (~5 снимков) + guard выбросов опыта (level-up только при `Xp ≥ 0.8·XpToLevel`, отброс невозможного `Xp > XpToLevel`) — устранён ложный темп ~1.08e9 опыт/ч и «застывшее» «До уровня».
  - **App/Orchestrator**: понижен порог уверенности 0.6 → 0.02 (геом. покрытие — слабый прокси, валидация парсером+sanity); ROI перечитываются каждый кадр (калибровка применяется без перезапуска).

### Changed
- **App/Виджет**: отключён режим «поверх всех окон» (`AlwaysOnTop`) — виджет не закрепляется поверх других окон.

### Notes
- Схема БД и EF-миграции не менялись. Тесты: 394 GREEN (Core 234 · Capture 96 · Data 64).
- В P2-бэклог (`specs/001-tbh-stats-helper/tasks.md`) вынесены: визуальный подсчёт сундуков (графические точки), `stageProgress`/завершение этапа, `stageId` по зелёному флагу Portal, slot-detection, cleanup временного `[Diag]`-блока (T062–T067).

## [0.1.6] - 2026-05-31

### Fixed
- **App**: починить запуск виджета, детекцию игры и захват; доделать калибровку ROI (3e003e4)
  - **App/XAML**: заменены отсутствующие в WinUI 3 кисти (`SystemControl*Brush`, `SystemFillColor*ForegroundBrush`) на актуальные Fluent-ресурсы в `WidgetWindow`/`CompareView`/`ChartsView`/`CalibrationView` — устранён `XamlParseException` → `0xC000027B` при старте.
  - **Capture/WindowTracking**: исправлен порядок static-инициализации `GameWindowTrackerOptions` (`DefaultTitleHints` объявлен до `Default`) — устранён `NullReferenceException` в `FindGameWindow` («Игра не найдена» при запущенной игре); убрана слишком широкая подсказка `"TBH"` (ложно совпадала с окном `TBHStats`/VS Code → захват не того окна).
  - **Capture/Wgc**: WinRT/CsWinRT-интероп приведён к идиомам .NET 8 — `RoGetActivationFactory` через кастомный `HStringMarshaler` (`UnmanagedType.HString` удалён в .NET 5+); `CreateForWindow` → `GraphicsCaptureItem.FromAbi`; `IDirect3DDevice` → `WinRT.MarshalInterface<IDirect3DDevice>.FromAbi` (устранены `MarshalDirectiveException` и «Failed to create a CCW for __ComObject»).
  - **App/Калибровка**: `CalibrationViewModel` получает общий `ICaptureSession`; команда `CaptureFrame` снимает кадр игры (`SoftwareBitmap`→`SoftwareBitmapSource`, BGRA8 Premultiplied). Превью в `ScrollViewer` с зумом (Ctrl+колесо, кнопки −/Вписать/+) и панорамированием; рисование рамки ROI мышью по кадру (координаты = доли от размера контента); убрана разработческая заглушка «T051».
  - **App/Калибровка**: устранён реентрантный крах при повторной разметке зоны — пакетное присваивание `X/Y/W/H` с подавлением промежуточных перерисовок и отложенным `RedrawRoiOverlay` через `DispatcherQueue` (вне pointer-события).
  - **App**: добавлен глобальный `Application.UnhandledException` — логирование исключения + `e.Handled`, единичный UI-сбой не завершает весь виджет.
  - **Tests**: регрессионные тесты `GameWindowTrackerOptionsTests` (подсказки не null; без ложных совпадений с `TBHStats`). Сборка решения — 0 ошибок / 0 предупреждений; Capture 96/96.

## [0.1.5] - 2026-05-31

### Added
- **Polish**: завершить Phase 6 — OCR-харнесс на реальных скриншотах, логирование, MSIX, a11y, расширяемость (3a1b8d6)

## [0.1.4] - 2026-05-31

### Added
- **US3**: визуализация трендов по этапу графиками + ретенция сэмплов (Phase 5, T043–T046) (14d921c)
  - **App/UI**: `ChartsView` + `ChartsHostWindow` + `ChartsViewModel`/`ChartsStageOption` — экран графиков трендов на LiveCharts2: три `CartesianChart` (золото/час, опыт/час, время прохождения) по выбранному этапу во времени, интерактивные тултипы по точкам, `ComboBox` выбора этапа, пустое состояние; кнопка «Графики» в виджете. Источник точек — реальные завершённые (non-partial) `StageRun` (одна точка на забег, x=`CompletedAtUtc`).
  - **Data**: ретенция/прореживание метрических сэмплов `IRunRepository.PruneSamplesAsync(stageId, olderThanUtc)` — двухшаговый bulk `ExecuteDeleteAsync` (зависимые `MetricSampleChest` → `MetricSample`; SQLite без `PRAGMA foreign_keys=ON` не каскадирует FK при bulk-delete), строгий cutoff (`TakenAtUtc < olderThanUtc`); `StageRun`/`StageAggregate` не затрагиваются (агрегаты сохраняются).
  - **Граница слоёв**: домен/данные без UI/WinRT (задел под MAUI), графики только в `TBHStats.App`.

### Tested
- 11 новых тестов на реальном временном SQLite, без моков: `GetSamplesTests` (6 — диапазон, граничная включительность, фильтр по этапу, пустой, сундуки через Include, переживание перезапуска), `PruneSamplesTests` (5 — удаление старше cutoff, изоляция этапа, идемпотентность, сохранность `StageRun`/агрегатов, удаление ненадёжных). Итого Core 151/151, Data 55/55 PASS. Сборка решения — 0 ошибок, 0 предупреждений.

### Notes
- Все три пользовательские истории (US1/US2/US3) теперь независимо функциональны.
- Тренды строятся по `StageRun` (даёт золото/ч, опыт/ч и время напрямую); `GetSamplesAsync`/`PruneSamplesAsync` (MetricSample) — задел под тонкие live-тренды и контролируемое прореживание истории сэмплов.

## [0.1.3] - 2026-05-31

### Added
- **US2**: история этапов и recency-aware выбор оптимального для фарма (Phase 4, T032–T042) (b54e008)
  - **Уточнение механики**: добыча зависит не только от этапа, но и от силы отряда (уровни/предметы/руны; сложность — уже измерение этапа). Усреднение по всей истории смешивает забеги разной силы → введена **recency-aware** агрегация: `StageAggregate` хранит И all-time, И «свежее окно» (последние `OptimizationProfile.RecentWindowSize` non-partial забегов, дефолт 10); ранжирование/рекомендация — по свежему окну (текущая сила), all-time — справочно. Прокси силы — выбранный герой (`HeroSnapshot`); предметы/руны/герои 2–3 в v1 не считываются (задокументировано в spec.md Assumptions, FR-008/009).
  - **Core/Optimization**: `StageAggregateCalculator` — чистый расчёт агрегата (all-time + окно + power-context + темпы сундуков); `OptimizationService` — recency-aware ранжирование/рекомендация по `OptimizationMetric` + `AggregationScope` (Recent/AllTime, tie-break recent best); `StageRanking`, `StagePowerContext`.
  - **Core/Models**: recent/power-поля `StageAggregate`, `StageAggregateChestRate.RecentRatePerHour`, `OptimizationProfile.RecentWindowSize`/`Scope`, enum `AggregationScope`.
  - **Capture**: `StageCompletionDetector` — детерминированная машина состояний InProgress/BossEngaged над `RawObservation` для сегментации забегов (FR-002).
  - **Data**: полная реализация `IRunRepository` (AddRun/GetRuns/GetSamples/AppendSample); recency-aware `StageAggregateRepository.RecomputeForStageAsync(stageId, recentWindowSize)` через калькулятор (upsert агрегата+ChestRates); аддитивная EF-миграция `AddRecencyAwareAggregation` (только AddColumn — история не теряется, FR-013).
  - **App/Services**: `RunRecorder` — сборка/запись `StageRun` (дельты золота/опыта с компенсацией level-up, накопление сундуков, контекст силы `HeroSnapshot`, флаг partial) → AddRun → Recompute; интеграция в петлю оркестратора; `OptimizationProfileService` — переключение метрики/окна/scope с персистентностью.
  - **App/UI**: `CompareView` + `CompareViewModel`/`CompareStageRow` — экран сравнения этапов (ранжирование по метрике, ★-отметка рекомендованного, контекст силы, пометка «⚠ устар.» при низкой силе относительно текущей, переключатели цели и Recent/All-time, пустое состояние; FR-017/019); кнопка «Сравнение» в виджете.

### Tested
- 53 новых теста на реальных объектах/SQLite, без моков: `OptimizationServiceTests` (18), `StageAggregateTests` (18, калькулятор), `RunRecordingTests` (10), `StageAggregateRecomputeTests` (7). Итого Core 151/151, Data 44/44 PASS. Сборка решения — 0 ошибок, 0 предупреждений.

### Notes
- US1 не затронута; observe-only продукта сохранён. `RunRecorder`/`StageCompletionDetector` механически готовы, но триггер завершения этапа зависит от визуальных полей MainZone (`StageProgress`/`BossPresent`/`StageTimeSeconds`), калибруемых на фикстурах/живой игре (T049/T051) — до этого история забегов не наполняется.

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

