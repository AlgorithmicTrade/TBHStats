namespace TBHStats.Data.Repositories;

using TBHStats.Core.Models;

/// <summary>
/// Скелет репозитория агрегатов этапов.
/// Полная реализация — в задаче T038.
/// </summary>
public sealed class StageAggregateRepository : IStageAggregateRepository
{
    private readonly TbhStatsDbContext _db;

    /// <param name="db">Контекст EF Core (внедряется через DI).</param>
    public StageAggregateRepository(TbhStatsDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StageAggregate>> GetAllAsync(CancellationToken ct)
        => throw new NotImplementedException("Реализуется в T038.");

    /// <inheritdoc />
    public Task RecomputeForStageAsync(int stageId, CancellationToken ct)
        => throw new NotImplementedException("Реализуется в T038.");
}
