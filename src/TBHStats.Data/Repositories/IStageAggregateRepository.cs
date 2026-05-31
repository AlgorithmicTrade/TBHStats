namespace TBHStats.Data.Repositories;

using TBHStats.Core.Models;

/// <summary>
/// Контракт репозитория материализованных агрегатов этапов (FR-008).
/// </summary>
public interface IStageAggregateRepository
{
    /// <summary>
    /// Возвращает агрегаты всех этапов (экран сравнения, FR-008).
    /// </summary>
    Task<IReadOnlyList<StageAggregate>> GetAllAsync(CancellationToken ct);

    /// <summary>
    /// Пересчитывает агрегат указанного этапа по всем его <c>IsPartial = false</c> забегам (FR-008, FR-010).
    /// Вызывается после добавления нового <see cref="StageRun"/>.
    /// </summary>
    Task RecomputeForStageAsync(int stageId, CancellationToken ct);
}
