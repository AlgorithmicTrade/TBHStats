namespace TBHStats.Data.Repositories;

using TBHStats.Core.Models;

/// <summary>
/// Скелет репозитория забегов и метрических сэмплов.
/// Полная реализация — в задаче T037.
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
    public Task AddRunAsync(StageRun run, CancellationToken ct)
        => throw new NotImplementedException("Реализуется в T037.");

    /// <inheritdoc />
    public Task<IReadOnlyList<StageRun>> GetRunsAsync(int stageId, CancellationToken ct)
        => throw new NotImplementedException("Реализуется в T037.");

    /// <inheritdoc />
    public Task<IReadOnlyList<MetricSample>> GetSamplesAsync(int stageId, DateRange range, CancellationToken ct)
        => throw new NotImplementedException("Реализуется в T037.");

    /// <inheritdoc />
    public Task AppendSampleAsync(MetricSample sample, CancellationToken ct)
        => throw new NotImplementedException("Реализуется в T037.");
}
