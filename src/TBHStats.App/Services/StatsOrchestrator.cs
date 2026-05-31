using Microsoft.Extensions.Logging;
using TBHStats.Capture;
using TBHStats.Capture.Tabs;
using TBHStats.Capture.Wgc;
using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;
using TBHStats.Core.Optimization;
using TBHStats.Core.Parsing;
using TBHStats.Data.Repositories;

namespace TBHStats.App.Services;

/// <summary>
/// Реализация <see cref="IStatsOrchestrator"/> — фоновая петля живой статистики (FR-004, US1).
/// </summary>
/// <remarks>
/// Логика петли (services.md §Оркестрация + data-model §Capture state machine):
/// <list type="number">
///   <item>Получить кадр через <c>ICaptureSession</c> (сессия управляет окном и состоянием сама).</item>
///   <item>null-кадр → <c>Waiting</c> или <c>NotFound</c> (по <c>session.State</c>); публиковать IsStale-снимок и ждать.</item>
///   <item>Определить активную вкладку через <c>ITabDetector</c> (ROI «activeTab» из калибровок).</item>
///   <item>Извлечь поля через <c>IFieldExtractor</c>.</item>
///   <item>Валидировать наблюдение через <c>IObservationValidator</c>.</item>
///   <item>Если надёжный — добавить в скользящий буфер и пересчитать темпы.</item>
///   <item>Опубликовать снимок через <see cref="SnapshotUpdated"/>.</item>
/// </list>
/// Исключения внутри петли логируются и не роняют петлю (нормальная работа при нестабильном захвате).
/// </remarks>
public sealed class StatsOrchestrator : IStatsOrchestrator
{
    // ── Зависимости ──────────────────────────────────────────────────────────

    private readonly ICaptureSession _session;
    private readonly ITabDetector _tabDetector;
    private readonly IFieldExtractor _fieldExtractor;
    private readonly IObservationValidator _validator;
    private readonly IMetricsCalculator _metricsCalculator;
    private readonly IGameMechanics _gameMechanics;
    private readonly ISettingsRepository _settingsRepository;
    private readonly ILogger<StatsOrchestrator> _logger;

    // ── Константы ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Минимальная уверенность OCR для принятия значения поля (R4, FR-005/FR-010).
    /// </summary>
    private const double ConfidenceThreshold = 0.6;

    /// <summary>
    /// Максимальный размер скользящего буфера надёжных сэмплов.
    /// </summary>
    private const int MaxReliableBufferSize = 200;

    /// <summary>
    /// Порог устаревания данных: если последний надёжный замер старше этого числа секунд —
    /// снимок помечается как IsStale.
    /// </summary>
    private const int StaleThresholdSeconds = 30;

    // ── Состояние ─────────────────────────────────────────────────────────────

    private volatile LiveStatsSnapshot _current = LiveStatsSnapshot.Empty;

    /// <summary>Последний надёжный сэмпл для проверки монотонности (передаётся в валидатор).</summary>
    private MetricSample? _lastReliableSample;

    /// <summary>Скользящий буфер надёжных сэмплов для вычисления темпов.</summary>
    private readonly List<MetricSample> _reliableBuffer = [];

    /// <summary>Последние известные «живые» значения, показываемые пока State=Waiting/NotFound.</summary>
    private long? _lastKnownGold;
    private int? _lastKnownHeroLevel;
    private string? _lastKnownHeroClass;
    private long? _lastKnownHeroDamage;
    private StageRef? _lastKnownStage;

    // ── Управление петлёй ──────────────────────────────────────────────────

    private Task _loopTask = Task.CompletedTask;
    private CancellationTokenSource? _cts;
    private readonly object _startLock = new();

    // ── Публичный API ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public LiveStatsSnapshot Current => _current;

    /// <inheritdoc/>
    public event EventHandler<LiveStatsSnapshot>? SnapshotUpdated;

    /// <summary>
    /// Создаёт оркестратор со всеми зависимостями.
    /// </summary>
    public StatsOrchestrator(
        ICaptureSession session,
        ITabDetector tabDetector,
        IFieldExtractor fieldExtractor,
        IObservationValidator validator,
        IMetricsCalculator metricsCalculator,
        IGameMechanics gameMechanics,
        ISettingsRepository settingsRepository,
        ILogger<StatsOrchestrator> logger)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(tabDetector);
        ArgumentNullException.ThrowIfNull(fieldExtractor);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(metricsCalculator);
        ArgumentNullException.ThrowIfNull(gameMechanics);
        ArgumentNullException.ThrowIfNull(settingsRepository);
        ArgumentNullException.ThrowIfNull(logger);

        _session            = session;
        _tabDetector        = tabDetector;
        _fieldExtractor     = fieldExtractor;
        _validator          = validator;
        _metricsCalculator  = metricsCalculator;
        _gameMechanics      = gameMechanics;
        _settingsRepository = settingsRepository;
        _logger             = logger;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken ct)
    {
        lock (_startLock)
        {
            if (_cts is not null && !_cts.IsCancellationRequested)
            {
                // Петля уже запущена
                return Task.CompletedTask;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _loopTask = Task.Run(() => RunLoopAsync(_cts.Token), _cts.Token);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task loopTask;

        lock (_startLock)
        {
            cts      = _cts;
            loopTask = _loopTask;
        }

        if (cts is null)
            return;

        try
        {
            await cts.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException) { /* уже disposed */ }

        try
        {
            await loopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* ожидаемо при остановке */ }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Петля захвата завершилась с исключением при остановке.");
        }
        finally
        {
            cts.Dispose();
        }
    }

    // ── Основная петля ─────────────────────────────────────────────────────────

    private async Task RunLoopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Петля захвата запущена.");

        // Кэш настроек: перечитываем при необходимости
        IReadOnlyList<RoiCalibration> rois = Array.Empty<RoiCalibration>();
        bool roisLoaded = false;

        while (!ct.IsCancellationRequested)
        {
            // ── Получить интервал опроса из настроек ─────────────────────────
            int pollIntervalMs = 1500;
            try
            {
                WidgetSettings settings = await _settingsRepository.GetWidgetSettingsAsync()
                    .ConfigureAwait(false);
                pollIntervalMs = settings.PollIntervalMs > 0 ? settings.PollIntervalMs : 1500;

                // Загружаем ROI-калибровки (однократно; перезагружаем при изменении настроек,
                // но для v1 достаточно загрузить один раз в начале каждой итерации только если не загружены).
                if (!roisLoaded)
                {
                    rois = await _settingsRepository.GetRoiCalibrationsAsync()
                        .ConfigureAwait(false);
                    roisLoaded = true;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Не удалось прочитать настройки; используется интервал по умолчанию.");
            }

            // ── Получить кадр ────────────────────────────────────────────────
            CapturedFrame? frame = null;
            try
            {
                frame = await _session.TryGetFrameAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка захвата кадра; переход в Waiting.");
                PublishStaleSnapshot(_session.State);
                await DelayAsync(pollIntervalMs, ct).ConfigureAwait(false);
                continue;
            }

            // ── null-кадр: окно не готово ────────────────────────────────────
            if (frame is null)
            {
                PublishStaleSnapshot(_session.State);
                await DelayAsync(pollIntervalMs, ct).ConfigureAwait(false);
                continue;
            }

            // ── Обработать кадр ───────────────────────────────────────────────
            using (frame)
            {
                try
                {
                    await ProcessFrameAsync(frame, rois, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Ошибка обработки кадра; публикуем IsStale-снимок.");
                    PublishStaleSnapshot(CaptureState.Capturing);
                }
            }

            await DelayAsync(pollIntervalMs, ct).ConfigureAwait(false);
        }

        _logger.LogInformation("Петля захвата остановлена.");
    }

    /// <summary>
    /// Обрабатывает один кадр: детекция вкладки → извлечение → валидация → публикация.
    /// </summary>
    private async Task ProcessFrameAsync(
        CapturedFrame frame,
        IReadOnlyList<RoiCalibration> rois,
        CancellationToken ct)
    {
        GameMechanicsConfig cfg = _gameMechanics.Current;

        // ── Детекция активной вкладки ────────────────────────────────────────
        TabRef? activeTab = null;
        RoiCalibration? activeTabRoi = FindActiveTabRoi(rois);
        if (activeTabRoi is not null)
        {
            try
            {
                activeTab = await _tabDetector.DetectActiveTabAsync(frame, activeTabRoi, cfg, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Детекция вкладки не удалась; продолжаем без activeTab.");
            }
        }

        // ── Извлечение полей ─────────────────────────────────────────────────
        RawObservation obs = await _fieldExtractor.ExtractAsync(frame, rois, activeTab, cfg, ct)
            .ConfigureAwait(false);

        // ── Валидация → MetricSample ──────────────────────────────────────────
        MetricSample sample = _validator.Validate(obs, _lastReliableSample, ConfidenceThreshold);

        // ── Обновить скользящий буфер и последний надёжный сэмпл ─────────────
        if (sample.IsReliable)
        {
            _lastReliableSample = sample;
            AddToReliableBuffer(sample);

            // Обновляем «последние известные» значения из надёжного сэмпла
            UpdateLastKnownValues(sample, obs);
        }

        // ── Вычислить темпы ───────────────────────────────────────────────────
        LiveRates rates = _reliableBuffer.Count > 1
            ? _metricsCalculator.ComputeLiveRates(_reliableBuffer)
            : new LiveRates(0, 0, new Dictionary<int, double>());

        // ── Собрать и опубликовать снимок ─────────────────────────────────────
        DateTime? lastReliableUtc = _lastReliableSample?.TakenAtUtc;
        bool isStale = IsSnapshotStale(CaptureState.Capturing, lastReliableUtc);

        PublishSnapshot(new LiveStatsSnapshot(
            State:           CaptureState.Capturing,
            Rates:           rates,
            Gold:            _lastKnownGold,
            HeroLevel:       _lastKnownHeroLevel,
            HeroClass:       _lastKnownHeroClass,
            HeroDamage:      _lastKnownHeroDamage,
            Stage:           _lastKnownStage,
            LastReliableUtc: lastReliableUtc,
            IsStale:         isStale));
    }

    // ── Вспомогательные методы ─────────────────────────────────────────────────

    /// <summary>
    /// Публикует снимок с устаревшими данными (окно недоступно или ошибка захвата).
    /// Последние известные значения сохраняются; IsStale=true.
    /// </summary>
    private void PublishStaleSnapshot(CaptureState state)
    {
        LiveRates rates = _reliableBuffer.Count > 1
            ? _metricsCalculator.ComputeLiveRates(_reliableBuffer)
            : new LiveRates(0, 0, new Dictionary<int, double>());

        PublishSnapshot(new LiveStatsSnapshot(
            State:           state,
            Rates:           rates,
            Gold:            _lastKnownGold,
            HeroLevel:       _lastKnownHeroLevel,
            HeroClass:       _lastKnownHeroClass,
            HeroDamage:      _lastKnownHeroDamage,
            Stage:           _lastKnownStage,
            LastReliableUtc: _lastReliableSample?.TakenAtUtc,
            IsStale:         true));
    }

    /// <summary>
    /// Атомарно обновляет <see cref="Current"/> и поднимает <see cref="SnapshotUpdated"/>.
    /// </summary>
    private void PublishSnapshot(LiveStatsSnapshot snapshot)
    {
        _current = snapshot;
        SnapshotUpdated?.Invoke(this, snapshot);
    }

    /// <summary>
    /// Находит ROI для поля «activeTab» в списке калибровок. Возвращает null, если не задан.
    /// </summary>
    private static RoiCalibration? FindActiveTabRoi(IReadOnlyList<RoiCalibration> rois)
    {
        foreach (RoiCalibration roi in rois)
        {
            if (string.Equals(roi.FieldKey, "activeTab", StringComparison.OrdinalIgnoreCase))
                return roi;
        }
        return null;
    }

    /// <summary>
    /// Добавляет сэмпл в буфер, ограничивая размер <see cref="MaxReliableBufferSize"/>.
    /// </summary>
    private void AddToReliableBuffer(MetricSample sample)
    {
        _reliableBuffer.Add(sample);
        if (_reliableBuffer.Count > MaxReliableBufferSize)
            _reliableBuffer.RemoveAt(0);
    }

    /// <summary>
    /// Обновляет «последние известные» значения из надёжного сэмпла и наблюдения.
    /// </summary>
    private void UpdateLastKnownValues(MetricSample sample, RawObservation obs)
    {
        if (sample.Gold.HasValue)
            _lastKnownGold = sample.Gold;
        if (sample.HeroLevel.HasValue)
            _lastKnownHeroLevel = sample.HeroLevel;
        if (sample.HeroDamage.HasValue)
            _lastKnownHeroDamage = sample.HeroDamage;

        // Класс героя — из сырого наблюдения (не персистируется в MetricSample напрямую)
        if (obs.HeroClassText is not null)
            _lastKnownHeroClass = obs.HeroClassText;

        // Текущий этап — из NextLocation (следующая локация = current+1) или StageRef сэмпла
        if (sample.NextLocation.HasValue)
        {
            // NextLocation = current+1; используем как индикатор текущего этапа
            _lastKnownStage = sample.NextLocation;
        }
    }

    /// <summary>
    /// Определяет, является ли снимок устаревшим:
    /// состояние не Capturing, либо последний надёжный замер старше <see cref="StaleThresholdSeconds"/>.
    /// </summary>
    private static bool IsSnapshotStale(CaptureState state, DateTime? lastReliableUtc)
    {
        if (state != CaptureState.Capturing)
            return true;

        if (lastReliableUtc is null)
            return true;

        return (DateTime.UtcNow - lastReliableUtc.Value).TotalSeconds > StaleThresholdSeconds;
    }

    /// <summary>
    /// Задержка с поддержкой отмены; не бросает при отмене (только возвращает).
    /// </summary>
    private static async Task DelayAsync(int milliseconds, CancellationToken ct)
    {
        try
        {
            await Task.Delay(milliseconds, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* нормальный выход */ }
    }
}
