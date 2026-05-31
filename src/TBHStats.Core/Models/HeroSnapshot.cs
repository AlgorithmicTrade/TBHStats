namespace TBHStats.Core.Models;

/// <summary>
/// Value-объект: снимок выбранного героя на момент начала забега (ADR-008).
/// Встраивается (owned entity) в <c>StageRun</c>.
/// </summary>
/// <param name="HeroClassId">FK → <see cref="HeroClass.Id"/>.</param>
/// <param name="Level">Уровень героя (≥1).</param>
/// <param name="Damage">Урон героя (≥0).</param>
public sealed record HeroSnapshot(
    int HeroClassId,
    int Level,
    long Damage)
{
    /// <summary>Уровень героя должен быть положительным.</summary>
    public int Level { get; init; } = Level >= 1
        ? Level
        : throw new ArgumentOutOfRangeException(nameof(Level), Level, "Level must be ≥ 1.");

    /// <summary>Урон не может быть отрицательным.</summary>
    public long Damage { get; init; } = Damage >= 0
        ? Damage
        : throw new ArgumentOutOfRangeException(nameof(Damage), Damage, "Damage must be ≥ 0.");
}
