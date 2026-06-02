namespace TBHStats.Data.Tests;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TBHStats.Core.Models;
using TBHStats.Data.Repositories;
using Xunit;

/// <summary>
/// Интеграционные тесты <see cref="IRunRepository.PruneOldRunsAsync"/> на реальном
/// временном файловом SQLite. Никаких in-memory провайдеров, никаких моков.
///
/// Паттерн создания контекста переиспользован из <see cref="RunRecordingFixture"/>
/// (тот же базовый класс). Каждый тест-класс получает уникальный временный файл БД
/// с реальными миграциями и сидингом справочников.
/// </summary>
public sealed class RunRepositoryPruneTests : RunRecordingFixture
{
    // ──────────────────────────────────────────────────────────────────────────
    // Вспомогательные методы
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Добавляет забег на указанный этап через фикстурный <see cref="RunRecordingFixture.Db"/>
    /// и возвращает его Id.
    /// </summary>
    private async Task<long> AddRunDirectAsync(
        int stageId,
        int heroClassId,
        DateTime completedAt,
        bool isPartial = false,
        IList<StageRunChest>? chests = null)
    {
        var run = new StageRun
        {
            StageId         = stageId,
            DurationSeconds = 300,
            GoldGained      = 5_000,
            XpGained        = 1_000,
            Hero            = new HeroSnapshot(heroClassId, Level: 20, Damage: 10_000),
            CompletedAtUtc  = completedAt,
            IsPartial       = isPartial,
            Chests          = chests ?? new List<StageRunChest>(),
        };
        Db.StageRuns.Add(run);
        await Db.SaveChangesAsync();
        return run.Id;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Тест 1: keepLast больше числа забегов → ничего не удаляется (возвращает 0)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Если забегов меньше keepLast, PruneOldRunsAsync должен вернуть 0
    /// и оставить все забеги нетронутыми.
    /// </summary>
    [Fact]
    public async Task PruneOldRunsAsync_WhenFewerThanKeepLast_DeletesNothing()
    {
        // Arrange
        var stage       = await Db.Stages.FirstAsync();
        int heroClassId = await SeedHeroClassAsync(Db, "warrior_prune1");

        var baseTime = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        long id1 = await AddRunDirectAsync(stage.Id, heroClassId, baseTime);
        long id2 = await AddRunDirectAsync(stage.Id, heroClassId, baseTime.AddMinutes(1));
        long id3 = await AddRunDirectAsync(stage.Id, heroClassId, baseTime.AddMinutes(2));

        var (ctx, repo) = CreateRunRepository();
        await using (ctx)
        {
            // Act
            int deleted = await repo.PruneOldRunsAsync(stage.Id, keepLast: 10, CancellationToken.None);

            // Assert: ничего не удалено
            deleted.Should().Be(0, "забегов меньше keepLast=10, удалять нечего");

            // Проверяем: все 3 забега остались в БД
            var remaining = await ctx.StageRuns
                .Where(r => r.StageId == stage.Id)
                .ToListAsync();
            remaining.Should().HaveCount(3, "все 3 забега должны остаться нетронутыми");
            remaining.Select(r => r.Id).Should().Contain(new[] { id1, id2, id3 });
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Тест 2: забегов больше keepLast → удалены самые старые, остались keepLast свежих
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// При 13 забегах и keepLast=10 должны быть удалены 3 самых старых (по CompletedAtUtc ASC)
    /// и остаться 10 самых свежих.
    /// </summary>
    [Fact]
    public async Task PruneOldRunsAsync_WhenMoreThanKeepLast_KeepsOnlyLatest()
    {
        // Arrange
        var stage       = await Db.Stages.FirstAsync();
        int heroClassId = await SeedHeroClassAsync(Db, "warrior_prune2");

        var baseTime = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        // 13 забегов с нарастающими CompletedAtUtc (minutes 0..12)
        var ids = new List<long>();
        for (int i = 0; i < 13; i++)
        {
            long id = await AddRunDirectAsync(stage.Id, heroClassId, baseTime.AddMinutes(i));
            ids.Add(id);
        }

        // ids[0] — самый старый (minute=0), ids[12] — самый свежий (minute=12)

        var (ctx, repo) = CreateRunRepository();
        await using (ctx)
        {
            // Act
            int deleted = await repo.PruneOldRunsAsync(stage.Id, keepLast: 10, CancellationToken.None);

            // Assert: удалено ровно 3
            deleted.Should().Be(3, "13 - 10 = 3 забега должны быть удалены");

            var remaining = await ctx.StageRuns
                .Where(r => r.StageId == stage.Id)
                .OrderBy(r => r.CompletedAtUtc)
                .ToListAsync();

            // Должны остаться ровно 10
            remaining.Should().HaveCount(10, "должно остаться ровно 10 самых свежих забегов");

            // Самый старый из оставшихся — ids[3] (minute=3), самый свежий — ids[12] (minute=12)
            remaining.Min(r => r.CompletedAtUtc).Should().Be(baseTime.AddMinutes(3),
                "3 самых старых (minute=0,1,2) должны быть удалены");
            remaining.Max(r => r.CompletedAtUtc).Should().Be(baseTime.AddMinutes(12),
                "самый свежий должен остаться");

            // Удалённые ids[0], ids[1], ids[2] — не должны присутствовать в БД
            remaining.Select(r => r.Id).Should().NotContain(ids[0],
                "ids[0] (oldest) должен быть удалён");
            remaining.Select(r => r.Id).Should().NotContain(ids[1],
                "ids[1] должен быть удалён");
            remaining.Select(r => r.Id).Should().NotContain(ids[2],
                "ids[2] должен быть удалён");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Тест 3: дочерние StageRunChest удаляемых забегов тоже удаляются (bulk cascade)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// У удаляемых забегов есть StageRunChest. После прунинга в таблице StageRunChests
    /// не должно остаться строк с StageRunId удалённых забегов.
    /// </summary>
    [Fact]
    public async Task PruneOldRunsAsync_DeletesChildChests()
    {
        // Arrange
        var stage       = await Db.Stages.FirstAsync();
        int heroClassId = await SeedHeroClassAsync(Db, "mage_prune3");
        var chestTypes  = await Db.ChestTypes.OrderBy(c => c.SortOrder).ToListAsync();
        chestTypes.Should().HaveCountGreaterOrEqualTo(1, "нужен хотя бы один тип сундука из сидинга");

        var baseTime = new DateTime(2026, 6, 2, 8, 0, 0, DateTimeKind.Utc);

        // Добавляем 3 забега, у каждого — StageRunChest
        var idsWithChests = new List<long>();
        for (int i = 0; i < 3; i++)
        {
            long id = await AddRunDirectAsync(
                stage.Id,
                heroClassId,
                baseTime.AddMinutes(i),
                chests: [new StageRunChest { ChestTypeId = chestTypes[0].Id, Count = i + 1 }]);
            idsWithChests.Add(id);
        }

        // Добавляем ещё 10 забегов без сундуков (свежие) — они останутся после прунинга
        for (int i = 3; i < 13; i++)
        {
            await AddRunDirectAsync(stage.Id, heroClassId, baseTime.AddMinutes(i));
        }

        // keepLast=10 → первые 3 (idsWithChests) должны быть удалены вместе с их Chests

        var (ctx, repo) = CreateRunRepository();
        await using (ctx)
        {
            // Act
            int deleted = await repo.PruneOldRunsAsync(stage.Id, keepLast: 10, CancellationToken.None);

            // Assert: удалено 3 StageRun
            deleted.Should().Be(3);

            // Проверяем, что осиротевших StageRunChest не осталось
            var orphanChests = await ctx.StageRunChests
                .Where(c => idsWithChests.Contains(c.StageRunId))
                .ToListAsync();

            orphanChests.Should().BeEmpty(
                "StageRunChest удалённых забегов должны быть удалены в двухшаговом bulk-DELETE");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Тест 4: прунинг одного этапа не трогает забеги другого этапа
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Забеги на двух разных этапах: прунинг по stageA не должен затрагивать записи stageB.
    /// </summary>
    [Fact]
    public async Task PruneOldRunsAsync_DoesNotAffectOtherStages()
    {
        // Arrange: берём два разных этапа
        var stages = await Db.Stages.Take(2).ToListAsync();
        stages.Should().HaveCount(2, "нужно минимум 2 этапа для теста изоляции");

        int heroClassId = await SeedHeroClassAsync(Db, "paladin_prune4");

        var baseTime = new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc);

        // stageA: добавляем 12 забегов (3 будут удалены при keepLast=9)
        for (int i = 0; i < 12; i++)
        {
            await AddRunDirectAsync(stages[0].Id, heroClassId, baseTime.AddMinutes(i));
        }

        // stageB: добавляем 5 забегов — они не должны быть затронуты
        var stageBIds = new List<long>();
        for (int i = 0; i < 5; i++)
        {
            long id = await AddRunDirectAsync(stages[1].Id, heroClassId, baseTime.AddMinutes(i));
            stageBIds.Add(id);
        }

        var (ctx, repo) = CreateRunRepository();
        await using (ctx)
        {
            // Act: прунинг только stageA
            int deleted = await repo.PruneOldRunsAsync(stages[0].Id, keepLast: 9, CancellationToken.None);

            // Assert: удалено ровно 3 из stageA
            deleted.Should().Be(3);

            // stageA: осталось 9
            int stageACount = await ctx.StageRuns
                .CountAsync(r => r.StageId == stages[0].Id);
            stageACount.Should().Be(9, "после прунинга на stageA должно остаться 9 забегов");

            // stageB: все 5 нетронуты
            int stageBCount = await ctx.StageRuns
                .CountAsync(r => r.StageId == stages[1].Id);
            stageBCount.Should().Be(5, "прунинг stageA не должен трогать забеги stageB");

            // Дополнительно: конкретные Id stageB должны присутствовать
            var stageBRemaining = await ctx.StageRuns
                .Where(r => r.StageId == stages[1].Id)
                .Select(r => r.Id)
                .ToListAsync();
            stageBRemaining.Should().Contain(stageBIds,
                "все Id забегов stageB должны оставаться нетронутыми");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Тест 5 (тай-брейк): равный CompletedAtUtc → детерминизм по Id DESC
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// При двух забегах с одинаковым CompletedAtUtc и keepLast=1
    /// должен остаться тот, у которого Id больше (ThenByDescending Id — детерминизм).
    /// </summary>
    [Fact]
    public async Task PruneOldRunsAsync_TieBreakById_KeepsHigherIdWhenTimestampEqual()
    {
        // Arrange
        var stage       = await Db.Stages.FirstAsync();
        int heroClassId = await SeedHeroClassAsync(Db, "necro_prune5");

        // Один и тот же CompletedAtUtc для обоих забегов
        var sameTime = new DateTime(2026, 6, 3, 9, 0, 0, DateTimeKind.Utc);

        long id1 = await AddRunDirectAsync(stage.Id, heroClassId, sameTime);
        long id2 = await AddRunDirectAsync(stage.Id, heroClassId, sameTime);

        // id2 > id1 (AUTOINCREMENT), поэтому при ThenByDescending(Id) → id2 в TopN
        id2.Should().BeGreaterThan(id1, "id2 должен быть присвоен позже — выше по AUTOINCREMENT");

        var (ctx, repo) = CreateRunRepository();
        await using (ctx)
        {
            // Act: keepLast=1 → остаётся 1 из 2
            int deleted = await repo.PruneOldRunsAsync(stage.Id, keepLast: 1, CancellationToken.None);

            // Assert: удалён 1
            deleted.Should().Be(1);

            var remaining = await ctx.StageRuns
                .Where(r => r.StageId == stage.Id)
                .ToListAsync();

            remaining.Should().HaveCount(1, "должен остаться ровно 1 забег");
            remaining[0].Id.Should().Be(id2,
                "при равном CompletedAtUtc тай-брейк Id DESC → остаётся забег с большим Id");
        }
    }
}
