namespace TBHStats.Core.Models;

/// <summary>
/// Мгновенный счётчик точек одного типа сундуков в MainZone на момент замера (data-model §MetricSample).
/// </summary>
/// <remarks>
/// <para>
/// Значение транзиентно: растёт при выпадении сундука, обнуляется при открытии.
/// Для подсчёта «получено за забег» используется суммирование положительных дельт
/// (<see cref="StageRunChest"/>), а не этот счётчик напрямую.
/// </para>
/// <para>
/// FK-связи и маппинг настраивает T009 через Fluent API.
/// </para>
/// </remarks>
public sealed class MetricSampleChest
{
    /// <summary>FK → <see cref="MetricSample.Id"/>. Часть составного PK.</summary>
    public long MetricSampleId { get; set; }

    /// <summary>FK → <see cref="ChestType.Id"/>. Часть составного PK.</summary>
    public int ChestTypeId { get; set; }

    /// <summary>
    /// Мгновенное число точек под иконкой данного типа сундука в MainZone (≥0).
    /// Транзиентное значение: не является накопленным итогом.
    /// </summary>
    public int Count { get; set; }
}
