namespace TBHStats.Core.Optimization;

using TBHStats.Core.Models;

/// <summary>
/// Контракт recency-aware ранжирования и рекомендации этапов по выбранной метрике (FR-008, FR-009, FR-017, FR-019).
/// </summary>
/// <remarks>
/// <para>
/// При <paramref name="scope"/> = <see cref="AggregationScope.Recent"/> (дефолт)
/// использует поля свежего окна агрегата (<c>RecentAvg*/RecentBest*</c>),
/// отражающие актуальную силу отряда.
/// При <see cref="AggregationScope.AllTime"/> — all-time поля (справочно).
/// </para>
/// <para>
/// Этапы с нулевым числом non-partial забегов в выбранном scope пропускаются
/// (не включаются в ранжирование и не могут быть рекомендованы).
/// </para>
/// </remarks>
public interface IOptimizationService
{
    /// <summary>
    /// Возвращает список <see cref="StageRanking"/> — все переданные этапы, ранжированные
    /// по убыванию выбранной метрики в выбранном scope.
    /// </summary>
    /// <param name="aggregates">Список агрегатов этапов для ранжирования.</param>
    /// <param name="metric">Метрика оптимизации (золото/час или опыт/час).</param>
    /// <param name="scope">
    /// Набор данных: <see cref="AggregationScope.Recent"/> (дефолт) — свежее окно;
    /// <see cref="AggregationScope.AllTime"/> — вся история.
    /// </param>
    /// <returns>
    /// Ранжированный список; <see cref="StageRanking.Rank"/> = 1-based место.
    /// Этапы без данных в выбранном scope в список не включаются.
    /// </returns>
    IReadOnlyList<StageRanking> RankStages(
        IReadOnlyList<StageAggregate> aggregates,
        OptimizationMetric metric,
        AggregationScope scope = AggregationScope.Recent);

    /// <summary>
    /// Рекомендует лучший этап: максимальное среднее значение метрики в выбранном scope.
    /// Tie-break — максимальный best-показатель той же метрики.
    /// </summary>
    /// <param name="aggregates">Список агрегатов этапов.</param>
    /// <param name="metric">Метрика оптимизации.</param>
    /// <param name="scope">Набор данных (см. <see cref="RankStages"/>).</param>
    /// <returns>
    /// <see cref="StageRanking"/> лучшего этапа или <c>null</c>, если ни один этап
    /// не имеет данных в выбранном scope.
    /// </returns>
    StageRanking? RecommendBestStage(
        IReadOnlyList<StageAggregate> aggregates,
        OptimizationMetric metric,
        AggregationScope scope = AggregationScope.Recent);
}
