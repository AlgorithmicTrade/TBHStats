namespace TBHStats.Core.Optimization;

using TBHStats.Core.Models;

/// <summary>
/// Реализация <see cref="IMetricsCalculator"/>: вычисление живых темпов добычи
/// по интервалам между надёжными сэмплами (FR-006, FR-005a).
/// </summary>
/// <remarks>
/// <para>
/// Темп каждого показателя вычисляется как Σдельта / Σelapsed × 3600.
/// Периоды недоступности окна между сэмплами не трактуются как «нулевая добыча» (FR-005a):
/// расчёт ведётся только по интервалам между соседними надёжными точками.
/// </para>
/// <para>
/// Отрицательные дельты золота и опыта (sanity-события) не дают отрицательного вклада
/// в числитель, но их интервал учитывается в знаменателе.
/// Для сундуков аналогично — отрицательная дельта (открытие) даёт вклад 0,
/// интервал участвует в знаменателе.
/// </para>
/// </remarks>
public sealed class MetricsCalculator : IMetricsCalculator
{
    /// <inheritdoc/>
    public LiveRates ComputeLiveRates(IReadOnlyList<MetricSample> reliableSamples)
    {
        if (reliableSamples.Count < 2)
        {
            return new LiveRates(0.0, 0.0, new Dictionary<int, double>());
        }

        double goldDeltaSum   = 0.0;
        double goldElapsedSum = 0.0;

        double xpDeltaSum   = 0.0;
        double xpElapsedSum = 0.0;

        // chestTypeId → (deltaSum, elapsedSum)
        var chestAccum = new Dictionary<int, (double DeltaSum, double ElapsedSum)>();

        for (int i = 0; i < reliableSamples.Count - 1; i++)
        {
            MetricSample a = reliableSamples[i];
            MetricSample b = reliableSamples[i + 1];

            double intervalSeconds = (b.TakenAtUtc - a.TakenAtUtc).TotalSeconds;

            // --- Золото ---
            if (a.Gold.HasValue && b.Gold.HasValue && intervalSeconds > 0.0)
            {
                double goldDelta = b.Gold.Value - a.Gold.Value;
                goldElapsedSum += intervalSeconds;
                if (goldDelta > 0.0)
                {
                    goldDeltaSum += goldDelta;
                }
            }

            // --- Опыт ---
            if (a.Xp.HasValue && b.Xp.HasValue && intervalSeconds > 0.0)
            {
                double xpDelta;

                bool levelUpByOne = a.HeroLevel.HasValue && b.HeroLevel.HasValue
                                    && b.HeroLevel.Value == a.HeroLevel.Value + 1;

                if (levelUpByOne && a.XpToLevel.HasValue)
                {
                    // Level-up компенсация: (xpToLevel - a.Xp) + b.Xp
                    xpDelta = (double)(a.XpToLevel.Value - a.Xp.Value) + b.Xp.Value;
                }
                else
                {
                    xpDelta = b.Xp.Value - a.Xp.Value;
                }

                xpElapsedSum += intervalSeconds;
                if (xpDelta > 0.0)
                {
                    xpDeltaSum += xpDelta;
                }
            }

            // --- Сундуки ---
            // Собираем все typeId из обоих сэмплов пары
            var aChestsByType = BuildChestLookup(a.Chests);
            var bChestsByType = BuildChestLookup(b.Chests);

            var allTypeIds = new HashSet<int>(aChestsByType.Keys);
            allTypeIds.UnionWith(bChestsByType.Keys);

            foreach (int typeId in allTypeIds)
            {
                if (!aChestsByType.TryGetValue(typeId, out int aCount)
                    || !bChestsByType.TryGetValue(typeId, out int bCount))
                {
                    // Тип присутствует только в одном сэмпле пары — пропускаем
                    continue;
                }

                if (!chestAccum.TryGetValue(typeId, out var acc))
                {
                    acc = (0.0, 0.0);
                }

                double chestDelta = bCount - aCount;
                double newDeltaSum   = acc.DeltaSum + (chestDelta > 0.0 ? chestDelta : 0.0);
                double newElapsedSum = acc.ElapsedSum + intervalSeconds;
                chestAccum[typeId] = (newDeltaSum, newElapsedSum);
            }
        }

        // --- Итоговые темпы ---
        double goldPerHour = goldElapsedSum > 0.0
            ? goldDeltaSum / goldElapsedSum * 3600.0
            : 0.0;

        double xpPerHour = xpElapsedSum > 0.0
            ? xpDeltaSum / xpElapsedSum * 3600.0
            : 0.0;

        var chestPerHour = new Dictionary<int, double>(chestAccum.Count);
        foreach (var (typeId, acc) in chestAccum)
        {
            if (acc.ElapsedSum > 0.0 && acc.DeltaSum > 0.0)
            {
                chestPerHour[typeId] = acc.DeltaSum / acc.ElapsedSum * 3600.0;
            }
        }

        return new LiveRates(goldPerHour, xpPerHour, chestPerHour);
    }

    private static Dictionary<int, int> BuildChestLookup(ICollection<MetricSampleChest> chests)
    {
        var lookup = new Dictionary<int, int>(chests.Count);
        foreach (MetricSampleChest chest in chests)
        {
            lookup[chest.ChestTypeId] = chest.Count;
        }
        return lookup;
    }
}
