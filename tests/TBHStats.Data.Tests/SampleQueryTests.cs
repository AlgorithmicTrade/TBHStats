namespace TBHStats.Data.Tests;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TBHStats.Core.Models;
using TBHStats.Data.Repositories;
using Xunit;

/// <summary>
/// Интеграционные тесты выборки и ретенции метрических сэмплов (T043, US3).
/// Реальный временный файловый SQLite — никаких in-memory провайдеров, никаких моков.
///
/// GREEN (T037 реализован): все тесты GetSamplesAsync.
/// TDD RED (T044 ещё не реализован): все тесты PruneSamplesAsync падают с NotImplementedException.
/// </summary>

// ─────────────────────────────────────────────────────────────────────────────
// Фикстура: уникальный temp-SQLite для SampleQueryTests
// Отдельный класс (отдельная temp-папка), не трогает RunRecordingFixture/TempDbFixture.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Временный файловый SQLite с реальными миграциями для тестов выборки/ретенции сэмплов.
/// </summary>
public abstract class SampleTestsFixture : IAsyncLifetime
{
    protected string DbPath { get; }
    protected TbhStatsDbContext Db { get; private set; } = null!;

    protected SampleTestsFixture()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tbhstats-sample-tests");
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
    /// Создаёт новый независимый контекст и репозиторий (симуляция «перезапуска»).
    /// Вызывающий обязан вызвать DisposeAsync на возвращённом контексте.
    /// </summary>
    protected (TbhStatsDbContext ctx, IRunRepository repo) CreateRunRepository()
    {
        var ctx = CreateContext();
        return (ctx, new RunRepository(ctx));
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
// GetSamplesAsync — выборка для трендов (GREEN, T037 реализован)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Тесты <see cref="IRunRepository.GetSamplesAsync"/> на реальном SQLite.
/// Все тесты этого класса GREEN после T037.
/// </summary>
public sealed class GetSamplesTests : SampleTestsFixture
{
    // ── 1. Сэмплы в диапазоне возвращаются, отсортированы по TakenAtUtc ASC, затем Id ASC ──

    [Fact]
    public async Task GetSamplesAsync_SamplesInRange_ReturnedSortedByTakenAtUtcThenId()
    {
        // Arrange
        var stage = await Db.Stages.FirstAsync();
        var t0    = new DateTime(2026, 5, 31, 10, 0, 0, DateTimeKind.Utc);

        var (ctx, repo) = CreateRunRepository();
        await using (ctx)
        {
            // Три сэмпла с разным временем — добавляем в обратном порядке чтобы проверить сортировку
            await repo.AppendSampleAsync(new MetricSample
            {
                StageId    = stage.Id,
                TakenAtUtc = t0.AddMinutes(20),
                Gold       = 3_000,
                IsReliable = true,
            }, CancellationToken.None);

            await repo.AppendSampleAsync(new MetricSample
            {
                StageId    = stage.Id,
                TakenAtUtc = t0.AddMinutes(5),
                Gold       = 1_000,
                IsReliable = true,
            }, CancellationToken.None);

            await repo.AppendSampleAsync(new MetricSample
            {
                StageId    = stage.Id,
                TakenAtUtc = t0.AddMinutes(10),
                Gold       = 2_000,
                IsReliable = true,
            }, CancellationToken.None);

            var range  = new DateRange(t0, t0.AddHours(1));
            var result = await repo.GetSamplesAsync(stage.Id, range, CancellationToken.None);

            // Assert: 3 сэмпла, отсортированы по TakenAtUtc
            result.Should().HaveCount(3);
            result[0].TakenAtUtc.Should().Be(t0.AddMinutes(5),  "первый — самый ранний");
            result[1].TakenAtUtc.Should().Be(t0.AddMinutes(10), "второй — средний");
            result[2].TakenAtUtc.Should().Be(t0.AddMinutes(20), "третий — поздний");

            // Золото совпадает
            result[0].Gold.Should().Be(1_000);
            result[1].Gold.Should().Be(2_000);
            result[2].Gold.Should().Be(3_000);
        }
    }

    // ── 2. Сэмплы ВНЕ диапазона не возвращаются; границы включительны ─────────

    [Fact]
    public async Task GetSamplesAsync_BoundaryInclusive_ExcludesOutOfRangeSamples()
    {
        // Arrange
        var stage = await Db.Stages.FirstAsync();
        var from  = new DateTime(2026, 5, 31, 12, 0, 0, DateTimeKind.Utc);
        var to    = new DateTime(2026, 5, 31, 14, 0, 0, DateTimeKind.Utc);

        var (ctx, repo) = CreateRunRepository();
        await using (ctx)
        {
            // Сэмпл строго раньше FromUtc — должен быть исключён
            await repo.AppendSampleAsync(new MetricSample
            {
                StageId    = stage.Id,
                TakenAtUtc = from.AddSeconds(-1),
                Gold       = 100,
                IsReliable = true,
            }, CancellationToken.None);

            // Сэмпл ровно на FromUtc — граница включительно, должен войти
            await repo.AppendSampleAsync(new MetricSample
            {
                StageId    = stage.Id,
                TakenAtUtc = from,
                Gold       = 200,
                IsReliable = true,
            }, CancellationToken.None);

            // Сэмпл внутри диапазона
            await repo.AppendSampleAsync(new MetricSample
            {
                StageId    = stage.Id,
                TakenAtUtc = from.AddHours(1),
                Gold       = 300,
                IsReliable = true,
            }, CancellationToken.None);

            // Сэмпл ровно на ToUtc — граница включительно, должен войти
            await repo.AppendSampleAsync(new MetricSample
            {
                StageId    = stage.Id,
                TakenAtUtc = to,
                Gold       = 400,
                IsReliable = true,
            }, CancellationToken.None);

            // Сэмпл строго позже ToUtc — должен быть исключён
            await repo.AppendSampleAsync(new MetricSample
            {
                StageId    = stage.Id,
                TakenAtUtc = to.AddSeconds(1),
                Gold       = 500,
                IsReliable = true,
            }, CancellationToken.None);

            var range  = new DateRange(from, to);
            var result = await repo.GetSamplesAsync(stage.Id, range, CancellationToken.None);

            // Assert: только 3 сэмпла (from, middle, to) — граничные включены, внешние исключены
            result.Should().HaveCount(3);
            result.Select(s => s.Gold).Should().BeEquivalentTo(new long?[] { 200, 300, 400 },
                opts => opts.WithStrictOrdering());
        }
    }

    // ── 3. Фильтрация по stageId — сэмплы другого этапа не попадают ──────────

    [Fact]
    public async Task GetSamplesAsync_FiltersByStageId_OtherStageSamplesExcluded()
    {
        // Arrange
        var stages = await Db.Stages.Take(2).ToListAsync();
        stages.Should().HaveCount(2, "нужно минимум 2 этапа");

        var t0 = new DateTime(2026, 5, 31, 15, 0, 0, DateTimeKind.Utc);

        var (ctx, repo) = CreateRunRepository();
        await using (ctx)
        {
            // Сэмплы для этапа 0
            await repo.AppendSampleAsync(new MetricSample
            {
                StageId    = stages[0].Id,
                TakenAtUtc = t0,
                Gold       = 1_000,
                IsReliable = true,
            }, CancellationToken.None);

            // Сэмплы для этапа 1
            await repo.AppendSampleAsync(new MetricSample
            {
                StageId    = stages[1].Id,
                TakenAtUtc = t0.AddMinutes(1),
                Gold       = 9_999,
                IsReliable = true,
            }, CancellationToken.None);

            var range = new DateRange(t0.AddHours(-1), t0.AddHours(1));

            var result0 = await repo.GetSamplesAsync(stages[0].Id, range, CancellationToken.None);
            var result1 = await repo.GetSamplesAsync(stages[1].Id, range, CancellationToken.None);

            // Assert
            result0.Should().HaveCount(1);
            result0.Should().OnlyContain(s => s.StageId == stages[0].Id);
            result0[0].Gold.Should().Be(1_000);

            result1.Should().HaveCount(1);
            result1.Should().OnlyContain(s => s.StageId == stages[1].Id);
            result1[0].Gold.Should().Be(9_999);
        }
    }

    // ── 4. Этап без сэмплов → пустой список (не null) ─────────────────────────

    [Fact]
    public async Task GetSamplesAsync_StageWithNoSamples_ReturnsEmptyList()
    {
        // Arrange
        var stage = await Db.Stages.FirstAsync();
        var range = new DateRange(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));

        var (ctx, repo) = CreateRunRepository();
        await using (ctx)
        {
            // Act — без каких-либо AppendSampleAsync
            var result = await repo.GetSamplesAsync(stage.Id, range, CancellationToken.None);

            // Assert
            result.Should().NotBeNull("GetSamplesAsync не должен возвращать null");
            result.Should().BeEmpty("для этапа без сэмплов список должен быть пустым");
        }
    }

    // ── 5. Сэмпл с сундуками: коллекция Chests восстанавливается через Include ─

    [Fact]
    public async Task GetSamplesAsync_SampleWithChests_ChestCollectionRestored()
    {
        // Arrange
        var stage      = await Db.Stages.FirstAsync();
        var chestTypes = await Db.ChestTypes.OrderBy(c => c.SortOrder).ToListAsync();
        chestTypes.Should().HaveCount(3, "сидинг создаёт 3 типа сундуков");

        var t0 = new DateTime(2026, 5, 31, 16, 0, 0, DateTimeKind.Utc);

        // Добавляем сэмпл с сундуками напрямую через контекст (AppendSampleAsync добавляет сэмпл целиком)
        var sample = new MetricSample
        {
            StageId    = stage.Id,
            TakenAtUtc = t0,
            Gold       = 5_000,
            IsReliable = true,
            Chests     =
            [
                new MetricSampleChest { ChestTypeId = chestTypes[0].Id, Count = 4 },
                new MetricSampleChest { ChestTypeId = chestTypes[1].Id, Count = 2 },
                new MetricSampleChest { ChestTypeId = chestTypes[2].Id, Count = 0 },
            ],
        };
        Db.MetricSamples.Add(sample);
        await Db.SaveChangesAsync();

        var (ctx, repo) = CreateRunRepository();
        await using (ctx)
        {
            var range  = new DateRange(t0.AddMinutes(-1), t0.AddMinutes(1));
            var result = await repo.GetSamplesAsync(stage.Id, range, CancellationToken.None);

            // Assert
            result.Should().HaveCount(1);
            var loaded = result[0];
            loaded.Chests.Should().HaveCount(3, "все три типа сундуков восстановлены через Include");
            loaded.Chests.Single(c => c.ChestTypeId == chestTypes[0].Id).Count.Should().Be(4);
            loaded.Chests.Single(c => c.ChestTypeId == chestTypes[1].Id).Count.Should().Be(2);
            loaded.Chests.Single(c => c.ChestTypeId == chestTypes[2].Id).Count.Should().Be(0);
        }
    }

    // ── 6. Переживание перезапуска: сэмплы на диске после закрытия контекста ──

    [Fact]
    public async Task GetSamplesAsync_AfterContextRestart_SamplesPersistedOnDisk()
    {
        // Arrange
        var stage = await Db.Stages.FirstAsync();
        var t0    = new DateTime(2026, 5, 31, 17, 0, 0, DateTimeKind.Utc);

        var (ctx1, repo1) = CreateRunRepository();
        await using (ctx1)
        {
            await repo1.AppendSampleAsync(new MetricSample
            {
                StageId    = stage.Id,
                TakenAtUtc = t0,
                Gold       = 12_000,
                Xp         = 800,
                IsReliable = true,
            }, CancellationToken.None);

            await repo1.AppendSampleAsync(new MetricSample
            {
                StageId    = stage.Id,
                TakenAtUtc = t0.AddMinutes(5),
                Gold       = 13_500,
                Xp         = 900,
                IsReliable = true,
            }, CancellationToken.None);
        }
        // ctx1 закрыт — данные должны быть на диске

        // Act — открыть НОВЫЙ контекст (симуляция перезапуска приложения)
        var (ctx2, repo2) = CreateRunRepository();
        await using (ctx2)
        {
            var range  = new DateRange(t0.AddMinutes(-1), t0.AddMinutes(10));
            var result = await repo2.GetSamplesAsync(stage.Id, range, CancellationToken.None);

            // Assert
            result.Should().HaveCount(2, "сэмплы должны пережить закрытие контекста (SC-004)");
            result[0].Gold.Should().Be(12_000);
            result[1].Gold.Should().Be(13_500);
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PruneSamplesAsync — ретенция (TDD RED: NotImplementedException до T044)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Тесты <see cref="IRunRepository.PruneSamplesAsync"/> на реальном SQLite.
/// TDD RED: все тесты этого класса ожидаемо падают с <see cref="NotImplementedException"/>
/// «Реализуется в T044.» — до написания реализации в T044.
/// </summary>
public sealed class PruneSamplesTests : SampleTestsFixture
{
    // ── 1. Удаляет только сэмплы строго раньше cutoff; более новые остаются ───

    [Fact]
    public async Task PruneSamplesAsync_RemovesOlderSamples_NewerSamplesRemain()
    {
        // Arrange
        var stage  = await Db.Stages.FirstAsync();
        var cutoff = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        // Два «старых» сэмпла (строго раньше cutoff)
        Db.MetricSamples.Add(new MetricSample
        {
            StageId    = stage.Id,
            TakenAtUtc = cutoff.AddHours(-2),
            Gold       = 100,
            IsReliable = true,
        });
        Db.MetricSamples.Add(new MetricSample
        {
            StageId    = stage.Id,
            TakenAtUtc = cutoff.AddMinutes(-1),
            Gold       = 200,
            IsReliable = true,
        });
        // Сэмпл ровно на cutoff — НЕ должен быть удалён (строгое <, а не <=)
        Db.MetricSamples.Add(new MetricSample
        {
            StageId    = stage.Id,
            TakenAtUtc = cutoff,
            Gold       = 300,
            IsReliable = true,
        });
        // Новый сэмпл после cutoff — НЕ должен быть удалён
        Db.MetricSamples.Add(new MetricSample
        {
            StageId    = stage.Id,
            TakenAtUtc = cutoff.AddHours(1),
            Gold       = 400,
            IsReliable = true,
        });
        await Db.SaveChangesAsync();

        var (ctx, repo) = CreateRunRepository();
        await using (ctx)
        {
            // Act — TDD RED: ожидаем NotImplementedException до T044
            int deleted = await repo.PruneSamplesAsync(stage.Id, cutoff, CancellationToken.None);

            // Assert (выполнятся только после реализации T044)
            deleted.Should().Be(2, "удалены только 2 сэмпла строго раньше cutoff");

            var range     = new DateRange(DateTime.MinValue.ToUniversalTime(), DateTime.MaxValue.ToUniversalTime());
            var remaining = await repo.GetSamplesAsync(stage.Id, range, CancellationToken.None);

            remaining.Should().HaveCount(2, "остаются сэмпл на cutoff и сэмпл после cutoff");
            remaining.Select(s => s.Gold).Should().BeEquivalentTo(new long?[] { 300, 400 },
                opts => opts.WithStrictOrdering());
        }
    }

    // ── 2. Не трогает сэмплы других этапов ────────────────────────────────────

    [Fact]
    public async Task PruneSamplesAsync_DoesNotAffectOtherStages()
    {
        // Arrange
        var stages = await Db.Stages.Take(2).ToListAsync();
        stages.Should().HaveCount(2, "нужно минимум 2 этапа");

        var cutoff = new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc);

        // Старые сэмплы для этапа 0 — должны быть удалены
        Db.MetricSamples.Add(new MetricSample
        {
            StageId    = stages[0].Id,
            TakenAtUtc = cutoff.AddHours(-1),
            Gold       = 111,
            IsReliable = true,
        });
        // Старые сэмплы для этапа 1 — НЕ должны быть затронуты (другой этап)
        Db.MetricSamples.Add(new MetricSample
        {
            StageId    = stages[1].Id,
            TakenAtUtc = cutoff.AddHours(-1),
            Gold       = 222,
            IsReliable = true,
        });
        await Db.SaveChangesAsync();

        var (ctx, repo) = CreateRunRepository();
        await using (ctx)
        {
            // Act — TDD RED: ожидаем NotImplementedException до T044
            int deleted = await repo.PruneSamplesAsync(stages[0].Id, cutoff, CancellationToken.None);

            // Assert
            deleted.Should().Be(1, "удалён только 1 сэмпл этапа 0");

            var range         = new DateRange(DateTime.MinValue.ToUniversalTime(), DateTime.MaxValue.ToUniversalTime());
            var stage0Samples = await repo.GetSamplesAsync(stages[0].Id, range, CancellationToken.None);
            var stage1Samples = await repo.GetSamplesAsync(stages[1].Id, range, CancellationToken.None);

            stage0Samples.Should().BeEmpty("все старые сэмплы этапа 0 удалены");
            stage1Samples.Should().HaveCount(1, "сэмплы этапа 1 не тронуты");
            stage1Samples[0].Gold.Should().Be(222);
        }
    }

    // ── 3. Идемпотентность: повторный вызов на уже прореженных → 0 удалённых ──

    [Fact]
    public async Task PruneSamplesAsync_CalledTwice_SecondCallDeletesZero()
    {
        // Arrange
        var stage  = await Db.Stages.FirstAsync();
        var cutoff = new DateTime(2026, 6, 3, 9, 0, 0, DateTimeKind.Utc);

        // Один старый сэмпл
        Db.MetricSamples.Add(new MetricSample
        {
            StageId    = stage.Id,
            TakenAtUtc = cutoff.AddHours(-3),
            Gold       = 555,
            IsReliable = true,
        });
        // Один новый сэмпл (останется после первого Prune)
        Db.MetricSamples.Add(new MetricSample
        {
            StageId    = stage.Id,
            TakenAtUtc = cutoff.AddHours(1),
            Gold       = 666,
            IsReliable = true,
        });
        await Db.SaveChangesAsync();

        var (ctx, repo) = CreateRunRepository();
        await using (ctx)
        {
            // Act — TDD RED: ожидаем NotImplementedException до T044
            int first  = await repo.PruneSamplesAsync(stage.Id, cutoff, CancellationToken.None);
            int second = await repo.PruneSamplesAsync(stage.Id, cutoff, CancellationToken.None);

            // Assert
            first.Should().Be(1,  "первый вызов удаляет 1 старый сэмпл");
            second.Should().Be(0, "второй вызов идемпотентен — нечего удалять");
        }
    }

    // ── 4. Prune не затрагивает StageRun/StageAggregate того же этапа ─────────

    [Fact]
    public async Task PruneSamplesAsync_DoesNotDeleteRunsOrAggregates()
    {
        // Arrange: создаём StageRun для этапа, добавляем старые сэмплы, запускаем Prune.
        // StageRun и StageAggregate должны остаться нетронутыми.
        var stage  = await Db.Stages.FirstAsync();
        var cutoff = new DateTime(2026, 6, 4, 8, 0, 0, DateTimeKind.Utc);

        // Добавляем HeroClass и StageRun через репозиторий
        var hc = new HeroClass { Key = "tester", DisplayName = "Tester", IsActive = true };
        Db.HeroClasses.Add(hc);
        await Db.SaveChangesAsync();

        var (ctx1, repo1) = CreateRunRepository();
        await using (ctx1)
        {
            await repo1.AddRunAsync(new StageRun
            {
                StageId         = stage.Id,
                DurationSeconds = 3_600,
                GoldGained      = 10_000,
                XpGained        = 5_000,
                Hero            = new HeroSnapshot(hc.Id, 50, 200_000),
                CompletedAtUtc  = new DateTime(2026, 6, 4, 7, 0, 0, DateTimeKind.Utc),
                IsPartial       = false,
            }, CancellationToken.None);
        }

        // Старый сэмпл для этого же этапа (строго раньше cutoff)
        Db.MetricSamples.Add(new MetricSample
        {
            StageId    = stage.Id,
            TakenAtUtc = cutoff.AddHours(-1),
            Gold       = 9_000,
            IsReliable = true,
        });
        await Db.SaveChangesAsync();

        var (ctx, repo) = CreateRunRepository();
        await using (ctx)
        {
            // Act — TDD RED: ожидаем NotImplementedException до T044
            int deleted = await repo.PruneSamplesAsync(stage.Id, cutoff, CancellationToken.None);

            // Assert: сэмпл удалён
            deleted.Should().Be(1);

            // StageRun этапа на месте (Prune не удалял StageRun)
            var runs = await repo.GetRunsAsync(stage.Id, CancellationToken.None);
            runs.Should().HaveCount(1, "StageRun не должен быть затронут Prune (data-model: агрегаты сохраняются)");
            runs[0].GoldGained.Should().Be(10_000);
        }
    }

    // ── 5. PruneSamplesAsync удаляет ненадёжные (IsReliable=false) сэмплы
    //       (они добавляются напрямую через DbContext, минуя AppendSampleAsync) ──

    [Fact]
    public async Task PruneSamplesAsync_DeletesUnreliableSamples_AddedDirectlyViaDbContext()
    {
        // Arrange
        var stage  = await Db.Stages.FirstAsync();
        var cutoff = new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc);

        // Ненадёжный сэмпл добавлен напрямую (AppendSampleAsync его отвергнет)
        Db.MetricSamples.Add(new MetricSample
        {
            StageId    = stage.Id,
            TakenAtUtc = cutoff.AddHours(-1),
            Gold       = 777,
            IsReliable = false,  // ненадёжный
        });
        // Надёжный сэмпл (старый) — тоже должен быть удалён
        Db.MetricSamples.Add(new MetricSample
        {
            StageId    = stage.Id,
            TakenAtUtc = cutoff.AddMinutes(-30),
            Gold       = 888,
            IsReliable = true,
        });
        // Новый сэмпл (должен остаться)
        Db.MetricSamples.Add(new MetricSample
        {
            StageId    = stage.Id,
            TakenAtUtc = cutoff.AddHours(1),
            Gold       = 999,
            IsReliable = true,
        });
        await Db.SaveChangesAsync();

        var (ctx, repo) = CreateRunRepository();
        await using (ctx)
        {
            // Act — TDD RED: ожидаем NotImplementedException до T044
            int deleted = await repo.PruneSamplesAsync(stage.Id, cutoff, CancellationToken.None);

            // Assert: оба старых сэмпла удалены (надёжный и ненадёжный)
            deleted.Should().Be(2, "Prune удаляет все сэмплы старше cutoff независимо от IsReliable");

            // Остался только новый
            var range     = new DateRange(DateTime.MinValue.ToUniversalTime(), DateTime.MaxValue.ToUniversalTime());
            var remaining = await repo.GetSamplesAsync(stage.Id, range, CancellationToken.None);
            remaining.Should().HaveCount(1);
            remaining[0].Gold.Should().Be(999);
        }
    }
}
