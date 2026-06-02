namespace TBHStats.Core.Models;

/// <summary>
/// Материализованный агрегат статистики этапа (data-model §StageAggregate).
/// Кэширует усреднённые и лучшие показатели по всем завершённым (не partial) забегам.
/// </summary>
/// <remarks>
/// <para>
/// Пересчитывается при добавлении / изменении <see cref="StageRun"/>.
/// В агрегаты «Best/Avg» включаются только забеги с <c>IsPartial = false</c> (FR-010).
/// </para>
/// <para>
/// PK = <see cref="StageId"/> (1:1 с <see cref="Stage"/>).
/// FK-связи и маппинг настраивает T009 через Fluent API.
/// </para>
/// </remarks>
public sealed class StageAggregate
{
    /// <summary>PK и FK → <see cref="Stage.Id"/>. Один агрегат на этап.</summary>
    public int StageId { get; set; }

    /// <summary>Число учтённых (IsPartial = false) забегов этапа.</summary>
    public int RunCount { get; set; }

    /// <summary>Среднее золото в час по учтённым забегам.</summary>
    public double AvgGoldPerHour { get; set; }

    /// <summary>Лучшее золото в час среди учтённых забегов.</summary>
    public double BestGoldPerHour { get; set; }

    /// <summary>Среднее абсолютное золото за один забег по учтённым (non-partial) забегам.</summary>
    public double AvgGoldGained { get; set; }

    /// <summary>Средний опыт в час по учтённым забегам.</summary>
    public double AvgXpPerHour { get; set; }

    /// <summary>Лучший опыт в час среди учтённых забегов.</summary>
    public double BestXpPerHour { get; set; }

    /// <summary>Средний абсолютный опыт за один забег по учтённым (non-partial) забегам.</summary>
    public double AvgXpGained { get; set; }

    /// <summary>Средняя продолжительность забега в секундах.</summary>
    public double AvgDurationSeconds { get; set; }

    /// <summary>Лучшая (наименьшая) продолжительность забега в секундах среди учтённых.</summary>
    public int BestDurationSeconds { get; set; }

    /// <summary>Момент последнего пересчёта агрегата (UTC).</summary>
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// Темпы выпадения сундуков по типам в час.
    /// Коллекция <see cref="StageAggregateChestRate"/> — по одной записи на тип сундука.
    /// </summary>
    public ICollection<StageAggregateChestRate> ChestRates { get; set; } = new List<StageAggregateChestRate>();

    // ──────────────── Свежее окно (recent) ────────────────
    // Считается по последним <see cref="OptimizationProfile.RecentWindowSize"/> non-partial забегам.
    // Ранжирование и рекомендация (FR-009) ведутся по этим полям, not all-time.

    /// <summary>
    /// Число non-partial забегов в свежем окне.
    /// Считается по последним <see cref="OptimizationProfile.RecentWindowSize"/> non-partial забегам этапа.
    /// </summary>
    public int RecentRunCount { get; set; }

    /// <summary>
    /// Среднее золото в час по свежему окну.
    /// Ранжирование (FR-009) использует это поле при <c>AggregationScope.Recent</c>.
    /// </summary>
    public double RecentAvgGoldPerHour { get; set; }

    /// <summary>
    /// Лучшее золото в час по свежему окну.
    /// Tie-break при одинаковом <see cref="RecentAvgGoldPerHour"/>.
    /// </summary>
    public double RecentBestGoldPerHour { get; set; }

    /// <summary>Средний опыт в час по свежему окну.</summary>
    public double RecentAvgXpPerHour { get; set; }

    /// <summary>Лучший опыт в час по свежему окну.</summary>
    public double RecentBestXpPerHour { get; set; }

    /// <summary>Среднее абсолютное золото за один забег по свежему окну.</summary>
    public double RecentAvgGoldGained { get; set; }

    /// <summary>Средний абсолютный опыт за один забег по свежему окну.</summary>
    public double RecentAvgXpGained { get; set; }

    /// <summary>Средняя продолжительность забега в секундах по свежему окну.</summary>
    public double RecentAvgDurationSeconds { get; set; }

    /// <summary>Лучшая (наименьшая) продолжительность забега в секундах по свежему окну.</summary>
    public int RecentBestDurationSeconds { get; set; }

    // ──────────────── Power-context свежего окна ────────────────
    // Прокси силы отряда по выбранному герою в забегах свежего окна.
    // Используется UI для пометки устаревших рекомендаций (уточнение 2026-05-31).

    /// <summary>
    /// Минимальный уровень выбранного героя среди забегов свежего окна.
    /// <c>null</c> — если свежее окно пустое (<see cref="RecentRunCount"/> = 0).
    /// </summary>
    public int? RecentHeroLevelMin { get; set; }

    /// <summary>
    /// Максимальный уровень выбранного героя среди забегов свежего окна.
    /// <c>null</c> — если свежее окно пустое (<see cref="RecentRunCount"/> = 0).
    /// </summary>
    public int? RecentHeroLevelMax { get; set; }

    /// <summary>
    /// Минимальный урон выбранного героя среди забегов свежего окна.
    /// <c>null</c> — если свежее окно пустое (<see cref="RecentRunCount"/> = 0).
    /// </summary>
    public long? RecentHeroDamageMin { get; set; }

    /// <summary>
    /// Максимальный урон выбранного героя среди забегов свежего окна.
    /// <c>null</c> — если свежее окно пустое (<see cref="RecentRunCount"/> = 0).
    /// </summary>
    public long? RecentHeroDamageMax { get; set; }
}
