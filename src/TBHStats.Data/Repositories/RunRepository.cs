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
}
