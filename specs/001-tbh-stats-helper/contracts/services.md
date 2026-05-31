# Internal Service Contracts: TBHStats

**Feature**: `001-tbh-stats-helper` | **Date**: 2026-05-31

> Это десктоп-приложение: «контракты» v1 — **внутренние C#-интерфейсы (швы между слоями)**, а не HTTP API.
> Каждый интерфейс отображает действия пользователя/системы из FR. HTTP-контракт будущего Android-клиента — в [remote-api.openapi.yaml](./remote-api.openapi.yaml) (FR-020, вне v1).
> Сигнатуры — иллюстративные (C# 12, nullable on). Точные типы уточняются в реализации.

---

## TBHStats.Capture — захват и распознавание

### `IGameWindowTracker` — поиск/отслеживание окна (FR-001, FR-005b)
```csharp
public interface IGameWindowTracker
{
    // Найти окно процесса игры (по имени процесса/заголовку). null = не найдено.
    GameWindowHandle? FindGameWindow();
    // Текущее состояние окна: видимо / свёрнуто / закрыто.
    WindowVisibility GetVisibility(GameWindowHandle window);
    // Размер клиентской области текущего кадра (для разворота нормализованных ROI).
    SizePx GetClientSize(GameWindowHandle window);
}
```

### `ICaptureSession` — кадры окна (FR-005/005a/005b, R1)
```csharp
public interface ICaptureSession : IAsyncDisposable
{
    CaptureState State { get; }              // NotFound | Capturing | Waiting (см. data-model state machine)
    event Action<CaptureState> StateChanged;
    // Один кадр содержимого окна (даже если перекрыто). null, если окно в Waiting/NotFound.
    Task<CapturedFrame?> TryGetFrameAsync(CancellationToken ct);
}
```

### `IOcrReader` — распознавание ROI (FR-002/FR-003, R2)
```csharp
public interface IOcrReader
{
    // Распознать значение из нормализованной области кадра по калибровке.
    Task<OcrResult> ReadAsync(CapturedFrame frame, RoiCalibration roi, CancellationToken ct);
}
public readonly record struct OcrResult(string RawText, double Confidence, bool Recognized);
```

### `ITabDetector` — какая вкладка открыта (FR-002a)
```csharp
public interface ITabDetector
{
    // Распознать активную вкладку по её названию (ROI `activeTab`). null = не определена достоверно.
    Task<TabRef?> DetectActiveTabAsync(CapturedFrame frame, GameMechanicsConfig cfg, CancellationToken ct);
}
public readonly record struct TabRef(int TabId, string Key, double Confidence);
```

### `IFieldExtractor` — кадр → набор сырых значений (FR-002/FR-002b)
```csharp
public interface IFieldExtractor
{
    // Прочитать поля одного кадра, ДОСТУПНЫЕ при текущей активной вкладке:
    //   Source=MainZone всегда + поля вкладки activeTab. Недоступные поля → не читаются (FR-002b).
    Task<RawObservation> ExtractAsync(
        CapturedFrame frame,
        IReadOnlyList<RoiCalibration> rois,
        TabRef? activeTab,
        CancellationToken ct);
}
// RawObservation: gold?, xp?, xpToLevel?, stageTime?, stageProgress?, bossPresent?, heroLevel?, heroDamage?,
//                 heroClassText?, stageText?, chests{type->int} (мгновенные «точки» MainZone, транзиентны),
//                 nextLocation?, activeTab?, perFieldConfidence
// Завершение этапа детектируется по прогрессбару/боссу (stageProgress/bossPresent) → закрытие StageRun.
// Поля с Source=Tab, чья вкладка не активна, отсутствуют в наблюдении (остаётся последнее достоверное).
// Накопление «получено сундуков за забег» = сумма положительных дельт chests (обнуление = открытие, не потеря).
```

---

## TBHStats.Core — домен, парсинг, оптимизация

### `IValueParser` — сокращённые числа и sanity (R4, FR-010)
```csharp
public interface IValueParser
{
    bool TryParseAbbreviatedNumber(string raw, out long value);   // "1.2K"/"3.4M"/"1,234"
    bool TryParseStageTimeSeconds(string raw, out int seconds);
    StageId? TryParseStageId(string raw, GameMechanicsConfig cfg); // "Act1 / Normal / 5"
}
```

### `IMetricsCalculator` — темпы по надёжным интервалам (FR-006, FR-005a)
```csharp
public interface IMetricsCalculator
{
    // Темпы по последовательности достоверных сэмплов; периоды недоступности не занижают результат.
    LiveRates ComputeLiveRates(IReadOnlyList<MetricSample> reliableSamples);
}
public readonly record struct LiveRates(double GoldPerHour, double XpPerHour, IReadOnlyDictionary<int,double> ChestPerHourByType);
```

### `IOptimizationService` — ранжирование и рекомендация (FR-008/FR-009/FR-017/FR-019)
```csharp
public interface IOptimizationService
{
    IReadOnlyList<StageRanking> RankStages(IReadOnlyList<StageAggregate> aggregates, OptimizationMetric metric);
    StageRanking? RecommendBestStage(IReadOnlyList<StageAggregate> aggregates, OptimizationMetric metric);
}
public enum OptimizationMetric { GoldPerHour, XpPerHour }   // FR-009 (Q2=A)
// StageRanking: StageId, Score, Rank, Reason ("лучший по золото/час")
```

### `IGameMechanics` — конфиг механик (FR-021)
```csharp
public interface IGameMechanics
{
    GameMechanicsConfig Current { get; }     // типы сундуков, классы, акты/сложности/этапы
    void Reload(GameMechanicsConfig cfg);    // добавление нового сундука/класса без правки кода
}
```

---

## TBHStats.Data — персистентность (FR-007/FR-008/FR-011/FR-013)

### `IRunRepository`
```csharp
public interface IRunRepository
{
    Task AddRunAsync(StageRun run, CancellationToken ct);           // FR-007
    Task<IReadOnlyList<StageRun>> GetRunsAsync(StageId stage, CancellationToken ct);
    Task<IReadOnlyList<MetricSample>> GetSamplesAsync(StageId stage, DateRange range, CancellationToken ct); // US3
    Task AppendSampleAsync(MetricSample sample, CancellationToken ct); // только IsReliable
}
```

### `IStageAggregateRepository`
```csharp
public interface IStageAggregateRepository
{
    Task<IReadOnlyList<StageAggregate>> GetAllAsync(CancellationToken ct);      // экран сравнения
    Task RecomputeForStageAsync(StageId stage, CancellationToken ct);          // FR-008 после нового забега
}
```

### `ISettingsRepository`
```csharp
public interface ISettingsRepository
{
    Task<WidgetSettings> GetWidgetSettingsAsync();   // FR-016
    Task SaveWidgetSettingsAsync(WidgetSettings s);
    Task<OptimizationProfile> GetOptimizationProfileAsync();
    Task SaveOptimizationProfileAsync(OptimizationProfile p);
    Task<IReadOnlyList<RoiCalibration>> GetRoiCalibrationsAsync();
    Task SaveRoiCalibrationsAsync(IReadOnlyList<RoiCalibration> rois); // FR-003 калибровка
}
```

---

## Оркестрация (TBHStats.App/Services) — связывает швы

`IStatsOrchestrator` (фоновая петля, FR-004):
1. `tracker.FindGameWindow()` → нет → состояние `NotFound`.
2. `session.TryGetFrameAsync()`; `Waiting` → показать последние достоверные, ждать (FR-005).
3. `tabDetector.DetectActiveTabAsync()` → определить активную вкладку (FR-002a); затем `extractor.ExtractAsync(frame, rois, activeTab)` читает только доступные поля (FR-002b) → `parser` → sanity/confidence фильтр (R4) → надёжные `MetricSample` (FR-005a).
4. `metrics.ComputeLiveRates()` → биндинг в виджет (живые показатели, P1).
5. при завершении этапа → собрать `StageRun` → `runRepo.AddRunAsync` → `aggRepo.RecomputeForStageAsync` (P2).
6. экран сравнения → `aggRepo.GetAllAsync` + `optimization.RankStages/RecommendBestStage` (P2).

---

## Соответствие FR → контракт

| FR | Контракт |
|----|----------|
| FR-001 | `IGameWindowTracker.FindGameWindow` |
| FR-002/003 | `IOcrReader`, `IFieldExtractor`, `RoiCalibration` |
| FR-002a | `ITabDetector.DetectActiveTabAsync` |
| FR-002b | `RoiCalibration.Source/TabId`, `IFieldExtractor` (фильтр по активной вкладке) |
| FR-004 | `IStatsOrchestrator` петля |
| FR-005/005a/005b | `ICaptureSession.State`, `IMetricsCalculator` (надёжные интервалы), нормализованные ROI |
| FR-006 | `IMetricsCalculator.ComputeLiveRates` |
| FR-007 | `IRunRepository.AddRunAsync` |
| FR-008 | `IStageAggregateRepository.RecomputeForStageAsync` |
| FR-009/017/019 | `IOptimizationService`, `OptimizationMetric` |
| FR-010 | парсер + sanity + `IsPartial` |
| FR-011/013 | EF Core репозитории + миграции |
| FR-016 | `ISettingsRepository` widget settings |
| FR-021 | `IGameMechanics` |
| FR-020 | [remote-api.openapi.yaml](./remote-api.openapi.yaml) (future) |
