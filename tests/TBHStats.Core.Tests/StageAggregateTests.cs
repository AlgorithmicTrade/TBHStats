namespace TBHStats.Core.Tests;

using FluentAssertions;
using TBHStats.Core.Models;
using TBHStats.Core.Optimization;
using Xunit;

/// <summary>
/// TDD RED-фаза (T033): тесты IStageAggregateCalculator.
/// StageAggregateCalculator — stub, бросает NotImplementedException до T038.
/// </summary>
public sealed class StageAggregateTests
{
    private readonly IStageAggregateCalculator _sut = new StageAggregateCalculator();

    // ────────────────────────────────────────────────────────────
    // Локальные хелперы
    // ────────────────────────────────────────────────────────────

    private static StageRun Run(
        int stageId,
        int durationSeconds,
        long goldGained,
        long xpGained,
        DateTime completedAtUtc,
        bool isPartial = false,
        int heroLevel = 1,
        long heroDamage = 0,
        int heroClassId = 1,
        IEnumerable<StageRunChest>? chests = null)
        => new StageRun
        {
            StageId = stageId,
            DurationSeconds = durationSeconds,
            GoldGained = goldGained,
            XpGained = xpGained,
            CompletedAtUtc = completedAtUtc,
            IsPartial = isPartial,
            Hero = new HeroSnapshot(heroClassId, heroLevel, heroDamage),
            Chests = chests is null
                ? new List<StageRunChest>()
                : new List<StageRunChest>(chests),
        };

    private static StageRunChest Chest(int chestTypeId, int count)
        => new StageRunChest { ChestTypeId = chestTypeId, Count = count };

    private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // ────────────────────────────────────────────────────────────
    // 1. Один non-partial забег: all-time == recent
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_SingleNonPartialRun_AllTimeEqualsRecentAndBestEqualsAvg()
    {
        // Arrange: 3600 сек, 7200 золота → GoldPerHour = 7200
        var run = Run(stageId: 5, durationSeconds: 3600, goldGained: 7200, xpGained: 3600,
            completedAtUtc: T0, heroLevel: 10, heroDamage: 500);
        var runs = new List<StageRun> { run };

        // Act
        var agg = _sut.Compute(5, runs, recentWindowSize: 5, chestTypeIds: Array.Empty<int>());

        // Assert all-time
        agg.RunCount.Should().Be(1);
        agg.AvgGoldPerHour.Should().BeApproximately(7200.0, 1e-6);
        agg.BestGoldPerHour.Should().BeApproximately(7200.0, 1e-6);
        agg.AvgXpPerHour.Should().BeApproximately(3600.0, 1e-6);
        agg.BestXpPerHour.Should().BeApproximately(3600.0, 1e-6);
        agg.AvgDurationSeconds.Should().BeApproximately(3600.0, 1e-6);
        agg.BestDurationSeconds.Should().Be(3600);

        // Assert recent == all-time
        agg.RecentRunCount.Should().Be(1);
        agg.RecentAvgGoldPerHour.Should().BeApproximately(7200.0, 1e-6);
        agg.RecentBestGoldPerHour.Should().BeApproximately(7200.0, 1e-6);
        agg.RecentAvgXpPerHour.Should().BeApproximately(3600.0, 1e-6);
        agg.RecentBestXpPerHour.Should().BeApproximately(3600.0, 1e-6);
        agg.RecentAvgDurationSeconds.Should().BeApproximately(3600.0, 1e-6);
        agg.RecentBestDurationSeconds.Should().Be(3600);
    }

    // ────────────────────────────────────────────────────────────
    // 2. Power-context одного забега: min == max
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_SingleNonPartialRun_PowerContextMinEqualsMax()
    {
        var run = Run(stageId: 5, durationSeconds: 1800, goldGained: 1000, xpGained: 500,
            completedAtUtc: T0, heroLevel: 15, heroDamage: 1234);

        var agg = _sut.Compute(5, new[] { run }, recentWindowSize: 10, chestTypeIds: Array.Empty<int>());

        agg.RecentHeroLevelMin.Should().Be(15);
        agg.RecentHeroLevelMax.Should().Be(15);
        agg.RecentHeroDamageMin.Should().Be(1234L);
        agg.RecentHeroDamageMax.Should().Be(1234L);
    }

    // ────────────────────────────────────────────────────────────
    // 3. Несколько забегов: avg, best, bestDuration
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_MultipleNonPartialRuns_CorrectAvgBestAndMinDuration()
    {
        // run A: 3600 с, 3600 золота → GoldPerHour = 3600
        // run B: 1800 с, 5400 золота → GoldPerHour = 10800
        var runA = Run(5, 3600, 3600, 1800, T0.AddMinutes(10));
        var runB = Run(5, 1800, 5400, 900,  T0.AddMinutes(20));
        var runs = new List<StageRun> { runA, runB };

        var agg = _sut.Compute(5, runs, recentWindowSize: 10, chestTypeIds: Array.Empty<int>());

        agg.RunCount.Should().Be(2);
        agg.AvgGoldPerHour.Should().BeApproximately((3600.0 + 10800.0) / 2.0, 1e-6);
        agg.BestGoldPerHour.Should().BeApproximately(10800.0, 1e-6);
        agg.AvgDurationSeconds.Should().BeApproximately((3600.0 + 1800.0) / 2.0, 1e-6);
        agg.BestDurationSeconds.Should().Be(1800); // min = лучший
    }

    // ────────────────────────────────────────────────────────────
    // 4. recentWindowSize < число забегов → окно = N самых свежих
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_WindowSmallerThanRunCount_RecentUsesNMostRecent()
    {
        // 3 забега; window=2 → в recent только самые свежие 2
        var old  = Run(5, 3600, 1800, 900,  T0);
        var mid  = Run(5, 1800, 7200, 3600, T0.AddHours(1));
        var fresh= Run(5, 900,  3600, 1800, T0.AddHours(2));
        var runs = new List<StageRun> { old, mid, fresh };

        var agg = _sut.Compute(5, runs, recentWindowSize: 2, chestTypeIds: Array.Empty<int>());

        // all-time: 3 забега
        agg.RunCount.Should().Be(3);

        // recent: только mid и fresh (самые свежие 2 по CompletedAtUtc убыв.)
        agg.RecentRunCount.Should().Be(2);

        // mid GoldPerHour = 7200/1800*3600 = 14400; fresh = 3600/900*3600 = 14400
        double midGph   = (double)7200 / 1800 * 3600;
        double freshGph = (double)3600 / 900  * 3600;
        agg.RecentAvgGoldPerHour.Should().BeApproximately((midGph + freshGph) / 2.0, 1e-6);

        // old не попал в recent, но входит в all-time avg
        double oldGph = (double)1800 / 3600 * 3600;
        agg.AvgGoldPerHour.Should().BeApproximately((oldGph + midGph + freshGph) / 3.0, 1e-6);
    }

    // ────────────────────────────────────────────────────────────
    // 5. recentWindowSize >= числа забегов → recent == all-time
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_WindowLargerThanRunCount_RecentEqualsAllTime()
    {
        var runA = Run(5, 3600, 3600, 1800, T0);
        var runB = Run(5, 1800, 1800, 900,  T0.AddHours(1));
        var runs = new List<StageRun> { runA, runB };

        var agg = _sut.Compute(5, runs, recentWindowSize: 100, chestTypeIds: Array.Empty<int>());

        agg.RunCount.Should().Be(agg.RecentRunCount);
        agg.AvgGoldPerHour.Should().BeApproximately(agg.RecentAvgGoldPerHour, 1e-6);
        agg.BestGoldPerHour.Should().BeApproximately(agg.RecentBestGoldPerHour, 1e-6);
        agg.AvgDurationSeconds.Should().BeApproximately(agg.RecentAvgDurationSeconds, 1e-6);
        agg.BestDurationSeconds.Should().Be(agg.RecentBestDurationSeconds);
    }

    // ────────────────────────────────────────────────────────────
    // 6. Partial-забег исключён из all-time и recent
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_PartialRunPresent_ExcludedFromAllTimeAndRecent()
    {
        var full    = Run(5, 1800, 3600, 1800, T0);
        var partial = Run(5, 900,  9999, 9999, T0.AddHours(1), isPartial: true);
        var runs = new List<StageRun> { full, partial };

        var agg = _sut.Compute(5, runs, recentWindowSize: 10, chestTypeIds: Array.Empty<int>());

        // partial не учитывается
        agg.RunCount.Should().Be(1);
        agg.RecentRunCount.Should().Be(1);

        double expectedGph = (double)3600 / 1800 * 3600;
        agg.AvgGoldPerHour.Should().BeApproximately(expectedGph, 1e-6);
        agg.BestGoldPerHour.Should().BeApproximately(expectedGph, 1e-6);
        agg.RecentAvgGoldPerHour.Should().BeApproximately(expectedGph, 1e-6);
        agg.RecentBestGoldPerHour.Should().BeApproximately(expectedGph, 1e-6);
    }

    // ────────────────────────────────────────────────────────────
    // 7. Power-context: диапазон при разных героях в окне
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_DifferentHeroesInWindow_PowerContextReflectsMinMaxRange()
    {
        var runA = Run(5, 1800, 3600, 1800, T0,              heroLevel: 5,  heroDamage: 100);
        var runB = Run(5, 1800, 3600, 1800, T0.AddHours(1),  heroLevel: 20, heroDamage: 800);
        var runC = Run(5, 1800, 3600, 1800, T0.AddHours(2),  heroLevel: 10, heroDamage: 400);
        var runs = new List<StageRun> { runA, runB, runC };

        var agg = _sut.Compute(5, runs, recentWindowSize: 3, chestTypeIds: Array.Empty<int>());

        agg.RecentHeroLevelMin.Should().Be(5);
        agg.RecentHeroLevelMax.Should().Be(20);
        agg.RecentHeroDamageMin.Should().Be(100L);
        agg.RecentHeroDamageMax.Should().Be(800L);
    }

    // ────────────────────────────────────────────────────────────
    // 8. Пустой список → нулевой агрегат, power null
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_EmptyRunList_ReturnsZeroAggregateWithNullPower()
    {
        var agg = _sut.Compute(7, Array.Empty<StageRun>(), recentWindowSize: 5,
            chestTypeIds: new[] { 1, 2 });

        agg.RunCount.Should().Be(0);
        agg.RecentRunCount.Should().Be(0);
        agg.AvgGoldPerHour.Should().BeApproximately(0.0, 1e-6);
        agg.BestGoldPerHour.Should().BeApproximately(0.0, 1e-6);
        agg.AvgXpPerHour.Should().BeApproximately(0.0, 1e-6);
        agg.BestXpPerHour.Should().BeApproximately(0.0, 1e-6);
        agg.AvgDurationSeconds.Should().BeApproximately(0.0, 1e-6);
        agg.BestDurationSeconds.Should().Be(0);
        agg.RecentAvgGoldPerHour.Should().BeApproximately(0.0, 1e-6);
        agg.RecentBestGoldPerHour.Should().BeApproximately(0.0, 1e-6);

        agg.RecentHeroLevelMin.Should().BeNull();
        agg.RecentHeroLevelMax.Should().BeNull();
        agg.RecentHeroDamageMin.Should().BeNull();
        agg.RecentHeroDamageMax.Should().BeNull();
    }

    // ────────────────────────────────────────────────────────────
    // 9. Все забеги partial → как пустой
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_AllRunsPartial_TreatedAsEmpty()
    {
        var p1 = Run(5, 900,  9999, 9999, T0,             isPartial: true);
        var p2 = Run(5, 600,  8888, 8888, T0.AddHours(1), isPartial: true);
        var runs = new List<StageRun> { p1, p2 };

        var agg = _sut.Compute(5, runs, recentWindowSize: 5, chestTypeIds: Array.Empty<int>());

        agg.RunCount.Should().Be(0);
        agg.RecentRunCount.Should().Be(0);
        agg.AvgGoldPerHour.Should().BeApproximately(0.0, 1e-6);
        agg.RecentHeroLevelMin.Should().BeNull();
        agg.RecentHeroLevelMax.Should().BeNull();
        agg.RecentHeroDamageMin.Should().BeNull();
        agg.RecentHeroDamageMax.Should().BeNull();
    }

    // ────────────────────────────────────────────────────────────
    // 10. StageId проброшен в результат
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_AnyInput_StageIdMatchesParameter()
    {
        var run = Run(42, 3600, 3600, 1800, T0);

        var agg = _sut.Compute(42, new[] { run }, recentWindowSize: 5, chestTypeIds: Array.Empty<int>());

        agg.StageId.Should().Be(42);
    }

    // ────────────────────────────────────────────────────────────
    // 11. Chest rates all-time: сумма Count / сумма времени * 3600
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_ChestRatesAllTime_CorrectRatePerHour()
    {
        // run A: 3600 с, 6 сундуков типа 1 → rate = 6/3600*3600 = 6.0/hr
        // run B: 1800 с, 3 сундука типа 1 → rate = 3/1800*3600 = 6.0/hr
        // all-time: (6+3) / (3600+1800) * 3600 = 9/5400*3600 = 6.0
        var runA = Run(5, 3600, 0, 0, T0,
            chests: new[] { Chest(chestTypeId: 1, count: 6) });
        var runB = Run(5, 1800, 0, 0, T0.AddHours(1),
            chests: new[] { Chest(chestTypeId: 1, count: 3) });
        var runs = new List<StageRun> { runA, runB };

        var agg = _sut.Compute(5, runs, recentWindowSize: 10, chestTypeIds: new[] { 1 });

        agg.ChestRates.Should().HaveCount(1);
        var rate = agg.ChestRates.Single(r => r.ChestTypeId == 1);
        rate.RatePerHour.Should().BeApproximately(6.0, 1e-6);
    }

    // ────────────────────────────────────────────────────────────
    // 12. Chest rates recent отличается от all-time (старые вне окна)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_ChestRatesRecent_DiffersFromAllTimeWhenOldRunsOutsideWindow()
    {
        // run old: 3600 с, 2 сундука типа 1 → oldRate = 2.0/hr
        // run fresh: 900 с, 9 сундуков типа 1 → freshRate = 36.0/hr
        // all-time: (2+9)/(3600+900)*3600 = 11/4500*3600 ≈ 8.8
        // recent (window=1): только fresh: 9/900*3600 = 36.0
        var runOld   = Run(5, 3600, 0, 0, T0,
            chests: new[] { Chest(1, 2) });
        var runFresh = Run(5, 900,  0, 0, T0.AddHours(2),
            chests: new[] { Chest(1, 9) });
        var runs = new List<StageRun> { runOld, runFresh };

        var agg = _sut.Compute(5, runs, recentWindowSize: 1, chestTypeIds: new[] { 1 });

        var rate = agg.ChestRates.Single(r => r.ChestTypeId == 1);

        double expectedAllTime = (2.0 + 9.0) / (3600.0 + 900.0) * 3600.0;
        double expectedRecent  = 9.0 / 900.0 * 3600.0;

        rate.RatePerHour.Should().BeApproximately(expectedAllTime, 1e-6);
        rate.RecentRatePerHour.Should().BeApproximately(expectedRecent, 1e-6);

        // они должны отличаться
        rate.RatePerHour.Should().NotBeApproximately(rate.RecentRatePerHour, 1e-6);
    }

    // ────────────────────────────────────────────────────────────
    // 13. ChestTypeId без сундуков → запись с rate 0
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_ChestTypeIdAbsentInRuns_EntryWithZeroRate()
    {
        var run = Run(5, 1800, 0, 0, T0,
            chests: new[] { Chest(chestTypeId: 1, count: 5) });

        // Запрашиваем типы 1 и 99 (99 — нет ни в одном забеге)
        var agg = _sut.Compute(5, new[] { run }, recentWindowSize: 5,
            chestTypeIds: new[] { 1, 99 });

        agg.ChestRates.Should().HaveCount(2);

        var absent = agg.ChestRates.Single(r => r.ChestTypeId == 99);
        absent.RatePerHour.Should().BeApproximately(0.0, 1e-6);
        absent.RecentRatePerHour.Should().BeApproximately(0.0, 1e-6);
    }

    // ────────────────────────────────────────────────────────────
    // 14. Несколько типов сундуков в chestTypeIds → несколько записей
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_MultipleChestTypeIds_OneEntryPerTypeId()
    {
        var run = Run(5, 3600, 0, 0, T0,
            chests: new[]
            {
                Chest(chestTypeId: 1, count: 4),
                Chest(chestTypeId: 2, count: 8),
            });

        var agg = _sut.Compute(5, new[] { run }, recentWindowSize: 5,
            chestTypeIds: new[] { 1, 2, 3 });

        agg.ChestRates.Should().HaveCount(3);

        var r1 = agg.ChestRates.Single(r => r.ChestTypeId == 1);
        var r2 = agg.ChestRates.Single(r => r.ChestTypeId == 2);
        var r3 = agg.ChestRates.Single(r => r.ChestTypeId == 3);

        r1.RatePerHour.Should().BeApproximately(4.0 / 3600.0 * 3600.0, 1e-6); // 4.0
        r2.RatePerHour.Should().BeApproximately(8.0 / 3600.0 * 3600.0, 1e-6); // 8.0
        r3.RatePerHour.Should().BeApproximately(0.0, 1e-6);
    }

    // ────────────────────────────────────────────────────────────
    // 15. Chest rates: partial-забеги не учитываются в rate
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_PartialRunsExcludedFromChestRates()
    {
        var full    = Run(5, 3600, 0, 0, T0,
            chests: new[] { Chest(1, 4) });
        var partial = Run(5, 1800, 0, 0, T0.AddHours(1), isPartial: true,
            chests: new[] { Chest(1, 100) }); // не должен войти в rate

        var agg = _sut.Compute(5, new[] { full, partial }, recentWindowSize: 10,
            chestTypeIds: new[] { 1 });

        var rate = agg.ChestRates.Single(r => r.ChestTypeId == 1);
        // только full: 4/3600*3600 = 4.0
        rate.RatePerHour.Should().BeApproximately(4.0, 1e-6);
    }

    // ────────────────────────────────────────────────────────────
    // 16. Power-context окна меньшего размера (не все забеги)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_PowerContextOnlyFromRecentWindow_NotFromAllRuns()
    {
        // 3 забега; window=2; самые свежие — runB и runC
        // runA (старый, вне окна) имеет экстремальные level/damage
        var runA = Run(5, 1800, 0, 0, T0,
            heroLevel: 1, heroDamage: 999_999L);
        var runB = Run(5, 1800, 0, 0, T0.AddHours(1),
            heroLevel: 5, heroDamage: 200L);
        var runC = Run(5, 1800, 0, 0, T0.AddHours(2),
            heroLevel: 8, heroDamage: 400L);

        var agg = _sut.Compute(5, new[] { runA, runB, runC }, recentWindowSize: 2,
            chestTypeIds: Array.Empty<int>());

        // runA вне окна → его Level=1 и Damage=999999 не влияют на power-context
        agg.RecentHeroLevelMin.Should().Be(5);
        agg.RecentHeroLevelMax.Should().Be(8);
        agg.RecentHeroDamageMin.Should().Be(200L);
        agg.RecentHeroDamageMax.Should().Be(400L);
    }

    // ────────────────────────────────────────────────────────────
    // 17. ChestRates empty list → пустая коллекция (не null)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_NoChestTypeIds_ChestRatesIsEmpty()
    {
        var run = Run(5, 1800, 3600, 1800, T0);

        var agg = _sut.Compute(5, new[] { run }, recentWindowSize: 5,
            chestTypeIds: Array.Empty<int>());

        agg.ChestRates.Should().NotBeNull();
        agg.ChestRates.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────
    // 18. Пустой список + несколько chestTypeIds → rate 0 для каждого
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_EmptyRunsWithChestTypeIds_AllRatesZero()
    {
        var agg = _sut.Compute(3, Array.Empty<StageRun>(), recentWindowSize: 5,
            chestTypeIds: new[] { 1, 2, 3 });

        agg.ChestRates.Should().HaveCount(3);
        foreach (var rate in agg.ChestRates)
        {
            rate.RatePerHour.Should().BeApproximately(0.0, 1e-6);
            rate.RecentRatePerHour.Should().BeApproximately(0.0, 1e-6);
        }
    }
}
