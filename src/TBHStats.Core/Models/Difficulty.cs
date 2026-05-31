namespace TBHStats.Core.Models;

/// <summary>
/// Справочник уровней сложности (ADR-008).
/// Порядок сложностей: "normal" (SortOrder=1) → "nightmare" (SortOrder=2).
/// </summary>
public sealed class Difficulty
{
    /// <summary>Суррогатный первичный ключ.</summary>
    public int Id { get; init; }

    /// <summary>Уникальный машинный ключ сложности: "normal" | "nightmare".</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Отображаемое название сложности.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Порядок отображения (определяет упорядоченность normal &lt; nightmare).</summary>
    public int SortOrder { get; init; }
}
