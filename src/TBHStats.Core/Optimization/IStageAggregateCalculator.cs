namespace TBHStats.Core.Optimization;

using TBHStats.Core.Models;

/// <summary>
/// Контракт вычисления агрегата статистики этапа из списка забегов (FR-009, FR-017).
/// </summary>
/// <remarks>
/// Чистая доменная функция без side-effect: из списка забегов одного этапа считает
/// <see cref="StageAggregate"/> с all-time и recency-aware полями.
/// Свежее окно — последние <paramref name="recentWindowSize"/> non-partial забегов,
/// отсортированных по <see cref="StageRun.CompletedAtUtc"/> убывающе.
/// Power-context заполняется из <see cref="StageRun.Hero"/> (<see cref="HeroSnapshot"/>).
/// Темпы сундуков рассчитываются для типов, перечисленных в <paramref name="chestTypeIds"/>.
/// </remarks>
public interface IStageAggregateCalculator
{
    /// <summary>
    /// Вычисляет <see cref="StageAggregate"/> для этапа по его забегам.
    /// </summary>
    /// <param name="stageId">Идентификатор этапа.</param>
    /// <param name="runs">Все известные забеги данного этапа (любой сортировки).</param>
    /// <param name="recentWindowSize">
    /// Размер свежего окна — число последних non-partial забегов, ≥ 1.
    /// Соответствует <see cref="OptimizationProfile.RecentWindowSize"/>.
    /// </param>
    /// <param name="chestTypeIds">
    /// Список идентификаторов типов сундуков из <c>GameMechanicsConfig</c>,
    /// для которых следует рассчитать <see cref="StageAggregateChestRate"/>.
    /// </param>
    /// <returns>Заполненный агрегат; <see cref="StageAggregate.StageId"/> = <paramref name="stageId"/>.</returns>
    StageAggregate Compute(
        int stageId,
        IReadOnlyList<StageRun> runs,
        int recentWindowSize,
        IReadOnlyList<int> chestTypeIds);
}
