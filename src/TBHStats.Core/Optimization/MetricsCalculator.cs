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
/// <para>
/// Защита от разрывов опыта — два уровня:
/// <list type="number">
/// <item>
/// <term>Структурный guard по уровню героя (первичный):</term>
/// <description>
/// Легитимный переход <c>HeroLevel</c> между соседними live-сэмплами — строго 0 (тот же уровень)
/// или +1 (одиночный level-up). Любой иной переход при известных обоих уровнях
/// (<c>levelDelta != 0 &amp;&amp; levelDelta != 1</c>, включая отрицательный — откат/разрыв)
/// трактуется как разрыв (смена героя, мультиуровневый скачок, misread heroLevel).
/// Такая пара полностью исключается из расчёта опыта/час — ни в числитель (<c>xpDeltaSum</c>),
/// ни в знаменатель (<c>xpElapsedSum</c>). Надёжно ловит смену героя в late-game,
/// где межгеройская XP-дельта может быть меньше <c>a.XpToLevel</c>
/// (магнитудный guard в этом случае не срабатывает).
/// </description>
/// </item>
/// <item>
/// <term>Магнитудный guard (вторичный, для Δуровня == 0):</term>
/// <description>
/// В не-level-up-ветке (простое вычитание, <c>levelDelta == 0</c>) положительная XP-дельта,
/// превышающая <c>a.XpToLevel</c>, физически невозможна в пределах одного уровня.
/// Ловит внутриуровневые OCR-выбросы, которые структурный guard по уровню пропускает
/// (оба уровня одинаковы). Пара также исключается целиком.
/// </description>
/// </item>
/// </list>
/// Компенсированная level-up-ветка (<c>nearFull &amp;&amp; levelUpByOne</c>)
/// обоими guard'ами не затрагивается — <c>levelDelta == +1</c> легитимен.
/// Золото и сундуки в той же итерации обрабатываются независимо.
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
                // Отбрасываем заведомо невозможные чтения (текущий опыт > нужного до уровня) —
                // это OCR-мусор; иначе он раздувает темп (наблюдалось 1.08e9 опыт/ч).
                bool plausible =
                    !(a.XpToLevel.HasValue && a.Xp.Value > a.XpToLevel.Value)
                    && !(b.XpToLevel.HasValue && b.Xp.Value > b.XpToLevel.Value);

                if (plausible)
                {
                    // Структурный guard по уровню героя (первичный).
                    // Легитимный переход HeroLevel между соседними live-сэмплами:
                    //   levelDelta == 0  — тот же уровень (норма)
                    //   levelDelta == +1 — одиночный level-up (норма)
                    // Любой иной переход (включая отрицательный) при обоих известных уровнях —
                    // разрыв: смена героя, мультиуровневый скачок, misread heroLevel.
                    // Условие именно (levelDelta != 0 && levelDelta != 1), а НЕ Math.Abs > 1:
                    // падение уровня (levelDelta == -1) — тоже разрыв и должно исключаться.
                    bool bothLevelsKnown = a.HeroLevel.HasValue && b.HeroLevel.HasValue;
                    int  levelDelta       = bothLevelsKnown ? b.HeroLevel!.Value - a.HeroLevel!.Value : 0;
                    bool heroLevelDiscontinuity = bothLevelsKnown && levelDelta != 0 && levelDelta != 1;

                    if (!heroLevelDiscontinuity)
                    {
                        double xpDelta;

                        bool levelUpByOne = bothLevelsKnown && levelDelta == 1;

                        // Level-up компенсацию применяем ТОЛЬКО если a.Xp реально у потолка
                        // (≥ 0.8·xpToLevel). Настоящий level-up происходит у полного опыта;
                        // «инкремент уровня» при низком a.Xp — это misread heroLevel, и
                        // компенсация (xpToLevel − a.Xp) инжектировала бы ~весь xpToLevel (выброс).
                        bool nearFull = a.XpToLevel.HasValue
                                        && a.Xp.Value >= a.XpToLevel.Value * 0.8;

                        bool isDiscontinuity = false;

                        if (levelUpByOne && nearFull)
                        {
                            // Level-up компенсация: (xpToLevel - a.Xp) + b.Xp
                            xpDelta = (double)(a.XpToLevel!.Value - a.Xp.Value) + b.Xp.Value;
                        }
                        else
                        {
                            xpDelta = b.Xp.Value - a.Xp.Value;

                            // Магнитудный guard (вторичный, для Δуровня == 0):
                            // в пределах одного уровня прирост опыта не может превысить
                            // потолок уровня a.XpToLevel. Положительная дельта выше потолка
                            // — это внутриуровневый OCR-выброс; пара исключается целиком
                            // из расчёта опыта/час (и из числителя, и из знаменателя).
                            if (a.XpToLevel.HasValue && xpDelta > (double)a.XpToLevel.Value)
                            {
                                isDiscontinuity = true;
                            }
                        }

                        if (!isDiscontinuity)
                        {
                            xpElapsedSum += intervalSeconds;
                            if (xpDelta > 0.0)
                            {
                                xpDeltaSum += xpDelta;
                            }
                        }
                    }
                    // При heroLevelDiscontinuity == true пара целиком пропускается
                    // для опыта/час (ни в xpDeltaSum, ни в xpElapsedSum).
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
