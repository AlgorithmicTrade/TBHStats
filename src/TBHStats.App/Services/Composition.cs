using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TBHStats.App.ViewModels;
using TBHStats.Capture;
using TBHStats.Capture.Chests;
using TBHStats.Capture.Ocr;
using TBHStats.Capture.Progress;
using TBHStats.Capture.Roi;
using TBHStats.Capture.Tabs;
using TBHStats.Capture.WindowTracking;
using TBHStats.Capture.Wgc;
using TBHStats.Core.Mechanics;
using TBHStats.Core.Optimization;
using TBHStats.Core.Parsing;
using TBHStats.Data;
using TBHStats.Data.Repositories;

namespace TBHStats.App.Services;

/// <summary>
/// Composition root приложения: единая точка регистрации зависимостей по слоям.
/// Вызывается из <c>App</c> при построении хоста.
/// </summary>
public static class Composition
{
    /// <summary>
    /// Регистрирует все сервисы TBHStats в контейнере.
    /// </summary>
    public static IServiceCollection AddTbhStatsServices(this IServiceCollection services)
    {
        // === TBHStats.Data — персистентность ====================================

        // DbContext: SQLite по пути %LOCALAPPDATA%\TBHStats\tbhstats.db
        // Путь вычисляется через DatabaseInitializer.GetDbPath() — единственный источник истины пути.
        // Строка подключения формируется через DatabaseInitializer.GetConnectionString для единообразия.
        string dbConnectionString = DatabaseInitializer.GetConnectionString(DatabaseInitializer.GetDbPath());
        services.AddDbContext<TbhStatsDbContext>(
            options => options.UseSqlite(dbConnectionString),
            ServiceLifetime.Scoped);

        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<IRunRepository, RunRepository>();
        services.AddScoped<IStageAggregateRepository, StageAggregateRepository>();

        // === TBHStats.Core — домен, парсинг, оптимизация ========================

        // IGameMechanics: singleton, инициализируется дефолтным конфигом (GameMechanicsConfig.CreateDefault())
        services.AddSingleton<IGameMechanics, GameMechanics>();
        services.AddSingleton<IValueParser, ValueParser>();
        services.AddSingleton<IObservationValidator, ObservationValidator>();
        services.AddSingleton<IMetricsCalculator, MetricsCalculator>();
        services.AddSingleton<IStageAggregateCalculator, StageAggregateCalculator>();
        services.AddSingleton<IOptimizationService, OptimizationService>();

        // === TBHStats.Capture — захват и распознавание ==========================

        // GameWindowTracker: параметры по умолчанию (GameWindowTrackerOptions.Default)
        services.AddSingleton<IGameWindowTracker, GameWindowTracker>();

        // RoiMapper: без зависимостей
        services.AddSingleton<IRoiMapper, RoiMapper>();

        // OcrReader: без зависимостей (движок Windows.Media.Ocr инициализируется лениво)
        services.AddSingleton<IOcrReader, OcrReader>();

        // TabNameMatcher: без зависимостей
        services.AddSingleton<ITabNameMatcher, TabNameMatcher>();

        // TabDetector зависит от IOcrReader и ITabNameMatcher
        services.AddSingleton<ITabDetector, TabDetector>();

        // ChestDotCounter реализует IChestDotCounter (legacy) и IChestPanelAnalyzer (ADR-022).
        // Singleton: не имеет статического изменяемого состояния кроме кэша буфера кадра
        // (кэш per-экземпляр, thread-safe через lock).
        services.AddSingleton<ChestDotCounter>();
        services.AddSingleton<IChestDotCounter>(sp => sp.GetRequiredService<ChestDotCounter>());
        services.AddSingleton<IChestPanelAnalyzer>(sp => sp.GetRequiredService<ChestDotCounter>());

        // ChestZoneAnalyzer: зонная локализация плашек по цвету + масштабонезависимый счёт по рядам (ADR-023).
        // Singleton: per-экземпляр кэш буфера кадра, thread-safe через lock.
        services.AddSingleton<IChestZoneAnalyzer, ChestZoneAnalyzer>();

        // StageProgressReader: визуальная детекция прогрессбара этапа по цвету заливки
        // (фиолетовый = путь, синий = босс; ADR-024). Singleton: per-экземпляр кэш буфера кадра, thread-safe через lock.
        services.AddSingleton<IStageProgressReader, StageProgressReader>();

        // FieldExtractor зависит от IOcrReader, IValueParser, IChestPanelAnalyzer (ADR-022 legacy fallback)
        // и IChestZoneAnalyzer (ADR-023 приоритетный путь).
        // IChestLayoutResolver больше не используется в пайплайне (сохранён в Core для возможных расширений).
        services.AddSingleton<IFieldExtractor, FieldExtractor>();

        // CaptureSession: создаётся как singleton; принимает IGameWindowTracker через DI.
        // Управляет жизненным циклом WGC-сессии самостоятельно (лениво на первом кадре).
        // IAsyncDisposable — освобождается при завершении хоста.
        services.AddSingleton<ICaptureSession, CaptureSession>();

        // StageCompletionDetector: singleton; детерминированная машина состояний без WinRT-зависимостей.
        services.AddSingleton<IStageCompletionDetector, StageCompletionDetector>();

        // RunRecorder: singleton; scoped-репозитории получает через IServiceScopeFactory
        // (scope создаётся и освобождается на каждый вызов PersistSegmentRunAsync).
        // IStageCompletionDetector больше не инжектируется в RunRecorder (сегментная логика
        // переехала в StatsOrchestrator — T064).
        services.AddSingleton<RunRecorder>(sp => new RunRecorder(
            scopeFactory:  sp.GetRequiredService<IServiceScopeFactory>(),
            gameMechanics: sp.GetRequiredService<IGameMechanics>(),
            logger:        sp.GetRequiredService<ILogger<RunRecorder>>()));

        // OptimizationProfileService: singleton; ISettingsRepository (scoped) получает через
        // IServiceScopeFactory — scope создаётся и освобождается на каждый вызов.
        services.AddSingleton<OptimizationProfileService>(sp => new OptimizationProfileService(
            scopeFactory: sp.GetRequiredService<IServiceScopeFactory>(),
            logger:       sp.GetRequiredService<ILogger<OptimizationProfileService>>()));

        // === TBHStats.App — оркестрация и ViewModel'и ==========================

        // LiveStatsViewModel: transient; зависит от singleton IStatsOrchestrator — безопасно.
        // Создаётся на UI-потоке (в WidgetWindow) — захватывает DispatcherQueue корректно.
        services.AddTransient<LiveStatsViewModel>();

        // CompareViewModel: transient; зависит от singletons + IServiceScopeFactory.
        // Создаётся на UI-потоке (для DispatcherQueue). Scoped-зависимости открывает через
        // IServiceScopeFactory самостоятельно внутри LoadAsync.
        services.AddTransient<CompareViewModel>();

        // ChartsViewModel: transient; scoped-зависимости через IServiceScopeFactory.
        // Создаётся на UI-потоке (для DispatcherQueue).
        services.AddTransient<ChartsViewModel>();

        // CalibrationViewModel: transient; зависит от scoped ISettingsRepository.
        // Регистрируем через фабрику с ScopedSettingsRepositoryProxy, чтобы не нарушать lifetime.
        services.AddTransient<CalibrationViewModel>(sp =>
            new CalibrationViewModel(
                new ScopedSettingsRepositoryProxy(sp),
                sp.GetRequiredService<IGameMechanics>(),
                sp.GetRequiredService<ICaptureSession>(),
                sp.GetRequiredService<IOcrReader>(),
                sp.GetRequiredService<IChestPanelAnalyzer>(),
                sp.GetRequiredService<IChestZoneAnalyzer>(),
                sp.GetRequiredService<IStageProgressReader>()));

        // StatsOrchestrator: singleton, зависит от сингтонов Capture/Core и scoped Data.
        // Scoped ISettingsRepository доступен через IServiceScopeFactory внутри петли
        // — для v1 используем singleton-обёртку: создаём scope явно на каждой итерации.
        // Чтобы DI не ломался из-за scoped→singleton, регистрируем через фабрику.
        services.AddSingleton<IStatsOrchestrator>(sp =>
        {
            // Для доступа к Scoped ISettingsRepository из singleton-оркестратора
            // создаём адаптер, который берёт репозиторий из нового scope на каждый вызов.
            ISettingsRepository settingsProxy = new ScopedSettingsRepositoryProxy(sp);

            return new StatsOrchestrator(
                session:            sp.GetRequiredService<ICaptureSession>(),
                tabDetector:        sp.GetRequiredService<ITabDetector>(),
                fieldExtractor:     sp.GetRequiredService<IFieldExtractor>(),
                validator:          sp.GetRequiredService<IObservationValidator>(),
                metricsCalculator:  sp.GetRequiredService<IMetricsCalculator>(),
                gameMechanics:      sp.GetRequiredService<IGameMechanics>(),
                settingsRepository: settingsProxy,
                logger:             sp.GetRequiredService<ILogger<StatsOrchestrator>>(),
                runRecorder:        sp.GetRequiredService<RunRecorder>());
        });

        return services;
    }
}
