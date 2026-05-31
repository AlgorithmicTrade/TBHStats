using Microsoft.Extensions.DependencyInjection;

namespace TBHStats.App.Services;

/// <summary>
/// Composition root приложения (T004): единая точка регистрации зависимостей по слоям.
/// Конкретные реализации добавляются в задачах Phase 2 и Phase 3+ (Core/Capture/Data пока пусты) —
/// здесь только скелет точек расширения, без моков и заглушек-реализаций.
/// </summary>
public static class Composition
{
    /// <summary>
    /// Регистрирует все сервисы TBHStats в контейнере. Вызывается из <c>App</c> при построении хоста.
    /// </summary>
    public static IServiceCollection AddTbhStatsServices(this IServiceCollection services)
    {
        // === TBHStats.Capture (Phase 2+: T012, T013, T014, T015, T022, T023) ===
        // services.AddSingleton<IGameWindowTracker, GameWindowTracker>();
        // services.AddSingleton<ICaptureSession, CaptureSession>();
        // services.AddSingleton<IOcrReader, OcrReader>();
        // services.AddSingleton<ITabDetector, TabDetector>();
        // services.AddSingleton<IFieldExtractor, FieldExtractor>();

        // === TBHStats.Data (Phase 2+: T009, T010, T011, T027, T037, T038) ===
        // services.AddDbContext<TbhStatsDbContext>(...);
        // services.AddScoped<IRunRepository, RunRepository>();
        // services.AddScoped<IStageAggregateRepository, StageAggregateRepository>();
        // services.AddScoped<ISettingsRepository, SettingsRepository>();

        // === TBHStats.Core (Phase 2+: T007, T008, T024, T025, T039) ===
        // services.AddSingleton<IValueParser, ValueParser>();
        // services.AddSingleton<IObservationValidator, ObservationValidator>();
        // services.AddSingleton<IMetricsCalculator, MetricsCalculator>();
        // services.AddSingleton<IOptimizationService, OptimizationService>();

        // === TBHStats.App services & ViewModels (Phase 3+: T026, T029, T036, T040, T042) ===
        // services.AddSingleton<IStatsOrchestrator, StatsOrchestrator>();
        // services.AddTransient<LiveStatsViewModel>();
        // services.AddTransient<CompareViewModel>();

        return services;
    }
}
