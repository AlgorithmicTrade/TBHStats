using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TBHStats.Capture;
using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;
using TBHStats.Data.Repositories;

namespace TBHStats.App.Services;

/// <summary>
/// Stateful-сервис сборки и записи завершённых забегов этапов (FR-007, US2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Назначение.</b> Накапливает прирост золота, опыта и сундуков по потоку надёжных кадров.
/// При получении сигнала завершения этапа от <see cref="IStageCompletionDetector"/>
/// собирает <see cref="StageRun"/> + <see cref="StageRunChest"/>, записывает через
/// <see cref="IRunRepository"/> и инициирует пересчёт агрегата через
/// <see cref="IStageAggregateRepository"/>.
/// </para>
/// <para>
/// <b>Lifetime-решение.</b> Сервис является singleton, а <see cref="IRunRepository"/>,
/// <see cref="IStageAggregateRepository"/> и <see cref="ISettingsRepository"/> — scoped.
/// Для корректной работы зависимостей конструктор принимает <see cref="IServiceScopeFactory"/>;
/// scope создаётся и немедленно освобождается при каждом обращении к репозиториям
/// (только в момент записи завершённого забега).
/// </para>
/// <para>
/// <b>Ограничение v1.</b> Завершение забега зависит от визуальных полей MainZone
/// (<c>StageProgress</c>, <c>BossPresent</c>, <c>StageTimeSeconds</c>), которые в v1
/// <c>FieldExtractor</c> пока возвращает null (ROI-калибровки MainZone настраиваются в T049).
/// Поэтому запись забегов срабатывает только после того, как эти поля начнут поставляться —
/// механизм готов и будет протестирован на фикстурах и живой игре в рамках T049/T051.
/// </para>
/// <para>
/// <b>Обработка ошибок.</b> Все исключения внутри <see cref="OnFrameAsync"/> перехватываются,
/// логируются через <see cref="LogWarning"/> и не пробрасываются — петля захвата не должна
/// падать из-за сбоя записи статистики.
/// </para>
/// </remarks>
public sealed class RunRecorder
{
    // ── Зависимости ──────────────────────────────────────────────────────────

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IStageCompletionDetector _completionDetector;
    private readonly IGameMechanics _gameMechanics;
    private readonly ILogger<RunRecorder> _logger;

    // ── Состояние текущего забега ─────────────────────────────────────────────

    /// <summary>StageId текущего накапливаемого забега (null — не определён).</summary>
    private int? _currentStageId;

    /// <summary>Первое надёжное значение золота в начале забега (baseline).</summary>
    private long? _startGold;

    /// <summary>Последнее надёжное значение золота за текущий забег.</summary>
    private long? _lastGold;

    /// <summary>UTC-время первого надёжного кадра текущего забега.</summary>
    private DateTime? _runStartUtc;

    /// <summary>Накопленный прирост опыта за текущий забег.</summary>
    private long _xpAccum;

    /// <summary>Предыдущий XP в пределах уровня (для компенсации level-up).</summary>
    private long? _prevXp;

    /// <summary>Предыдущий XpToLevel (для компенсации level-up).</summary>
    private long? _prevXpToLevel;

    /// <summary>Предыдущий уровень героя (для детекции level-up).</summary>
    private int? _prevHeroLevel;

    /// <summary>Накопленные счётчики сундуков по ChestTypeId за текущий забег.</summary>
    private readonly Dictionary<int, int> _chestAccum = new();

    /// <summary>Предыдущие мгновенные «точки» сундуков (для вычисления положительных дельт).</summary>
    private readonly Dictionary<int, int> _prevChestPoints = new();

    /// <summary>Признак наличия золотого baseline (первое надёжное Gold установлено).</summary>
    private bool _hasBaseline;

    /// <summary>Последний надёжный уровень героя.</summary>
    private int? _lastHeroLevel;

    /// <summary>Последний надёжный урон героя.</summary>
    private long? _lastHeroDamage;

    /// <summary>Последний надёжный текст класса героя.</summary>
    private string? _lastHeroClassText;

    /// <summary>
    /// Признак того, что в ходе текущего забега stageId менялся
    /// (забег будет помечен IsPartial=true).
    /// </summary>
    private bool _stageChangedDuringRun;

    // ── Конструктор ───────────────────────────────────────────────────────────

    /// <summary>
    /// Создаёт <see cref="RunRecorder"/>.
    /// </summary>
    /// <param name="scopeFactory">
    /// Фабрика scope для получения scoped-зависимостей
    /// (<see cref="IRunRepository"/>, <see cref="IStageAggregateRepository"/>,
    /// <see cref="ISettingsRepository"/>) из singleton-контекста.
    /// </param>
    /// <param name="completionDetector">Детектор завершения этапа (singleton).</param>
    /// <param name="gameMechanics">Конфигурация механик игры (singleton).</param>
    /// <param name="logger">Логгер.</param>
    public RunRecorder(
        IServiceScopeFactory scopeFactory,
        IStageCompletionDetector completionDetector,
        IGameMechanics gameMechanics,
        ILogger<RunRecorder> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(completionDetector);
        ArgumentNullException.ThrowIfNull(gameMechanics);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory       = scopeFactory;
        _completionDetector = completionDetector;
        _gameMechanics      = gameMechanics;
        _logger             = logger;
    }

    // ── Публичный API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Обрабатывает один кадр: накапливает данные текущего забега и при
    /// получении сигнала завершения — собирает и записывает <see cref="StageRun"/>.
    /// </summary>
    /// <param name="obs">Сырое наблюдение кадра.</param>
    /// <param name="sample">Валидированный MetricSample кадра.</param>
    /// <param name="ct">Токен отмены.</param>
    public async Task OnFrameAsync(RawObservation obs, MetricSample sample, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(obs);
        ArgumentNullException.ThrowIfNull(sample);

        try
        {
            await ProcessFrameInternalAsync(obs, sample, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RunRecorder: необработанное исключение при обработке кадра; пропускаем кадр.");
        }
    }

    /// <summary>
    /// Сбрасывает все накопители текущего забега и состояние детектора завершения.
    /// Вызывается при смене этапа, потере окна или перезапуске петли.
    /// </summary>
    public void Reset()
    {
        _currentStageId         = null;
        _startGold              = null;
        _lastGold               = null;
        _runStartUtc            = null;
        _xpAccum                = 0;
        _prevXp                 = null;
        _prevXpToLevel          = null;
        _prevHeroLevel          = null;
        _chestAccum.Clear();
        _prevChestPoints.Clear();
        _hasBaseline            = false;
        _lastHeroLevel          = null;
        _lastHeroDamage         = null;
        _lastHeroClassText      = null;
        _stageChangedDuringRun  = false;

        _completionDetector.Reset();
    }

    // ── Внутренняя логика ──────────────────────────────────────────────────────

    /// <summary>
    /// Основная логика обработки одного кадра (без внешнего try/catch).
    /// </summary>
    private async Task ProcessFrameInternalAsync(RawObservation obs, MetricSample sample, CancellationToken ct)
    {
        // Шаг 1: передаём кадр детектору завершения.
        StageCompletionEvent? completionEvent = _completionDetector.Observe(obs);

        // Шаг 2: определяем stageId кадра.
        int? frameStageId = sample.StageId;

        // Шаг 3: обработка смены этапа.
        if (_currentStageId.HasValue && frameStageId.HasValue && frameStageId.Value != _currentStageId.Value)
        {
            // StageId изменился без сигнала завершения — не записываем partial-забег,
            // просто сбрасываем накопители и начинаем заново для нового этапа.
            // v1-решение: незавершённые при смене этапа забеги не пишем, чтобы не плодить мусор.
            _logger.LogDebug(
                "RunRecorder: stageId сменился с {OldStage} на {NewStage} без события завершения — сброс накопителей.",
                _currentStageId.Value, frameStageId.Value);

            ResetAccumulatorsOnly();
            _stageChangedDuringRun = false;
        }

        // Обновляем текущий stageId (приоритет — ненулевое значение).
        if (frameStageId.HasValue)
        {
            if (_currentStageId.HasValue && _currentStageId.Value != frameStageId.Value)
                _stageChangedDuringRun = true;

            _currentStageId = frameStageId.Value;
        }

        // Шаг 4: накопление данных из надёжного сэмпла.
        if (sample.IsReliable)
        {
            AccumulateReliableFrame(obs, sample);
        }

        // Шаг 5: обработка события завершения.
        if (completionEvent is not null)
        {
            await HandleCompletionAsync(completionEvent.Value, obs, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Накапливает данные текущего забега из надёжного кадра.
    /// </summary>
    private void AccumulateReliableFrame(RawObservation obs, MetricSample sample)
    {
        DateTime frameUtc = obs.TakenAtUtc;

        // Установить время начала накопления (первый надёжный кадр).
        _runStartUtc ??= frameUtc;

        // ── Gold ──────────────────────────────────────────────────────────────
        if (sample.Gold.HasValue)
        {
            if (!_hasBaseline)
            {
                _startGold   = sample.Gold.Value;
                _hasBaseline = true;
            }

            _lastGold = sample.Gold.Value;
        }

        // ── XP с компенсацией level-up ────────────────────────────────────────
        if (sample.Xp.HasValue)
        {
            long curXp    = sample.Xp.Value;
            int  curLevel = sample.HeroLevel ?? _prevHeroLevel ?? 1;

            if (_prevXp.HasValue)
            {
                bool leveledUp = _prevHeroLevel.HasValue && curLevel > _prevHeroLevel.Value;
                long xpDelta;

                if (leveledUp && _prevXpToLevel.HasValue)
                {
                    // Прирост: добор до конца прошлого уровня + XP на текущем.
                    long doborToLevelEnd = Math.Max(0L, _prevXpToLevel.Value - _prevXp.Value);
                    xpDelta = doborToLevelEnd + Math.Max(0L, curXp);
                }
                else
                {
                    xpDelta = Math.Max(0L, curXp - _prevXp.Value);
                }

                _xpAccum += xpDelta;
            }

            _prevXp        = curXp;
            _prevXpToLevel = sample.XpToLevel;
            _prevHeroLevel = curLevel;
        }

        // ── Chests: положительные дельты мгновенных точек ────────────────────
        foreach (KeyValuePair<int, int> kv in obs.Chests)
        {
            int chestTypeId  = kv.Key;
            int currentPoints = kv.Value;

            if (_prevChestPoints.TryGetValue(chestTypeId, out int prevPoints))
            {
                int delta = currentPoints - prevPoints;
                if (delta > 0)
                {
                    _chestAccum.TryGetValue(chestTypeId, out int accumulated);
                    _chestAccum[chestTypeId] = accumulated + delta;
                }
            }

            _prevChestPoints[chestTypeId] = currentPoints;
        }

        // ── Hero context ──────────────────────────────────────────────────────
        if (sample.HeroLevel.HasValue)
            _lastHeroLevel    = sample.HeroLevel.Value;
        if (sample.HeroDamage.HasValue)
            _lastHeroDamage   = sample.HeroDamage.Value;
        if (obs.HeroClassText is not null)
            _lastHeroClassText = obs.HeroClassText;
    }

    /// <summary>
    /// Собирает <see cref="StageRun"/> и записывает его в репозиторий.
    /// </summary>
    private async Task HandleCompletionAsync(StageCompletionEvent ev, RawObservation lastObs, CancellationToken ct)
    {
        if (!_currentStageId.HasValue)
        {
            _logger.LogWarning("RunRecorder: событие завершения без известного stageId — пропускаем запись.");
            ResetAccumulatorsOnly();
            return;
        }

        int stageId = _currentStageId.Value;
        bool isPartial = false;

        // ── DurationSeconds ───────────────────────────────────────────────────
        int durationSeconds;
        if (ev.StageTimeSeconds.HasValue && ev.StageTimeSeconds.Value > 0)
        {
            durationSeconds = ev.StageTimeSeconds.Value;
        }
        else if (_runStartUtc.HasValue)
        {
            durationSeconds = Math.Max(1, (int)(ev.CompletedAtUtc - _runStartUtc.Value).TotalSeconds);
        }
        else
        {
            durationSeconds = 1;
            isPartial       = true;
        }

        // ── Gold ──────────────────────────────────────────────────────────────
        long goldGained;
        if (_hasBaseline && _lastGold.HasValue && _startGold.HasValue)
        {
            goldGained = Math.Max(0L, _lastGold.Value - _startGold.Value);
        }
        else
        {
            goldGained = 0;
            isPartial  = true;
        }

        // ── XP ────────────────────────────────────────────────────────────────
        long xpGained = Math.Max(0L, _xpAccum);

        // ── HeroSnapshot ──────────────────────────────────────────────────────
        int heroLevel  = _lastHeroLevel  ?? 1;
        long heroDamage = _lastHeroDamage ?? 0;
        int heroClassId = ResolveHeroClassId(_lastHeroClassText ?? lastObs.HeroClassText, ref isPartial);

        HeroSnapshot hero;
        try
        {
            hero = new HeroSnapshot(heroClassId, heroLevel, heroDamage);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            _logger.LogWarning(ex, "RunRecorder: некорректные значения HeroSnapshot (level={Level}, damage={Damage}); корректируем.", heroLevel, heroDamage);
            hero      = new HeroSnapshot(heroClassId, Math.Max(1, heroLevel), Math.Max(0L, heroDamage));
            isPartial = true;
        }

        // ── IsPartial: stageId менялся ────────────────────────────────────────
        if (_stageChangedDuringRun)
            isPartial = true;

        // ── Chests ────────────────────────────────────────────────────────────
        List<StageRunChest> chests = BuildChests();

        // ── Сборка StageRun ───────────────────────────────────────────────────
        StageRun run = new()
        {
            StageId         = stageId,
            DurationSeconds = durationSeconds,
            GoldGained      = goldGained,
            XpGained        = xpGained,
            Hero            = hero,
            CompletedAtUtc  = ev.CompletedAtUtc,
            IsPartial       = isPartial,
            Chests          = chests,
        };

        // ── Запись в репозиторий ──────────────────────────────────────────────
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();

            IRunRepository runRepo = scope.ServiceProvider.GetRequiredService<IRunRepository>();
            await runRepo.AddRunAsync(run, ct).ConfigureAwait(false);

            ISettingsRepository settingsRepo = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
            OptimizationProfile profile = await settingsRepo.GetOptimizationProfileAsync().ConfigureAwait(false);

            IStageAggregateRepository aggRepo = scope.ServiceProvider.GetRequiredService<IStageAggregateRepository>();
            await aggRepo.RecomputeForStageAsync(stageId, profile.RecentWindowSize, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "RunRecorder: записан забег stageId={StageId}, duration={Duration}s, gold={Gold}, xp={Xp}, partial={IsPartial}.",
                stageId, durationSeconds, goldGained, xpGained, isPartial);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RunRecorder: не удалось записать StageRun для stageId={StageId}.", stageId);
        }

        // Детектор уже сбросился внутри Observe(); сбрасываем накопители.
        ResetAccumulatorsOnly();
    }

    /// <summary>
    /// Резолвит Id класса героя по текстовому значению из OCR.
    /// Совпадение ищется по <see cref="HeroClass.Key"/> и <see cref="HeroClass.DisplayName"/>
    /// без учёта регистра. При отсутствии совпадения возвращает первый активный класс
    /// (или 0 если список пуст) и помечает забег как partial.
    /// </summary>
    private int ResolveHeroClassId(string? heroClassText, ref bool isPartial)
    {
        GameMechanicsConfig cfg = _gameMechanics.Current;
        IReadOnlyList<HeroClass> heroClasses = cfg.HeroClasses;

        if (!string.IsNullOrWhiteSpace(heroClassText))
        {
            // Точное совпадение по Key или DisplayName (без учёта регистра).
            foreach (HeroClass hc in heroClasses)
            {
                if (!hc.IsActive)
                    continue;

                if (string.Equals(hc.Key, heroClassText, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(hc.DisplayName, heroClassText, StringComparison.OrdinalIgnoreCase))
                {
                    return hc.Id;
                }
            }

            // Частичное совпадение (Contains).
            foreach (HeroClass hc in heroClasses)
            {
                if (!hc.IsActive)
                    continue;

                if (heroClassText.Contains(hc.Key, StringComparison.OrdinalIgnoreCase)
                    || heroClassText.Contains(hc.DisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    return hc.Id;
                }
            }
        }

        // Фолбэк: первый активный класс.
        foreach (HeroClass hc in heroClasses)
        {
            if (hc.IsActive)
            {
                _logger.LogWarning(
                    "RunRecorder: класс героя не распознан (текст='{Text}'); используется фолбэк Id={Id}. IsPartial=true.",
                    heroClassText, hc.Id);
                isPartial = true;
                return hc.Id;
            }
        }

        // Список классов пуст (дефолтный конфиг v1 — классы открываются динамически).
        _logger.LogWarning(
            "RunRecorder: справочник HeroClasses пуст, класс героя не определён. IsPartial=true.");
        isPartial = true;
        return 0;
    }

    /// <summary>
    /// Формирует список <see cref="StageRunChest"/> из накопленных счётчиков.
    /// Включает только типы с Count &gt; 0.
    /// </summary>
    private List<StageRunChest> BuildChests()
    {
        List<StageRunChest> result = new(_chestAccum.Count);
        foreach (KeyValuePair<int, int> kv in _chestAccum)
        {
            if (kv.Value > 0)
            {
                result.Add(new StageRunChest
                {
                    ChestTypeId = kv.Key,
                    Count       = kv.Value,
                });
            }
        }
        return result;
    }

    /// <summary>
    /// Сбрасывает только накопители текущего забега (без вызова completionDetector.Reset()).
    /// </summary>
    private void ResetAccumulatorsOnly()
    {
        _currentStageId        = null;
        _startGold             = null;
        _lastGold              = null;
        _runStartUtc           = null;
        _xpAccum               = 0;
        _prevXp                = null;
        _prevXpToLevel         = null;
        _prevHeroLevel         = null;
        _chestAccum.Clear();
        _prevChestPoints.Clear();
        _hasBaseline           = false;
        _stageChangedDuringRun = false;
        // hero-контекст сохраняем намеренно — может быть полезен для следующего забега.
    }
}
