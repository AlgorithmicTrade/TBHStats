namespace TBHStats.Core.Models;

/// <summary>
/// Справочник этапов игры (ADR-008).
/// Пространство: 3 акта × 2 сложности × 10 этапов = 60 записей.
/// Уникальность: (ActId, DifficultyId, Number).
/// </summary>
public sealed class Stage
{
    /// <summary>Суррогатный первичный ключ.</summary>
    public int Id { get; init; }

    /// <summary>FK → <see cref="Act.Id"/>.</summary>
    public int ActId { get; init; }

    /// <summary>FK → <see cref="Difficulty.Id"/>.</summary>
    public int DifficultyId { get; init; }

    /// <summary>Номер этапа внутри акта/сложности (1..10).</summary>
    public int Number { get; init; }
}
