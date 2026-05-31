namespace TBHStats.Core.Models;

/// <summary>
/// Справочник актов игры (ADR-008).
/// Текущее количество актов: 3. Номер акта — 1..N.
/// </summary>
public sealed class Act
{
    /// <summary>Суррогатный первичный ключ.</summary>
    public int Id { get; init; }

    /// <summary>Порядковый номер акта (1..N; сейчас N=3).</summary>
    public int Number { get; init; }

    /// <summary>Отображаемое название акта.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Порядок отображения в UI.</summary>
    public int SortOrder { get; init; }
}
