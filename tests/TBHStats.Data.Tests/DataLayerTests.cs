namespace TBHStats.Data.Tests;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TBHStats.Core.Models;
using Xunit;

/// <summary>
/// Интеграционные тесты слоя данных на реальном временном SQLite-файле.
/// Каждый тест-класс получает уникальный временный файл БД, удаляемый после прогона.
/// Никаких in-memory провайдеров, никаких моков.
/// </summary>

// ─────────────────────────────────────────────────────────────────────────────
// Вспомогательная фикстура: временный файл БД + IAsyncLifetime
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Базовый класс для тест-классов, работающих с временным SQLite-файлом.
/// Каждый экземпляр получает свой уникальный путь к файлу БД.
/// InitializeAsync применяет реальные миграции + сидинг.
/// DisposeAsync освобождает контекст и удаляет файл (best-effort).
/// </summary>
public abstract class TempDbFixture : IAsyncLifetime
{
    protected string DbPath { get; }
    protected TbhStatsDbContext Db { get; private set; } = null!;

    protected TempDbFixture()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tbhstats-tests");
        Directory.CreateDirectory(dir);
        DbPath = Path.Combine(dir, $"{Guid.NewGuid()}.db");
    }

    protected TbhStatsDbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<TbhStatsDbContext>();
        DatabaseInitializer.ConfigureSqlite(builder, DbPath);
        return new TbhStatsDbContext(builder.Options);
    }

    public async Task InitializeAsync()
    {
        Db = CreateContext();
        await DatabaseInitializer.InitializeAsync(Db, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await Db.DisposeAsync();
        // Best-effort: убираем временный файл и WAL/SHM артефакты
        foreach (string suffix in new[] { "", "-wal", "-shm" })
        {
            string file = DbPath + suffix;
            try { if (File.Exists(file)) File.Delete(file); }
            catch { /* best-effort — SQLite может ещё держать файл */ }
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 1. Миграции и сидинг
// ─────────────────────────────────────────────────────────────────────────────

public sealed class MigrationAndSeedingTests : TempDbFixture
{
    [Fact]
    public async Task InitializeAsync_FreshDb_CreatesSchemaTables()
    {
        // БД уже инициализирована в InitializeAsync фикстуры.
        // Проверяем, что таблицы присутствуют через прямой запрос к каждому DbSet.
        int stageCount = await Db.Stages.CountAsync();
        int tabCount   = await Db.Tabs.CountAsync();
        int chestCount = await Db.ChestTypes.CountAsync();
        int actCount   = await Db.Acts.CountAsync();
        int diffCount  = await Db.Difficulties.CountAsync();

        stageCount.Should().Be(60);
        tabCount.Should().Be(9);
        chestCount.Should().Be(3);
        actCount.Should().Be(3);
        diffCount.Should().Be(2);
    }

    [Fact]
    public async Task InitializeAsync_FreshDb_SeedsWidgetSettingsSingleton()
    {
        int widgetCount = await Db.WidgetSettings.CountAsync();
        widgetCount.Should().Be(1);
    }

    [Fact]
    public async Task InitializeAsync_FreshDb_SeedsOptimizationProfileSingleton()
    {
        int profileCount = await Db.OptimizationProfiles.CountAsync();
        profileCount.Should().Be(1);
    }

    [Fact]
    public async Task InitializeAsync_FreshDb_StageUniquenessByActDifficultyNumber()
    {
        // Act1/normal/1 и Act1/nightmare/1 — разные этапы (ADR-008 edge case).
        // Убеждаемся, что оба существуют (разные DifficultyId).
        var act1 = await Db.Acts.SingleAsync(a => a.Number == 1);
        var normal = await Db.Difficulties.SingleAsync(d => d.Key == "normal");
        var nightmare = await Db.Difficulties.SingleAsync(d => d.Key == "nightmare");

        var act1Normal1 = await Db.Stages.SingleOrDefaultAsync(
            s => s.ActId == act1.Id && s.DifficultyId == normal.Id && s.Number == 1);
        var act1Nightmare1 = await Db.Stages.SingleOrDefaultAsync(
            s => s.ActId == act1.Id && s.DifficultyId == nightmare.Id && s.Number == 1);

        act1Normal1.Should().NotBeNull("этап Act1/normal/1 должен существовать после сидинга");
        act1Nightmare1.Should().NotBeNull("этап Act1/nightmare/1 должен существовать после сидинга");
        act1Normal1!.Id.Should().NotBe(act1Nightmare1!.Id, "одинаковый номер на разных сложностях — разные этапы");
    }

    [Fact]
    public async Task InitializeAsync_Repeated_SeedingIsIdempotent_NoStageDuplicates()
    {
        // Повторный вызов InitializeAsync НЕ должен создавать дубли.
        await DatabaseInitializer.InitializeAsync(Db, CancellationToken.None);

        int stageCount = await Db.Stages.CountAsync();
        stageCount.Should().Be(60, "повторный сидинг не должен добавлять дубли этапов");
    }

    [Fact]
    public async Task InitializeAsync_Repeated_SeedingIsIdempotent_NoTabDuplicates()
    {
        await DatabaseInitializer.InitializeAsync(Db, CancellationToken.None);

        int tabCount = await Db.Tabs.CountAsync();
        tabCount.Should().Be(9, "повторный сидинг не должен добавлять дубли вкладок");
    }

    [Fact]
    public async Task InitializeAsync_Repeated_SeedingIsIdempotent_NoWidgetSettingsDuplicates()
    {
        await DatabaseInitializer.InitializeAsync(Db, CancellationToken.None);

        int widgetCount = await Db.WidgetSettings.CountAsync();
        widgetCount.Should().Be(1, "повторный сидинг не должен добавлять второй синглтон WidgetSettings");
    }

    [Fact]
    public async Task InitializeAsync_FreshDb_StagesCountMatchesThreeActsTwoDifficultiesTenNumbers()
    {
        // 3 × 2 × 10 = 60; проверяем по каждому акту
        var acts = await Db.Acts.ToListAsync();
        acts.Should().HaveCount(3);

        foreach (var act in acts)
        {
            int countForAct = await Db.Stages.CountAsync(s => s.ActId == act.Id);
            countForAct.Should().Be(20,
                $"для акта {act.Number} ожидается 2 сложности × 10 этапов = 20");
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 2. CRUD StageRun + owned HeroSnapshot + StageRunChest
// ─────────────────────────────────────────────────────────────────────────────

public sealed class StageRunCrudTests : TempDbFixture
{
    /// <summary>
    /// Создаёт HeroClass в БД и возвращает его Id.
    /// Необходимо, т.к. StageRun.Hero.HeroClassId — FK на HeroClasses,
    /// а HeroClasses по умолчанию пуст (классы открываются динамически).
    /// </summary>
    private async Task<int> SeedHeroClassAsync(TbhStatsDbContext ctx, string key = "warrior")
    {
        var hc = new HeroClass { Key = key, DisplayName = "Warrior", IsActive = true };
        ctx.HeroClasses.Add(hc);
        await ctx.SaveChangesAsync();
        return hc.Id;
    }

    [Fact]
    public async Task SaveStageRun_AndReopen_PersistsAllFields()
    {
        // Arrange
        int heroClassId = await SeedHeroClassAsync(Db);
        var stage = await Db.Stages.FirstAsync();

        var run = new StageRun
        {
            StageId        = stage.Id,
            DurationSeconds = 300,
            GoldGained     = 12_500,
            XpGained       = 8_000,
            Hero           = new HeroSnapshot(heroClassId, 42, 99_000),
            CompletedAtUtc  = new DateTime(2026, 5, 31, 12, 0, 0, DateTimeKind.Utc),
            IsPartial      = false,
        };

        Db.StageRuns.Add(run);
        await Db.SaveChangesAsync();
        long runId = run.Id;

        // Act — открыть НОВЫЙ контекст (симуляция «перезапуска»)
        await using TbhStatsDbContext ctx2 = CreateContext();
        var loaded = await ctx2.StageRuns
            .Include(r => r.Chests)
            .SingleAsync(r => r.Id == runId);

        // Assert
        loaded.StageId.Should().Be(stage.Id);
        loaded.DurationSeconds.Should().Be(300);
        loaded.GoldGained.Should().Be(12_500);
        loaded.XpGained.Should().Be(8_000);
        loaded.CompletedAtUtc.Should().Be(new DateTime(2026, 5, 31, 12, 0, 0, DateTimeKind.Utc));
        loaded.IsPartial.Should().BeFalse();
        loaded.Hero.HeroClassId.Should().Be(heroClassId);
        loaded.Hero.Level.Should().Be(42);
        loaded.Hero.Damage.Should().Be(99_000);
    }

    [Fact]
    public async Task SaveStageRun_WithChests_PersistsChestCounts()
    {
        // Arrange
        int heroClassId = await SeedHeroClassAsync(Db, "mage");
        var stage = await Db.Stages.FirstAsync();
        var chestTypes = await Db.ChestTypes.OrderBy(c => c.SortOrder).ToListAsync();
        chestTypes.Should().HaveCount(3, "ожидаем 3 типа сундуков из дефолтного сидинга");

        var run = new StageRun
        {
            StageId         = stage.Id,
            DurationSeconds = 180,
            GoldGained      = 5_000,
            XpGained        = 3_000,
            Hero            = new HeroSnapshot(heroClassId, 10, 1_500),
            CompletedAtUtc  = new DateTime(2026, 5, 31, 14, 0, 0, DateTimeKind.Utc),
            IsPartial       = false,
            Chests =
            [
                new StageRunChest { ChestTypeId = chestTypes[0].Id, Count = 5 },  // brown
                new StageRunChest { ChestTypeId = chestTypes[1].Id, Count = 2 },  // blue
                new StageRunChest { ChestTypeId = chestTypes[2].Id, Count = 0 },  // red = 0
            ],
        };

        Db.StageRuns.Add(run);
        await Db.SaveChangesAsync();
        long runId = run.Id;

        // Act — новый контекст
        await using TbhStatsDbContext ctx2 = CreateContext();
        var loaded = await ctx2.StageRuns
            .Include(r => r.Chests)
            .SingleAsync(r => r.Id == runId);

        // Assert
        loaded.Chests.Should().HaveCount(3);
        loaded.Chests.Single(c => c.ChestTypeId == chestTypes[0].Id).Count.Should().Be(5);
        loaded.Chests.Single(c => c.ChestTypeId == chestTypes[1].Id).Count.Should().Be(2);
        loaded.Chests.Single(c => c.ChestTypeId == chestTypes[2].Id).Count.Should().Be(0);
    }

    [Fact]
    public async Task SaveStageRun_IsPartialTrue_PersistedCorrectly()
    {
        // Arrange
        int heroClassId = await SeedHeroClassAsync(Db, "rogue");
        var stage = await Db.Stages.FirstAsync();

        var run = new StageRun
        {
            StageId         = stage.Id,
            DurationSeconds = 90,
            GoldGained      = 1_000,
            XpGained        = 500,
            Hero            = new HeroSnapshot(heroClassId, 5, 200),
            CompletedAtUtc  = new DateTime(2026, 5, 31, 15, 0, 0, DateTimeKind.Utc),
            IsPartial       = true,
        };

        Db.StageRuns.Add(run);
        await Db.SaveChangesAsync();
        long runId = run.Id;

        // Act
        await using TbhStatsDbContext ctx2 = CreateContext();
        var loaded = await ctx2.StageRuns.SingleAsync(r => r.Id == runId);

        // Assert
        loaded.IsPartial.Should().BeTrue("partial-забег должен персистироваться как IsPartial=true");
    }

    [Fact]
    public async Task SaveMultipleStageRuns_QueryByStageId_ReturnsCorrectCount()
    {
        // Arrange
        int heroClassId = await SeedHeroClassAsync(Db, "paladin");
        var stages = await Db.Stages.Take(2).ToListAsync();

        // 3 забега на stage[0], 1 на stage[1]
        for (int i = 0; i < 3; i++)
        {
            Db.StageRuns.Add(new StageRun
            {
                StageId         = stages[0].Id,
                DurationSeconds = 120 + i * 10,
                GoldGained      = 2_000L + i * 100,
                XpGained        = 1_000,
                Hero            = new HeroSnapshot(heroClassId, 15, 3_000),
                CompletedAtUtc  = new DateTime(2026, 5, 31, 10, 0, i, DateTimeKind.Utc),
                IsPartial       = false,
            });
        }
        Db.StageRuns.Add(new StageRun
        {
            StageId         = stages[1].Id,
            DurationSeconds = 150,
            GoldGained      = 3_000,
            XpGained        = 2_000,
            Hero            = new HeroSnapshot(heroClassId, 15, 3_000),
            CompletedAtUtc  = new DateTime(2026, 5, 31, 11, 0, 0, DateTimeKind.Utc),
            IsPartial       = false,
        });
        await Db.SaveChangesAsync();

        // Act
        await using TbhStatsDbContext ctx2 = CreateContext();
        int countForStage0 = await ctx2.StageRuns.CountAsync(r => r.StageId == stages[0].Id);
        int countForStage1 = await ctx2.StageRuns.CountAsync(r => r.StageId == stages[1].Id);

        // Assert
        countForStage0.Should().Be(3);
        countForStage1.Should().Be(1);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 3. MetricSample + StageRef ValueConverter + MetricSampleChest
// ─────────────────────────────────────────────────────────────────────────────

public sealed class MetricSampleCrudTests : TempDbFixture
{
    [Fact]
    public async Task SaveMetricSample_WithNextLocation_RoundTripsStageRef()
    {
        // Arrange
        var sample = new MetricSample
        {
            TakenAtUtc   = new DateTime(2026, 5, 31, 8, 0, 0, DateTimeKind.Utc),
            Gold         = 100_000,
            Xp           = 500,
            XpToLevel    = 10_000,
            IsReliable   = true,
            HeroLevel    = 30,
            HeroDamage   = 50_000,
            NextLocation = new StageRef(1, "normal", 2),
        };

        Db.MetricSamples.Add(sample);
        await Db.SaveChangesAsync();
        long sampleId = sample.Id;

        // Act — новый контекст
        await using TbhStatsDbContext ctx2 = CreateContext();
        var loaded = await ctx2.MetricSamples.SingleAsync(s => s.Id == sampleId);

        // Assert
        loaded.NextLocation.Should().NotBeNull();
        loaded.NextLocation!.Value.ActNumber.Should().Be(1);
        loaded.NextLocation.Value.DifficultyKey.Should().Be("normal");
        loaded.NextLocation.Value.StageNumber.Should().Be(2);
    }

    [Fact]
    public async Task SaveMetricSample_WithNullNextLocation_RoundTripsAsNull()
    {
        // Arrange
        var sample = new MetricSample
        {
            TakenAtUtc   = new DateTime(2026, 5, 31, 9, 0, 0, DateTimeKind.Utc),
            IsReliable   = false,
            NextLocation = null,
        };

        Db.MetricSamples.Add(sample);
        await Db.SaveChangesAsync();
        long sampleId = sample.Id;

        // Act
        await using TbhStatsDbContext ctx2 = CreateContext();
        var loaded = await ctx2.MetricSamples.SingleAsync(s => s.Id == sampleId);

        // Assert
        loaded.NextLocation.Should().BeNull("null NextLocation должен сохраняться и читаться как null");
    }

    [Fact]
    public async Task SaveMetricSample_WithNightmareNextLocation_RoundTripsCorrectDifficultyKey()
    {
        // Arrange: nightmare-сложность в StageRef
        var sample = new MetricSample
        {
            TakenAtUtc   = new DateTime(2026, 5, 31, 10, 0, 0, DateTimeKind.Utc),
            IsReliable   = true,
            NextLocation = new StageRef(3, "nightmare", 10),
        };

        Db.MetricSamples.Add(sample);
        await Db.SaveChangesAsync();
        long sampleId = sample.Id;

        // Act
        await using TbhStatsDbContext ctx2 = CreateContext();
        var loaded = await ctx2.MetricSamples.SingleAsync(s => s.Id == sampleId);

        // Assert
        loaded.NextLocation!.Value.ActNumber.Should().Be(3);
        loaded.NextLocation.Value.DifficultyKey.Should().Be("nightmare");
        loaded.NextLocation.Value.StageNumber.Should().Be(10);
    }

    [Fact]
    public async Task SaveMetricSample_WithChests_RoundTripsChestCounts()
    {
        // Arrange
        var chestTypes = await Db.ChestTypes.OrderBy(c => c.SortOrder).ToListAsync();

        var sample = new MetricSample
        {
            TakenAtUtc = new DateTime(2026, 5, 31, 11, 0, 0, DateTimeKind.Utc),
            Gold       = 200_000,
            IsReliable = true,
            Chests =
            [
                new MetricSampleChest { ChestTypeId = chestTypes[0].Id, Count = 3 },
                new MetricSampleChest { ChestTypeId = chestTypes[1].Id, Count = 1 },
            ],
        };

        Db.MetricSamples.Add(sample);
        await Db.SaveChangesAsync();
        long sampleId = sample.Id;

        // Act
        await using TbhStatsDbContext ctx2 = CreateContext();
        var loaded = await ctx2.MetricSamples
            .Include(s => s.Chests)
            .SingleAsync(s => s.Id == sampleId);

        // Assert
        loaded.Chests.Should().HaveCount(2);
        loaded.Chests.Single(c => c.ChestTypeId == chestTypes[0].Id).Count.Should().Be(3);
        loaded.Chests.Single(c => c.ChestTypeId == chestTypes[1].Id).Count.Should().Be(1);
    }

    [Fact]
    public async Task SaveMetricSample_AllNullableFieldsNull_RoundTripsCorrectly()
    {
        // Arrange: все nullable-поля = null
        var sample = new MetricSample
        {
            TakenAtUtc = new DateTime(2026, 5, 31, 12, 0, 0, DateTimeKind.Utc),
            StageId    = null,
            Gold       = null,
            Xp         = null,
            XpToLevel  = null,
            HeroLevel  = null,
            HeroDamage = null,
            IsReliable = false,
            NextLocation = null,
        };

        Db.MetricSamples.Add(sample);
        await Db.SaveChangesAsync();
        long sampleId = sample.Id;

        // Act
        await using TbhStatsDbContext ctx2 = CreateContext();
        var loaded = await ctx2.MetricSamples.SingleAsync(s => s.Id == sampleId);

        // Assert
        loaded.Gold.Should().BeNull();
        loaded.Xp.Should().BeNull();
        loaded.XpToLevel.Should().BeNull();
        loaded.HeroLevel.Should().BeNull();
        loaded.HeroDamage.Should().BeNull();
        loaded.StageId.Should().BeNull();
        loaded.NextLocation.Should().BeNull();
    }

    [Fact]
    public async Task MetricSamples_FilterByStageIdAndDateRange_ReturnsCorrectSubset()
    {
        // Arrange: несколько сэмплов с разными StageId и датами
        var stage = await Db.Stages.FirstAsync();

        var t1 = new DateTime(2026, 5, 31, 8, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 5, 31, 9, 0, 0, DateTimeKind.Utc);
        var t3 = new DateTime(2026, 5, 31, 10, 0, 0, DateTimeKind.Utc);

        Db.MetricSamples.AddRange(
            new MetricSample { TakenAtUtc = t1, StageId = stage.Id, IsReliable = true },
            new MetricSample { TakenAtUtc = t2, StageId = stage.Id, IsReliable = true },
            new MetricSample { TakenAtUtc = t3, StageId = null,     IsReliable = false }
        );
        await Db.SaveChangesAsync();

        // Act
        await using TbhStatsDbContext ctx2 = CreateContext();
        var samples = await ctx2.MetricSamples
            .Where(s => s.StageId == stage.Id
                        && s.TakenAtUtc >= t1
                        && s.TakenAtUtc <= t2)
            .ToListAsync();

        // Assert
        samples.Should().HaveCount(2, "только сэмплы с нужным StageId и в диапазоне дат");
        samples.All(s => s.StageId == stage.Id).Should().BeTrue();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 4. Ограничения целостности: составные PK, FK, уникальные индексы
// ─────────────────────────────────────────────────────────────────────────────

public sealed class IntegrityConstraintTests : TempDbFixture
{
    private async Task<int> SeedHeroClassAsync(TbhStatsDbContext ctx, string key = "warrior")
    {
        var hc = new HeroClass { Key = key, DisplayName = "Test Hero", IsActive = true };
        ctx.HeroClasses.Add(hc);
        await ctx.SaveChangesAsync();
        return hc.Id;
    }

    [Fact]
    public async Task StageRunChest_DuplicateCompositeKey_ThrowsOnAddOrSave()
    {
        // Arrange
        int heroClassId = await SeedHeroClassAsync(Db);
        var stage = await Db.Stages.FirstAsync();
        var chestType = await Db.ChestTypes.FirstAsync();

        // EF Core обнаруживает дублирующий составной PK ещё в change tracker
        // (до SQL), поэтому исключение может быть брошено как при Add, так и при SaveChanges.
        // Оборачиваем весь блок записи в лямбду.
        Func<Task> act = async () =>
        {
            var run = new StageRun
            {
                StageId         = stage.Id,
                DurationSeconds = 120,
                GoldGained      = 1_000,
                XpGained        = 500,
                Hero            = new HeroSnapshot(heroClassId, 1, 100),
                CompletedAtUtc  = new DateTime(2026, 5, 31, 10, 0, 0, DateTimeKind.Utc),
                IsPartial       = false,
                Chests =
                [
                    new StageRunChest { ChestTypeId = chestType.Id, Count = 3 },
                    // дубль (StageRunId будет тот же, ChestTypeId тот же) — нарушение PK
                    new StageRunChest { ChestTypeId = chestType.Id, Count = 5 },
                ],
            };

            Db.StageRuns.Add(run);
            await Db.SaveChangesAsync();
        };

        // Act & Assert
        await act.Should().ThrowAsync<Exception>(
            "дублирующий составной PK (StageRunId, ChestTypeId) должен вызывать исключение");
    }

    [Fact]
    public async Task StageRun_FkToNonexistentStage_ThrowsOnSaveChanges()
    {
        // Arrange
        int heroClassId = await SeedHeroClassAsync(Db, "ranger");
        const int nonExistentStageId = 999_999;

        var run = new StageRun
        {
            StageId         = nonExistentStageId,
            DurationSeconds = 120,
            GoldGained      = 1_000,
            XpGained        = 500,
            Hero            = new HeroSnapshot(heroClassId, 1, 100),
            CompletedAtUtc  = new DateTime(2026, 5, 31, 10, 0, 0, DateTimeKind.Utc),
            IsPartial       = false,
        };

        Db.StageRuns.Add(run);

        // Act & Assert
        Func<Task> act = () => Db.SaveChangesAsync();
        await act.Should().ThrowAsync<Exception>(
            "FK на несуществующий Stage должен вызывать исключение при SaveChanges");
    }

    [Fact]
    public async Task Stage_DuplicateActDifficultyNumber_ThrowsOnSaveChanges()
    {
        // Arrange: пытаемся вставить Stage с той же тройкой (ActId, DifficultyId, Number),
        // которая уже есть после сидинга (уникальный индекс IX_Stages_ActId_DifficultyId_Number).
        var existingStage = await Db.Stages.FirstAsync();

        // Используем тот же (ActId, DifficultyId, Number) — нарушение уникального индекса
        var duplicate = new Stage
        {
            ActId        = existingStage.ActId,
            DifficultyId = existingStage.DifficultyId,
            Number       = existingStage.Number,
        };

        Db.Stages.Add(duplicate);

        // Act & Assert
        Func<Task> act = () => Db.SaveChangesAsync();
        await act.Should().ThrowAsync<Exception>(
            "уникальный индекс (ActId, DifficultyId, Number) должен отклонить дубль этапа");
    }

    [Fact]
    public async Task MetricSampleChest_DuplicateCompositeKey_ThrowsOnAddOrSave()
    {
        // Arrange
        var chestType = await Db.ChestTypes.FirstAsync();

        // EF Core обнаруживает дублирующий составной PK ещё в change tracker.
        Func<Task> act = async () =>
        {
            var sample = new MetricSample
            {
                TakenAtUtc = new DateTime(2026, 5, 31, 8, 0, 0, DateTimeKind.Utc),
                IsReliable = true,
                Chests =
                [
                    new MetricSampleChest { ChestTypeId = chestType.Id, Count = 2 },
                    // дубль (MetricSampleId, ChestTypeId) — нарушение PK
                    new MetricSampleChest { ChestTypeId = chestType.Id, Count = 4 },
                ],
            };

            Db.MetricSamples.Add(sample);
            await Db.SaveChangesAsync();
        };

        // Act & Assert
        await act.Should().ThrowAsync<Exception>(
            "дублирующий составной PK (MetricSampleId, ChestTypeId) должен вызывать исключение");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 5. StageAggregate: CRUD и проверка структуры
// ─────────────────────────────────────────────────────────────────────────────

public sealed class StageAggregateCrudTests : TempDbFixture
{
    [Fact]
    public async Task SaveStageAggregate_AndReopen_PersistsAllMetrics()
    {
        // Arrange
        var stage = await Db.Stages.FirstAsync();

        var aggregate = new StageAggregate
        {
            StageId             = stage.Id,
            RunCount            = 5,
            AvgGoldPerHour      = 72_000.0,
            BestGoldPerHour     = 90_000.0,
            AvgXpPerHour        = 15_000.0,
            BestXpPerHour       = 20_000.0,
            AvgDurationSeconds  = 250.4,
            BestDurationSeconds = 210,
            UpdatedAtUtc        = new DateTime(2026, 5, 31, 13, 0, 0, DateTimeKind.Utc),
        };

        Db.StageAggregates.Add(aggregate);
        await Db.SaveChangesAsync();

        // Act — новый контекст
        await using TbhStatsDbContext ctx2 = CreateContext();
        var loaded = await ctx2.StageAggregates.SingleAsync(a => a.StageId == stage.Id);

        // Assert
        loaded.RunCount.Should().Be(5);
        loaded.AvgGoldPerHour.Should().BeApproximately(72_000.0, precision: 0.001);
        loaded.BestGoldPerHour.Should().BeApproximately(90_000.0, precision: 0.001);
        loaded.AvgXpPerHour.Should().BeApproximately(15_000.0, precision: 0.001);
        loaded.BestXpPerHour.Should().BeApproximately(20_000.0, precision: 0.001);
        loaded.AvgDurationSeconds.Should().BeApproximately(250.4, precision: 0.001);
        loaded.BestDurationSeconds.Should().Be(210);
        loaded.UpdatedAtUtc.Should().Be(new DateTime(2026, 5, 31, 13, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task UpdateStageAggregate_ChangesPersistedInNewContext()
    {
        // Arrange
        var stage = await Db.Stages.FirstAsync();

        var aggregate = new StageAggregate
        {
            StageId             = stage.Id,
            RunCount            = 1,
            AvgGoldPerHour      = 50_000.0,
            BestGoldPerHour     = 50_000.0,
            AvgXpPerHour        = 10_000.0,
            BestXpPerHour       = 10_000.0,
            AvgDurationSeconds  = 300.0,
            BestDurationSeconds = 300,
            UpdatedAtUtc        = new DateTime(2026, 5, 31, 10, 0, 0, DateTimeKind.Utc),
        };

        Db.StageAggregates.Add(aggregate);
        await Db.SaveChangesAsync();

        // Act — обновление в новом контексте
        await using TbhStatsDbContext ctx2 = CreateContext();
        var toUpdate = await ctx2.StageAggregates.SingleAsync(a => a.StageId == stage.Id);
        toUpdate.RunCount        = 3;
        toUpdate.AvgGoldPerHour  = 65_000.0;
        toUpdate.UpdatedAtUtc    = new DateTime(2026, 5, 31, 11, 0, 0, DateTimeKind.Utc);
        await ctx2.SaveChangesAsync();

        // Assert — проверить в третьем контексте
        await using TbhStatsDbContext ctx3 = CreateContext();
        var final = await ctx3.StageAggregates.SingleAsync(a => a.StageId == stage.Id);
        final.RunCount.Should().Be(3);
        final.AvgGoldPerHour.Should().BeApproximately(65_000.0, precision: 0.001);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 6. DatabaseInitializer — утилиты пути
// ─────────────────────────────────────────────────────────────────────────────

public sealed class DatabaseInitializerPathTests
{
    [Fact]
    public void GetDbPath_ReturnsAbsolutePathEndingInTbhstatsDb()
    {
        string path = DatabaseInitializer.GetDbPath();

        path.Should().EndWith("tbhstats.db");
        Path.IsPathRooted(path).Should().BeTrue("путь должен быть абсолютным");
    }

    [Fact]
    public void GetConnectionString_ReturnsDataSourceFormat()
    {
        const string dbPath = @"C:\Temp\test.db";
        string connStr = DatabaseInitializer.GetConnectionString(dbPath);

        connStr.Should().Be($"Data Source={dbPath}");
    }

    [Fact]
    public void GetDbPath_DirectoryExists_AfterCall()
    {
        string path = DatabaseInitializer.GetDbPath();
        string dir  = Path.GetDirectoryName(path)!;

        Directory.Exists(dir).Should().BeTrue("GetDbPath должен создавать директорию");
    }
}
