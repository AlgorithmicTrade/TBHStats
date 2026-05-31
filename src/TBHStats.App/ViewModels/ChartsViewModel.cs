using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using SkiaSharp;
using TBHStats.Core.Models;
using TBHStats.Data;
using TBHStats.Data.Repositories;

namespace TBHStats.App.ViewModels;

/// <summary>
/// ViewModel экрана графиков трендов по этапу (US3, FR-015/FR-016).
/// </summary>
/// <remarks>
/// <para>
/// Показывает три линейных тренда для выбранного этапа:
/// золото/час, опыт/час и время прохождения (в минутах).
/// Каждая точка = один завершённый non-partial <see cref="StageRun"/>,
/// x = <see cref="StageRun.CompletedAtUtc"/>, y = соответствующий показатель.
/// </para>
/// <para>
/// Lifetime: Transient. Создаётся на UI-потоке (для корректного захвата <see cref="DispatcherQueue"/>).
/// Scoped-зависимости (<see cref="IRunRepository"/>, <see cref="TbhStatsDbContext"/>)
/// получаются через <see cref="IServiceScopeFactory"/> — scope открывается и закрывается
/// при каждом вызове <see cref="LoadAsync"/> / <see cref="RebuildSeriesAsync"/>.
/// </para>
/// </remarks>
public sealed partial class ChartsViewModel : ObservableObject
{
    // ── Зависимости ──────────────────────────────────────────────────────────

    private readonly IServiceScopeFactory      _scopeFactory;
    private readonly ILogger<ChartsViewModel>  _logger;
    private readonly DispatcherQueue?          _dispatcher;

    // Флаг: true во время выполнения LoadAsync, чтобы OnSelectedStageChanged
    // не запускал лишний RebuildSeriesAsync, пока LoadAsync сам строит серии.
    private bool _isLoading;

    // ── Конструктор ───────────────────────────────────────────────────────────

    /// <summary>
    /// Инициализирует ViewModel.
    /// Должен создаваться на UI-потоке, чтобы корректно захватить <see cref="DispatcherQueue"/>.
    /// </summary>
    public ChartsViewModel(
        IServiceScopeFactory     scopeFactory,
        ILogger<ChartsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _logger       = logger;

        // Захватываем DispatcherQueue текущего (UI) потока.
        // Если конструктор вызван не на UI-потоке (тесты, headless) — dispatcher будет null,
        // и обновления применятся синхронно.
        _dispatcher = DispatcherQueue.GetForCurrentThread();
    }

    // ── Observable-свойства ───────────────────────────────────────────────────

    /// <summary>
    /// Этапы, по которым есть хотя бы один non-partial <see cref="StageRun"/>.
    /// Используется в ComboBox (DisplayMemberPath="Label").
    /// </summary>
    public ObservableCollection<ChartsStageOption> StageOptions { get; } = [];

    /// <summary>
    /// Текущий выбранный этап.
    /// Смена значения запускает перестроение трендов.
    /// </summary>
    [ObservableProperty]
    private ChartsStageOption? _selectedStage;

    /// <summary>Серия тренда золото/час (одна линия).</summary>
    [ObservableProperty]
    private ISeries[] _goldSeries = Array.Empty<ISeries>();

    /// <summary>Серия тренда опыт/час (одна линия).</summary>
    [ObservableProperty]
    private ISeries[] _xpSeries = Array.Empty<ISeries>();

    /// <summary>Серия тренда времени прохождения, мин (одна линия).</summary>
    [ObservableProperty]
    private ISeries[] _durationSeries = Array.Empty<ISeries>();

    /// <summary>Ось X (время завершения забега) — общая для всех трёх графиков.</summary>
    [ObservableProperty]
    private Axis[] _xAxes = BuildDefaultXAxes();

    /// <summary>Ось Y графика золото/час.</summary>
    [ObservableProperty]
    private Axis[] _goldYAxes = BuildDefaultYAxes("Золото/ч");

    /// <summary>Ось Y графика опыт/час.</summary>
    [ObservableProperty]
    private Axis[] _xpYAxes = BuildDefaultYAxes("Опыт/ч");

    /// <summary>Ось Y графика времени прохождения.</summary>
    [ObservableProperty]
    private Axis[] _durationYAxes = BuildDefaultYAxes("Время, мин");

    /// <summary>
    /// true, если нет этапов с историей ИЛИ у выбранного этапа нет non-partial забегов.
    /// </summary>
    [ObservableProperty]
    private bool _isEmpty = true;

    /// <summary>
    /// Краткий статус, например «Этап 1-5 Nightmare: 12 забегов».
    /// </summary>
    [ObservableProperty]
    private string _statusText = "—";

    // ── Partial-методы CommunityToolkit ──────────────────────────────────────

    /// <summary>
    /// Вызывается CommunityToolkit.Mvvm при смене <see cref="SelectedStage"/>.
    /// Запускает перестроение серий без блокировки UI-потока.
    /// Подавляется во время выполнения <see cref="LoadAsync"/> (флаг <c>_isLoading</c>).
    /// </summary>
    partial void OnSelectedStageChanged(ChartsStageOption? value)
    {
        if (value is null || _isLoading) return;

        // fire-and-forget: не блокируем UI-поток; исключения перехватываются внутри
        _ = RebuildSeriesAsync(value.StageId);
    }

    // ── Команды ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Заполняет <see cref="StageOptions"/>, выбирает первый этап (если <see cref="SelectedStage"/> пуст),
    /// строит тренды.
    /// </summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        _isLoading = true;
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            IRunRepository    repo = scope.ServiceProvider.GetRequiredService<IRunRepository>();
            TbhStatsDbContext db   = scope.ServiceProvider.GetRequiredService<TbhStatsDbContext>();

            // 1. Метки этапов.
            Dictionary<int, string> stageLabels = await BuildStageLabelsAsync(db)
                .ConfigureAwait(false);

            // 2. Находим StageId-ы, у которых есть хотя бы один non-partial забег.
            List<int> stageIdsWithRuns = await db.StageRuns
                .AsNoTracking()
                .Where(r => !r.IsPartial)
                .Select(r => r.StageId)
                .Distinct()
                .OrderBy(id => id)
                .ToListAsync()
                .ConfigureAwait(false);

            // 3. Строим список опций.
            List<ChartsStageOption> options = stageIdsWithRuns
                .Select(id => new ChartsStageOption
                {
                    StageId = id,
                    Label   = stageLabels.TryGetValue(id, out string? lbl) ? lbl : id.ToString(),
                })
                .ToList();

            // 4. Загружаем забеги выбранного (или первого) этапа.
            int? targetStageId = SelectedStage is not null && stageIdsWithRuns.Contains(SelectedStage.StageId)
                ? SelectedStage.StageId
                : stageIdsWithRuns.Count > 0 ? stageIdsWithRuns[0] : (int?)null;

            List<StageRun>? runs = null;
            if (targetStageId.HasValue)
            {
                IReadOnlyList<StageRun> raw = await repo
                    .GetRunsAsync(targetStageId.Value, CancellationToken.None)
                    .ConfigureAwait(false);
                runs = raw.Where(r => !r.IsPartial)
                          .OrderBy(r => r.CompletedAtUtc)
                          .ToList();
            }

            // 5. Формируем серии (вне UI-потока — только вычисления).
            ISeries[] goldSeries     = Array.Empty<ISeries>();
            ISeries[] xpSeries       = Array.Empty<ISeries>();
            ISeries[] durationSeries = Array.Empty<ISeries>();
            bool      isEmpty        = true;
            string    statusText     = "—";

            if (runs is { Count: > 0 } && targetStageId.HasValue)
            {
                (goldSeries, xpSeries, durationSeries) = BuildAllSeries(runs);
                isEmpty    = false;
                string lbl = stageLabels.TryGetValue(targetStageId.Value, out string? l) ? l : targetStageId.Value.ToString();
                statusText = $"Этап {lbl}: {runs.Count} забег{RunCountSuffix(runs.Count)}";
            }
            else if (options.Count == 0)
            {
                statusText = "Нет этапов с историей";
            }
            else
            {
                statusText = "Нет завершённых забегов для выбранного этапа";
            }

            // 6. Применяем всё на UI-потоке.
            ChartsStageOption? targetOption = options.FirstOrDefault(o => o.StageId == targetStageId);

            void ApplyToUi()
            {
                StageOptions.Clear();
                foreach (ChartsStageOption opt in options)
                    StageOptions.Add(opt);

                // _isLoading=true подавляет OnSelectedStageChanged, поэтому присваиваем
                // через сгенерированное свойство — лишний RebuildSeriesAsync не запустится.
                if (SelectedStage is null || targetOption is not null)
                    SelectedStage = targetOption;

                GoldSeries     = goldSeries;
                XpSeries       = xpSeries;
                DurationSeries = durationSeries;
                IsEmpty        = isEmpty;
                StatusText     = statusText;
            }

            if (_dispatcher is not null)
                _dispatcher.TryEnqueue(ApplyToUi);
            else
                ApplyToUi();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ChartsViewModel.LoadAsync: не удалось загрузить данные графиков.");
        }
        finally
        {
            _isLoading = false;
        }
    }

    // ── Внутренние методы ────────────────────────────────────────────────────

    /// <summary>
    /// Перестраивает серии для заданного этапа.
    /// Вызывается fire-and-forget из <see cref="OnSelectedStageChanged"/>.
    /// </summary>
    private async Task RebuildSeriesAsync(int stageId)
    {
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            IRunRepository repo = scope.ServiceProvider.GetRequiredService<IRunRepository>();
            TbhStatsDbContext db = scope.ServiceProvider.GetRequiredService<TbhStatsDbContext>();

            IReadOnlyList<StageRun> raw = await repo
                .GetRunsAsync(stageId, CancellationToken.None)
                .ConfigureAwait(false);

            List<StageRun> runs = raw.Where(r => !r.IsPartial)
                                     .OrderBy(r => r.CompletedAtUtc)
                                     .ToList();

            ISeries[] goldSeries;
            ISeries[] xpSeries;
            ISeries[] durationSeries;
            bool      isEmpty;
            string    statusText;

            if (runs.Count > 0)
            {
                (goldSeries, xpSeries, durationSeries) = BuildAllSeries(runs);
                isEmpty = false;

                Dictionary<int, string> stageLabels = await BuildStageLabelsAsync(db)
                    .ConfigureAwait(false);
                string lbl = stageLabels.TryGetValue(stageId, out string? l) ? l : stageId.ToString();
                statusText = $"Этап {lbl}: {runs.Count} забег{RunCountSuffix(runs.Count)}";
            }
            else
            {
                goldSeries     = Array.Empty<ISeries>();
                xpSeries       = Array.Empty<ISeries>();
                durationSeries = Array.Empty<ISeries>();
                isEmpty        = true;
                statusText     = "Нет завершённых забегов для выбранного этапа";
            }

            void ApplyToUi()
            {
                GoldSeries     = goldSeries;
                XpSeries       = xpSeries;
                DurationSeries = durationSeries;
                IsEmpty        = isEmpty;
                StatusText     = statusText;
            }

            if (_dispatcher is not null)
                _dispatcher.TryEnqueue(ApplyToUi);
            else
                ApplyToUi();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ChartsViewModel.RebuildSeriesAsync(stageId={StageId}): не удалось перестроить серии.", stageId);
        }
    }

    /// <summary>
    /// Строит три серии (золото/ч, опыт/ч, время) из списка завершённых забегов.
    /// </summary>
    private static (ISeries[] gold, ISeries[] xp, ISeries[] duration) BuildAllSeries(
        IReadOnlyList<StageRun> runs)
    {
        DateTimePoint[] goldPts     = new DateTimePoint[runs.Count];
        DateTimePoint[] xpPts       = new DateTimePoint[runs.Count];
        DateTimePoint[] durationPts = new DateTimePoint[runs.Count];

        for (int i = 0; i < runs.Count; i++)
        {
            StageRun r = runs[i];
            goldPts[i]     = new DateTimePoint(r.CompletedAtUtc, r.GoldPerHour);
            xpPts[i]       = new DateTimePoint(r.CompletedAtUtc, r.XpPerHour);
            durationPts[i] = new DateTimePoint(r.CompletedAtUtc, r.DurationSeconds / 60.0);
        }

        ISeries[] gold = [
            new LineSeries<DateTimePoint>
            {
                Name                   = "Золото/ч",
                Values                 = goldPts,
                GeometrySize           = 8,
                GeometryStroke         = new SolidColorPaint(SKColors.Goldenrod) { StrokeThickness = 2 },
                GeometryFill           = new SolidColorPaint(SKColors.Goldenrod),
                Stroke                 = new SolidColorPaint(SKColors.Goldenrod) { StrokeThickness = 2 },
                Fill                   = null,
                LineSmoothness         = 0,
                XToolTipLabelFormatter = p => p.Model?.DateTime.ToString("dd.MM HH:mm") ?? string.Empty,
                YToolTipLabelFormatter = p => $"{p.Model?.Value:N0} /ч",
            }
        ];

        ISeries[] xp = [
            new LineSeries<DateTimePoint>
            {
                Name                   = "Опыт/ч",
                Values                 = xpPts,
                GeometrySize           = 8,
                GeometryStroke         = new SolidColorPaint(SKColors.CornflowerBlue) { StrokeThickness = 2 },
                GeometryFill           = new SolidColorPaint(SKColors.CornflowerBlue),
                Stroke                 = new SolidColorPaint(SKColors.CornflowerBlue) { StrokeThickness = 2 },
                Fill                   = null,
                LineSmoothness         = 0,
                XToolTipLabelFormatter = p => p.Model?.DateTime.ToString("dd.MM HH:mm") ?? string.Empty,
                YToolTipLabelFormatter = p => $"{p.Model?.Value:N0} /ч",
            }
        ];

        ISeries[] duration = [
            new LineSeries<DateTimePoint>
            {
                Name                   = "Время, мин",
                Values                 = durationPts,
                GeometrySize           = 8,
                GeometryStroke         = new SolidColorPaint(SKColors.MediumSeaGreen) { StrokeThickness = 2 },
                GeometryFill           = new SolidColorPaint(SKColors.MediumSeaGreen),
                Stroke                 = new SolidColorPaint(SKColors.MediumSeaGreen) { StrokeThickness = 2 },
                Fill                   = null,
                LineSmoothness         = 0,
                XToolTipLabelFormatter = p => p.Model?.DateTime.ToString("dd.MM HH:mm") ?? string.Empty,
                YToolTipLabelFormatter = p => $"{p.Model?.Value:F1} мин",
            }
        ];

        return (gold, xp, duration);
    }

    /// <summary>
    /// Строит ось X с форматированием дат (dd.MM HH:mm).
    /// UnitWidth и MinStep = 1 минута в тиках — подходит для типичных интервалов между забегами.
    /// </summary>
    private static Axis[] BuildDefaultXAxes() =>
    [
        new Axis
        {
            Labeler  = value => new DateTime((long)value).ToString("dd.MM HH:mm"),
            UnitWidth = TimeSpan.FromMinutes(1).Ticks,
            MinStep   = TimeSpan.FromMinutes(1).Ticks,
        }
    ];

    /// <summary>
    /// Строит ось Y с заданным заголовком и неотрицательным минимумом.
    /// </summary>
    private static Axis[] BuildDefaultYAxes(string name) =>
    [
        new Axis
        {
            Name    = name,
            MinLimit = 0,
        }
    ];

    /// <summary>
    /// Загружает метки этапов через EF Core: join Stage + Act + Difficulty.
    /// Формат: «{actNumber}-{stageNumber} {difficultyDisplayName}», напр. «1-5 Nightmare».
    /// Скопировано из <see cref="CompareViewModel"/> (единственная DRY-точка в App-слое).
    /// </summary>
    private static async Task<Dictionary<int, string>> BuildStageLabelsAsync(TbhStatsDbContext db)
    {
        List<Stage>      stages       = await db.Stages.AsNoTracking().ToListAsync().ConfigureAwait(false);
        List<Act>        acts         = await db.Acts.AsNoTracking().ToListAsync().ConfigureAwait(false);
        List<Difficulty> difficulties = await db.Difficulties.AsNoTracking().ToListAsync().ConfigureAwait(false);

        Dictionary<int, int>    actNumber   = acts.ToDictionary(a => a.Id, a => a.Number);
        Dictionary<int, string> diffDisplay = difficulties.ToDictionary(d => d.Id, d => d.DisplayName);

        Dictionary<int, string> labels = new(stages.Count);
        foreach (Stage s in stages)
        {
            int    actNum   = actNumber.TryGetValue(s.ActId, out int n) ? n : s.ActId;
            string diffName = diffDisplay.TryGetValue(s.DifficultyId, out string? dn) ? dn : s.DifficultyId.ToString();
            labels[s.Id] = $"{actNum}-{s.Number} {diffName}";
        }

        return labels;
    }

    /// <summary>
    /// Возвращает правильное окончание для слова «забег» в зависимости от числа.
    /// </summary>
    private static string RunCountSuffix(int count)
    {
        int abs = Math.Abs(count);
        int mod100 = abs % 100;
        int mod10  = abs % 10;

        if (mod100 is >= 11 and <= 14) return "ов";
        return mod10 switch
        {
            1 => string.Empty,
            2 or 3 or 4 => "а",
            _ => "ов",
        };
    }
}
