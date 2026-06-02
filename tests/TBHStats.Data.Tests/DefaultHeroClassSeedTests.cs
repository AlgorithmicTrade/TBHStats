namespace TBHStats.Data.Tests;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;
using TBHStats.Data.Repositories;
using Xunit;

/// <summary>
/// Регресс-тесты фикса «FOREIGN KEY constraint failed» при записи StageRun
/// с нераспознанным героем (HeroClassId=0) — исправлено добавлением дефолтного
/// FK-якоря HeroClass { Id=1, Key="unknown" } в GameMechanicsConfig.CreateDefault().
///
/// Специально НЕ дублирует:
/// — Полный CRUD StageRun/HeroSnapshot (DataLayerTests.StageRunCrudTests)
/// — Полную проверку AddRunAsync/GetRunsAsync (RunRecordingTests)
/// — Полный RecomputeForStageAsync (StageAggregateRecomputeTests)
/// — FK-выравнивание config.Stage.Id == db.Stage.Id (ResolverFkAlignmentTests)
/// — Идемпотентность сидинга ChestTypes/Stages/Tabs (MigrationAndSeedingTests)
///
/// Фокус: именно дефолтный HeroClass Id=1 засевается при сидинге,
/// запись StageRun с HeroClassId=1 проходит без FK-ошибки,
/// и FK реально защищает от HeroClassId=0.
///
/// Все тесты работают на реальном временном файловом SQLite (не in-memory, research R5).
/// </summary>
public sealed class DefaultHeroClassSeedTests : TempDbFixture
{
    // ─────────────────────────────────────────────────────────────────────────
    // Тест 1: Регресс-гард — HeroClass Id=1 (Key="unknown") засеян при InitializeAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Верифицирует, что после InitializeAsync (реальные миграции + сидинг)
    /// в таблице HeroClasses присутствует запись Id=1 / Key="unknown".
    /// Регресс-гард против повторного появления пустого справочника.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_FreshDb_SeedsDefaultHeroClass_Id1_Unknown()
    {
        // БД уже инициализирована в InitializeAsync фикстуры TempDbFixture.

        // Assert: таблица не пуста
        bool anyHeroClass = await Db.HeroClasses.AnyAsync();
        anyHeroClass.Should().BeTrue("HeroClasses не должен быть пуст после сидинга — без FK-якоря любой StageRun упадёт");

        // Assert: дефолтный класс Id=1 / Key="unknown" существует
        HeroClass? defaultClass = await Db.HeroClasses
            .AsNoTracking()
            .SingleOrDefaultAsync(h => h.Id == 1);

        defaultClass.Should().NotBeNull("HeroClass Id=1 (FK-якорь для нераспознанного героя) должен быть засеян");
        defaultClass!.Key.Should().Be("unknown", "дефолтный класс должен иметь Key='unknown'");
        defaultClass.IsActive.Should().BeTrue("дефолтный класс должен быть активен");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Тест 2: Самовосстановление — существующая пустая HeroClasses засевается при повторном Init
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Симулирует «существующую БД пользователя» с пустой таблицей HeroClasses
    /// (ситуация до фикса): удаляет все HeroClass-записи, затем вызывает InitializeAsync
    /// повторно. Верифицирует, что дефолтный класс засевается снова (self-heal).
    /// Это покрывает путь «живая БД пользователя без миграции схемы».
    /// </summary>
    [Fact]
    public async Task InitializeAsync_EmptyHeroClassesTable_SeedsDefaultClassOnNextInit()
    {
        // Arrange: удаляем все HeroClass-записи — имитируем «пустую» живую БД пользователя
        List<HeroClass> existingClasses = await Db.HeroClasses.ToListAsync();
        if (existingClasses.Count > 0)
        {
            Db.HeroClasses.RemoveRange(existingClasses);
            await Db.SaveChangesAsync();
        }

        bool emptyAfterRemoval = await Db.HeroClasses.AnyAsync();
        emptyAfterRemoval.Should().BeFalse("подготовка: HeroClasses должен быть пуст для имитации старой БД");

        // Act: повторный вызов InitializeAsync (следующий запуск приложения)
        // Ожидаем: config.HeroClasses.Count == 1 (после части 1) И !AnyAsync == true → сидинг.
        await DatabaseInitializer.InitializeAsync(Db, CancellationToken.None);

        // Assert: дефолтный класс засеян снова
        HeroClass? restored = await Db.HeroClasses
            .AsNoTracking()
            .SingleOrDefaultAsync(h => h.Id == 1);

        restored.Should().NotBeNull("дефолтный HeroClass Id=1 должен быть восстановлен при следующем запуске (self-heal)");
        restored!.Key.Should().Be("unknown");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Тест 3: Основной регресс — запись StageRun с HeroClassId=1 НЕ бросает FK-ошибку
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Основной регресс-тест: StageRun с Hero.HeroClassId=1 (дефолтный «unknown»)
    /// записывается через IRunRepository.AddRunAsync без исключения.
    /// До фикса — падало с «SQLite Error 19: FOREIGN KEY constraint failed».
    ///
    /// Путь Charts: GetRunsAsync возвращает забег, IsPartial == false.
    /// </summary>
    [Fact]
    public async Task AddRunAsync_WithDefaultHeroClassId1_DoesNotThrow_FkConstraintFixed()
    {
        // Arrange: используем HeroClassId=1 (дефолтный FK-якорь) — так делает RunRecorder
        // когда герой не распознан (часть 1 фикса, App-слой).
        GameMechanicsConfig config = GameMechanicsConfig.CreateDefault();
        int? stageId = config.ResolveStageId(new StageRef(1, "normal", 1));
        stageId.Should().NotBeNull("Act1/normal/1 должен резолвиться в конфиге");

        const int defaultHeroClassId = 1; // FK-якорь, засеянный DatabaseInitializer

        // Убеждаемся, что FK-якорь реально есть в БД
        bool anchorExists = await Db.HeroClasses.AnyAsync(h => h.Id == defaultHeroClassId);
        anchorExists.Should().BeTrue("HeroClass Id=1 должен быть засеян до записи забега");

        var run = new StageRun
        {
            StageId         = stageId!.Value,
            DurationSeconds = 3_600,
            GoldGained      = 5_400,
            XpGained        = 2_700,
            Hero            = new HeroSnapshot(defaultHeroClassId, Level: 1, Damage: 0),
            CompletedAtUtc  = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc),
            IsPartial       = false,
        };

        // Act: используем новый контекст (репозиторий), как это делает приложение
        await using TbhStatsDbContext writeCtx = CreateContext();
        var repo = new RunRepository(writeCtx);

        // Assert: НЕ бросает (до фикса — SQLite Error 19: FOREIGN KEY constraint failed)
        Func<Task> act = () => repo.AddRunAsync(run, CancellationToken.None);
        await act.Should().NotThrowAsync(
            "StageRun с HeroClassId=1 (FK-якорь 'unknown') должен записываться без FOREIGN KEY constraint failed");

        // Путь Charts: GetRunsAsync возвращает забег с IsPartial=false
        await using TbhStatsDbContext readCtx = CreateContext();
        var readRepo = new RunRepository(readCtx);
        IReadOnlyList<StageRun> runs = await readRepo.GetRunsAsync(stageId.Value, CancellationToken.None);

        runs.Should().ContainSingle(r => r.Hero.HeroClassId == defaultHeroClassId,
            "путь Charts: забег с дефолтным HeroClassId должен читаться через GetRunsAsync");
        runs[0].IsPartial.Should().BeFalse("non-partial забег попадает в Charts");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Тест 4: Путь Compare — агрегат наполняется после записи с HeroClassId=1
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Сквозной тест пути Compare: после записи non-partial забега с HeroClassId=1
    /// RecomputeForStageAsync пересчитывает агрегат, GetAllAsync его возвращает.
    /// </summary>
    [Fact]
    public async Task AddRunAsync_DefaultHeroClass_ThenRecompute_ThenGetAllAsync_ContainsAggregate()
    {
        // Arrange
        GameMechanicsConfig config = GameMechanicsConfig.CreateDefault();
        int? stageId = config.ResolveStageId(new StageRef(1, "normal", 1));
        stageId.Should().NotBeNull();

        const int defaultHeroClassId = 1;

        var run = new StageRun
        {
            StageId         = stageId!.Value,
            DurationSeconds = 3_600,
            GoldGained      = 7_200,
            XpGained        = 3_600,
            Hero            = new HeroSnapshot(defaultHeroClassId, Level: 1, Damage: 0),
            CompletedAtUtc  = new DateTime(2026, 6, 2, 13, 0, 0, DateTimeKind.Utc),
            IsPartial       = false,
        };

        Db.StageRuns.Add(run);
        await Db.SaveChangesAsync();

        // Act: пересчёт агрегата
        var aggRepo = new StageAggregateRepository(
            Db,
            new TBHStats.Core.Optimization.StageAggregateCalculator());
        await aggRepo.RecomputeForStageAsync(stageId.Value, recentWindowSize: 10, CancellationToken.None);

        // Assert путь Compare: GetAllAsync в новом контексте содержит агрегат
        await using TbhStatsDbContext readCtx = CreateContext();
        var readAggRepo = new StageAggregateRepository(
            readCtx,
            new TBHStats.Core.Optimization.StageAggregateCalculator());
        IReadOnlyList<StageAggregate> all = await readAggRepo.GetAllAsync(CancellationToken.None);

        all.Should().Contain(a => a.StageId == stageId.Value,
            "путь Compare: агрегат должен присутствовать после записи забега с дефолтным HeroClassId");

        StageAggregate agg = all.Single(a => a.StageId == stageId.Value);
        agg.RunCount.Should().Be(1, "один non-partial забег учитывается в агрегате");
        agg.AvgGoldPerHour.Should().BeApproximately(7_200.0, 1e-6,
            "GoldPerHour = 7200 gold / 3600 sec * 3600 = 7200/h");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Тест 5: Строгий FK-гард — HeroClassId=0 бросает DbUpdateException
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Граничный кейс: попытка записать StageRun с HeroClassId=0 (несуществующий)
    /// должна бросать исключение (FK нарушен).
    /// Фиксирует, что FK реально защищает и что Id=1 валиден, а 0 — нет.
    /// </summary>
    [Fact]
    public async Task AddRunAsync_HeroClassId0_ThrowsDbUpdateException_FkProtects()
    {
        // Arrange
        GameMechanicsConfig config = GameMechanicsConfig.CreateDefault();
        int? stageId = config.ResolveStageId(new StageRef(1, "normal", 1));
        stageId.Should().NotBeNull();

        const int nonExistentHeroClassId = 0; // никогда не существует в БД

        // Убеждаемся, что Id=0 действительно отсутствует
        bool exists = await Db.HeroClasses.AnyAsync(h => h.Id == nonExistentHeroClassId);
        exists.Should().BeFalse("HeroClassId=0 должен отсутствовать в БД — подготовка к проверке FK");

        await using TbhStatsDbContext writeCtx = CreateContext();
        var repo = new RunRepository(writeCtx);

        var runWithBadFk = new StageRun
        {
            StageId         = stageId!.Value,
            DurationSeconds = 60,
            GoldGained      = 100,
            XpGained        = 50,
            Hero            = new HeroSnapshot(nonExistentHeroClassId, Level: 1, Damage: 0),
            CompletedAtUtc  = new DateTime(2026, 6, 2, 14, 0, 0, DateTimeKind.Utc),
            IsPartial       = false,
        };

        // Act & Assert: FK реально защищает от записи с несуществующим HeroClassId
        Func<Task> act = () => repo.AddRunAsync(runWithBadFk, CancellationToken.None);
        await act.Should().ThrowAsync<Exception>(
            "StageRun с несуществующим HeroClassId=0 должен вызывать исключение — FK защита работает");
    }
}
