namespace TBHStats.Data.Tests;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TBHStats.Core.Models;
using TBHStats.Data.Repositories;
using Xunit;

/// <summary>
/// Интеграционные тесты валидности истории после расширения механик (SC-010, FR-013, ADR-009).
///
/// Инвариант: добавление нового типа сундука / класса героя / вкладки осуществляется
/// через данные конфига (GameMechanicsConfig), а НЕ через изменение схемы БД.
/// Ранее накопленная история (StageRun/StageRunChest) остаётся валидной после расширения.
///
/// Все тесты работают на реальном временном файловом SQLite (не in-memory).
/// Расширение механик выполняется прямым добавлением через DbContext (imitate config-driven seeding),
/// поскольку DatabaseInitializer.SeedMechanicsAsync жёстко вызывает только CreateDefault()
/// и не принимает кастомный конфиг — это зафиксировано в отчёте.
/// </summary>
public sealed class HistoryValidityTests : HistoryValidityFixture
{
    // ────────────────────────────────────────────────────────────
    // 1. Схема не изменилась после расширения механик
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExtendMechanics_NewChestType_NoNewMigrationsRequired()
    {
        // Arrange: запоминаем список применённых миграций ДО расширения
        var migrationsBeforeExtension = (await Db.Database.GetAppliedMigrationsAsync()).ToList();
        migrationsBeforeExtension.Should().NotBeEmpty("миграции должны быть применены при инициализации");

        // Act: добавляем новый ChestType в БД напрямую (имитация config-driven расширения)
        await AddGreenChestTypeAsync(Db);

        // Assert: список применённых миграций не изменился — никакой новой миграции не потребовалось
        var migrationsAfterExtension = (await Db.Database.GetAppliedMigrationsAsync()).ToList();
        migrationsAfterExtension.Should().BeEquivalentTo(
            migrationsBeforeExtension,
            "добавление нового типа сундука = INSERT в ChestTypes, не миграция схемы (FR-013)");
    }

    [Fact]
    public async Task ExtendMechanics_NewHeroClass_NoNewMigrationsRequired()
    {
        // Arrange
        var migrationsBeforeExtension = (await Db.Database.GetAppliedMigrationsAsync()).ToList();

        // Act: добавляем новый HeroClass
        var heroClass = new HeroClass { Key = "mage", DisplayName = "Маг", IsActive = true };
        Db.HeroClasses.Add(heroClass);
        await Db.SaveChangesAsync();

        // Assert: схема не изменилась
        var migrationsAfterExtension = (await Db.Database.GetAppliedMigrationsAsync()).ToList();
        migrationsAfterExtension.Should().BeEquivalentTo(
            migrationsBeforeExtension,
            "добавление класса героя = INSERT в HeroClasses, не миграция схемы (FR-013)");
    }

    // ────────────────────────────────────────────────────────────
    // 2. Старая история остаётся валидной после расширения
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task OldHistory_AfterExtendingChestTypes_RunsReadableWithIntactValues()
    {
        // Arrange: записываем историю на дефолтных типах сундуков 1/2/3
        int heroClassId = await SeedHeroClassAsync(Db);
        var stage = await Db.Stages.FirstAsync();

        var run = new StageRun
        {
            StageId         = stage.Id,
            DurationSeconds = 3600,
            GoldGained      = 50_000,
            XpGained        = 20_000,
            Hero            = new HeroSnapshot(heroClassId, 30, 75_000),
            CompletedAtUtc  = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            IsPartial       = false,
            Chests          = new List<StageRunChest>
            {
                new StageRunChest { ChestTypeId = 1, Count = 10 },
                new StageRunChest { ChestTypeId = 2, Count = 4 },
                new StageRunChest { ChestTypeId = 3, Count = 1 },
            },
        };

        Db.StageRuns.Add(run);
        await Db.SaveChangesAsync();
        long runId = run.Id;

        // Act: «расширяем механики» — добавляем новый ChestType Id=4
        await AddGreenChestTypeAsync(Db);

        // Assert в том же контексте: старый run читается без ошибок
        var loadedRun = await Db.StageRuns
            .AsNoTracking()
            .Include(r => r.Chests)
            .SingleAsync(r => r.Id == runId);

        loadedRun.GoldGained.Should().Be(50_000, "GoldGained должен сохраниться");
        loadedRun.XpGained.Should().Be(20_000,   "XpGained должен сохраниться");
        loadedRun.DurationSeconds.Should().Be(3600);
        loadedRun.IsPartial.Should().BeFalse();
        loadedRun.Chests.Should().HaveCount(3, "три сундука дефолтных типов должны читаться без ошибок");
        loadedRun.Chests.Should().Contain(c => c.ChestTypeId == 1 && c.Count == 10);
        loadedRun.Chests.Should().Contain(c => c.ChestTypeId == 2 && c.Count == 4);
        loadedRun.Chests.Should().Contain(c => c.ChestTypeId == 3 && c.Count == 1);
    }

    [Fact]
    public async Task OldHistory_AfterExtendingChestTypes_HeroSnapshotPreserved()
    {
        // Arrange
        int heroClassId = await SeedHeroClassAsync(Db);
        var stage = await Db.Stages.FirstAsync();

        var run = new StageRun
        {
            StageId         = stage.Id,
            DurationSeconds = 1800,
            GoldGained      = 10_000,
            XpGained        = 5_000,
            Hero            = new HeroSnapshot(heroClassId, 55, 150_000),
            CompletedAtUtc  = new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc),
            IsPartial       = false,
        };

        Db.StageRuns.Add(run);
        await Db.SaveChangesAsync();
        long runId = run.Id;

        // Act: расширяем механики
        await AddGreenChestTypeAsync(Db);

        // Assert: HeroSnapshot полностью сохранён
        var loaded = await Db.StageRuns
            .AsNoTracking()
            .SingleAsync(r => r.Id == runId);

        loaded.Hero.HeroClassId.Should().Be(heroClassId);
        loaded.Hero.Level.Should().Be(55);
        loaded.Hero.Damage.Should().Be(150_000);
    }

    // ────────────────────────────────────────────────────────────
    // 3. Новый тип сундука используется в новых забегах
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task NewChestType_AfterExtension_CanBeUsedInNewRun_RoundTrip()
    {
        // Arrange: добавляем новый тип сундука
        int newChestTypeId = await AddGreenChestTypeAsync(Db);
        int heroClassId    = await SeedHeroClassAsync(Db);
        var stage          = await Db.Stages.FirstAsync();

        var run = new StageRun
        {
            StageId         = stage.Id,
            DurationSeconds = 2700,
            GoldGained      = 30_000,
            XpGained        = 12_000,
            Hero            = new HeroSnapshot(heroClassId, 20, 25_000),
            CompletedAtUtc  = new DateTime(2026, 3, 10, 14, 0, 0, DateTimeKind.Utc),
            IsPartial       = false,
            Chests          = new List<StageRunChest>
            {
                new StageRunChest { ChestTypeId = 1,             Count = 6 },
                new StageRunChest { ChestTypeId = newChestTypeId, Count = 3 }, // новый тип
            },
        };

        Db.StageRuns.Add(run);
        await Db.SaveChangesAsync();
        long runId = run.Id;

        // Act: читаем через новый контекст (симуляция «перезапуска приложения»)
        using var ctx2 = CreateContext();
        var loadedRun = await ctx2.StageRuns
            .AsNoTracking()
            .Include(r => r.Chests)
            .SingleAsync(r => r.Id == runId);

        // Assert: забег с новым ChestType читается корректно
        loadedRun.Chests.Should().HaveCount(2);
        loadedRun.Chests.Should().Contain(c => c.ChestTypeId == newChestTypeId && c.Count == 3,
            "новый тип сундука (Id=4) должен персистироваться и читаться без ошибок");
    }

    // ────────────────────────────────────────────────────────────
    // 4. Перезапуск: данные на месте после пересоздания DbContext
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AfterRestart_OldRunsAndNewChestType_BothReadable()
    {
        // Arrange: записываем «старые» данные, затем расширяем механики
        int heroClassId = await SeedHeroClassAsync(Db);
        var stage       = await Db.Stages.FirstAsync();

        // Старый run с типами 1/2/3
        var oldRun = new StageRun
        {
            StageId         = stage.Id,
            DurationSeconds = 3600,
            GoldGained      = 100_000,
            XpGained        = 40_000,
            Hero            = new HeroSnapshot(heroClassId, 45, 200_000),
            CompletedAtUtc  = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IsPartial       = false,
            Chests          = new List<StageRunChest>
            {
                new StageRunChest { ChestTypeId = 1, Count = 15 },
                new StageRunChest { ChestTypeId = 2, Count = 6 },
                new StageRunChest { ChestTypeId = 3, Count = 2 },
            },
        };
        Db.StageRuns.Add(oldRun);
        await Db.SaveChangesAsync();
        long oldRunId = oldRun.Id;

        // Добавляем новый ChestType
        int newChestTypeId = await AddGreenChestTypeAsync(Db);

        // Новый run с новым типом
        var (_, repo) = CreateRunRepository();
        await using var _ = CreateContext(); // продолжаем с репозиторием через CreateRunRepository

        var newRunCtx = CreateContext();
        await using (newRunCtx)
        {
            int hcId2 = await SeedHeroClassAsync(newRunCtx, "archer");
            var newRun = new StageRun
            {
                StageId         = stage.Id,
                DurationSeconds = 1800,
                GoldGained      = 50_000,
                XpGained        = 20_000,
                Hero            = new HeroSnapshot(hcId2, 20, 30_000),
                CompletedAtUtc  = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                IsPartial       = false,
                Chests          = new List<StageRunChest>
                {
                    new StageRunChest { ChestTypeId = newChestTypeId, Count = 4 },
                },
            };
            newRunCtx.StageRuns.Add(newRun);
            await newRunCtx.SaveChangesAsync();
        }

        // Act: пересоздаём DbContext — симуляция перезапуска
        using var restartCtx = CreateContext();

        // Assert 1: старый забег читается с целыми данными
        var loadedOldRun = await restartCtx.StageRuns
            .AsNoTracking()
            .Include(r => r.Chests)
            .SingleAsync(r => r.Id == oldRunId);

        loadedOldRun.GoldGained.Should().Be(100_000, "старые данные переживают расширение механик");
        loadedOldRun.Chests.Should().HaveCount(3);

        // Assert 2: новый ChestType присутствует в БД
        var chestTypes = await restartCtx.ChestTypes.ToListAsync();
        chestTypes.Should().HaveCount(4, "после расширения в БД 4 типа сундуков");
        chestTypes.Should().Contain(ct => ct.Key == "green" && ct.Id == newChestTypeId);

        // Assert 3: новый забег с ChestType 4 читается корректно
        var allRuns = await restartCtx.StageRuns
            .AsNoTracking()
            .Include(r => r.Chests)
            .Where(r => r.StageId == stage.Id)
            .ToListAsync();

        allRuns.Should().HaveCount(2, "в БД два забега: старый (типы 1-3) и новый (тип 4)");
        allRuns.Should().Contain(r => r.Chests.Any(c => c.ChestTypeId == newChestTypeId),
            "новый забег с новым типом сундука должен читаться");
    }

    // ────────────────────────────────────────────────────────────
    // 5. FK-целостность: ChestType 1/2/3 резолвятся после добавления нового
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DefaultChestTypes_AfterAddingNewType_FkResolvedCorrectly()
    {
        // Act: добавляем новый тип — не должно нарушать существующие FK
        int newId = await AddGreenChestTypeAsync(Db);

        // Assert: все 4 типа читаются, Id уникальны
        var allTypes = await Db.ChestTypes.OrderBy(c => c.Id).ToListAsync();
        allTypes.Should().HaveCount(4);
        allTypes.Select(c => c.Id).Should().OnlyHaveUniqueItems("Id типов сундуков должны быть уникальны");
        allTypes.Should().Contain(ct => ct.Id == 1 && ct.Key == "brown");
        allTypes.Should().Contain(ct => ct.Id == 2 && ct.Key == "blue");
        allTypes.Should().Contain(ct => ct.Id == 3 && ct.Key == "red");
        allTypes.Should().Contain(ct => ct.Id == newId && ct.Key == "green");
    }

    [Fact]
    public async Task OldRuns_ReferencingChestTypeIds123_FkNotBrokenByExtension()
    {
        // Arrange: записываем забег с ChestType FK 1, 2, 3
        int heroClassId = await SeedHeroClassAsync(Db);
        var stage       = await Db.Stages.FirstAsync();

        var run = new StageRun
        {
            StageId         = stage.Id,
            DurationSeconds = 3600,
            GoldGained      = 80_000,
            XpGained        = 35_000,
            Hero            = new HeroSnapshot(heroClassId, 60, 500_000),
            CompletedAtUtc  = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            IsPartial       = false,
            Chests          = new List<StageRunChest>
            {
                new StageRunChest { ChestTypeId = 1, Count = 20 },
                new StageRunChest { ChestTypeId = 2, Count = 8 },
                new StageRunChest { ChestTypeId = 3, Count = 3 },
            },
        };
        Db.StageRuns.Add(run);
        await Db.SaveChangesAsync();

        // Act: расширяем — добавляем ChestType с Id=4
        await AddGreenChestTypeAsync(Db);

        // Assert: StageRunChest с FK 1/2/3 читаются через JOIN без ошибок
        // (используем прямой SQL-запрос как подтверждение FK-целостности)
        var chestCounts = await Db.StageRunChests
            .AsNoTracking()
            .Where(c => c.StageRunId == run.Id)
            .OrderBy(c => c.ChestTypeId)
            .ToListAsync();

        chestCounts.Should().HaveCount(3,
            "FK на ChestType 1/2/3 должны разрешаться после добавления нового типа");
        chestCounts[0].ChestTypeId.Should().Be(1);
        chestCounts[1].ChestTypeId.Should().Be(2);
        chestCounts[2].ChestTypeId.Should().Be(3);
    }

    // ────────────────────────────────────────────────────────────
    // 6. Идемпотентность сидинга после расширения
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RepeatedInitialization_AfterExtension_DoesNotDuplicateDefaultTypes()
    {
        // Arrange: добавляем новый тип
        await AddGreenChestTypeAsync(Db);

        // Act: повторный вызов DatabaseInitializer (идемпотентность)
        await DatabaseInitializer.InitializeAsync(Db, CancellationToken.None);

        // Assert: дефолтные 3 типа не задублированы (IdempotentSeeding проверяет AnyAsync)
        int chestTypeCount = await Db.ChestTypes.CountAsync();
        chestTypeCount.Should().Be(4,
            "повторный сидинг не должен добавлять дубли; наш новый ChestType 'green' сохранён");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Фикстура для HistoryValidityTests
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Базовая фикстура для HistoryValidityTests: временный файловый SQLite + реальные миграции.
/// Предоставляет хелперы для расширения механик и записи тестовых данных.
/// </summary>
public abstract class HistoryValidityFixture : IAsyncLifetime
{
    protected string DbPath { get; }
    protected TbhStatsDbContext Db { get; private set; } = null!;

    protected HistoryValidityFixture()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tbhstats-history-tests");
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
    /// Создаёт новый независимый контекст и RunRepository (симуляция «перезапуска»).
    /// </summary>
    protected (TbhStatsDbContext ctx, IRunRepository repo) CreateRunRepository()
    {
        var ctx = CreateContext();
        return (ctx, new RunRepository(ctx));
    }

    /// <summary>
    /// Добавляет новый ChestType (Key="green", Id=4) в БД напрямую через DbContext.
    ///
    /// Примечание об архитектурном ограничении: DatabaseInitializer.SeedMechanicsAsync
    /// жёстко использует только GameMechanicsConfig.CreateDefault() и не принимает кастомный конфиг.
    /// Поэтому расширение через сидинг-путь выполняется прямым INSERT через DbContext —
    /// имитируя то, что в продакшн-коде должна делать расширяемая seed-логика (FR-013, ADR-009).
    /// Этот тест одновременно фиксирует ограничение: DatabaseInitializer не поддерживает
    /// передачу кастомного GameMechanicsConfig — расширение механик требует ручного INSERT.
    /// </summary>
    /// <returns>Id добавленного ChestType.</returns>
    protected async Task<int> AddGreenChestTypeAsync(TbhStatsDbContext ctx)
    {
        var greenChest = new ChestType
        {
            Id          = 4,
            Key         = "green",
            DisplayName = "Эпический",
            ColorLabel  = "зелёный",
            SortOrder   = 4,
            IsActive    = true,
        };
        ctx.ChestTypes.Add(greenChest);
        await ctx.SaveChangesAsync();
        return greenChest.Id;
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
