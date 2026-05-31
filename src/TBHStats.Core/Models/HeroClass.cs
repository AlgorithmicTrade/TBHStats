namespace TBHStats.Core.Models;

/// <summary>
/// Справочник классов героя (ADR-009).
/// Сущность читается из БД/конфига; не дублируется как enum в коде.
/// </summary>
public sealed class HeroClass
{
    /// <summary>Суррогатный первичный ключ.</summary>
    public int Id { get; init; }

    /// <summary>Уникальный машинный ключ класса (например "warrior", "mage").</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Отображаемое название класса.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Активна ли запись (soft-delete).</summary>
    public bool IsActive { get; init; } = true;
}
