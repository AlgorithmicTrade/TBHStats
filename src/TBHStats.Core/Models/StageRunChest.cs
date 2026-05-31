namespace TBHStats.Core.Models;

/// <summary>
/// Счётчик сундуков одного типа за конкретный забег этапа (data-model §StageRunChest).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Count"/> — накопленный итог за забег: сумма положительных дельт счётчика
/// точек MainZone. Обнуление точек при открытии сундуков ≠ потеря: счётчик
/// накопленного итога не уменьшается (data-model «транзиентный счётчик»).
/// </para>
/// <para>
/// Составной PK (<see cref="StageRunId"/>, <see cref="ChestTypeId"/>) и FK-связи
/// настраивает T009 через Fluent API — EF-атрибуты отсутствуют намеренно.
/// </para>
/// </remarks>
public sealed class StageRunChest
{
    /// <summary>FK → <see cref="StageRun.Id"/>. Часть составного PK.</summary>
    public long StageRunId { get; set; }

    /// <summary>FK → <see cref="ChestType.Id"/>. Часть составного PK.</summary>
    public int ChestTypeId { get; set; }

    /// <summary>
    /// Накопленный итог сундуков данного типа за забег (≥0).
    /// Неубывает: складываются только положительные приросты точек MainZone.
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// Навигационное свойство к родительскому забегу.
    /// Заполняется EF при загрузке связанных данных.
    /// </summary>
    public StageRun? Run { get; set; }
}
