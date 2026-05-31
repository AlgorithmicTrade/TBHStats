namespace TBHStats.Core.Tests;

using FluentAssertions;
using TBHStats.Core.Models;
using TBHStats.Core.Optimization;
using Xunit;

/// <summary>
/// TDD red-тесты для <see cref="OptimizationService"/> (T032).
/// Реализация — stub, бросающий <see cref="NotImplementedException"/> (реализуется в T039).
/// Все тесты ДОЛЖНЫ ПАДАТЬ до реализации T039 — это ожидаемое поведение red-фазы.
/// </summary>
public sealed class OptimizationServiceTests
{
    private static readonly DateTime BaseTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly OptimizationService _sut = new();

    // =========================================================================
    // Вспомогательная фабрика StageAggregate
    // =========================================================================

    /// <summary>
    /// Строит <see cref="StageAggregate"/> с явно заданными ключевыми полями.
    /// Все прочие поля остаются на дефолте (0 / null).
    /// </summary>
    private static StageAggregate MakeAggregate(
        int stageId,
        int recentRunCount = 0,
        double recentAvgGold = 0,
        double recentBestGold = 0,
        double recentAvgXp = 0,
        double recentBestXp = 0,
        int runCount = 0,
        double avgGold = 0,
        double bestGold = 0,
        double avgXp = 0,
        double bestXp = 0,
        int? heroLevelMin = null,
        int? heroLevelMax = null,
        long? heroDamageMin = null,
        long? heroDamageMax = null)
        => new()
        {
            StageId                = stageId,
            RecentRunCount         = recentRunCount,
            RecentAvgGoldPerHour   = recentAvgGold,
            RecentBestGoldPerHour  = recentBestGold,
            RecentAvgXpPerHour     = recentAvgXp,
            RecentBestXpPerHour    = recentBestXp,
            RunCount               = runCount,
            AvgGoldPerHour         = avgGold,
            BestGoldPerHour        = bestGold,
            AvgXpPerHour           = avgXp,
            BestXpPerHour          = bestXp,
            RecentHeroLevelMin     = heroLevelMin,
            RecentHeroLevelMax     = heroLevelMax,
            RecentHeroDamageMin    = heroDamageMin,
            RecentHeroDamageMax    = heroDamageMax,
            UpdatedAtUtc           = BaseTime,
        };

    // =========================================================================
    // Тест 1 — Recent GoldPerHour: правильный порядок рангов (три этапа)
    // =========================================================================

    [Fact]
    public void RankStages_RecentGoldPerHour_ThreeStages_ReturnsDescendingRanks()
    {
        // Arrange
        var aggregates = new[]
        {
            MakeAggregate(stageId: 1, recentRunCount: 3, recentAvgGold: 1000, recentBestGold: 1500),
            MakeAggregate(stageId: 2, recentRunCount: 2, recentAvgGold: 3000, recentBestGold: 4000),
            MakeAggregate(stageId: 3, recentRunCount: 5, recentAvgGold: 2000, recentBestGold: 2500),
        };

        // Act
        var result = _sut.RankStages(aggregates, OptimizationMetric.GoldPerHour, AggregationScope.Recent);

        // Assert
        result.Should().HaveCount(3);

        var rank1 = result.Single(r => r.Rank == 1);
        var rank2 = result.Single(r => r.Rank == 2);
        var rank3 = result.Single(r => r.Rank == 3);

        rank1.StageId.Should().Be(2);   // RecentAvgGold = 3000 (лучший)
        rank2.StageId.Should().Be(3);   // RecentAvgGold = 2000
        rank3.StageId.Should().Be(1);   // RecentAvgGold = 1000

        rank1.Score.Should().Be(3000);
        rank2.Score.Should().Be(2000);
        rank3.Score.Should().Be(1000);
    }

    // =========================================================================
    // Тест 2 — Recent XpPerHour: другой порядок по сравнению с золотом
    // =========================================================================

    [Fact]
    public void RankStages_RecentXpPerHour_ReturnsOrderDifferentFromGold()
    {
        // Arrange
        // Золото: этап 1 > этап 2 > этап 3
        // Опыт:  этап 3 > этап 1 > этап 2 — другой порядок
        var aggregates = new[]
        {
            MakeAggregate(stageId: 1, recentRunCount: 3, recentAvgGold: 3000, recentAvgXp: 500),
            MakeAggregate(stageId: 2, recentRunCount: 3, recentAvgGold: 2000, recentAvgXp: 200),
            MakeAggregate(stageId: 3, recentRunCount: 3, recentAvgGold: 1000, recentAvgXp: 800),
        };

        // Act
        var goldRanking = _sut.RankStages(aggregates, OptimizationMetric.GoldPerHour, AggregationScope.Recent);
        var xpRanking   = _sut.RankStages(aggregates, OptimizationMetric.XpPerHour,   AggregationScope.Recent);

        // Assert: по золоту rank1 — этап 1
        goldRanking.Single(r => r.Rank == 1).StageId.Should().Be(1);

        // Assert: по опыту rank1 — этап 3 (другой этап)
        xpRanking.Single(r => r.Rank == 1).StageId.Should().Be(3);
        xpRanking.Single(r => r.Rank == 2).StageId.Should().Be(1);
        xpRanking.Single(r => r.Rank == 3).StageId.Should().Be(2);
    }

    // =========================================================================
    // Тест 3 — Переключение scope Recent ↔ AllTime меняет ранжирование
    // =========================================================================

    [Fact]
    public void RankStages_ScopeSwitch_RecentVsAllTime_DifferentOrder()
    {
        // Arrange:
        // Этап 10: слаб в all-time (avgGold=500), силён в recent (recentAvgGold=5000)
        // Этап 11: силён в all-time (avgGold=4000), слаб в recent (recentAvgGold=1000)
        var aggregates = new[]
        {
            MakeAggregate(stageId: 10,
                recentRunCount: 3, recentAvgGold: 5000, recentBestGold: 6000,
                runCount: 10, avgGold: 500, bestGold: 600),
            MakeAggregate(stageId: 11,
                recentRunCount: 3, recentAvgGold: 1000, recentBestGold: 1500,
                runCount: 10, avgGold: 4000, bestGold: 5000),
        };

        // Act
        var recentRanking  = _sut.RankStages(aggregates, OptimizationMetric.GoldPerHour, AggregationScope.Recent);
        var allTimeRanking = _sut.RankStages(aggregates, OptimizationMetric.GoldPerHour, AggregationScope.AllTime);

        // Assert: по свежему — этап 10 первый
        recentRanking.Single(r => r.Rank == 1).StageId.Should().Be(10);

        // Assert: по all-time — этап 11 первый
        allTimeRanking.Single(r => r.Rank == 1).StageId.Should().Be(11);
    }

    // =========================================================================
    // Тест 4 — Tie-break: равный Avg → побеждает тот, у кого Best выше
    // =========================================================================

    [Fact]
    public void RankStages_TieOnAvgGold_TieBreakByBestGold()
    {
        // Arrange: оба этапа имеют одинаковый RecentAvgGoldPerHour, но разный RecentBestGoldPerHour
        var aggregates = new[]
        {
            MakeAggregate(stageId: 20, recentRunCount: 2, recentAvgGold: 2000, recentBestGold: 2500),
            MakeAggregate(stageId: 21, recentRunCount: 2, recentAvgGold: 2000, recentBestGold: 3000),
        };

        // Act
        var result = _sut.RankStages(aggregates, OptimizationMetric.GoldPerHour, AggregationScope.Recent);

        // Assert: этап 21 лучше по tie-break (best 3000 > 2500)
        result.Single(r => r.Rank == 1).StageId.Should().Be(21);
        result.Single(r => r.Rank == 2).StageId.Should().Be(20);
    }

    // =========================================================================
    // Тест 5 — Tie-break XpPerHour: равный RecentAvgXp → побеждает тот, у кого RecentBestXp выше
    // =========================================================================

    [Fact]
    public void RankStages_TieOnAvgXp_TieBreakByBestXp()
    {
        // Arrange
        var aggregates = new[]
        {
            MakeAggregate(stageId: 30, recentRunCount: 2, recentAvgXp: 1000, recentBestXp: 1200),
            MakeAggregate(stageId: 31, recentRunCount: 2, recentAvgXp: 1000, recentBestXp: 1800),
        };

        // Act
        var result = _sut.RankStages(aggregates, OptimizationMetric.XpPerHour, AggregationScope.Recent);

        // Assert: этап 31 лучше по tie-break (bestXp 1800 > 1200)
        result.Single(r => r.Rank == 1).StageId.Should().Be(31);
        result.Single(r => r.Rank == 2).StageId.Should().Be(30);
    }

    // =========================================================================
    // Тест 6 — Этап с RecentRunCount==0 исключён из Recent-ранжирования
    // =========================================================================

    [Fact]
    public void RankStages_RecentRunCountZero_StageExcludedFromRecentRanking()
    {
        // Arrange: этап 40 — нет recent-данных, этап 41 — есть
        var aggregates = new[]
        {
            MakeAggregate(stageId: 40, recentRunCount: 0, recentAvgGold: 9999, runCount: 5, avgGold: 5000),
            MakeAggregate(stageId: 41, recentRunCount: 3, recentAvgGold: 1000, runCount: 3, avgGold: 1000),
        };

        // Act
        var result = _sut.RankStages(aggregates, OptimizationMetric.GoldPerHour, AggregationScope.Recent);

        // Assert: в Recent-ранжировании только этап 41
        result.Should().HaveCount(1);
        result.Single().StageId.Should().Be(41);
    }

    // =========================================================================
    // Тест 7 — Этап с RecentRunCount==0 включён в AllTime (если RunCount > 0)
    // =========================================================================

    [Fact]
    public void RankStages_AllTime_IncludesStageWithNoRecentDataIfRunCountPositive()
    {
        // Arrange: этап 50 — нет recent, но RunCount=5; этап 51 — есть recent и RunCount=3
        var aggregates = new[]
        {
            MakeAggregate(stageId: 50, recentRunCount: 0, recentAvgGold: 0,
                          runCount: 5, avgGold: 3000, bestGold: 4000),
            MakeAggregate(stageId: 51, recentRunCount: 3, recentAvgGold: 1000,
                          runCount: 3, avgGold: 1000, bestGold: 1200),
        };

        // Act
        var result = _sut.RankStages(aggregates, OptimizationMetric.GoldPerHour, AggregationScope.AllTime);

        // Assert: оба включены; этап 50 — rank 1 (avgGold=3000 > avgGold=1000)
        result.Should().HaveCount(2);
        result.Single(r => r.Rank == 1).StageId.Should().Be(50);
    }

    // =========================================================================
    // Тест 8 — RecommendBestStage возвращает элемент с Rank == 1
    // =========================================================================

    [Fact]
    public void RecommendBestStage_ReturnsRankOneStage()
    {
        // Arrange
        var aggregates = new[]
        {
            MakeAggregate(stageId: 60, recentRunCount: 3, recentAvgGold: 500,  recentBestGold: 600),
            MakeAggregate(stageId: 61, recentRunCount: 3, recentAvgGold: 2500, recentBestGold: 3000),
            MakeAggregate(stageId: 62, recentRunCount: 3, recentAvgGold: 1500, recentBestGold: 2000),
        };

        // Act
        var recommendation = _sut.RecommendBestStage(aggregates, OptimizationMetric.GoldPerHour);

        // Assert
        recommendation.Should().NotBeNull();
        recommendation!.Rank.Should().Be(1);
        recommendation.StageId.Should().Be(61); // наибольший RecentAvgGold
    }

    // =========================================================================
    // Тест 9 — Пустой список → RankStages возвращает пустой список
    // =========================================================================

    [Fact]
    public void RankStages_EmptyList_ReturnsEmptyList()
    {
        // Arrange
        var aggregates = Array.Empty<StageAggregate>();

        // Act
        var result = _sut.RankStages(aggregates, OptimizationMetric.GoldPerHour);

        // Assert
        result.Should().BeEmpty();
    }

    // =========================================================================
    // Тест 10 — Пустой список → RecommendBestStage возвращает null
    // =========================================================================

    [Fact]
    public void RecommendBestStage_EmptyList_ReturnsNull()
    {
        // Arrange
        var aggregates = Array.Empty<StageAggregate>();

        // Act
        var result = _sut.RecommendBestStage(aggregates, OptimizationMetric.GoldPerHour);

        // Assert
        result.Should().BeNull();
    }

    // =========================================================================
    // Тест 11 — Все этапы без данных в scope → оба метода возвращают пустое / null
    // =========================================================================

    [Fact]
    public void RankStages_AllStagesHaveNoRecentData_ReturnsEmpty()
    {
        // Arrange: у всех этапов RecentRunCount == 0
        var aggregates = new[]
        {
            MakeAggregate(stageId: 70, recentRunCount: 0, recentAvgGold: 9000),
            MakeAggregate(stageId: 71, recentRunCount: 0, recentAvgGold: 8000),
        };

        // Act
        var result = _sut.RankStages(aggregates, OptimizationMetric.GoldPerHour, AggregationScope.Recent);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void RecommendBestStage_AllStagesHaveNoRecentData_ReturnsNull()
    {
        // Arrange
        var aggregates = new[]
        {
            MakeAggregate(stageId: 80, recentRunCount: 0),
            MakeAggregate(stageId: 81, recentRunCount: 0),
        };

        // Act
        var result = _sut.RecommendBestStage(aggregates, OptimizationMetric.GoldPerHour, AggregationScope.Recent);

        // Assert
        result.Should().BeNull();
    }

    // =========================================================================
    // Тест 12 — Power-context корректно пробрасывается из recent-полей агрегата
    // =========================================================================

    [Fact]
    public void RankStages_PowerContextPropagatedFromRecentFields()
    {
        // Arrange
        var aggregates = new[]
        {
            MakeAggregate(stageId: 90,
                recentRunCount: 3, recentAvgGold: 1000,
                heroLevelMin: 10, heroLevelMax: 15,
                heroDamageMin: 5000L, heroDamageMax: 8000L),
        };

        // Act
        var result = _sut.RankStages(aggregates, OptimizationMetric.GoldPerHour, AggregationScope.Recent);

        // Assert
        result.Should().HaveCount(1);
        var power = result[0].Power;
        power.HeroLevelMin.Should().Be(10);
        power.HeroLevelMax.Should().Be(15);
        power.HeroDamageMin.Should().Be(5000L);
        power.HeroDamageMax.Should().Be(8000L);
    }

    // =========================================================================
    // Тест 13 — Power-context с null-полями (свежее окно пустое, AllTime-ранжирование)
    // =========================================================================

    [Fact]
    public void RankStages_AllTime_PowerContextIsNullWhenRecentWindowEmpty()
    {
        // Arrange: RecentRunCount = 0, поэтому power-поля null; AllTime-ранжирование (RunCount > 0)
        var aggregates = new[]
        {
            MakeAggregate(stageId: 100,
                recentRunCount: 0,
                runCount: 5, avgGold: 2000, bestGold: 2500,
                heroLevelMin: null, heroLevelMax: null,
                heroDamageMin: null, heroDamageMax: null),
        };

        // Act
        var result = _sut.RankStages(aggregates, OptimizationMetric.GoldPerHour, AggregationScope.AllTime);

        // Assert
        result.Should().HaveCount(1);
        var power = result[0].Power;
        power.HeroLevelMin.Should().BeNull();
        power.HeroLevelMax.Should().BeNull();
        power.HeroDamageMin.Should().BeNull();
        power.HeroDamageMax.Should().BeNull();
    }

    // =========================================================================
    // Тест 14 — Дефолтный scope (вызов без 3-го аргумента) == Recent
    // =========================================================================

    [Fact]
    public void RankStages_DefaultScope_EqualsRecent()
    {
        // Arrange: этап с RecentRunCount > 0 и этап с RecentRunCount == 0 (только AllTime)
        var aggregates = new[]
        {
            MakeAggregate(stageId: 110, recentRunCount: 3, recentAvgGold: 2000, recentBestGold: 2500,
                          runCount: 3, avgGold: 2000),
            MakeAggregate(stageId: 111, recentRunCount: 0, recentAvgGold: 0,
                          runCount: 10, avgGold: 9000, bestGold: 10000),
        };

        // Act — без 3-го аргумента (дефолт)
        var defaultResult = _sut.RankStages(aggregates, OptimizationMetric.GoldPerHour);
        // Act — явный Recent
        var recentResult  = _sut.RankStages(aggregates, OptimizationMetric.GoldPerHour, AggregationScope.Recent);

        // Assert: этап 111 исключён (RecentRunCount == 0) — дефолт совпадает с Recent
        defaultResult.Should().HaveCount(1);
        defaultResult.Single().StageId.Should().Be(110);

        // Одинаковые результаты
        defaultResult.Should().BeEquivalentTo(recentResult);
    }

    // =========================================================================
    // Тест 15 — RecommendBestStage DefaultScope: без аргумента == Recent
    // =========================================================================

    [Fact]
    public void RecommendBestStage_DefaultScope_EqualsRecent()
    {
        // Arrange
        var aggregates = new[]
        {
            MakeAggregate(stageId: 120, recentRunCount: 2, recentAvgGold: 3000, recentBestGold: 3500),
            MakeAggregate(stageId: 121, recentRunCount: 0, recentAvgGold: 0,
                          runCount: 10, avgGold: 10000),
        };

        // Act
        var defaultRec = _sut.RecommendBestStage(aggregates, OptimizationMetric.GoldPerHour);
        var recentRec  = _sut.RecommendBestStage(aggregates, OptimizationMetric.GoldPerHour, AggregationScope.Recent);

        // Assert: рекомендован этап 120 (единственный с RecentRunCount > 0)
        defaultRec.Should().NotBeNull();
        defaultRec!.StageId.Should().Be(120);

        // Дефолт совпадает с явным Recent
        recentRec.Should().NotBeNull();
        recentRec!.StageId.Should().Be(defaultRec.StageId);
    }

    // =========================================================================
    // Тест 16 — Reason: непустая строка; для GoldPerHour + Recent содержит ключевые слова
    // =========================================================================

    [Fact]
    public void RankStages_Reason_NotNullOrWhiteSpace()
    {
        // Arrange
        var aggregates = new[]
        {
            MakeAggregate(stageId: 130, recentRunCount: 2, recentAvgGold: 1000, recentBestGold: 1200),
        };

        // Act
        var result = _sut.RankStages(aggregates, OptimizationMetric.GoldPerHour, AggregationScope.Recent);

        // Assert
        result.Should().HaveCount(1);
        result[0].Reason.Should().NotBeNullOrWhiteSpace();
    }

    // =========================================================================
    // Тест 17 — AllTime: tie-break по BestGoldPerHour (all-time best, не recent)
    // =========================================================================

    [Fact]
    public void RankStages_AllTime_TieBreakByAllTimeBestGold()
    {
        // Arrange: равный AvgGoldPerHour, разный BestGoldPerHour (all-time)
        var aggregates = new[]
        {
            MakeAggregate(stageId: 140, runCount: 5, avgGold: 2000, bestGold: 2200),
            MakeAggregate(stageId: 141, runCount: 5, avgGold: 2000, bestGold: 3500),
        };

        // Act
        var result = _sut.RankStages(aggregates, OptimizationMetric.GoldPerHour, AggregationScope.AllTime);

        // Assert: этап 141 первый (best 3500 > 2200)
        result.Single(r => r.Rank == 1).StageId.Should().Be(141);
        result.Single(r => r.Rank == 2).StageId.Should().Be(140);
    }
}
