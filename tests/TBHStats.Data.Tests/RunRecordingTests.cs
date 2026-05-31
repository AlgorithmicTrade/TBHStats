namespace TBHStats.Data.Tests;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TBHStats.Core.Models;
using TBHStats.Data.Repositories;
using Xunit;

/// <summary>
/// TDD RED: интеграционные тесты <see cref="IRunRepository"/> на реальном временном файловом SQLite.
/// Тесты компилируются, но падают с <see cref="NotImplementedException"/> «Реализуется в T037.»,
/// пока RunRepository является скелетом. Станут зелёными после реализации в T037.
///
/// Никаких in-memory-провайдеров, никаких моков. Каждый тест-класс получает уникальный
/// временный файл БД с реальными миграциями и сидингом механик.
/// </summary>

// ─────────────────────────────────────────────────────────────────────────────
// Фикстура реального temp-SQLite для RunRecordingTests
// (аналог TempDbFixture из DataLayerTests.cs, не ломает тот файл)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Базовый класс с временным файловым SQLite + реальными миграциями.
/// Предоставляет фабричный метод <see cref="CreateRunRepository"/> для получения
/// экземпляра <see cref="IRunRepository"/> с новым контекстом (симуляция «перезапуска»).
/// </summary>
public abstract class RunRecordingFixture : IAsyncLifetime
{
    protected string DbPath { get; }
    protected TbhStatsDbContext Db { get; private set; } = null!;

    protected RunRecordingFixture()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tbhstats-run-tests");
        Directory.CreateDirectory(dir);
        DbPath = Path.Combine(dir, $"{Guid.NewGuid()}.db");
    }

    protected TbhStatsDbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<TbhStatsDbContext>();
        DatabaseInitializer.ConfigureSqlite(builder, DbPath);
        return new TbhStatsDbContext(builder.Options);
    }

    /// <summary>
    /// Создаёт новый независимый контекст и репозиторий (симуляция «перезапуска приложения»).
    /// Вызывающий обязан вызвать DisposeAsync на возвращённом контексте после использования.
    /// </summary>
    protected (TbhStatsDbContext ctx, IRunRepository repo) CreateRunRepository()
    {
        var ctx = CreateContext();
        return (ctx, new RunRepository(ctx));
    }

    /// <summary>
    /// Добавляет HeroClass в БД и возвращает его Id.
    /// HeroClasses пуст по умолчанию (классы открываются динамически).
    /// </summary>
    protected async Task<int> SeedHeroClassAsync(TbhStatsDbContext ctx, string key = "warrior")
    {
        var hc = new HeroClass { Key = key, DisplayName = key, IsActive = true };
        ctx.HeroClasses.Add(hc);
        await ctx.SaveChangesAsync();
        return hc.Id;
    }

    public async Task InitializeAsync()
    {
        Db = CreateContext();
        await DatabaseInitializer.InitializeAsync(Db, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await Db.DisposeAsync();
        foreach (string suffix in new[] { "", "-wal", "-shm" })
        {
            string file = DbPath + suffix;
            try { if (File.Exists(file)) File.Delete(file); }
            catch { /* best-effort */ }
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// RunRecordingTests: тесты IRunRepository.AddRunAsync / GetRunsAsync
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Тесты записи/чтения забегов через <see cref="IRunRepository"/>.
/// TDD RED: все падают с NotImplementedException до реализации T037.
/// </summary>
public sealed class RunRecordingTests : RunRecordingFixture
{
    // ── 1. AddRunAsync сохраняет StageRun, GetRunsAsync возвращает его ────────

    [Fact]
    public async Task AddRunAsync_ThenGetRunsAsync_ReturnsRunWithMatchingFields()
    {
        // Arrange
        int heroClassId = await SeedHeroClassAsync(Db);
        var stage = await Db.Stages.FirstAsync();

        var (ctx, repo) = CreateRunRepository();
        await using (ctx)
        {
            // Репо работает со своим контекстом — через AddRunAsync
            var heroClassId2 = await SeedHeroClassAsync(ctx, "archer");
            var run = new StageRun
            {
                StageId         = stage.Id,
                DurationSeconds = 300,
                GoldGained      = 12_500,
                XpGained        = 8_000,
                Hero            = new HeroSnapshot(heroClassId2, 42, 99_000),
                CompletedAtUtc  = new DateTime(2026, 5, 31, 12, 0, 0, DateTimeKind.Utc),
                IsPartial       = false,
            };

            // Act
            await repo.AddRunAsync(run, CancellationToken.None);

            var runs = await repo.GetRunsAsync(stage.Id, CancellationToken.None);

            // Assert
            runs.Should().HaveCount(1);
            var loaded = runs[0];
            loaded.StageId.Should().Be(stage.Id);
            loaded.DurationSeconds.Should().Be(300);
            loaded.GoldGained.Should().Be(12_500);
            loaded.XpGained.Should().Be(8_000);
            loaded.CompletedAtUtc.Should().Be(new DateTime(2026, 5, 31, 12, 0, 0, DateTimeKind.Utc));
            loaded.IsPartial.Should().BeFalse();
        }
    }

    // ── 2. HeroSnapshot (owned entity) персистится и читается в новом контексте ──

    [Fact]
    public async Task AddRunAsync_HeroSnapshot_PersistedAndReadableInNewContext()
    {
        // Arrange: использовать фикстурный Db для сидинга этапа, затем новый репо
        var stage = await Db.Stages.FirstAsync();

        var (ctx1, repo1) = CreateRunRepository();
        int heroClassId;
        long runId;
        await using (ctx1)
        {
            heroClassId = await SeedHeroClassAsync(ctx1, "paladin");
            var run = new StageRun
            {
                StageId         = stage.Id,
                DurationSeconds = 240,
                GoldGained      = 5_000,
                XpGained        = 2_000,
                Hero            = new HeroSnapshot(heroClassId, 55, 150_000),
                CompletedAtUtc  = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc),
                IsPartial       = false,
            };
            await repo1.AddRunAsync(run, CancellationToken.None);
            runId = run.Id;
        }

        // Act — открыть НОВЫЙ контекст (перезапуск)
        var (ctx2, repo2) = CreateRunRepository();
        await using (ctx2)
        {
            var runs = await repo2.GetRunsAsync(stage.Id, CancellationToken.None);

            // Assert: HeroSnapshot сохранился полностью
            runs.Should().ContainSingle(r => r.Id == runId);
            var loaded = runs.Single(r => r.Id == runId);
            loaded.Hero.HeroClassId.Should().Be(heroClassId, "HeroClassId должен персистироваться");
            loaded.Hero.Level.Should().Be(55,              "Level героя должен персистироваться");
            loaded.Hero.Damage.Should().Be(150_000,        "Damage героя должен персистироваться");
        }
    }

    // ── 3. StageRunChest персистируются (brown/blue/red с разными Count) ────────

    [Fact]
    public async Task AddRunAsync_WithChests_ChestsPersistedWithCorrectCounts()
    {
        // Arrange
        var stage = await Db.Stages.FirstAsync();
        var chestTypes = await Db.ChestTypes.OrderBy(c => c.SortOrder).ToListAsync();
        chestTypes.Should().HaveCount(3, "сидинг создаёт 3 типа сундуков");

        var (ctx1, repo1) = CreateRunRepository();
        long runId;
        int hcId;
        await using (ctx1)
        {
            hcId = await SeedHeroClassAsync(ctx1, "mage");
            var run = new StageRun
            {
                StageId         = stage.Id,
                DurationSeconds = 180,
                GoldGained      = 4_000,
                XpGained        = 1_500,
                Hero            = new HeroSnapshot(hcId, 20, 8_000),
                CompletedAtUtc  = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc),
                IsPartial       = false,
                Chests =
                [
                    new StageRunChest { ChestTypeId = chestTypes[0].Id, Count = 7 },  // brown
                    new StageRunChest { ChestTypeId = chestTypes[1].Id, Count = 3 },  // blue
                    new StageRunChest { ChestTypeId = chestTypes[2].Id, Count = 0 },  // red = 0
                ],
            };
            await repo1.AddRunAsync(run, CancellationToken.None);
            runId = run.Id;
        }

        // Act — новый контекст
        var (ctx2, repo2) = CreateRunRepository();
        await using (ctx2)
        {
            var runs = await repo2.GetRunsAsync(stage.Id, CancellationToken.None);

            // Assert
            var loaded = runs.Should().ContainSingle(r => r.Id == runId).Subject;
            loaded.Chests.Should().HaveCount(3, "все три типа сундуков должны быть восстановлены");
            loaded.Chests.Single(c => c.ChestTypeId == chestTypes[0].Id).Count.Should().Be(7);
            loaded.Chests.Single(c => c.ChestTypeId == chestTypes[1].Id).Count.Should().Be(3);
            loaded.Chests.Single(c => c.ChestTypeId == chestTypes[2].Id).Count.Should().Be(0);
        }
    }

    // ── 4. Переживание перезапуска: данные на диске после закрытия контекста ───

    [Fact]
    public async Task AddRunAsync_AfterContextRestart_RunAndChestsSurvive()
    {
        // Arrange
        var stage = await Db.Stages.FirstAsync();
        var chestType = await Db.ChestTypes.FirstAsync();
        long runId;

        var (ctx1, repo1) = CreateRunRepository();
        await using (ctx1)
        {
            int hcId = await SeedHeroClassAsync(ctx1, "druid");
            var run = new StageRun
            {
                StageId         = stage.Id,
                DurationSeconds = 420,
                GoldGained      = 20_000,
                XpGained        = 10_000,
                Hero            = new HeroSnapshot(hcId, 77, 300_000),
                CompletedAtUtc  = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc),
                IsPartial       = false,
                Chests          = [new StageRunChest { ChestTypeId = chestType.Id, Count = 4 }],
            };
            await repo1.AddRunAsync(run, CancellationToken.None);
            runId = run.Id;
        }
        // ctx1 закрыт — файл должен содержать данные

        // Act — открыть НОВЫЙ контекст (симуляция перезапуска приложения)
        var (ctx2, repo2) = CreateRunRepository();
        await using (ctx2)
        {
            var runs = await repo2.GetRunsAsync(stage.Id, CancellationToken.None);

            // Assert
            runs.Should().ContainSingle(r => r.Id == runId,
                "забег должен пережить закрытие контекста и остаться на диске (FR-011, SC-004)");
            var loaded = runs.Single(r => r.Id == runId);
            loaded.Chests.Should().ContainSingle(c => c.ChestTypeId == chestType.Id && c.Count == 4);
        }
    }

    // ── 5. GetRunsAsync фильтрует по stageId — забеги разных этапов не смешиваются ──

    [Fact]
    public async Task GetRunsAsync_FiltersByStageId_DoesNotMixDifferentStages()
    {
        // Arrange: два разных этапа
        var stages = await Db.Stages.Take(2).ToListAsync();
        stages.Should().HaveCount(2);

        var (ctx1, repo1) = CreateRunRepository();
        await using (ctx1)
        {
            int hcId = await SeedHeroClassAsync(ctx1, "necro");
            await repo1.AddRunAsync(new StageRun
            {
                StageId         = stages[0].Id,
                DurationSeconds = 120,
                GoldGained      = 1_000,
                XpGained        = 500,
                Hero            = new HeroSnapshot(hcId, 10, 500),
                CompletedAtUtc  = new DateTime(2026, 6, 1, 11, 0, 0, DateTimeKind.Utc),
                IsPartial       = false,
            }, CancellationToken.None);

            await repo1.AddRunAsync(new StageRun
            {
                StageId         = stages[1].Id,
                DurationSeconds = 150,
                GoldGained      = 2_000,
                XpGained        = 800,
                Hero            = new HeroSnapshot(hcId, 10, 500),
                CompletedAtUtc  = new DateTime(2026, 6, 1, 11, 5, 0, DateTimeKind.Utc),
                IsPartial       = false,
            }, CancellationToken.None);
        }

        // Act
        var (ctx2, repo2) = CreateRunRepository();
        await using (ctx2)
        {
            var runsForStage0 = await repo2.GetRunsAsync(stages[0].Id, CancellationToken.None);
            var runsForStage1 = await repo2.GetRunsAsync(stages[1].Id, CancellationToken.None);

            // Assert
            runsForStage0.Should().HaveCount(1, "только забег этапа 0");
            runsForStage1.Should().HaveCount(1, "только забег этапа 1");
            runsForStage0.Should().OnlyContain(r => r.StageId == stages[0].Id);
            runsForStage1.Should().OnlyContain(r => r.StageId == stages[1].Id);
        }
    }

    // ── 6. Несколько забегов одного этапа возвращаются все ───────────────────

    [Fact]
    public async Task GetRunsAsync_MultipleRunsSameStage_ReturnsAll()
    {
        // Arrange
        var stage = await Db.Stages.FirstAsync();

        var (ctx1, repo1) = CreateRunRepository();
        await using (ctx1)
        {
            int hcId = await SeedHeroClassAsync(ctx1, "barbarian");
            for (int i = 0; i < 3; i++)
            {
                await repo1.AddRunAsync(new StageRun
                {
                    StageId         = stage.Id,
                    DurationSeconds = 100 + i * 20,
                    GoldGained      = 1_000L + i * 500,
                    XpGained        = 400,
                    Hero            = new HeroSnapshot(hcId, 30, 12_000),
                    CompletedAtUtc  = new DateTime(2026, 6, 1, 12, 0, i * 2, DateTimeKind.Utc),
                    IsPartial       = false,
                }, CancellationToken.None);
            }
        }

        // Act
        var (ctx2, repo2) = CreateRunRepository();
        await using (ctx2)
        {
            var runs = await repo2.GetRunsAsync(stage.Id, CancellationToken.None);

            // Assert: все 3 забега возвращены
            runs.Should().HaveCount(3);
            runs.Should().OnlyContain(r => r.StageId == stage.Id);
        }
    }

    // ── 7. IsPartial=true сохраняется и читается ─────────────────────────────

    [Fact]
    public async Task AddRunAsync_IsPartialTrue_PersistedAndReturned()
    {
        // Arrange
        var stage = await Db.Stages.FirstAsync();

        var (ctx1, repo1) = CreateRunRepository();
        long runId;
        await using (ctx1)
        {
            int hcId = await SeedHeroClassAsync(ctx1, "rogue");
            var run = new StageRun
            {
                StageId         = stage.Id,
                DurationSeconds = 60,
                GoldGained      = 300,
                XpGained        = 100,
                Hero            = new HeroSnapshot(hcId, 3, 50),
                CompletedAtUtc  = new DateTime(2026, 6, 1, 13, 0, 0, DateTimeKind.Utc),
                IsPartial       = true,
            };
            await repo1.AddRunAsync(run, CancellationToken.None);
            runId = run.Id;
        }

        // Act
        var (ctx2, repo2) = CreateRunRepository();
        await using (ctx2)
        {
            var runs = await repo2.GetRunsAsync(stage.Id, CancellationToken.None);

            // Assert
            var loaded = runs.Should().ContainSingle(r => r.Id == runId).Subject;
            loaded.IsPartial.Should().BeTrue("partial-флаг должен сохраняться (нужен для исключения из агрегатов FR-010)");
        }
    }

    // ── 8. GoldGained = 0 и XpGained = 0 допустимы и персистируются ──────────

    [Fact]
    public async Task AddRunAsync_ZeroGoldAndXp_PersistedCorrectly()
    {
        // Arrange
        var stage = await Db.Stages.FirstAsync();

        var (ctx1, repo1) = CreateRunRepository();
        long runId;
        await using (ctx1)
        {
            int hcId = await SeedHeroClassAsync(ctx1, "monk");
            var run = new StageRun
            {
                StageId         = stage.Id,
                DurationSeconds = 10,
                GoldGained      = 0,
                XpGained        = 0,
                Hero            = new HeroSnapshot(hcId, 1, 0),
                CompletedAtUtc  = new DateTime(2026, 6, 1, 14, 0, 0, DateTimeKind.Utc),
                IsPartial       = false,
            };
            await repo1.AddRunAsync(run, CancellationToken.None);
            runId = run.Id;
        }

        // Act
        var (ctx2, repo2) = CreateRunRepository();
        await using (ctx2)
        {
            var runs = await repo2.GetRunsAsync(stage.Id, CancellationToken.None);

            // Assert
            var loaded = runs.Should().ContainSingle(r => r.Id == runId).Subject;
            loaded.GoldGained.Should().Be(0, "GoldGained=0 допустим (≥0) и должен персистироваться");
            loaded.XpGained.Should().Be(0,   "XpGained=0 допустим (≥0) и должен персистироваться");
        }
    }

    // ── 9. Два забега с разным Hero.Level/Damage — снимки не путаются (Edge Case «респек») ──

    [Fact]
    public async Task AddRunAsync_TwoRunsDifferentHeroContext_SnapshotsStoredIndependently()
    {
        // Arrange: один этап, два забега — разные уровень/урон героя
        var stage = await Db.Stages.FirstAsync();

        var (ctx1, repo1) = CreateRunRepository();
        long run1Id, run2Id;
        await using (ctx1)
        {
            int hcId = await SeedHeroClassAsync(ctx1, "shaman");
            var run1 = new StageRun
            {
                StageId         = stage.Id,
                DurationSeconds = 200,
                GoldGained      = 7_000,
                XpGained        = 3_000,
                Hero            = new HeroSnapshot(hcId, 10, 5_000),
                CompletedAtUtc  = new DateTime(2026, 6, 1, 15, 0, 0, DateTimeKind.Utc),
                IsPartial       = false,
            };
            var run2 = new StageRun
            {
                StageId         = stage.Id,
                DurationSeconds = 220,
                GoldGained      = 8_000,
                XpGained        = 3_500,
                // после «респека» — другой уровень и урон
                Hero            = new HeroSnapshot(hcId, 25, 40_000),
                CompletedAtUtc  = new DateTime(2026, 6, 1, 16, 0, 0, DateTimeKind.Utc),
                IsPartial       = false,
            };
            await repo1.AddRunAsync(run1, CancellationToken.None);
            await repo1.AddRunAsync(run2, CancellationToken.None);
            run1Id = run1.Id;
            run2Id = run2.Id;
        }

        // Act
        var (ctx2, repo2) = CreateRunRepository();
        await using (ctx2)
        {
            var runs = await repo2.GetRunsAsync(stage.Id, CancellationToken.None);

            // Assert: каждый забег хранит свой снимок
            runs.Should().HaveCount(2);
            var loaded1 = runs.Single(r => r.Id == run1Id);
            var loaded2 = runs.Single(r => r.Id == run2Id);

            loaded1.Hero.Level.Should().Be(10,    "забег 1 хранит Level=10 до респека");
            loaded1.Hero.Damage.Should().Be(5_000, "забег 1 хранит Damage=5000 до респека");

            loaded2.Hero.Level.Should().Be(25,     "забег 2 хранит Level=25 после респека");
            loaded2.Hero.Damage.Should().Be(40_000, "забег 2 хранит Damage=40000 после респека");
        }
    }

    // ── 10. GetRunsAsync для этапа без забегов возвращает пустой список, не null ──

    [Fact]
    public async Task GetRunsAsync_StageWithNoRuns_ReturnsEmptyList()
    {
        // Arrange: этап с гарантированно нулевым числом забегов
        var stage = await Db.Stages.FirstAsync();

        var (ctx, repo) = CreateRunRepository();
        await using (ctx)
        {
            // Act
            var runs = await repo.GetRunsAsync(stage.Id, CancellationToken.None);

            // Assert
            runs.Should().NotBeNull("GetRunsAsync не должен возвращать null");
            runs.Should().BeEmpty("для этапа без добавленных забегов список должен быть пустым");
        }
    }
}
