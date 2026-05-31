namespace TBHStats.Data.Repositories;

using TBHStats.Core.Models;

/// <summary>
/// Скелет репозитория настроек приложения.
/// Полная реализация — в задаче T027.
/// </summary>
public sealed class SettingsRepository : ISettingsRepository
{
    private readonly TbhStatsDbContext _db;

    /// <param name="db">Контекст EF Core (внедряется через DI).</param>
    public SettingsRepository(TbhStatsDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public Task<WidgetSettings> GetWidgetSettingsAsync()
        => throw new NotImplementedException("Реализуется в T027.");

    /// <inheritdoc />
    public Task SaveWidgetSettingsAsync(WidgetSettings s)
        => throw new NotImplementedException("Реализуется в T027.");

    /// <inheritdoc />
    public Task<OptimizationProfile> GetOptimizationProfileAsync()
        => throw new NotImplementedException("Реализуется в T027.");

    /// <inheritdoc />
    public Task SaveOptimizationProfileAsync(OptimizationProfile p)
        => throw new NotImplementedException("Реализуется в T027.");

    /// <inheritdoc />
    public Task<IReadOnlyList<RoiCalibration>> GetRoiCalibrationsAsync()
        => throw new NotImplementedException("Реализуется в T027.");

    /// <inheritdoc />
    public Task SaveRoiCalibrationsAsync(IReadOnlyList<RoiCalibration> rois)
        => throw new NotImplementedException("Реализуется в T027.");
}
