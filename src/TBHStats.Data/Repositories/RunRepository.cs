namespace TBHStats.Data.Repositories;

using Microsoft.EntityFrameworkCore;
using TBHStats.Core.Models;

/// <summary>
/// Репозиторий забегов этапов и метрических сэмплов (FR-007, FR-005a, US3).
/// </summary>
public sealed class RunRepository : IRunRepository
{
    private readonly TbhStatsDbContext _db;

    /// <param name="db">Контекст EF Core (внедряется через DI).</param>
    public RunRepository(TbhStatsDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Сохраняет весь граф: owned HeroSnapshot и коллекцию Chests — одним SaveChanges.
    /// EF присваивает Id после вставки.
    /// </remarks>
    public async Task AddRunAsync(StageRun run, CancellationToken ct)
    {
        _db.StageRuns.Add(run);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Возвращает все забеги указанного этапа, включая коллекцию Chests (explicit Include).
    /// Owned HeroSnapshot загружается автоматически EF.
    /// Порядок: CompletedAtUtc ascending, затем Id ascending — детерминированный.
    /// </remarks>
    public async Task<IReadOnlyList<StageRun>> GetRunsAsync(int stageId, CancellationToken ct)
    {
        return await _db.StageRuns
            .AsNoTracking()
            .Where(r => r.StageId == stageId)
            .Include(r => r.Chests)
            .OrderBy(r => r.CompletedAtUtc)
            .ThenBy(r => r.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Выборка сэмплов этапа в диапазоне [FromUtc, ToUtc] включительно (DateRange).
    /// MetricSampleChest включается через Include для восстановления коллекции Chests.
    /// </remarks>
    public async Task<IReadOnlyList<MetricSample>> GetSamplesAsync(
        int stageId, DateRange range, CancellationToken ct)
    {
        return await _db.MetricSamples
            .AsNoTracking()
            .Where(s => s.StageId == stageId
                     && s.TakenAtUtc >= range.FromUtc
                     && s.TakenAtUtc <= range.ToUtc)
            .Include(s => s.Chests)
            .OrderBy(s => s.TakenAtUtc)
            .ThenBy(s => s.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Сохраняет сэмпл ТОЛЬКО если IsReliable == true (FR-005a).
    /// Ненадёжные сэмплы молча игнорируются (no-op).
    /// </remarks>
    public async Task AppendSampleAsync(MetricSample sample, CancellationToken ct)
    {
        if (!sample.IsReliable)
        {
            return;
        }

        _db.MetricSamples.Add(sample);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Удаляет метрические сэмплы этапа <paramref name="stageId"/> с <c>TakenAtUtc &lt; olderThanUtc</c>
    /// (строго раньше cutoff; сэмпл ровно на границе остаётся).
    /// Удаление двухшаговое:
    ///   1. Bulk-DELETE зависимых <see cref="MetricSampleChest"/> тех же сэмплов
    ///      (SQLite без PRAGMA foreign_keys=ON не каскадирует FK при ExecuteDeleteAsync).
    ///   2. Bulk-DELETE самих <see cref="MetricSample"/>.
    /// Возвращает число удалённых родительских строк (MetricSample).
    /// <see cref="StageAggregate"/> и <see cref="StageRun"/> не затрагиваются.
    /// </remarks>
    public async Task<int> PruneSamplesAsync(int stageId, DateTime olderThanUtc, CancellationToken ct)
    {
        // Шаг 1: удалить зависимые MetricSampleChest для затрагиваемых сэмплов.
        // ExecuteDeleteAsync работает с bulk-SQL и не загружает граф EF,
        // поэтому FK-каскад SQLite (PRAGMA foreign_keys) не срабатывает автоматически.
        // Удаляем детей явно перед родителями, чтобы избежать осиротевших строк.
        await _db.MetricSampleChests
            .Where(c => _db.MetricSamples
                .Where(s => s.StageId == stageId && s.TakenAtUtc < olderThanUtc)
                .Select(s => s.Id)
                .Contains(c.MetricSampleId))
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        // Шаг 2: удалить сами сэмплы и вернуть их количество.
        return await _db.MetricSamples
            .Where(s => s.StageId == stageId && s.TakenAtUtc < olderThanUtc)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Оставляет <paramref name="keepLast"/> самых свежих забегов этапа
    /// (сортировка: <c>CompletedAtUtc DESC</c>, затем <c>Id DESC</c> для детерминизма).
    /// Удаление двухшаговое:
    ///   1. Bulk-DELETE зависимых <see cref="StageRunChest"/> (FK <c>StageRunId</c>)
    ///      для удаляемых забегов — SQLite без PRAGMA foreign_keys не каскадирует FK.
    ///   2. Bulk-DELETE самих <see cref="StageRun"/>.
    /// Список Id удаляемых забегов материализуется через <c>ToListAsync</c> перед
    /// <c>ExecuteDeleteAsync</c>, чтобы избежать проблем трансляции вложенных NOT-IN
    /// подзапросов в SQLite при <c>ExecuteDeleteAsync</c> (EF Core / SQLite ограничение).
    /// Возвращает число удалённых родительских строк (<see cref="StageRun"/>).
    /// </remarks>
    public async Task<int> PruneOldRunsAsync(int stageId, int keepLast, CancellationToken ct)
    {
        // Определяем Id забегов, которые нужно ОСТАВИТЬ (keepLast самых свежих).
        IQueryable<long> idsToKeepQuery = _db.StageRuns
            .Where(r => r.StageId == stageId)
            .OrderByDescending(r => r.CompletedAtUtc)
            .ThenByDescending(r => r.Id)
            .Take(keepLast)
            .Select(r => r.Id);

        // Материализуем список Id для удаления, чтобы NOT-IN над подзапросом
        // гарантированно транслировался в SQLite без ошибок трансляции EF Core.
        List<long> idsToKeep = await idsToKeepQuery
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Если забегов keepLast или меньше — нечего удалять.
        if (idsToKeep.Count < keepLast)
        {
            return 0;
        }

        List<long> idsToDelete = await _db.StageRuns
            .Where(r => r.StageId == stageId && !idsToKeep.Contains(r.Id))
            .Select(r => r.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (idsToDelete.Count == 0)
        {
            return 0;
        }

        // Шаг 1: удалить дочерние StageRunChest для удаляемых забегов.
        // ExecuteDeleteAsync работает с bulk-SQL и не загружает граф EF,
        // поэтому FK-каскад SQLite не срабатывает — удаляем детей явно.
        await _db.StageRunChests
            .Where(c => idsToDelete.Contains(c.StageRunId))
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        // Шаг 2: удалить сами забеги и вернуть их количество.
        return await _db.StageRuns
            .Where(r => idsToDelete.Contains(r.Id))
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }
}
