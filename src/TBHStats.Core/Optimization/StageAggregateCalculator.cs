namespace TBHStats.Core.Optimization;

using TBHStats.Core.Models;

/// <summary>
/// Реализация <see cref="IStageAggregateCalculator"/>.
/// Чистая функция: без I/O, без часов, без внешних зависимостей.
/// </summary>
public sealed class StageAggregateCalculator : IStageAggregateCalculator
{
    /// <inheritdoc/>
    public StageAggregate Compute(
        int stageId,
        IReadOnlyList<StageRun> runs,
        int recentWindowSize,
        IReadOnlyList<int> chestTypeIds)
    {
        // Фильтрация: только завершённые (non-partial) забеги.
        var completed = runs.Where(r => !r.IsPartial).ToList();

        // Свежее окно: last N по CompletedAtUtc убыванию.
        var window = completed
            .OrderByDescending(r => r.CompletedAtUtc)
            .Take(recentWindowSize)
            .ToList();

        var result = new StageAggregate
        {
            StageId = stageId,

            // ── All-time ──
            RunCount             = completed.Count,
            AvgGoldPerHour       = WeightedRatePerHour(completed, r => r.GoldGained),
            BestGoldPerHour      = Max(completed, r => r.GoldPerHour),
            AvgGoldGained        = Average(completed, r => (double)r.GoldGained),
            AvgXpPerHour         = WeightedRatePerHour(completed, r => r.XpGained),
            BestXpPerHour        = Max(completed, r => r.XpPerHour),
            AvgXpGained          = Average(completed, r => (double)r.XpGained),
            AvgDurationSeconds   = Average(completed, r => r.DurationSeconds),
            BestDurationSeconds  = MinInt(completed, r => r.DurationSeconds),

            // ── Recent ──
            RecentRunCount            = window.Count,
            RecentAvgGoldPerHour      = WeightedRatePerHour(window, r => r.GoldGained),
            RecentBestGoldPerHour     = Max(window, r => r.GoldPerHour),
            RecentAvgGoldGained       = Average(window, r => (double)r.GoldGained),
            RecentAvgXpPerHour        = WeightedRatePerHour(window, r => r.XpGained),
            RecentBestXpPerHour       = Max(window, r => r.XpPerHour),
            RecentAvgXpGained         = Average(window, r => (double)r.XpGained),
            RecentAvgDurationSeconds  = Average(window, r => r.DurationSeconds),
            RecentBestDurationSeconds = MinInt(window, r => r.DurationSeconds),

            // ── Power-context ──
            RecentHeroLevelMin  = window.Count > 0 ? window.Min(r => r.Hero.Level)  : (int?)null,
            RecentHeroLevelMax  = window.Count > 0 ? window.Max(r => r.Hero.Level)  : (int?)null,
            RecentHeroDamageMin = window.Count > 0 ? window.Min(r => r.Hero.Damage) : (long?)null,
            RecentHeroDamageMax = window.Count > 0 ? window.Max(r => r.Hero.Damage) : (long?)null,

            // ── Chest rates ──
            ChestRates = BuildChestRates(stageId, chestTypeIds, completed, window),
        };

        return result;
    }

    // ────────────────────────────────────────────────────────────
    // Вспомогательные методы
    // ────────────────────────────────────────────────────────────

    private static double Average(List<StageRun> source, Func<StageRun, double> selector)
        => source.Count == 0 ? 0.0 : source.Average(selector);

    private static double Max(List<StageRun> source, Func<StageRun, double> selector)
        => source.Count == 0 ? 0.0 : source.Max(selector);

    private static int MinInt(List<StageRun> source, Func<StageRun, int> selector)
        => source.Count == 0 ? 0 : source.Min(selector);

    private static ICollection<StageAggregateChestRate> BuildChestRates(
        int stageId,
        IReadOnlyList<int> chestTypeIds,
        List<StageRun> allTime,
        List<StageRun> window)
    {
        var rates = new List<StageAggregateChestRate>(chestTypeIds.Count);

        foreach (var typeId in chestTypeIds)
        {
            rates.Add(new StageAggregateChestRate
            {
                StageId           = stageId,
                ChestTypeId       = typeId,
                RatePerHour       = ChestRatePerHour(allTime, typeId),
                RecentRatePerHour = ChestRatePerHour(window,  typeId),
            });
        }

        return rates;
    }

    /// <summary>
    /// Темп накопления ресурса в час по набору забегов <paramref name="source"/>,
    /// взвешенный по времени: Σ gained / Σ DurationSeconds * 3600.
    /// При нулевой суммарной длительности возвращает 0.
    /// </summary>
    private static double WeightedRatePerHour(List<StageRun> source, Func<StageRun, long> gainedSelector)
    {
        long totalGained   = 0;
        long totalDuration = 0;
        foreach (var run in source)
        {
            totalGained   += gainedSelector(run);
            totalDuration += run.DurationSeconds;
        }
        return totalDuration == 0 ? 0.0 : (double)totalGained / totalDuration * 3600.0;
    }

    /// <summary>
    /// Темп выпадения сундуков типа <paramref name="chestTypeId"/> в час
    /// по набору забегов <paramref name="source"/>.
    /// Формула: (сумма Count) / (сумма DurationSeconds) * 3600.
    /// При нулевой суммарной длительности возвращает 0.
    /// </summary>
    private static double ChestRatePerHour(List<StageRun> source, int chestTypeId)
    {
        long totalCount    = 0;
        long totalDuration = 0;

        foreach (var run in source)
        {
            totalDuration += run.DurationSeconds;
            foreach (var chest in run.Chests)
            {
                if (chest.ChestTypeId == chestTypeId)
                    totalCount += chest.Count;
            }
        }

        return totalDuration == 0 ? 0.0 : (double)totalCount / totalDuration * 3600.0;
    }
}
