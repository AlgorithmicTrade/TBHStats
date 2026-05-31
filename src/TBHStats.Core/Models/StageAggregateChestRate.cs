namespace TBHStats.Core.Models;

/// <summary>
/// Темп выпадения сундуков одного типа для конкретного этапа (data-model §StageAggregate).
/// Хранится как часть материализованного кэша <see cref="StageAggregate"/>.
/// </summary>
/// <remarks>
/// Составной PK (<see cref="StageId"/>, <see cref="ChestTypeId"/>) и FK-связи
/// настраивает T009 через Fluent API.
/// </remarks>
public sealed class StageAggregateChestRate
{
    /// <summary>FK → <see cref="Stage.Id"/> и FK → <see cref="StageAggregate.StageId"/>. Часть составного PK.</summary>
    public int StageId { get; set; }

    /// <summary>FK → <see cref="ChestType.Id"/>. Часть составного PK.</summary>
    public int ChestTypeId { get; set; }

    /// <summary>Средний темп выпадения сундуков данного типа в час по всем учтённым забегам.</summary>
    public double RatePerHour { get; set; }
}
