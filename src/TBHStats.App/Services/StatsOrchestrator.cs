using System.Linq;
using Microsoft.Extensions.Logging;
using TBHStats.Capture;
using TBHStats.Capture.Ocr;
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
    private readonly IOcrReader _ocrReader;
    private readonly ILogger<StatsOrchestrator> _logger;
    private readonly RunRecorder? _runRecorder;

    // ── Константы ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Минимальная уверенность OCR для принятия значения поля (R4, FR-005/FR-010).
    /// </summary>
    /// <remarks>
    /// Уверенность здесь — геометрическое покрытие (доля площади ROI, занятая распознанным текстом),
    /// а НЕ вероятностная достоверность. Для коротких числовых полей покрытие объективно низкое:
    /// на живой игре корректные чтения дают 0.08–0.48 (напр. heroLevel «27» → 0.08, gold → 0.23).
    /// Поэтому порог низкий — он лишь отсекает почти-пустой шум; основная валидация значений
    /// выполняется парсером (число/время/этап обязаны корректно распарситься) и confidence-фильтром
    /// в <c>IObservationValidator</c> (золото — расходуемый баланс, монотонность НЕ проверяется).
    /// </remarks>
    private const double ConfidenceThreshold = 0.02;

    /// <summary>
    /// Максимальный размер скользящего буфера надёжных сэмплов.
    /// </summary>
    private const int MaxReliableBufferSize = 200;

    /// <summary>
    /// Порог устаревания данных: если последний надёжный замер старше этого числа секунд —
    /// снимок помечается как IsStale.
    /// </summary>
    private const int StaleThresholdSeconds = 30;

    /// <summary>Окно живого темпа: учитываются только надёжные сэмплы за последние N секунд
    /// (отзывчивость к смене этапа). Подбирается эмпирически; ~90 с покрывает 1–2 клира этапа.</summary>
    private const int LiveRateWindowSeconds = 90;

    // ── Состояние ─────────────────────────────────────────────────────────────

    private volatile LiveStatsSnapshot _current = LiveStatsSnapshot.Empty;

    /// <summary>Последний надёжный сэмпл (база для детекции смены героя и отображения «последних известных»).</summary>
    private MetricSample? _lastReliableSample;

    /// <summary>Скользящий буфер надёжных сэмплов для вычисления темпов.</summary>
    private readonly List<MetricSample> _reliableBuffer = [];

    /// <summary>Последние известные «живые» значения, показываемые пока State=Waiting/NotFound.</summary>
    private long? _lastKnownGold;
    private long? _lastKnownXp;
    private long? _lastKnownXpToLevel;
    private int? _lastKnownHeroLevel;
    private string? _lastKnownHeroClass;
    private long? _lastKnownHeroDamage;
    private StageRef? _lastKnownStage;
    private IReadOnlyDictionary<int, int> _lastKnownChests = new Dictionary<int, int>();

    /// <summary>Прогресс этапа из последнего кадра (визуальный детектор, [0..1]), null до первого кадра.</summary>
    private double? _lastKnownStageProgress;
    /// <summary>Признак боя с боссом из последнего кадра, null до первого кадра.</summary>
    private bool? _lastKnownBossPresent;
    /// <summary>Момент UTC, когда начался текущий сегмент этапа (привязан к визуальному прогрессу, T063 Phase 2 fix). null = таймер не запущен.</summary>
    private DateTime? _stageSegmentStartUtc;
    /// <summary>Последнее посчитанное время на этапе (секунды), null до первого старта сегмента.</summary>
    private int? _lastKnownStageElapsedSeconds;
    /// <summary>Был ли босс (BossPresent / прогресс ≈1.0) в ТЕКУЩЕМ сегменте — отличает «этап пройден» от «рестарт».</summary>
    private bool _bossSeenInSegment;
    /// <summary>
    /// Начат ли текущий сегмент от НАБЛЮДАЕМОГО сброса прогресса (т.е. начало этапа было увидено).
    /// false — сегмент стартован «с середины» (первый кадр после запуска виджета/возврата окна),
    /// его длительность НЕ отражает полное время прохождения → при завершении по боссу НЕ сохраняется.
    /// </summary>
    private bool _segmentStartedFromReset;
    /// <summary>Длительность последней ПРОЙДЕННОЙ (по боссу) попытки в секундах — показывается в скобках. null до первого прохождения.</summary>
    private int? _lastCompletedStageSeconds;

    /// <summary>Порог падения прогресса (абсолютный), трактуемый как граница сегмента (новый/перезапущенный этап).</summary>
    private const double StageProgressDropThreshold = 0.10;
    /// <summary>Порог прогресса, при котором считаем, что был бой с боссом (1.0 vs макс. пути ≈0.95).</summary>
    private const double BossProgressThreshold = 0.99;

    // ── EMA-сглаживание темпов (FR-006; сглаживание дёрганья OCR по ~5 снимкам) ──
    // α = 2/(N+1), N=5 → 1/3. Сглаживаем публикуемые золото/ч и опыт/ч; «До уровня» считается
    // по сглаженному опыт/ч. null = ещё не инициализировано (первое значение берётся как есть).
    private const double EmaAlpha = 2.0 / (5 + 1);
    private double? _emaGoldPerHour;
    private double? _emaXpPerHour;
    private IReadOnlyDictionary<int, double> _lastChestPerHour = new Dictionary<int, double>();

    /// <summary>
    /// Нормализованный ключ класса героя, чьи сэмплы сейчас находятся в буфере (эпоха измерения).
    /// null — эпоха ещё не инициализирована. При смене ключа буфер сбрасывается «с нуля».
    /// </summary>
    private string? _currentHeroClassKey;

    /// <summary>Время последнего диагностического лога здоровья захвата (UTC). Throttle ~30 с.</summary>
    private DateTime _lastHealthLogUtc = DateTime.MinValue;

    /// <summary>
    /// Флаг «потеря окна уже обработана» — предотвращает повторный сброс <see cref="RunRecorder"/>
    /// на каждой итерации цикла, пока окно остаётся недоступным (NotFound).
    /// Сбрасывается в <c>false</c> при успешной обработке кадра (State=Capturing).
    /// Waiting (окно свёрнуто) НЕ вызывает сброс: незавершённый забег должен быть продолжен
    /// после восстановления окна — сохраняем накопители.
    /// </summary>
    private bool _windowLostHandled;

    /// <summary>
    /// Стабилизатор поля nextLocation: debounce 2 кадра + sanity по ResolveStageId.
    /// Гасит транзиентный OCR-шум от фонового огня на локациях ~3-6..3-10 (ADR-008).
    /// </summary>
    private readonly NextLocationStabilizer _nextLocationStabilizer = new();

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
    /// <param name="runRecorder">
    /// Опциональный recorder забегов. Если null — запись забегов отключена
    /// (поведение US1 не затрагивается). Регистрируется в <c>Composition</c>
    /// после T036 как ненулевой.
    /// </param>
    public StatsOrchestrator(
        ICaptureSession session,
        ITabDetector tabDetector,
        IFieldExtractor fieldExtractor,
        IObservationValidator validator,
        IMetricsCalculator metricsCalculator,
        IGameMechanics gameMechanics,
        ISettingsRepository settingsRepository,
        IOcrReader ocrReader,
        ILogger<StatsOrchestrator> logger,
        RunRecorder? runRecorder = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(tabDetector);
        ArgumentNullException.ThrowIfNull(fieldExtractor);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(metricsCalculator);
        ArgumentNullException.ThrowIfNull(gameMechanics);
        ArgumentNullException.ThrowIfNull(settingsRepository);
        ArgumentNullException.ThrowIfNull(ocrReader);
        ArgumentNullException.ThrowIfNull(logger);

        _session            = session;
        _tabDetector        = tabDetector;
        _fieldExtractor     = fieldExtractor;
        _validator          = validator;
        _metricsCalculator  = metricsCalculator;
        _gameMechanics      = gameMechanics;
        _settingsRepository = settingsRepository;
        _ocrReader          = ocrReader;
        _logger             = logger;
        _runRecorder        = runRecorder;
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

        // ROI-калибровки: объявляем до цикла, чтобы при исключении сохранялся последний успешный набор.
        IReadOnlyList<RoiCalibration> rois = Array.Empty<RoiCalibration>();

        while (!ct.IsCancellationRequested)
        {
            // ── Получить интервал опроса и ROI-калибровки из настроек ────────
            int pollIntervalMs = 1500;
            try
            {
                WidgetSettings settings = await _settingsRepository.GetWidgetSettingsAsync()
                    .ConfigureAwait(false);
                pollIntervalMs = settings.PollIntervalMs > 0 ? settings.PollIntervalMs : 1500;

                // Перечитываем ROI каждую итерацию: калибровка применяется без перезапуска приложения
                // (ранее латч roisLoaded грузил их один раз — изменения калибровки игнорировались до рестарта).
                // SELECT по таблице ~21 строки ничтожен по стоимости — settings читаются так же каждую итерацию.
                rois = await _settingsRepository.GetRoiCalibrationsAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Не удалось прочитать настройки/калибровки; используется предыдущий набор ROI.");
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

                // Сброс RunRecorder при полной потере окна (NotFound) — один раз на эпизод.
                // Waiting (свёрнутое окно) не сбрасывается: игра ещё жива, незавершённый
                // забег должен продолжиться после восстановления окна.
                if (_session.State == CaptureState.NotFound && !_windowLostHandled)
                {
                    _windowLostHandled = true;
                    _runRecorder?.Reset();
                    _logger.LogDebug("RunRecorder сброшен: окно игры потеряно (NotFound).");

                    // Сбросить сегментный таймер: при возврате окна не должно ложно сработать
                    // «завершение по боссу» из-за устаревшего флага _bossSeenInSegment.
                    // _lastCompletedStageSeconds НЕ сбрасываем — это историческая справка.
                    _stageSegmentStartUtc    = null;
                    _bossSeenInSegment       = false;
                    _segmentStartedFromReset = false;
                    _lastKnownStageProgress  = null;

                    // Сбросить debounce-стрик nextLocation: после возврата окна на другом
                    // этапе устаревший pending не должен мешать быстрому определению этапа.
                    // Stable-значение также сбрасывается — потребует 2 кадра для нового этапа
                    // (приемлемая задержка при восстановлении окна).
                    _nextLocationStabilizer.Reset();
                }

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

        // Окно доступно — снимаем флаг «потеря окна» (следующая потеря снова вызовет Reset).
        _windowLostHandled = false;

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

        // ── Стабилизация «Этап» (nextLocation) — оконное голосование + sanity ──
        // Гасит транзиентный/флакирующий OCR-шум nextLocation (наблюдается в акте 3).
        // Stabile-значение записывается обратно в sample.NextLocation — оно используется
        // и в резолве StageId ниже, и в обновлении _lastKnownStage, и в RunRecorder.
        sample.NextLocation = _nextLocationStabilizer.Observe(sample.NextLocation, cfg);

        // ── Резолв StageId из NextLocation через конфиг (T063 Phase 3) ───────
        // Текущий этап = nextLocation − 1 (тот же источник, что в _lastKnownStage).
        // Резолв выполняется в оркестраторе: у валидатора нет конфига by design (ADR-009).
        StageRef? currentStageRef = sample.NextLocation?.Previous();
        sample.StageId = currentStageRef.HasValue ? cfg.ResolveStageId(currentStageRef.Value) : null;

        // «Этап» берётся из nextLocation (MainZone, видна всегда) и НЕ зависит от чтения
        // золота/опыта (sample.IsReliable). Обновляем каждый Capturing-кадр из стабилизированного
        // значения; при отсутствии (null) держим последнее известное.
        if (currentStageRef.HasValue)
            _lastKnownStage = currentStageRef.Value;

        // ── Диагностика здоровья захвата (throttled ~30 с, только когда нет достоверных данных) ──
        if (!sample.IsReliable)
        {
            DateTime nowUtc = DateTime.UtcNow;
            if ((nowUtc - _lastHealthLogUtc).TotalSeconds >= 30)
            {
                _lastHealthLogUtc = nowUtc;
                string activeTabKey = activeTab?.Key ?? "none";
                string recognized = obs.PerFieldConfidence.Count > 0
                    ? string.Join(", ", obs.PerFieldConfidence.Select(kv => $"{kv.Key}={kv.Value:F2}"))
                    : "(нет распознанных полей)";
                _logger.LogInformation(
                    "Захват активен, но достоверных данных нет. ROI={RoiCount}, activeTab={ActiveTab}, распознано: {Recognized}",
                    rois.Count, activeTabKey, recognized);

                // ВРЕМЕННАЯ ДИАГНОСТИКА (удалить после решения проблемы пустых значений):
                // сырой OCR-текст ключевых ROI + размеры кадра.
                _logger.LogInformation(
                    "[Diag] bitmap={Bw}x{Bh} client={Cw}x{Ch} ROI={Count}",
                    frame.Bitmap.PixelWidth, frame.Bitmap.PixelHeight,
                    frame.ClientSize.Width, frame.ClientSize.Height, rois.Count);

                foreach (string key in new[] { "activeTab", "gold", "xp", "heroLevel" })
                {
                    RoiCalibration? r = null;
                    foreach (RoiCalibration cand in rois)
                        if (string.Equals(cand.FieldKey, key, StringComparison.OrdinalIgnoreCase)) { r = cand; break; }

                    if (r is null)
                    {
                        _logger.LogInformation("[Diag] {Key}: ROI не задан", key);
                        continue;
                    }

                    try
                    {
                        OcrResult dr = await _ocrReader.ReadAsync(frame, r, ct).ConfigureAwait(false);
                        string text = dr.RawText is { Length: > 0 } ? dr.RawText.Replace("\r", " ").Replace("\n", " ") : "";
                        if (text.Length > 80) text = text[..80];
                        _logger.LogInformation(
                            "[Diag] {Key}: rec={Rec} conf={Conf:F2} text='{Text}'",
                            key, dr.Recognized, dr.Confidence, text);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _logger.LogInformation("[Diag] {Key}: ошибка OCR {Err}", key, ex.Message);
                    }
                }
                // КОНЕЦ ВРЕМЕННОЙ ДИАГНОСТИКИ
            }
        }

        // ── Обновить скользящий буфер и последний надёжный сэмпл ─────────────
        if (sample.IsReliable)
        {
            // ── Детекция смены героя: два независимых сигнала ──────────────────
            //
            // Сигнал 1 — класс героя (belt-and-suspenders поверх структурного guard в MetricsCalculator).
            // Нормализуем класс героя из OCR-текста в известный ключ конфига (case-insensitive Contains).
            // Защита от OCR-шума: сброс только когда новый класс РАСПОЗНАН (newClassKey != null) и
            // реально отличается от текущей эпохи. Нераспознанный текст (null) сброс НЕ вызывает.
            //
            // Сигнал 2 — структурные инварианты HeroLevel/XpToLevel (HeroSwitchDetector в Core).
            // Надёжен даже когда OCR не читает HeroClassText на кадрах переключения героя.
            // Логика: падение уровня (герой не теряет уровни) или смена потолка XpToLevel без
            // сигнатуры level-up (потолок меняется только при level-up, а level-up = Xp ≥ 80% потолка).
            MetricSample? prevReliable = _lastReliableSample;
            string? newClassKey = ResolveHeroClassKey(obs.HeroClassText, cfg);
            bool classSwitch = newClassKey is not null
                               && _currentHeroClassKey is not null
                               && newClassKey != _currentHeroClassKey;
            bool signalSwitch = prevReliable is not null
                                && HeroSwitchDetector.IsHeroSwitch(prevReliable, sample);

            if (classSwitch || signalSwitch)
            {
                _logger.LogInformation(
                    "Обнаружена смена героя (class:{Cls}, signal:{Sig}); живые темпы пересчитываются с нуля.",
                    classSwitch, signalSwitch);
                _reliableBuffer.Clear();
                _lastReliableSample = null;
                _emaXpPerHour       = null;
                _emaGoldPerHour     = null;
                _lastChestPerHour   = new Dictionary<int, double>();
            }

            // Обновляем ключ эпохи (первая инициализация происходит без сброса, т.к. _currentHeroClassKey == null).
            if (newClassKey is not null)
                _currentHeroClassKey = newClassKey;

            _lastReliableSample = sample;
            AddToReliableBuffer(sample);

            // Обновляем «последние известные» значения из надёжного сэмпла
            UpdateLastKnownValues(sample, obs);
        }

        // ── Передать кадр в RunRecorder (запись забегов) ─────────────────────
        if (_runRecorder is not null)
        {
            try
            {
                await _runRecorder.OnFrameAsync(obs, sample, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RunRecorder вернул необработанное исключение; петля продолжается.");
            }
        }

        // ── Вычислить темпы + EMA-сглаживание ─────────────────────────────────

        // Живой темп считаем по короткому скользящему окну (последние LiveRateWindowSeconds),
        // а НЕ по всему буферу — иначе значение лагает и медленно сходится (этапы идут <60 с).
        IReadOnlyList<MetricSample> recent;
        if (_reliableBuffer.Count > 0)
        {
            DateTime latestUtc = _reliableBuffer[^1].TakenAtUtc;
            var slice = new List<MetricSample>(_reliableBuffer.Count);
            for (int i = _reliableBuffer.Count - 1; i >= 0; i--)
            {
                if ((latestUtc - _reliableBuffer[i].TakenAtUtc).TotalSeconds > LiveRateWindowSeconds)
                    break;                       // буфер упорядочен по времени — дальше только старее
                slice.Add(_reliableBuffer[i]);
            }
            slice.Reverse();                     // восстановить хронологический порядок для ComputeLiveRates
            recent = slice;
        }
        else
        {
            recent = _reliableBuffer;
        }

        LiveRates raw = recent.Count > 1
            ? _metricsCalculator.ComputeLiveRates(recent)
            : new LiveRates(0, 0, new Dictionary<int, double>());

        if (recent.Count > 1)
        {
            if (RateOutlierDetector.IsXpRateOutlier(raw.XpPerHour, _emaXpPerHour))
            {
                // Переходный OCR-misread XP: ложная дельта отравила кумулятивную ставку.
                // Отбрасываем кадр (НЕ обновляем EMA выбросом — оставляем последние корректные темпы
                // для отображения) и сбрасываем окно, чтобы ложная дельта не держалась в буфере.
                _logger.LogWarning(
                    "[XpRateOutlier] raw={Raw:F0}/ч ema={Ema:F0}/ч — выброс отброшен, окно темпов сброшено.",
                    raw.XpPerHour, _emaXpPerHour ?? 0.0);
                _reliableBuffer.Clear();
                _lastReliableSample = null;
                // EMA НЕ трогаем: _emaXpPerHour/_emaGoldPerHour/_lastChestPerHour сохраняют последние корректные значения.
            }
            else
            {
                _emaGoldPerHour   = Ema(_emaGoldPerHour, raw.GoldPerHour);
                _emaXpPerHour     = Ema(_emaXpPerHour, raw.XpPerHour);
                _lastChestPerHour = raw.ChestPerHourByType;
            }
        }
        // else (recent.Count <= 1, окно перестраивается после сброса): EMA НЕ обновляем — держим последние
        // корректные темпы (без обнуления). Это отличие от сброса при СМЕНЕ ГЕРОЯ (там EMA=null → показывает 0).

        LiveRates rates = new(_emaGoldPerHour ?? 0.0, _emaXpPerHour ?? 0.0, _lastChestPerHour);

        // ── Прогресс этапа / признак босса из текущего кадра ──────────────────
        // prevProgress берём ДО обновления last-known — для детекции падения.
        double? prevProgress = _lastKnownStageProgress;
        if (obs.StageProgress.HasValue)
            _lastKnownStageProgress = obs.StageProgress;
        if (obs.BossPresent.HasValue)
            _lastKnownBossPresent = obs.BossPresent;

        // Босс в текущем сегменте: явный BossPresent ИЛИ прогресс ≈1.0 (бой), ADR-024.
        if (obs.BossPresent == true ||
            (obs.StageProgress is double bp && bp >= BossProgressThreshold))
        {
            _bossSeenInSegment = true;
        }

        // ── Сегментный таймер этапа, синхронизированный с ПРОГРЕССОМ (T063 Phase 2 fix) ──
        // Прогресс по ходу этапа растёт; заметное падение = новый/перезапущенный этап → таймер с нуля.
        DateTime segmentNow = DateTime.UtcNow;
        if (obs.StageProgress is double cur)
        {
            bool progressDropped = prevProgress.HasValue
                && cur < prevProgress.Value - StageProgressDropThreshold;

            if (progressDropped)
            {
                // Перед падением был босс → этап ПРОЙДЕН: сохранить длительность завершённой попытки.
                // НО только если завершаемый сегмент был начат от наблюдаемого сброса (начало этапа
                // увидено) — иначе длительность считалась бы «с середины» (виджет запущен на фазе
                // босса) и не отражала бы полное время прохождения.
                if (_bossSeenInSegment && _segmentStartedFromReset && _stageSegmentStartUtc.HasValue)
                {
                    _lastCompletedStageSeconds =
                        (int)Math.Max(0, (segmentNow - _stageSegmentStartUtc.Value).TotalSeconds);
                }
                // Падение без босса (рестарт/смерть) или сегмент «с середины» — длительность НЕ сохраняем.

                // Вариант 2: в любом случае немедленно стартуем новый сегмент с нуля.
                // Это падение — наблюдаемое начало нового этапа → сегмент валиден для учёта времени.
                _stageSegmentStartUtc    = segmentNow;
                _bossSeenInSegment       = false;
                _segmentStartedFromReset = true;
            }
            else if (_stageSegmentStartUtc is null)
            {
                // Первый кадр с прогрессом — запустить таймер, но начало этапа НЕ наблюдалось
                // (стартуем «с середины»): время этой попытки не сохраняем при завершении.
                _stageSegmentStartUtc    = segmentNow;
                _segmentStartedFromReset = false;
            }
        }

        _lastKnownStageElapsedSeconds = _stageSegmentStartUtc.HasValue
            ? (int)Math.Max(0, (segmentNow - _stageSegmentStartUtc.Value).TotalSeconds)
            : null;

        // ── Собрать и опубликовать снимок ─────────────────────────────────────
        DateTime? lastReliableUtc = _lastReliableSample?.TakenAtUtc;
        bool isStale = IsSnapshotStale(CaptureState.Capturing, lastReliableUtc);

        PublishSnapshot(new LiveStatsSnapshot(
            State:                        CaptureState.Capturing,
            Rates:                        rates,
            Gold:                         _lastKnownGold,
            Xp:                           _lastKnownXp,
            XpToLevel:                    _lastKnownXpToLevel,
            HeroLevel:                    _lastKnownHeroLevel,
            HeroClass:                    _lastKnownHeroClass,
            HeroDamage:                   _lastKnownHeroDamage,
            Stage:                        _lastKnownStage,
            StageProgress:                _lastKnownStageProgress,
            BossPresent:                  _lastKnownBossPresent,
            StageElapsedSeconds:          _lastKnownStageElapsedSeconds,
            LastCompletedStageSeconds:    _lastCompletedStageSeconds,
            LastReliableUtc:              lastReliableUtc,
            IsStale:                      isStale,
            Chests:                       _lastKnownChests));
    }

    // ── Вспомогательные методы ─────────────────────────────────────────────────

    /// <summary>
    /// Публикует снимок с устаревшими данными (окно недоступно или ошибка захвата).
    /// Последние известные значения сохраняются; IsStale=true.
    /// </summary>
    private void PublishStaleSnapshot(CaptureState state)
    {
        // Используем последние сглаженные темпы (EMA не обновляем — окно недоступно).
        LiveRates rates = new(_emaGoldPerHour ?? 0.0, _emaXpPerHour ?? 0.0, _lastChestPerHour);

        // Таймер этапа НЕ инкрементируется — отдаём последнее посчитанное значение.
        PublishSnapshot(new LiveStatsSnapshot(
            State:                        state,
            Rates:                        rates,
            Gold:                         _lastKnownGold,
            Xp:                           _lastKnownXp,
            XpToLevel:                    _lastKnownXpToLevel,
            HeroLevel:                    _lastKnownHeroLevel,
            HeroClass:                    _lastKnownHeroClass,
            HeroDamage:                   _lastKnownHeroDamage,
            Stage:                        _lastKnownStage,
            StageProgress:                _lastKnownStageProgress,
            BossPresent:                  _lastKnownBossPresent,
            StageElapsedSeconds:          _lastKnownStageElapsedSeconds,
            LastCompletedStageSeconds:    _lastCompletedStageSeconds,
            LastReliableUtc:              _lastReliableSample?.TakenAtUtc,
            IsStale:                      true,
            Chests:                       _lastKnownChests));
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
        if (sample.Xp.HasValue)
            _lastKnownXp = sample.Xp;
        if (sample.XpToLevel.HasValue)
            _lastKnownXpToLevel = sample.XpToLevel;
        if (sample.HeroLevel.HasValue)
            _lastKnownHeroLevel = sample.HeroLevel;
        if (sample.HeroDamage.HasValue)
            _lastKnownHeroDamage = sample.HeroDamage;

        // Класс героя — из сырого наблюдения (не персистируется в MetricSample напрямую)
        if (obs.HeroClassText is not null)
            _lastKnownHeroClass = obs.HeroClassText;

        // _lastKnownStage обновляется ВНЕ гейта IsReliable (сразу после резолва currentStageRef выше),
        // т.к. nextLocation читается из MainZone (видна всегда) и не зависит от золота/опыта.

        // Счётчики сундуков транзиентны: обновляем ВСЕГДА при надёжном сэмпле,
        // даже если список пуст — отражает актуальное состояние OCR-кадра.
        _lastKnownChests = sample.Chests.ToDictionary(c => c.ChestTypeId, c => c.Count);
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
    /// Экспоненциальное скользящее среднее (EMA) для сглаживания темпов.
    /// Первое значение принимается как есть; далее prev + α·(raw − prev), α = <see cref="EmaAlpha"/>.
    /// </summary>
    private static double Ema(double? previous, double raw)
        => previous is double p ? p + EmaAlpha * (raw - p) : raw;

    /// <summary>
    /// Нормализует сырой OCR-текст класса героя в машинный ключ из справочника <see cref="GameMechanicsConfig.HeroClasses"/>.
    /// Сопоставление — case-insensitive Contains по <see cref="HeroClass.Key"/> и <see cref="HeroClass.DisplayName"/>.
    /// Возвращает <see cref="HeroClass.Key"/> первого совпавшего класса, иначе <c>null</c>.
    /// </summary>
    private static string? ResolveHeroClassKey(string? heroClassText, GameMechanicsConfig cfg)
    {
        if (heroClassText is null or { Length: 0 })
            return null;

        foreach (HeroClass heroClass in cfg.HeroClasses)
        {
            if (heroClassText.Contains(heroClass.Key, StringComparison.OrdinalIgnoreCase) ||
                heroClassText.Contains(heroClass.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                return heroClass.Key;
            }
        }

        return null;
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
