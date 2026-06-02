namespace TBHStats.Data.Tests;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;
using TBHStats.Data.Repositories;
using Xunit;

/// <summary>
/// Интеграционные тесты связки «резолвер StageRef → валидный FK в БД → персистенция».
/// Проверяет новый аспект Phase 3: <see cref="GameMechanicsConfig.ResolveStageId"/> возвращает
/// тот же Stage.Id, что был засеян в БД через <see cref="DatabaseInitializer"/>,
/// и что <see cref="StageRun"/> с этим stageId успешно записывается и читается через
/// <see cref="IRunRepository"/>/<see cref="IStageAggregateRepository"/>.
///
/// Специально НЕ дублирует:
/// — CRUD полей StageRun/HeroSnapshot (DataLayerTests.StageRunCrudTests)
/// — Полную проверку AddRunAsync/GetRunsAsync (RunRecordingTests)
/// — Полный RecomputeForStageAsync (StageAggregateRecomputeTests)
/// — Расширяемость механик (HistoryValidityTests)
/// Фокус: именно FK-выравнивание config.Stage.Id == db.Stage.Id и сквозной путь через резолвер.
///
/// Все тесты работают на реальном временном файловом SQLite (не in-memory, research R5, quality.md).
/// </summary>
public sealed class ResolverFkAlignmentTests : TempDbFixture
{
    // ── Вспомогательные методы ───────────────────────────────────────────────

    private async Task<int> SeedHeroClassAsync(string key = "warrior")
    {
        var hc = new HeroClass { Key = key, DisplayName = key, IsActive = true };
        Db.HeroClasses.Add(hc);
        await Db.SaveChangesAsync();
        return hc.Id;
    }

    private static StageAggregateRepository CreateAggregateRepo(TbhStatsDbContext ctx)
        => new StageAggregateRepository(ctx, new TBHStats.Core.Optimization.StageAggregateCalculator());

    // ─────────────────────────────────────────────────────────────────────────
    // Тест 1: ResolveStageId возвращает Id, существующий в засеянной БД
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Проверяет, что Stage.Id из <see cref="GameMechanicsConfig.ResolveStageId"/>
    /// совпадает с реальной строкой в таблице Stages засеянной БД,
    /// и что (ActId, DifficultyId, Number) той строки соответствует резолвнутому StageRef.
    /// </summary>
    [Theory]
    [InlineData(1, "normal",    1)]   // первый этап матрицы: Id=1
    [InlineData(1, "nightmare", 5)]   // Act1/nightmare/5: Id=15
    [InlineData(2, "normal",    5)]   // Act2/normal/5: Id=25
    [InlineData(3, "nightmare", 10)]  // последний этап матрицы: Id=60
    public async Task ResolveStageId_ReturnedIdExistsInDb_WithMatchingActDifficultyNumber(
        int actNumber, string difficultyKey, int stageNumber)
    {
        // Arrange
        GameMechanicsConfig config = GameMechanicsConfig.CreateDefault();
        var stageRef = new StageRef(actNumber, difficultyKey, stageNumber);

        // Act: резолвим через конфиг
        int? resolvedId = config.ResolveStageId(stageRef);

        // Assert 1: резолвер не вернул null
        resolvedId.Should().NotBeNull(
            $"StageRef({actNumber}, {difficultyKey}, {stageNumber}) должен резолвиться в конфиге");

        int stageId = resolvedId!.Value;

        // Assert 2: запись с этим Id существует в засеянной БД (FK-выравнивание)
        Stage? dbStage = await Db.Stages.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == stageId);
        dbStage.Should().NotBeNull(
            $"Stage.Id={stageId} из ResolveStageId должен существовать в засеянной БД (config.Id == db.Id)");

        // Assert 3: (ActId, DifficultyId, Number) БД соответствует StageRef через конфиг-справочники
        Act? dbAct = await Db.Acts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == dbStage!.ActId);
        dbAct.Should().NotBeNull();
        dbAct!.Number.Should().Be(actNumber, $"акт в БД должен иметь Number={actNumber}");

        Difficulty? dbDiff = await Db.Difficulties.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == dbStage!.DifficultyId);
        dbDiff.Should().NotBeNull();
        dbDiff!.Key.Should().BeEquivalentTo(difficultyKey,
            $"сложность в БД должна иметь Key={difficultyKey}");

        dbStage!.Number.Should().Be(stageNumber,
            $"этап в БД должен иметь Number={stageNumber}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Тест 2: ResolveStageId → AddRunAsync → GetRunsAsync (сквозной путь FK→персистенция)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Сквозной тест: резолвим StageRef через GameMechanicsConfig (как это делает StatsOrchestrator),
    /// записываем StageRun с этим stageId через IRunRepository, и читаем его обратно.
    /// Это подтверждает, что резолвнутый Id — валидный FK и путь Charts работает после Phase 3.
    /// </summary>
    [Fact]
    public async Task ResolveStageId_Then_AddRunAsync_Then_GetRunsAsync_RoundTrip()
    {
        // Arrange: резолвим StageRef так же, как StatsOrchestrator делает при обработке кадра
        GameMechanicsConfig config = GameMechanicsConfig.CreateDefault();
        var stageRef = new StageRef(1, "normal", 1);
        int? stageId = config.ResolveStageId(stageRef);
        stageId.Should().NotBeNull("Act1/normal/1 должен резолвиться");

        int heroClassId = await SeedHeroClassAsync("mage");

        // Act: создаём репозиторий и записываем забег
        await using TbhStatsDbContext writeCtx = CreateContext();
        var runRepo = new RunRepository(writeCtx);

        var run = new StageRun
        {
            StageId         = stageId!.Value,
            DurationSeconds = 3_600,
            GoldGained      = 7_200,
            XpGained        = 3_600,
            Hero            = new HeroSnapshot(heroClassId, Level: 15, Damage: 20_000),
            CompletedAtUtc  = new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc),
            IsPartial       = false,
        };

        await runRepo.AddRunAsync(run, CancellationToken.None);

        // Assert: читаем через новый контекст (симуляция перезапуска — путь Charts)
        await using TbhStatsDbContext readCtx = CreateContext();
        var readRepo = new RunRepository(readCtx);

        IReadOnlyList<StageRun> runs = await readRepo.GetRunsAsync(stageId.Value, CancellationToken.None);

        runs.Should().HaveCount(1,
            "забег с резолвнутым stageId должен читаться через GetRunsAsync (путь Charts)");
        runs[0].StageId.Should().Be(stageId.Value);
        runs[0].GoldGained.Should().Be(7_200);
        runs[0].IsPartial.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Тест 3: ResolveStageId → RecomputeForStageAsync → GetAllAsync (путь Compare)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Сквозной тест: после записи забега через резолвнутый stageId вызываем RecomputeForStageAsync,
    /// затем GetAllAsync. Подтверждает, что путь Compare наполняется после Phase 3.
    /// </summary>
    [Fact]
    public async Task ResolveStageId_Then_RecomputeForStage_Then_GetAllAsync_ContainsAggregate()
    {
        // Arrange
        GameMechanicsConfig config = GameMechanicsConfig.CreateDefault();
        var stageRef = new StageRef(2, "nightmare", 5);
        int? stageId = config.ResolveStageId(stageRef);
        stageId.Should().NotBeNull("Act2/nightmare/5 должен резолвиться");

        int heroClassId = await SeedHeroClassAsync("paladin");

        // Записываем non-partial забег напрямую (тот же Db что и сид)
        var run = new StageRun
        {
            StageId         = stageId!.Value,
            DurationSeconds = 3_600,
            GoldGained      = 9_000,
            XpGained        = 4_500,
            Hero            = new HeroSnapshot(heroClassId, Level: 25, Damage: 50_000),
            CompletedAtUtc  = new DateTime(2026, 6, 2, 11, 0, 0, DateTimeKind.Utc),
            IsPartial       = false,
        };
        Db.StageRuns.Add(run);
        await Db.SaveChangesAsync();

        // Act: пересчитываем агрегат
        var aggRepo = CreateAggregateRepo(Db);
        await aggRepo.RecomputeForStageAsync(stageId.Value, recentWindowSize: 10, CancellationToken.None);

        // Assert: GetAllAsync в новом контексте возвращает агрегат для этого stageId (путь Compare)
        await using TbhStatsDbContext readCtx = CreateContext();
        var readAggRepo = CreateAggregateRepo(readCtx);
        IReadOnlyList<StageAggregate> all = await readAggRepo.GetAllAsync(CancellationToken.None);

        all.Should().Contain(a => a.StageId == stageId.Value,
            "GetAllAsync должен вернуть агрегат для stageId, записанного через резолвер (путь Compare)");

        StageAggregate agg = all.Single(a => a.StageId == stageId.Value);
        agg.RunCount.Should().Be(1, "один non-partial забег должен входить в агрегат");
        agg.AvgGoldPerHour.Should().BeApproximately(9_000.0, 1e-6,
            "GoldPerHour = 9000 gold / 3600 s * 3600 = 9000/h");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Тест 4: все 60 этапов конфига резолвятся и каждый Id присутствует в БД
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Верифицирует полное FK-выравнивание: все 60 Stage.Id из GameMechanicsConfig
    /// существуют в засеянной БД, и ни один конфигурационный Id не является «висячим».
    /// Это доказывает инвариант «config.Stage.Id == db.Stage.Id для всей матрицы 3×2×10».
    /// </summary>
    [Fact]
    public async Task AllConfigStageIds_ExistInSeededDb_FullMatrix60()
    {
        // Arrange
        GameMechanicsConfig config = GameMechanicsConfig.CreateDefault();

        // Act: загружаем все засеянные Id из БД
        List<int> dbStageIds = await Db.Stages.AsNoTracking()
            .Select(s => s.Id)
            .OrderBy(id => id)
            .ToListAsync();

        // Assert 1: 60 этапов засеяно
        dbStageIds.Should().HaveCount(60, "матрица 3×2×10 = 60 этапов");

        // Assert 2: каждый Stage.Id из конфига существует в БД (FK-выравнивание)
        foreach (Stage configStage in config.Stages)
        {
            dbStageIds.Should().Contain(configStage.Id,
                $"Stage.Id={configStage.Id} из конфига должен быть в засеянной БД");
        }

        // Assert 3: множества совпадают полностью (нет лишних Id ни в конфиге, ни в БД)
        IEnumerable<int> configIds = config.Stages.Select(s => s.Id).OrderBy(id => id);
        configIds.Should().BeEquivalentTo(dbStageIds,
            "множество Stage.Id в конфиге должно точно совпадать с множеством в БД");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Тест 5: ResolveStageId возвращает null для несуществующего StageRef
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Граничный случай: резолвер корректно возвращает null для отсутствующих StageRef,
    /// что предотвращает запись забегов с битым FK (охранная логика StatsOrchestrator/RunRecorder).
    /// </summary>
    [Theory]
    [InlineData(99, "normal",    1)]   // несуществующий акт
    [InlineData(1,  "heroic",    1)]   // несуществующая сложность
    [InlineData(1,  "normal",    99)]  // несуществующий номер этапа
    public void ResolveStageId_NonexistentStageRef_ReturnsNull(
        int actNumber, string difficultyKey, int stageNumber)
    {
        GameMechanicsConfig config = GameMechanicsConfig.CreateDefault();
        var stageRef = new StageRef(actNumber, difficultyKey, stageNumber);

        int? resolved = config.ResolveStageId(stageRef);

        resolved.Should().BeNull(
            $"StageRef({actNumber}, {difficultyKey}, {stageNumber}) не существует — резолвер должен вернуть null");
    }
}
