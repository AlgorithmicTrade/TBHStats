namespace TBHStats.Data.Repositories;

using TBHStats.Core.Models;

/// <summary>
/// Контракт репозитория настроек приложения: виджет, профиль оптимизации, калибровки ROI (FR-016, FR-003).
/// </summary>
public interface ISettingsRepository
{
    /// <summary>Возвращает настройки виджета (FR-016).</summary>
    Task<WidgetSettings> GetWidgetSettingsAsync();

    /// <summary>Сохраняет настройки виджета.</summary>
    Task SaveWidgetSettingsAsync(WidgetSettings s);

    /// <summary>Возвращает профиль оптимизации.</summary>
    Task<OptimizationProfile> GetOptimizationProfileAsync();

    /// <summary>Сохраняет профиль оптимизации.</summary>
    Task SaveOptimizationProfileAsync(OptimizationProfile p);

    /// <summary>Возвращает все калибровки ROI.</summary>
    Task<IReadOnlyList<RoiCalibration>> GetRoiCalibrationsAsync();

    /// <summary>Сохраняет набор калибровок ROI (FR-003).</summary>
    Task SaveRoiCalibrationsAsync(IReadOnlyList<RoiCalibration> rois);
}
