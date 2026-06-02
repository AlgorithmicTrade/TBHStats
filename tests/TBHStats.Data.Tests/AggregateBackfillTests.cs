namespace TBHStats.Data.Tests;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TBHStats.Core.Models;
using TBHStats.Core.Optimization;
using TBHStats.Data.Repositories;
using Xunit;

/// <summary>
/// Интеграционные тесты идемпотентного бэкфилла устаревших агрегатов (задача T007-backfill).
/// Симулирует пост-миграционное состояние: агрегат имеет AvgGoldGained=0/AvgXpGained=0,
/// но AvgGoldPerHour>0 (заполнен до миграции AddAvgGainedToStageAggregate).
/// Проверяет, что BackfillStaleAggregatesAsync корректно пересчитывает Gained-поля.
/// Все тесты работают на реальном временном файловом SQLite (не in-memory, research R5).
/// </summary>
public sealed class AggregateBackfillTests : TempDbFixture
{
    // ─────────────────────────────────────────────────────────────────────────
    // Вспомогательные методы
    // ─────────────────────────────────────────────────────────────────────────

    private StageAggregateRepository CreateRepo(TbhStatsDbContext ctx)
        => new StageAggregateRepository(ctx, new StageAggregateCalculator());

    private async Task<int> SeedHeroClassAsync(TbhStatsDbContext ctx, string key = "warrior")
    {
        var hc = new HeroClass { Key = key, DisplayName = "Test Hero", IsActive = true };
        ctx.HeroClasses.Add(hc);
        await ctx.SaveChangesAsync();
        return hc.Id;
    }

    /// <summary>
    /// Добавляет non-partial StageRun с заданными gold/xp.
    /// </summary>
    private async Task AddRunAsync(
        TbhStatsDbContext ctx,
        int stageId,
        int heroClassId,
        long gold,
        long xp,
        int durationSec,
        DateTime completedAt)
    {
        ctx.StageRuns.Add(new StageRun
        {
            StageId         = stageId,
            DurationSeconds = durationSec,
            GoldGained      = gold,
            XpGained        = xp,
            Hero            = new HeroSnapshot(heroClassId, Level: 20, Damage: 10_000),
            CompletedAtUtc  = completedAt,
            IsPartial       = false,
        });
        await ctx.SaveChangesAsync();
    }

    /// <summary>
    /// Записывает StageAggregate с нулевыми Gained-полями но ненулевым GoldPerHour
    /// — имитирует пост-миграционное состояние.
    /// </summary>
    private async Task InsertStaleAggregateAsync(TbhStatsDbContext ctx, int stageId)
    {
        ctx.StageAggregates.Add(new StageAggregate
        {
            StageId            = stageId,
            RunCount           = 3,
            AvgGoldPerHour     = 260_552.0,   // заполнен до миграции
            BestGoldPerHour    = 400_000.0,
            AvgXpPerHour       = 120_000.0,
            BestXpPerHour      = 180_000.0,
            AvgDurationSeconds = 300.0,
            BestDurationSeconds = 240,
            // Gained-поля = 0 (дефолт колонки после миграции)
            AvgGoldGained      = 0.0,
            AvgXpGained        = 0.0,
            RecentAvgGoldGained = 0.0,
            RecentAvgXpGained  = 0.0,
            UpdatedAtUtc       = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        await ctx.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Тест 1: BackfillStaleAggregatesAsync пересчитывает устаревший агрегат
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BackfillStaleAggregatesAsync_StaleAggregate_RecalculatesGainedFields()
    {
        // Arrange: 3 забега с gold>0 и xp>0
        var stage       = await Db.Stages.FirstAsync();
        int heroClassId = await SeedHeroClassAsync(Db);

        var t0 = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);
        await AddRunAsync(Db, stage.Id, heroClassId, gold: 100_000, xp: 50_000, durationSec: 300, completedAt: t0);
        await AddRunAsync(Db, stage.Id, heroClassId, gold: 200_000, xp: 80_000, durationSec: 300, completedAt: t0.AddMinutes(10));
        await AddRunAsync(Db, stage.Id, heroClassId, gold: 150_000, xp: 60_000, durationSec: 300, completedAt: t0.AddMinutes(20));

        // Имитируем пост-миграционный агрегат с Gained=0
        await InsertStaleAggregateAsync(Db, stage.Id);

        // Убеждаемся в «устаревшем» состоянии
        var before = await Db.StageAggregates.AsNoTracking().SingleAsync(a => a.StageId == stage.Id);
        before.AvgGoldGained.Should().Be(0.0, "исходный агрегат — пост-миграционное состояние");
        before.AvgXpGained.Should().Be(0.0);

        var repo = CreateRepo(Db);

        // Act
        await DatabaseInitializer.BackfillStaleAggregatesAsync(
            Db,
            repo,
            recentWindowSize: 10,
            logger: null,
            CancellationToken.None);

        // Assert: в новом контексте Gained-поля должны быть пересчитаны
        await using var ctx2 = CreateContext();
        var after = await ctx2.StageAggregates.SingleAsync(a => a.StageId == stage.Id);

        // AvgGoldGained = avg(100000, 200000, 150000) = 150000
        after.AvgGoldGained.Should().BeApproximately(150_000.0, 1e-6,
            "AvgGoldGained должен быть пересчитан из реальных забегов");

        // AvgXpGained = avg(50000, 80000, 60000) = 63333.33...
        after.AvgXpGained.Should().BeGreaterThan(0.0,
            "AvgXpGained должен быть пересчитан из реальных забегов");

        after.RecentAvgGoldGained.Should().BeGreaterThan(0.0,
            "RecentAvgGoldGained должен быть пересчитан");
        after.RecentAvgXpGained.Should().BeGreaterThan(0.0,
            "RecentAvgXpGained должен быть пересчитан");

        // PerHour-поля не должны были обнулиться
        after.AvgGoldPerHour.Should().BeGreaterThan(0.0,
            "AvgGoldPerHour не должен обнуляться при бэкфилле");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Тест 2: BackfillStaleAggregatesAsync — идемпотентность
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BackfillStaleAggregatesAsync_CalledTwice_DoesNotCorruptValues()
    {
        // Arrange
        var stage       = await Db.Stages.FirstAsync();
        int heroClassId = await SeedHeroClassAsync(Db, "mage");

        var t0 = new DateTime(2026, 6, 2, 8, 0, 0, DateTimeKind.Utc);
        await AddRunAsync(Db, stage.Id, heroClassId, gold: 120_000, xp: 40_000, durationSec: 360, completedAt: t0);
        await AddRunAsync(Db, stage.Id, heroClassId, gold: 180_000, xp: 60_000, durationSec: 360, completedAt: t0.AddMinutes(15));

        await InsertStaleAggregateAsync(Db, stage.Id);

        var repo = CreateRepo(Db);

        // Act: первый бэкфилл
        await DatabaseInitializer.BackfillStaleAggregatesAsync(
            Db, repo, recentWindowSize: 10, logger: null, CancellationToken.None);

        // Запоминаем значения после первого бэкфилла
        await using var ctx2 = CreateContext();
        var afterFirst = await ctx2.StageAggregates.SingleAsync(a => a.StageId == stage.Id);
        double goldGainedFirst = afterFirst.AvgGoldGained;
        double xpGainedFirst   = afterFirst.AvgXpGained;
        goldGainedFirst.Should().BeGreaterThan(0.0, "после первого бэкфилла gained > 0");

        // Act: второй бэкфилл (должен быть no-op — условие self-off)
        await using var ctx3 = CreateContext();
        var repo3 = CreateRepo(ctx3);
        await DatabaseInitializer.BackfillStaleAggregatesAsync(
            ctx3, repo3, recentWindowSize: 10, logger: null, CancellationToken.None);

        // Assert: значения не изменились
        await using var ctx4 = CreateContext();
        var afterSecond = await ctx4.StageAggregates.SingleAsync(a => a.StageId == stage.Id);

        afterSecond.AvgGoldGained.Should().BeApproximately(goldGainedFirst, 1e-6,
            "повторный бэкфилл не должен менять уже пересчитанные значения");
        afterSecond.AvgXpGained.Should().BeApproximately(xpGainedFirst, 1e-6,
            "повторный бэкфилл не должен менять уже пересчитанные значения");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Тест 3: Этап без устаревшего агрегата не затрагивается
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BackfillStaleAggregatesAsync_FreshAggregate_NotRecomputed()
    {
        // Arrange: агрегат уже содержит Gained > 0 (пересчитан ранее)
        var stage       = await Db.Stages.FirstAsync();
        int heroClassId = await SeedHeroClassAsync(Db, "rogue");

        var t0 = new DateTime(2026, 6, 2, 9, 0, 0, DateTimeKind.Utc);
        await AddRunAsync(Db, stage.Id, heroClassId, gold: 90_000, xp: 30_000, durationSec: 300, completedAt: t0);

        // Агрегат с Gained > 0 — не устаревший
        Db.StageAggregates.Add(new StageAggregate
        {
            StageId             = stage.Id,
            RunCount            = 1,
            AvgGoldPerHour      = 1_080_000.0,
            BestGoldPerHour     = 1_080_000.0,
            AvgXpPerHour        = 360_000.0,
            BestXpPerHour       = 360_000.0,
            AvgDurationSeconds  = 300.0,
            BestDurationSeconds = 300,
            AvgGoldGained       = 90_000.0,   // уже пересчитан
            AvgXpGained         = 30_000.0,
            RecentAvgGoldGained = 90_000.0,
            RecentAvgXpGained   = 30_000.0,
            UpdatedAtUtc        = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc),
        });
        await Db.SaveChangesAsync();

        var repo = CreateRepo(Db);

        // Act
        await DatabaseInitializer.BackfillStaleAggregatesAsync(
            Db, repo, recentWindowSize: 10, logger: null, CancellationToken.None);

        // Assert: UpdatedAtUtc не изменился (бэкфилл не вызывался)
        await using var ctx2 = CreateContext();
        var agg = await ctx2.StageAggregates.SingleAsync(a => a.StageId == stage.Id);

        agg.UpdatedAtUtc.Should().Be(
            new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc),
            "свежий агрегат не должен быть затронут бэкфиллом");
        agg.AvgGoldGained.Should().BeApproximately(90_000.0, 1e-6);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Тест 4: Этап без забегов не вызывает бэкфилл
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BackfillStaleAggregatesAsync_NoRuns_NoBackfillAttempted()
    {
        // Arrange: агрегат есть, но забегов нет — не должно быть вызовов RecomputeForStageAsync
        var stage = await Db.Stages.FirstAsync();
        await InsertStaleAggregateAsync(Db, stage.Id);

        var repo = CreateRepo(Db);

        // Act — не должно бросать исключений и не должно менять агрегат (нет runs с gained>0)
        Func<Task> act = () => DatabaseInitializer.BackfillStaleAggregatesAsync(
            Db, repo, recentWindowSize: 10, logger: null, CancellationToken.None);

        await act.Should().NotThrowAsync();

        // Assert: агрегат не изменился (нет eligible runs → бэкфилл не запускался)
        await using var ctx2 = CreateContext();
        var agg = await ctx2.StageAggregates.SingleAsync(a => a.StageId == stage.Id);

        agg.AvgGoldGained.Should().Be(0.0,
            "при отсутствии runs с gained>0 бэкфилл не должен запускаться");
    }
}
