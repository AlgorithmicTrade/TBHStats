using Microsoft.Extensions.DependencyInjection;
using TBHStats.Core.Models;
using TBHStats.Data.Repositories;

namespace TBHStats.App.Services;

/// <summary>
/// Прокси-адаптер <see cref="ISettingsRepository"/>, позволяющий singleton-сервисам
/// (например, <see cref="StatsOrchestrator"/>) использовать scoped <see cref="ISettingsRepository"/>
/// без нарушения lifetime-правил DI.
/// На каждый вызов создаётся временный scope; scope освобождается сразу после вызова.
/// </summary>
internal sealed class ScopedSettingsRepositoryProxy : ISettingsRepository
{
    private readonly IServiceProvider _sp;

    /// <param name="sp">Root service provider (singleton lifetime).</param>
    public ScopedSettingsRepositoryProxy(IServiceProvider sp)
    {
        ArgumentNullException.ThrowIfNull(sp);
        _sp = sp;
    }

    /// <inheritdoc/>
    public async Task<WidgetSettings> GetWidgetSettingsAsync()
    {
        await using AsyncServiceScope scope = _sp.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<ISettingsRepository>()
            .GetWidgetSettingsAsync()
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SaveWidgetSettingsAsync(WidgetSettings s)
    {
        await using AsyncServiceScope scope = _sp.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<ISettingsRepository>()
            .SaveWidgetSettingsAsync(s)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<OptimizationProfile> GetOptimizationProfileAsync()
    {
        await using AsyncServiceScope scope = _sp.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<ISettingsRepository>()
            .GetOptimizationProfileAsync()
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SaveOptimizationProfileAsync(OptimizationProfile p)
    {
        await using AsyncServiceScope scope = _sp.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<ISettingsRepository>()
            .SaveOptimizationProfileAsync(p)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RoiCalibration>> GetRoiCalibrationsAsync()
    {
        await using AsyncServiceScope scope = _sp.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<ISettingsRepository>()
            .GetRoiCalibrationsAsync()
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SaveRoiCalibrationsAsync(IReadOnlyList<RoiCalibration> rois)
    {
        await using AsyncServiceScope scope = _sp.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<ISettingsRepository>()
            .SaveRoiCalibrationsAsync(rois)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<WindowPlacement?> GetWindowPlacementAsync(string windowKey, CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = _sp.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<ISettingsRepository>()
            .GetWindowPlacementAsync(windowKey, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SaveWindowPlacementAsync(WindowPlacement placement, CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = _sp.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<ISettingsRepository>()
            .SaveWindowPlacementAsync(placement, ct)
            .ConfigureAwait(false);
    }
}
