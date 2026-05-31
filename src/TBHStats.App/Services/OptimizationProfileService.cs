using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TBHStats.Core.Models;
using TBHStats.Data.Repositories;

namespace TBHStats.App.Services;

/// <summary>
/// Singleton-сервис персистентности и переключения профиля оптимизации (FR-009, ADR-007).
/// </summary>
/// <remarks>
/// <para>
/// <b>Назначение.</b> Предоставляет единую точку чтения и изменения <see cref="OptimizationProfile"/>:
/// метрику (<see cref="OptimizationMetric"/>), набор данных (<see cref="AggregationScope"/>) и
/// размер свежего окна (<see cref="OptimizationProfile.RecentWindowSize"/>).
/// </para>
/// <para>
/// <b>Lifetime-решение.</b> Сервис является singleton, а <see cref="ISettingsRepository"/> — scoped.
/// Для корректной работы конструктор принимает <see cref="IServiceScopeFactory"/>;
/// scope создаётся и немедленно освобождается при каждом обращении к репозиторию.
/// </para>
/// <para>
/// <b>Immutable-профиль.</b> <see cref="OptimizationProfile"/> имеет init-сеттеры, поэтому
/// «изменение» реализуется через создание нового экземпляра с копированием остальных полей
/// из текущего профиля (copy-with-change).
/// </para>
/// </remarks>
public sealed class OptimizationProfileService
{
    // ── Зависимости ──────────────────────────────────────────────────────────

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OptimizationProfileService> _logger;

    // ── Конструктор ───────────────────────────────────────────────────────────

    /// <summary>
    /// Создаёт <see cref="OptimizationProfileService"/>.
    /// </summary>
    /// <param name="scopeFactory">
    /// Фабрика scope для получения scoped-зависимости <see cref="ISettingsRepository"/>
    /// из singleton-контекста.
    /// </param>
    /// <param name="logger">Логгер.</param>
    public OptimizationProfileService(
        IServiceScopeFactory scopeFactory,
        ILogger<OptimizationProfileService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    // ── Публичный API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Возвращает текущий профиль оптимизации из хранилища.
    /// </summary>
    /// <returns>
    /// Актуальный <see cref="OptimizationProfile"/> (если запись отсутствует — дефолтный).
    /// </returns>
    public async Task<OptimizationProfile> GetAsync()
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        ISettingsRepository settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        return await settings.GetOptimizationProfileAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Сохраняет профиль оптимизации в хранилище.
    /// </summary>
    /// <param name="profile">Профиль для сохранения.</param>
    /// <exception cref="ArgumentNullException">
    /// Если <paramref name="profile"/> равен <c>null</c>.
    /// </exception>
    public async Task SaveAsync(OptimizationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        ISettingsRepository settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        await settings.SaveOptimizationProfileAsync(profile).ConfigureAwait(false);
    }

    /// <summary>
    /// Изменяет метрику оптимизации, сохраняя остальные параметры профиля.
    /// </summary>
    /// <param name="metric">Новая метрика оптимизации.</param>
    public async Task SetMetricAsync(OptimizationMetric metric)
    {
        OptimizationProfile cur = await GetAsync().ConfigureAwait(false);
        OptimizationProfile updated = new(metric, cur.RecentWindowSize, cur.Scope);
        await SaveAsync(updated).ConfigureAwait(false);

        _logger.LogInformation(
            "OptimizationProfileService: метрика изменена на {Metric}.",
            metric);
    }

    /// <summary>
    /// Изменяет набор данных для ранжирования, сохраняя остальные параметры профиля.
    /// </summary>
    /// <param name="scope">Новый набор данных (<see cref="AggregationScope"/>).</param>
    public async Task SetScopeAsync(AggregationScope scope)
    {
        OptimizationProfile cur = await GetAsync().ConfigureAwait(false);
        OptimizationProfile updated = new(cur.SelectedMetric, cur.RecentWindowSize, scope);
        await SaveAsync(updated).ConfigureAwait(false);

        _logger.LogInformation(
            "OptimizationProfileService: набор данных изменён на {Scope}.",
            scope);
    }

    /// <summary>
    /// Изменяет размер свежего окна, сохраняя остальные параметры профиля.
    /// </summary>
    /// <param name="recentWindowSize">Новый размер свежего окна (≥ 1).</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Если <paramref name="recentWindowSize"/> меньше 1.
    /// </exception>
    public async Task SetRecentWindowSizeAsync(int recentWindowSize)
    {
        if (recentWindowSize < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(recentWindowSize),
                recentWindowSize,
                "RecentWindowSize должен быть ≥ 1.");
        }

        OptimizationProfile cur = await GetAsync().ConfigureAwait(false);
        OptimizationProfile updated = new(cur.SelectedMetric, recentWindowSize, cur.Scope);
        await SaveAsync(updated).ConfigureAwait(false);

        _logger.LogInformation(
            "OptimizationProfileService: размер свежего окна изменён на {WindowSize}.",
            recentWindowSize);
    }
}
