namespace TBHStats.Core.Optimization;

using TBHStats.Core.Models;

/// <summary>
/// Recency-aware ранжирование и рекомендация этапов по выбранной метрике (T039).
/// </summary>
public sealed class OptimizationService : IOptimizationService
{
    /// <inheritdoc/>
    public IReadOnlyList<StageRanking> RankStages(
        IReadOnlyList<StageAggregate> aggregates,
        OptimizationMetric metric,
        AggregationScope scope = AggregationScope.Recent)
    {
        var (scoreSelector, tieSelector, hasData, reasonLabel) =
            GetSelectors(metric, scope);

        var ranked = aggregates
            .Where(a => hasData(a))
            .OrderByDescending(a => scoreSelector(a))
            .ThenByDescending(a => tieSelector(a))
            .Select((agg, index) => new StageRanking(
                StageId: agg.StageId,
                Score:   scoreSelector(agg),
                Rank:    index + 1,
                Reason:  reasonLabel,
                Power:   new StagePowerContext(
                    agg.RecentHeroLevelMin,
                    agg.RecentHeroLevelMax,
                    agg.RecentHeroDamageMin,
                    agg.RecentHeroDamageMax)))
            .ToList();

        return ranked;
    }

    /// <inheritdoc/>
    public StageRanking? RecommendBestStage(
        IReadOnlyList<StageAggregate> aggregates,
        OptimizationMetric metric,
        AggregationScope scope = AggregationScope.Recent)
    {
        var ranked = RankStages(aggregates, metric, scope);
        return ranked.Count == 0 ? null : ranked[0];
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Вспомогательный метод: выбор полей по (metric, scope)
    // ──────────────────────────────────────────────────────────────────────────

    private static (
        Func<StageAggregate, double> scoreSelector,
        Func<StageAggregate, double> tieSelector,
        Func<StageAggregate, bool>   hasData,
        string                       reasonLabel)
    GetSelectors(OptimizationMetric metric, AggregationScope scope)
    {
        return (metric, scope) switch
        {
            (OptimizationMetric.GoldPerHour, AggregationScope.Recent) =>
                (a => a.RecentAvgGoldPerHour,
                 a => a.RecentBestGoldPerHour,
                 a => a.RecentRunCount > 0,
                 "золото/час (свежее окно)"),

            (OptimizationMetric.XpPerHour, AggregationScope.Recent) =>
                (a => a.RecentAvgXpPerHour,
                 a => a.RecentBestXpPerHour,
                 a => a.RecentRunCount > 0,
                 "опыт/час (свежее окно)"),

            (OptimizationMetric.GoldPerHour, AggregationScope.AllTime) =>
                (a => a.AvgGoldPerHour,
                 a => a.BestGoldPerHour,
                 a => a.RunCount > 0,
                 "золото/час (вся история)"),

            (OptimizationMetric.XpPerHour, AggregationScope.AllTime) =>
                (a => a.AvgXpPerHour,
                 a => a.BestXpPerHour,
                 a => a.RunCount > 0,
                 "опыт/час (вся история)"),

            _ => throw new ArgumentOutOfRangeException(
                nameof(metric),
                $"Неизвестная комбинация метрики и scope: {metric}, {scope}."),
        };
    }
}
