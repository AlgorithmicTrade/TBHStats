namespace TBHStats.Data.Tests;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TBHStats.Core.Models;
using TBHStats.Core.Optimization;
using TBHStats.Data.Repositories;
using Xunit;

/// <summary>
/// Интеграционные тесты <see cref="StageAggregateRepository.RecomputeForStageAsync"/>
/// на реальном временном SQLite-файле (T038).
/// Никаких in-memory провайдеров, никаких моков.
/// </summary>
public sealed class StageAggregateRecomputeTests : TempDbFixture
{
    // ─────────────────────────────────────────────────────────────────────────
    // Вспомогательные методы
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Создаёт репозиторий агрегатов с реальным <see cref="StageAggregateCalculator"/> (без моков).
    /// </summary>
    private StageAggregateRepository CreateRepo(TbhStatsDbContext ctx)
        => new StageAggregateRepository(ctx, new StageAggregateCalculator());

    /// <summary>
    /// Создаёт HeroClass и возвращает его Id.
    /// </summary>
    private async Task<int> SeedHeroClassAsync(TbhStatsDbContext ctx, string key = "warrior")
    {
        var hc = new HeroClass { Key = key, DisplayName = "Test Hero", IsActive = true };
        ctx.HeroClasses.Add(hc);
        await ctx.SaveChangesAsync();
        return hc.Id;
    }

    /// <summary>
    /// Создаёт и сохраняет non-partial <see cref="StageRun"/> для указанного этапа.
    /// </summary>
    private async Task<StageRun> AddRunAsync(
        TbhStatsDbContext ctx,
        int stageId,
        int heroClassId,
        long gold,
        long xp,
        int durationSec,
        DateTime completedAt,
        bool isPartial = false,
        IEnumerable<StageRunChest>? chests = null)
    {
        var run = new StageRun
        {
            StageId         = stageId,
            DurationSeconds = durationSec,
            GoldGained      = gold,
            XpGained        = xp,
            Hero            = new HeroSnapshot(heroClassId, Level: 20, Damage: 10_000),
            CompletedAtUtc  = completedAt,
            IsPartial       = isPartial,
            Chests          = chests?.ToList() ?? new List<StageRunChest>(),
        };
        ctx.StageRuns.Add(run);
        await ctx.SaveChangesAsync();
        return run;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Тест 1: all-time поля сохранены и переживают перезапуск
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecomputeForStageAsync_MultipleNonPartialRuns_PersistsAllTimeFields()
    {
        // Arrange
        var stage       = await Db.Stages.FirstAsync();
        int heroClassId = await SeedHeroClassAsync(Db);

        // 3 забега: gold 3600/7200/3600 в час (duration 3600 c => gold=3600,7200,3600)
        var t0 = new DateTime(2026, 5, 31, 10, 0, 0, DateTimeKind.Utc);
        await AddRunAsync(Db, stage.Id, heroClassId, gold: 3_600, xp: 1_800, durationSec: 3_600, completedAt: t0);
        await AddRunAsync(Db, stage.Id, heroClassId, gold: 7_200, xp: 3_600, durationSec: 3_600, completedAt: t0.AddHours(1));
        await AddRunAsync(Db, stage.Id, heroClassId, gold: 3_600, xp: 1_800, durationSec: 3_600, completedAt: t0.AddHours(2));

        var repo = CreateRepo(Db);

        // Act
        await repo.RecomputeForStageAsync(stage.Id, recentWindowSize: 10, CancellationToken.None);

        // Assert — прочитать в НОВОМ контексте (перезапуск)
        await using var ctx2 = CreateContext();
        var agg = await ctx2.StageAggregates.SingleAsync(a => a.StageId == stage.Id);

        agg.RunCount.Should().Be(3);
        agg.AvgGoldPerHour.Should().BeApproximately(4_800.0, 1e-6, "среднее (3600+7200+3600)/3 gold/ч * 3600/3600");
        agg.BestGoldPerHour.Should().BeApproximately(7_200.0, 1e-6);
        agg.AvgXpPerHour.Should().BeApproximately(2_400.0, 1e-6);
        agg.BestXpPerHour.Should().BeApproximately(3_600.0, 1e-6);
        agg.AvgDurationSeconds.Should().BeApproximately(3_600.0, 1e-6);
        agg.BestDurationSeconds.Should().Be(3_600);
        agg.UpdatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Тест 2: recent-поля отличаются от all-time при recentWindowSize < RunCount
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecomputeForStageAsync_RecentWindowSmallerThanTotal_RecentFieldsDiffer()
    {
        // Arrange
        var stage       = await Db.Stages.FirstAsync();
        int heroClassId = await SeedHeroClassAsync(Db, "mage");

        var t0 = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);

        // 4 забега с разным золотом:
        //   старые: 3600 и 3600 gold/3600 sec = 3600/h каждый (oldest CompletedAt)
        //   свежие: 7200 и 9000 gold/3600 sec = 7200/h и 9000/h (newest CompletedAt)
        await AddRunAsync(Db, stage.Id, heroClassId, gold: 3_600, xp: 1_000, durationSec: 3_600, completedAt: t0);
        await AddRunAsync(Db, stage.Id, heroClassId, gold: 3_600, xp: 1_000, durationSec: 3_600, completedAt: t0.AddHours(1));
        await AddRunAsync(Db, stage.Id, heroClassId, gold: 7_200, xp: 2_000, durationSec: 3_600, completedAt: t0.AddHours(2));
        await AddRunAsync(Db, stage.Id, heroClassId, gold: 9_000, xp: 3_000, durationSec: 3_600, completedAt: t0.AddHours(3));

        var repo = CreateRepo(Db);

        // Act: свежее окно = 2 (последние 2 забега: 7200 и 9000 gold/h)
        await repo.RecomputeForStageAsync(stage.Id, recentWindowSize: 2, CancellationToken.None);

        // Assert
        await using var ctx2 = CreateContext();
        var agg = await ctx2.StageAggregates.SingleAsync(a => a.StageId == stage.Id);

        // all-time: среднее = (3600+3600+7200+9000)/4 = 5850, best = 9000
        agg.RunCount.Should().Be(4);
        agg.AvgGoldPerHour.Should().BeApproximately(5_850.0, 1e-6);
        agg.BestGoldPerHour.Should().BeApproximately(9_000.0, 1e-6);

        // recent (2 свежих): среднее = (7200+9000)/2 = 8100, best = 9000
        agg.RecentRunCount.Should().Be(2);
        agg.RecentAvgGoldPerHour.Should().BeApproximately(8_100.0, 1e-6);
        agg.RecentBestGoldPerHour.Should().BeApproximately(9_000.0, 1e-6);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Тест 3: partial-забег исключён из RunCount
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecomputeForStageAsync_PartialRunExcluded_RunCountCorrect()
    {
        // Arrange
        var stage       = await Db.Stages.FirstAsync();
        int heroClassId = await SeedHeroClassAsync(Db, "rogue");

        var t0 = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);

        await AddRunAsync(Db, stage.Id, heroClassId, gold: 5_000, xp: 1_000, durationSec: 3_600,
            completedAt: t0, isPartial: false);
        await AddRunAsync(Db, stage.Id, heroClassId, gold: 1_000, xp: 200, durationSec: 1_000,
            completedAt: t0.AddMinutes(30), isPartial: true);

        var repo = CreateRepo(Db);

        // Act
        await repo.RecomputeForStageAsync(stage.Id, recentWindowSize: 10, CancellationToken.None);

        // Assert
        await using var ctx2 = CreateContext();
        var agg = await ctx2.StageAggregates.SingleAsync(a => a.StageId == stage.Id);

        agg.RunCount.Should().Be(1, "partial-забег не учитывается в агрегате");
        agg.AvgGoldPerHour.Should().BeApproximately(5_000.0, 1e-6);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Тест 4: ChestRates сохранены по активным типам сундуков
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecomputeForStageAsync_ChestRatesPersisted_ByActiveChestTypes()
    {
        // Arrange
        var stage       = await Db.Stages.FirstAsync();
        int heroClassId = await SeedHeroClassAsync(Db, "paladin");

        var chestTypes = await Db.ChestTypes.OrderBy(c => c.Id).ToListAsync();
        chestTypes.Should().HaveCount(3, "ожидаем 3 типа сундуков из сидинга");

        var t0 = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);

        // Один забег 3600 секунд: 3 brown + 1 blue + 0 red
        await AddRunAsync(Db, stage.Id, heroClassId, gold: 5_000, xp: 1_000, durationSec: 3_600,
            completedAt: t0,
            chests:
            [
                new StageRunChest { ChestTypeId = chestTypes[0].Id, Count = 3 },
                new StageRunChest { ChestTypeId = chestTypes[1].Id, Count = 1 },
                new StageRunChest { ChestTypeId = chestTypes[2].Id, Count = 0 },
            ]);

        var repo = CreateRepo(Db);

        // Act
        await repo.RecomputeForStageAsync(stage.Id, recentWindowSize: 10, CancellationToken.None);

        // Assert — новый контекст
        await using var ctx2 = CreateContext();
        var agg = await ctx2.StageAggregates
            .Include(a => a.ChestRates)
            .SingleAsync(a => a.StageId == stage.Id);

        agg.ChestRates.Should().HaveCount(3, "по одной записи на каждый активный тип сундука");

        // 3 brown / 3600 sec * 3600 = 3.0/h
        var brownRate = agg.ChestRates.Single(r => r.ChestTypeId == chestTypes[0].Id);
        brownRate.RatePerHour.Should().BeApproximately(3.0, 1e-6);
        brownRate.RecentRatePerHour.Should().BeApproximately(3.0, 1e-6);

        // 1 blue / 3600 sec * 3600 = 1.0/h
        var blueRate = agg.ChestRates.Single(r => r.ChestTypeId == chestTypes[1].Id);
        blueRate.RatePerHour.Should().BeApproximately(1.0, 1e-6);

        // 0 red / 3600 sec * 3600 = 0.0/h
        var redRate = agg.ChestRates.Single(r => r.ChestTypeId == chestTypes[2].Id);
        redRate.RatePerHour.Should().BeApproximately(0.0, 1e-6);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Тест 5: Power-context (RecentHeroLevelMin/Max, RecentHeroDamageMin/Max)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecomputeForStageAsync_PowerContextPersisted_MinMaxFromRecentWindow()
    {
        // Arrange
        var stage       = await Db.Stages.FirstAsync();
        int heroClassId = await SeedHeroClassAsync(Db, "ranger");

        var t0 = new DateTime(2026, 6, 1, 11, 0, 0, DateTimeKind.Utc);

        // 3 забега с разными level/damage; окно = 2 (2 свежих)
        // oldest: level=10, damage=5000
        // middle: level=20, damage=15000
        // newest: level=30, damage=25000
        async Task AddRunWithHeroAsync(int level, long damage, DateTime at)
        {
            var run = new StageRun
            {
                StageId         = stage.Id,
                DurationSeconds = 3_600,
                GoldGained      = 3_600,
                XpGained        = 1_000,
                Hero            = new HeroSnapshot(heroClassId, level, damage),
                CompletedAtUtc  = at,
                IsPartial       = false,
            };
            Db.StageRuns.Add(run);
            await Db.SaveChangesAsync();
        }

        await AddRunWithHeroAsync(10, 5_000, t0);
        await AddRunWithHeroAsync(20, 15_000, t0.AddHours(1));
        await AddRunWithHeroAsync(30, 25_000, t0.AddHours(2));

        var repo = CreateRepo(Db);

        // Act: recent window = 2 → берём записи level=30 и level=20
        await repo.RecomputeForStageAsync(stage.Id, recentWindowSize: 2, CancellationToken.None);

        // Assert
        await using var ctx2 = CreateContext();
        var agg = await ctx2.StageAggregates.SingleAsync(a => a.StageId == stage.Id);

        agg.RecentRunCount.Should().Be(2);
        agg.RecentHeroLevelMin.Should().Be(20);
        agg.RecentHeroLevelMax.Should().Be(30);
        agg.RecentHeroDamageMin.Should().Be(15_000);
        agg.RecentHeroDamageMax.Should().Be(25_000);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Тест 6: Идемпотентность — повторный Recompute не создаёт дублей
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecomputeForStageAsync_CalledTwice_NoChestRateDuplicates()
    {
        // Arrange
        var stage       = await Db.Stages.FirstAsync();
        int heroClassId = await SeedHeroClassAsync(Db, "sorcerer");

        var chestTypes = await Db.ChestTypes.OrderBy(c => c.Id).ToListAsync();

        var t0 = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        await AddRunAsync(Db, stage.Id, heroClassId, gold: 3_600, xp: 1_000, durationSec: 3_600,
            completedAt: t0,
            chests:
            [
                new StageRunChest { ChestTypeId = chestTypes[0].Id, Count = 2 },
            ]);

        var repo = CreateRepo(Db);

        // Act — первый пересчёт
        await repo.RecomputeForStageAsync(stage.Id, recentWindowSize: 10, CancellationToken.None);

        // Добавляем второй забег и пересчитываем повторно (тот же контекст — продолжаем работу)
        await AddRunAsync(Db, stage.Id, heroClassId, gold: 7_200, xp: 2_000, durationSec: 3_600,
            completedAt: t0.AddHours(1),
            chests:
            [
                new StageRunChest { ChestTypeId = chestTypes[0].Id, Count = 4 },
            ]);

        // Второй пересчёт
        await repo.RecomputeForStageAsync(stage.Id, recentWindowSize: 10, CancellationToken.None);

        // Assert — новый контекст
        await using var ctx2 = CreateContext();
        var agg = await ctx2.StageAggregates
            .Include(a => a.ChestRates)
            .SingleAsync(a => a.StageId == stage.Id);

        // Один агрегат (no duplicate StageAggregate rows)
        int aggCount = await ctx2.StageAggregates.CountAsync(a => a.StageId == stage.Id);
        aggCount.Should().Be(1, "повторный Recompute не должен создавать дублей агрегата");

        // ChestRates: по одной записи на тип (3 активных типа сундука)
        agg.ChestRates.Should().HaveCount(3, "повторный Recompute не создаёт дублей ChestRates");

        // RunCount должен отражать оба забега
        agg.RunCount.Should().Be(2);

        // brown rate: (2+4)=6 chestsTotal / (3600+3600)=7200 sec * 3600 = 3.0/h
        var brownRate = agg.ChestRates.Single(r => r.ChestTypeId == chestTypes[0].Id);
        brownRate.RatePerHour.Should().BeApproximately(3.0, 1e-6);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Тест 7: GetAllAsync возвращает агрегаты с ChestRates
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_AfterRecompute_ReturnsAggregatesWithChestRates()
    {
        // Arrange
        var stages      = await Db.Stages.Take(2).ToListAsync();
        int heroClassId = await SeedHeroClassAsync(Db, "berserker");

        var t0 = new DateTime(2026, 6, 1, 14, 0, 0, DateTimeKind.Utc);
        var chestTypes = await Db.ChestTypes.OrderBy(c => c.Id).ToListAsync();

        // Добавляем по одному забегу на два разных этапа
        foreach (var stage in stages)
        {
            await AddRunAsync(Db, stage.Id, heroClassId, gold: 3_600, xp: 1_000, durationSec: 3_600,
                completedAt: t0,
                chests:
                [
                    new StageRunChest { ChestTypeId = chestTypes[0].Id, Count = 1 },
                ]);
            t0 = t0.AddHours(1);
        }

        var repo = CreateRepo(Db);

        // Пересчитываем агрегаты для обоих этапов
        foreach (var stage in stages)
        {
            await repo.RecomputeForStageAsync(stage.Id, recentWindowSize: 10, CancellationToken.None);
        }

        // Act — GetAllAsync через новый контекст
        await using var ctx2 = CreateContext();
        var repoCtx2 = new StageAggregateRepository(ctx2, new StageAggregateCalculator());
        var all = await repoCtx2.GetAllAsync(CancellationToken.None);

        // Assert
        all.Should().HaveCountGreaterOrEqualTo(2, "должны вернуться агрегаты для обоих этапов");

        var agg0 = all.Single(a => a.StageId == stages[0].Id);
        var agg1 = all.Single(a => a.StageId == stages[1].Id);

        agg0.ChestRates.Should().HaveCount(3, "ChestRates подгружаются вместе с агрегатом");
        agg1.ChestRates.Should().HaveCount(3);
        agg0.RunCount.Should().Be(1);
        agg1.RunCount.Should().Be(1);
    }
}
