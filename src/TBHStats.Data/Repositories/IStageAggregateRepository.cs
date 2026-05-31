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
    /// <param name="stageId">Идентификатор этапа.</param>
    /// <param name="recentWindowSize">
    /// Размер свежего окна (число последних non-partial забегов для recency-aware полей).
    /// Соответствует <see cref="TBHStats.Core.Models.OptimizationProfile.RecentWindowSize"/>.
    /// </param>
    /// <param name="ct">Токен отмены.</param>
    Task RecomputeForStageAsync(int stageId, int recentWindowSize, CancellationToken ct);
}
