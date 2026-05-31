namespace TBHStats.Core.Optimization;

/// <summary>
/// Диапазон силы отряда (прокси по выбранному герою) для забегов свежего окна (уточнение 2026-05-31).
/// Используется UI для отображения контекста рекомендации и пометки устаревших этапов.
/// </summary>
/// <param name="HeroLevelMin">Минимальный уровень выбранного героя в забегах окна; <c>null</c> — окно пустое.</param>
/// <param name="HeroLevelMax">Максимальный уровень выбранного героя в забегах окна; <c>null</c> — окно пустое.</param>
/// <param name="HeroDamageMin">Минимальный урон выбранного героя в забегах окна; <c>null</c> — окно пустое.</param>
/// <param name="HeroDamageMax">Максимальный урон выбранного героя в забегах окна; <c>null</c> — окно пустое.</param>
public readonly record struct StagePowerContext(
    int? HeroLevelMin,
    int? HeroLevelMax,
    long? HeroDamageMin,
    long? HeroDamageMax);
