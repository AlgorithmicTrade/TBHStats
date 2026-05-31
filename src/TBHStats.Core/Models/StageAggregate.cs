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

    /// <summary>Средний опыт в час по учтённым забегам.</summary>
    public double AvgXpPerHour { get; set; }

    /// <summary>Лучший опыт в час среди учтённых забегов.</summary>
    public double BestXpPerHour { get; set; }

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
}
