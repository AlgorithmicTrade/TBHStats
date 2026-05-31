namespace TBHStats.Core.Models;

/// <summary>
/// Справочник типов сундуков игры Task Bar Hero (ADR-009, ADR-011).
/// Текущие ключи: "brown", "blue", "red".
/// Сущность читается из БД/конфига; не дублируется как enum в коде.
/// </summary>
public sealed class ChestType
{
    /// <summary>Суррогатный первичный ключ.</summary>
    public int Id { get; init; }

    /// <summary>Уникальный машинный ключ: "brown" | "blue" | "red".</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Отображаемое название (локализованное или дефолтное).</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Цветовая метка (A11y): «коричневый», «синий», «красный».
    /// Не является единственным различителем — дополняет иконку/ключ.
    /// </summary>
    public string ColorLabel { get; init; } = string.Empty;

    /// <summary>Порядок отображения в UI.</summary>
    public int SortOrder { get; init; }

    /// <summary>Активна ли запись (soft-delete).</summary>
    public bool IsActive { get; init; } = true;
}
