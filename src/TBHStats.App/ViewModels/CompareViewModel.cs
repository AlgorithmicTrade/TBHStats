using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using TBHStats.App.Services;
using TBHStats.Core.Models;
using TBHStats.Core.Optimization;
using TBHStats.Data;
using TBHStats.Data.Repositories;

namespace TBHStats.App.ViewModels;

/// <summary>
/// ViewModel экрана сравнения этапов (US2, FR-008/FR-009/FR-017/FR-019).
/// </summary>
/// <remarks>
/// <para>
/// Показывает таблицу всех этапов с накопленной историей, ранжированных по выбранной метрике
/// (золото/час или опыт/час) в выбранном scope (свежее окно / вся история).
/// Отмечает рекомендованный этап, пометку «устаревших» строк (IsStalePower) и
/// контекст силы отряда (диапазон уровня/урона).
/// </para>
/// <para>
/// Lifetime: Transient. Создаётся на UI-потоке (для корректного захвата DispatcherQueue).
/// Scoped-зависимости (<see cref="IStageAggregateRepository"/>, <see cref="TbhStatsDbContext"/>)
/// получаются через <see cref="IServiceScopeFactory"/> — scope открывается и закрывается
/// при каждом вызове <see cref="LoadAsync"/>.
/// </para>
/// </remarks>
public sealed partial class CompareViewModel : ObservableObject
{
    // ── Пороги эвристики IsStalePower ────────────────────────────────────────
    // Эмпирически уточняемо. Строка считается «устаревшей» если:
    //   - разница уровней: текущий уровень > HeroLevelMax окна более чем на LevelStaleDelta, ИЛИ
    //   - разница урона:   текущий урон > HeroDamageMax окна более чем в (1 / DamageStaleRatio) раз
    //     (т.е. HeroDamageMax < currentDamage * DamageStaleRatio).
    private const int    LevelStaleDelta  = 3;    // уровней
    private const double DamageStaleRatio = 0.5;  // HeroDamageMax < currentDamage * 0.5

    // ── Зависимости ──────────────────────────────────────────────────────────

    private readonly IServiceScopeFactory        _scopeFactory;
    private readonly IOptimizationService        _optimization;
    private readonly OptimizationProfileService  _profileService;
    private readonly IStatsOrchestrator          _orchestrator;
    private readonly ILogger<CompareViewModel>   _logger;
    private readonly DispatcherQueue?            _dispatcher;
    private readonly RunRecorder                 _runRecorder;

    // ── Анти-реентранси автообновления ───────────────────────────────────────

    private bool _isReloading;
    private bool _reloadPending;

    // ── Конструктор ───────────────────────────────────────────────────────────

    /// <summary>
    /// Инициализирует ViewModel.
    /// Должен создаваться на UI-потоке, чтобы корректно захватить <see cref="DispatcherQueue"/>.
    /// </summary>
    public CompareViewModel(
        IServiceScopeFactory       scopeFactory,
        IOptimizationService       optimization,
        OptimizationProfileService profileService,
        IStatsOrchestrator         orchestrator,
        ILogger<CompareViewModel>  logger,
        RunRecorder                runRecorder)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(optimization);
        ArgumentNullException.ThrowIfNull(profileService);
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(runRecorder);

        _scopeFactory   = scopeFactory;
        _optimization   = optimization;
        _profileService = profileService;
        _orchestrator   = orchestrator;
        _logger         = logger;
        _runRecorder    = runRecorder;

        // Захватываем DispatcherQueue текущего (UI) потока.
        // Если конструктор вызван не на UI-потоке (тесты, headless) — dispatcher будет null,
        // и обновления применятся синхронно.
        _dispatcher = DispatcherQueue.GetForCurrentThread();
    }

    // ── Автообновление ────────────────────────────────────────────────────────

    /// <summary>
    /// Подписывается на <see cref="RunRecorder.RunsChanged"/> для автообновления таблицы.
    /// Идемпотентно: повторный вызов не создаёт двойную подписку.
    /// Вызывать из OnLoaded страницы на UI-потоке.
    /// </summary>
    public void StartAutoRefresh()
    {
        _runRecorder.RunsChanged -= OnRunsChanged;
        _runRecorder.RunsChanged += OnRunsChanged;
    }

    /// <summary>
    /// Отписывается от <see cref="RunRecorder.RunsChanged"/>.
    /// Вызывать из OnUnloaded страницы, чтобы singleton <see cref="RunRecorder"/>
    /// не удерживал ссылку на закрытую страницу.
    /// </summary>
    public void StopAutoRefresh()
    {
        _runRecorder.RunsChanged -= OnRunsChanged;
    }

    /// <summary>
    /// Обработчик события RunsChanged: поднимается на фоновом потоке,
    /// маршалируется в UI-поток перед перезагрузкой.
    /// </summary>
    private void OnRunsChanged(object? sender, EventArgs e)
    {
        if (_dispatcher is not null)
            _dispatcher.TryEnqueue(() => _ = ReloadSafeAsync());
        else
            _ = ReloadSafeAsync();
    }

    /// <summary>
    /// Коалесцирующая перезагрузка: предотвращает наложение параллельных вызовов LoadAsync.
    /// Если перезагрузка уже идёт — выставляет флаг _reloadPending и возвращается.
    /// После завершения текущей загрузки запускает ещё одну, если был pending.
    /// </summary>
    private async Task ReloadSafeAsync()
    {
        if (_isReloading)
        {
            _reloadPending = true;
            return;
        }

        _isReloading = true;
        try
        {
            do
            {
                _reloadPending = false;
                await LoadAsync().ConfigureAwait(false);
            }
            while (_reloadPending);
        }
        finally
        {
            _isReloading = false;
        }
    }

    // ── Observable-свойства ───────────────────────────────────────────────────

    /// <summary>Отсортированный ранжированный список строк этапов.</summary>
    public ObservableCollection<CompareStageRow> Stages { get; } = [];

    /// <summary>Рекомендованный этап (null — данных нет или ни один этап не ранжирован).</summary>
    [ObservableProperty]
    private CompareStageRow? _recommended;

    /// <summary>Выбранная метрика оптимизации.</summary>
    [ObservableProperty]
    private OptimizationMetric _selectedMetric;

    /// <summary>Выбранный набор данных для ранжирования.</summary>
    [ObservableProperty]
    private AggregationScope _scope;

    /// <summary>
    /// true, если нет агрегатов с данными в выбранном scope.
    /// Показывать заглушку «Нет данных» при первом запуске (Edge Case).
    /// </summary>
    [ObservableProperty]
    private bool _isEmpty = true;

    /// <summary>
    /// Текстовый статус рекомендации, например:
    /// «Рекомендован: 1-5 Nightmare — лучший по золото/час, свежее окно».
    /// </summary>
    [ObservableProperty]
    private string _statusText = "—";

    // ── Команды ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Загружает (или перезагружает) данные экрана сравнения: агрегаты, профиль,
    /// метки этапов, ранжирование и рекомендацию.
    /// </summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            // Открываем один scope для репозитория и DbContext.
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            IStageAggregateRepository repo = scope.ServiceProvider
                .GetRequiredService<IStageAggregateRepository>();
            TbhStatsDbContext db = scope.ServiceProvider
                .GetRequiredService<TbhStatsDbContext>();

            // 1. Загружаем агрегаты и профиль параллельно.
            IReadOnlyList<StageAggregate> aggregates = await repo
                .GetAllAsync(CancellationToken.None)
                .ConfigureAwait(false);

            OptimizationProfile profile = await _profileService
                .GetAsync()
                .ConfigureAwait(false);

            // 2. Синхронизируем observable-свойства профиля (без маршализации — читаем просто).
            SelectedMetric = profile.SelectedMetric;
            Scope          = profile.Scope;

            // 3. Загружаем метки этапов через DbContext (read-only join).
            (Dictionary<int, string> stageLabels,
             Dictionary<int, string> stageNumberLabels,
             Dictionary<int, string> difficultyLabels) = await BuildStageLabelsAsync(db)
                .ConfigureAwait(false);

            // 4. Ранжируем.
            IReadOnlyList<StageRanking> rankings = _optimization.RankStages(
                aggregates, profile.SelectedMetric, profile.Scope);

            StageRanking? bestRanking = _optimization.RecommendBestStage(
                aggregates, profile.SelectedMetric, profile.Scope);

            // 5. Текущая сила отряда из живого снимка (для IsStalePower).
            LiveStatsSnapshot snapshot = _orchestrator.Current;
            int?  currentLevel  = snapshot.HeroLevel;
            long? currentDamage = snapshot.HeroDamage;

            // 6. Строим строки.
            List<CompareStageRow> rows = new(rankings.Count);
            foreach (StageRanking ranking in rankings)
            {
                StageAggregate? agg = aggregates
                    .FirstOrDefault(a => a.StageId == ranking.StageId);

                if (agg is null) continue; // не должно быть, но защищаемся

                string label = stageLabels.TryGetValue(ranking.StageId, out string? lbl)
                    ? lbl
                    : ranking.StageId.ToString();

                string numberLabel = stageNumberLabels.TryGetValue(ranking.StageId, out string? nl)
                    ? nl
                    : ranking.StageId.ToString();

                string diffLabel = difficultyLabels.TryGetValue(ranking.StageId, out string? dl)
                    ? dl
                    : string.Empty;

                (double goldPH, double xpPH, double avgGold, double avgXp, int runCount) =
                    SelectScopeMetrics(agg, profile.Scope);

                bool isRecommended = bestRanking is not null
                    && ranking.StageId == bestRanking.StageId;

                bool isStalePower = ComputeIsStalePower(
                    ranking.Power, currentLevel, currentDamage);

                rows.Add(new CompareStageRow
                {
                    StageId          = ranking.StageId,
                    StageLabel       = label,
                    StageNumberLabel = numberLabel,
                    DifficultyLabel  = diffLabel,
                    Rank             = ranking.Rank,
                    IsRecommended    = isRecommended,
                    GoldPerHour      = goldPH,
                    XpPerHour        = xpPH,
                    GoldPerHourText  = FormatRate(goldPH),
                    XpPerHourText    = FormatRate(xpPH),
                    AvgGoldGained    = avgGold,
                    AvgXpGained      = avgXp,
                    AvgGoldText      = FormatAmount(avgGold),
                    AvgXpText        = FormatAmount(avgXp),
                    RunCount         = runCount,
                    PowerText        = BuildPowerText(ranking.Power),
                    IsStalePower     = isStalePower,
                    Reason           = ranking.Reason,
                });
            }

            // 7. Рекомендованная строка.
            CompareStageRow? recommendedRow = rows.FirstOrDefault(r => r.IsRecommended);

            // 8. Статус.
            string status = recommendedRow is not null
                ? $"Рекомендован: {recommendedRow.StageLabel} — {recommendedRow.Reason}"
                : "Нет данных для рекомендации";

            // 9. Обновляем UI-коллекцию на UI-потоке.
            void ApplyToUi()
            {
                Stages.Clear();
                foreach (CompareStageRow row in rows)
                    Stages.Add(row);

                Recommended = recommendedRow;
                IsEmpty     = Stages.Count == 0;
                StatusText  = status;
            }

            if (_dispatcher is not null)
                _dispatcher.TryEnqueue(ApplyToUi);
            else
                ApplyToUi();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CompareViewModel.LoadAsync: не удалось загрузить данные сравнения этапов.");
        }
    }

    /// <summary>
    /// Изменяет метрику оптимизации и перезагружает таблицу.
    /// </summary>
    /// <param name="metric">Новая метрика.</param>
    [RelayCommand]
    public async Task SetMetricAsync(OptimizationMetric metric)
    {
        try
        {
            await _profileService.SetMetricAsync(metric).ConfigureAwait(false);
            await LoadAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CompareViewModel.SetMetricAsync: ошибка при смене метрики {Metric}.", metric);
        }
    }

    /// <summary>
    /// Изменяет набор данных для ранжирования и перезагружает таблицу.
    /// </summary>
    /// <param name="aggScope">Новый набор данных.</param>
    [RelayCommand]
    public async Task SetScopeAsync(AggregationScope aggScope)
    {
        try
        {
            await _profileService.SetScopeAsync(aggScope).ConfigureAwait(false);
            await LoadAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CompareViewModel.SetScopeAsync: ошибка при смене scope {Scope}.", aggScope);
        }
    }

    /// <summary>
    /// Изменяет размер свежего окна и перезагружает таблицу.
    /// </summary>
    /// <param name="windowSize">Новый размер окна (≥ 1).</param>
    [RelayCommand]
    public async Task SetWindowSizeAsync(int windowSize)
    {
        try
        {
            await _profileService.SetRecentWindowSizeAsync(windowSize).ConfigureAwait(false);
            await LoadAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CompareViewModel.SetWindowSizeAsync: ошибка при смене размера окна {WindowSize}.", windowSize);
        }
    }

    // ── Внутренние вспомогательные методы ────────────────────────────────────

    /// <summary>
    /// Загружает метки этапов через EF Core: join Stage + Act + Difficulty.
    /// Возвращает три словаря по StageId:
    /// <list type="bullet">
    ///   <item><c>stageLabels</c> — полная метка «{actNum}-{stageNum} {diffDisplayName}», напр. «1-5 Nightmare».</item>
    ///   <item><c>stageNumberLabels</c> — только номерная часть «{actNum}-{stageNum}», напр. «1-5».</item>
    ///   <item><c>difficultyLabels</c> — только сложность, напр. «Normal» / «Nightmare».</item>
    /// </list>
    /// </summary>
    private static async Task<(
        Dictionary<int, string> stageLabels,
        Dictionary<int, string> stageNumberLabels,
        Dictionary<int, string> difficultyLabels)>
        BuildStageLabelsAsync(TbhStatsDbContext db)
    {
        // Загружаем справочники одним запросом каждый (небольшой объём).
        List<Stage>      stages       = await db.Stages.AsNoTracking().ToListAsync().ConfigureAwait(false);
        List<Act>        acts         = await db.Acts.AsNoTracking().ToListAsync().ConfigureAwait(false);
        List<Difficulty> difficulties = await db.Difficulties.AsNoTracking().ToListAsync().ConfigureAwait(false);

        Dictionary<int, int>    actNumber    = acts.ToDictionary(a => a.Id, a => a.Number);
        Dictionary<int, string> diffDisplay  = difficulties.ToDictionary(d => d.Id, d => d.DisplayName);

        Dictionary<int, string> labels       = new(stages.Count);
        Dictionary<int, string> numberLabels = new(stages.Count);
        Dictionary<int, string> diffLabels   = new(stages.Count);

        foreach (Stage s in stages)
        {
            int    actNum   = actNumber.TryGetValue(s.ActId, out int n) ? n : s.ActId;
            string diffName = diffDisplay.TryGetValue(s.DifficultyId, out string? dn) ? dn : s.DifficultyId.ToString();

            labels[s.Id]       = $"{actNum}-{s.Number} {diffName}";
            numberLabels[s.Id] = $"{actNum}-{s.Number}";
            diffLabels[s.Id]   = diffName;
        }

        return (labels, numberLabels, diffLabels);
    }

    /// <summary>
    /// Выбирает значения метрик и счётчик забегов по выбранному scope.
    /// </summary>
    private static (double goldPH, double xpPH, double avgGold, double avgXp, int runCount) SelectScopeMetrics(
        StageAggregate agg, AggregationScope scope)
    {
        return scope == AggregationScope.Recent
            ? (agg.RecentAvgGoldPerHour, agg.RecentAvgXpPerHour, agg.RecentAvgGoldGained, agg.RecentAvgXpGained, agg.RecentRunCount)
            : (agg.AvgGoldPerHour,       agg.AvgXpPerHour,       agg.AvgGoldGained,       agg.AvgXpGained,       agg.RunCount);
    }

    /// <summary>
    /// Определяет, устарели ли данные строки относительно текущей силы отряда.
    /// </summary>
    /// <remarks>
    /// Эвристика (эмпирически уточняемо):
    /// <list type="bullet">
    ///   <item>Если текущий уровень известен и <c>power.HeroLevelMax &lt; currentLevel - LevelStaleDelta</c> — устарело.</item>
    ///   <item>Если текущий урон известен и <c>power.HeroDamageMax &lt; currentDamage * DamageStaleRatio</c> — устарело.</item>
    ///   <item>Если текущая сила неизвестна (оба null) — не устарело.</item>
    /// </list>
    /// </remarks>
    internal static bool ComputeIsStalePower(
        StagePowerContext power,
        int? currentLevel,
        long? currentDamage)
    {
        if (currentLevel.HasValue && power.HeroLevelMax.HasValue)
        {
            if (power.HeroLevelMax.Value < currentLevel.Value - LevelStaleDelta)
                return true;
        }

        if (currentDamage.HasValue && power.HeroDamageMax.HasValue)
        {
            if (power.HeroDamageMax.Value < currentDamage.Value * DamageStaleRatio)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Строит строку диапазона силы отряда для отображения.
    /// Формат: «ур. {min}–{max}, урон {minFmt}–{maxFmt}» или «—» при отсутствии данных.
    /// </summary>
    private static string BuildPowerText(StagePowerContext power)
    {
        if (power.HeroLevelMin is null || power.HeroLevelMax is null)
            return "—";

        string levelPart  = $"ур. {power.HeroLevelMin}–{power.HeroLevelMax}";
        string damagePart = power.HeroDamageMin.HasValue && power.HeroDamageMax.HasValue
            ? $", урон {FormatLong(power.HeroDamageMin.Value)}–{FormatLong(power.HeroDamageMax.Value)}"
            : string.Empty;

        return levelPart + damagePart;
    }

    /// <summary>
    /// Форматирует темп (золото/час, опыт/час) с разделителем тысяч и суффиксом «/ч».
    /// Например: 1 234 567 → «1 234 567/ч».
    /// </summary>
    private static string FormatRate(double value)
    {
        if (value <= 0) return "0/ч";
        return $"{value:N0}/ч";
    }

    /// <summary>
    /// Форматирует целое число с разделителем тысяч.
    /// </summary>
    private static string FormatLong(long value) => value.ToString("N0");

    /// <summary>
    /// Форматирует среднее абсолютное значение (золото/опыт за забег) с разделителем тысяч, без суффикса.
    /// Например: 12 345.6 → «12 346».
    /// </summary>
    private static string FormatAmount(double value) => value <= 0 ? "0" : value.ToString("N0");
}
