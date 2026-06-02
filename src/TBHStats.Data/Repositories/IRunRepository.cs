namespace TBHStats.Data.Repositories;

using TBHStats.Core.Models;

/// <summary>
/// Контракт репозитория забегов и метрических сэмплов (FR-007, FR-005a, US3).
/// </summary>
public interface IRunRepository
{
    /// <summary>
    /// Сохраняет завершённый (или прерванный) забег этапа (FR-007).
    /// </summary>
    Task AddRunAsync(StageRun run, CancellationToken ct);

    /// <summary>
    /// Возвращает все забеги указанного этапа (<paramref name="stageId"/> = <see cref="Stage.Id"/>).
    /// </summary>
    Task<IReadOnlyList<StageRun>> GetRunsAsync(int stageId, CancellationToken ct);

    /// <summary>
    /// Возвращает метрические сэмплы этапа в заданном диапазоне времени (US3).
    /// </summary>
    Task<IReadOnlyList<MetricSample>> GetSamplesAsync(int stageId, DateRange range, CancellationToken ct);

    /// <summary>
    /// Сохраняет метрический сэмпл. Принимает только записи с <see cref="MetricSample.IsReliable"/> == true (FR-005a).
    /// </summary>
    Task AppendSampleAsync(MetricSample sample, CancellationToken ct);

    /// <summary>
    /// Удаляет (прореживает) метрические сэмплы этапа, снятые СТРОГО раньше <paramref name="olderThanUtc"/> (US3, ретенция).
    /// Агрегаты (StageAggregate) НЕ затрагиваются. Возвращает число удалённых сэмплов.
    /// </summary>
    Task<int> PruneSamplesAsync(int stageId, DateTime olderThanUtc, CancellationToken ct);

    /// <summary>
    /// Удаляет забеги этапа <paramref name="stageId"/>, оставляя только
    /// <paramref name="keepLast"/> самых свежих (по <c>CompletedAtUtc DESC</c>, затем <c>Id DESC</c>).
    /// Возвращает число удалённых <see cref="StageRun"/>.
    /// Дочерние <see cref="StageRunChest"/> удаляются явно первым шагом
    /// (двухшаговый bulk-DELETE, аналогично <see cref="PruneSamplesAsync"/>),
    /// так как SQLite без <c>PRAGMA foreign_keys=ON</c> не каскадирует FK при <c>ExecuteDeleteAsync</c>.
    /// </summary>
    Task<int> PruneOldRunsAsync(int stageId, int keepLast, CancellationToken ct);
}
