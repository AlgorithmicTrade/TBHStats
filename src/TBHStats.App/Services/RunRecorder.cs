using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;
using TBHStats.Data.Repositories;

namespace TBHStats.App.Services;

/// <summary>
/// Тонкий сервис персистентности завершённых сегментов забегов (FR-007, US2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Назначение.</b> Принимает от <see cref="StatsOrchestrator"/> уже готовые сводные данные
/// сегмента (goldGained, xpGained, chests, heroLevel, heroDamage, heroClassText, durationSeconds)
/// и записывает <see cref="StageRun"/> через <see cref="IRunRepository"/>, после чего
/// инициирует пересчёт агрегата через <see cref="IStageAggregateRepository"/>.
/// </para>
/// <para>
/// <b>Lifetime-решение.</b> Сервис является singleton; <see cref="IRunRepository"/>,
/// <see cref="IStageAggregateRepository"/> и <see cref="ISettingsRepository"/> — scoped.
/// Для корректной работы конструктор принимает <see cref="IServiceScopeFactory"/>;
/// scope создаётся и немедленно освобождается при каждом обращении к репозиториям.
/// </para>
/// </remarks>
public sealed class RunRecorder
{
    // ── Константы ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Максимальное число сохраняемых забегов на этап.
    /// Старые забеги сверх этого лимита физически удаляются после каждой записи.
    /// </summary>
    private const int MaxRunsPerStage = 10;

    // ── Зависимости ──────────────────────────────────────────────────────────

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IGameMechanics _gameMechanics;
    private readonly ILogger<RunRecorder> _logger;

    // ── Конструктор ───────────────────────────────────────────────────────────

    /// <summary>
    /// Создаёт <see cref="RunRecorder"/>.
    /// </summary>
    /// <param name="scopeFactory">
    /// Фабрика scope для получения scoped-зависимостей
    /// (<see cref="IRunRepository"/>, <see cref="IStageAggregateRepository"/>,
    /// <see cref="ISettingsRepository"/>) из singleton-контекста.
    /// </param>
    /// <param name="gameMechanics">Конфигурация механик игры (singleton).</param>
    /// <param name="logger">Логгер.</param>
    public RunRecorder(
        IServiceScopeFactory scopeFactory,
        IGameMechanics gameMechanics,
        ILogger<RunRecorder> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(gameMechanics);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory  = scopeFactory;
        _gameMechanics = gameMechanics;
        _logger        = logger;
    }

    // ── Публичный API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Поднимается после успешной записи забега и пересчёта агрегата.
    /// Вызывается на фоновом потоке (<see cref="StatsOrchestrator"/> использует Task.Run);
    /// подписчик сам обязан маршалировать в UI-поток при необходимости.
    /// </summary>
    public event EventHandler? RunsChanged;

    /// <summary>
    /// Записывает завершённый сегмент забега в репозиторий и инициирует пересчёт агрегата.
    /// Вызывается из <see cref="StatsOrchestrator"/> только когда этап распознан
    /// (<c>_segmentStageId.HasValue</c>) и сегмент прошёл валидацию по боссу.
    /// </summary>
    /// <param name="stageId">Идентификатор этапа (из GameMechanicsConfig).</param>
    /// <param name="durationSeconds">Длительность сегмента в секундах (≥1).</param>
    /// <param name="goldGained">Прирост золота за сегмент (≥0).</param>
    /// <param name="xpGained">Прирост опыта за сегмент (≥0).</param>
    /// <param name="chests">Счётчики сундуков по ChestTypeId (только Count&gt;0).</param>
    /// <param name="heroLevel">Последний надёжный уровень героя (null → 1).</param>
    /// <param name="heroDamage">Последний надёжный урон героя (null → 0).</param>
    /// <param name="heroClassText">Текст класса героя из OCR (null → фолбэк Id=1).</param>
    /// <param name="completedAtUtc">Момент завершения сегмента (UTC).</param>
    /// <param name="ct">Токен отмены.</param>
    public async Task PersistSegmentRunAsync(
        int stageId,
        int durationSeconds,
        long goldGained,
        long xpGained,
        IReadOnlyDictionary<int, int> chests,
        int? heroLevel,
        long? heroDamage,
        string? heroClassText,
        DateTime completedAtUtc,
        CancellationToken ct)
    {
        bool persisted = false;
        try
        {
            // ── HeroSnapshot ──────────────────────────────────────────────────
            bool isPartial = false;
            int heroClassId = ResolveHeroClassId(heroClassText, ref isPartial);
            int level       = heroLevel ?? 1;
            long damage     = heroDamage ?? 0;

            HeroSnapshot hero;
            try
            {
                hero = new HeroSnapshot(heroClassId, level, damage);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                _logger.LogWarning(ex,
                    "RunRecorder: некорректные значения HeroSnapshot (level={Level}, damage={Damage}); корректируем.",
                    level, damage);
                hero = new HeroSnapshot(heroClassId, Math.Max(1, level), Math.Max(0L, damage));
            }

            // ── Chests ────────────────────────────────────────────────────────
            List<StageRunChest> chestList = BuildChests(chests);

            // ── Сборка StageRun ───────────────────────────────────────────────
            StageRun run = new()
            {
                StageId         = stageId,
                DurationSeconds = Math.Max(1, durationSeconds),
                GoldGained      = goldGained,
                XpGained        = xpGained,
                Hero            = hero,
                CompletedAtUtc  = completedAtUtc,
                IsPartial       = false,
                Chests          = chestList,
            };

            // ── Запись в репозиторий ──────────────────────────────────────────
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();

            IRunRepository runRepo = scope.ServiceProvider.GetRequiredService<IRunRepository>();
            await runRepo.AddRunAsync(run, ct).ConfigureAwait(false);

            int pruned = await runRepo.PruneOldRunsAsync(stageId, MaxRunsPerStage, ct).ConfigureAwait(false);
            if (pruned > 0)
            {
                _logger.LogDebug(
                    "RunRecorder: прунинг: удалено {Count} старых забегов stageId={StageId}.",
                    pruned, stageId);
            }

            ISettingsRepository settingsRepo = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
            OptimizationProfile profile = await settingsRepo.GetOptimizationProfileAsync().ConfigureAwait(false);

            IStageAggregateRepository aggRepo = scope.ServiceProvider.GetRequiredService<IStageAggregateRepository>();
            await aggRepo.RecomputeForStageAsync(stageId, profile.RecentWindowSize, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "RunRecorder: записан сегментный забег stageId={StageId}, duration={Duration}s, gold={Gold}, xp={Xp}.",
                stageId, run.DurationSeconds, goldGained, xpGained);

            persisted = true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "RunRecorder: не удалось записать StageRun для stageId={StageId}.", stageId);
        }

        if (persisted)
        {
            try
            {
                RunsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RunRecorder: подписчик RunsChanged бросил исключение.");
            }
        }
    }

    /// <summary>
    /// No-op сброс (оставлен для совместимости вызовов из оркестратора при потере окна).
    /// Состояния накопления нет — метод безопасен как no-op.
    /// </summary>
    public void Reset() { /* нет состояния для сброса */ }

    // ── Внутренние вспомогательные методы ─────────────────────────────────────

    /// <summary>
    /// Резолвит Id класса героя по текстовому значению из OCR.
    /// Совпадение ищется по <see cref="HeroClass.Key"/> и <see cref="HeroClass.DisplayName"/>
    /// без учёта регистра. При отсутствии совпадения возвращает первый активный класс
    /// (фолбэк — всегда Id=1 «Не определён» после сидинга).
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

        // Фолбэк: первый активный класс (после сидинга — всегда Id=1 «Не определён»).
        foreach (HeroClass hc in heroClasses)
        {
            if (hc.IsActive)
            {
                _logger.LogDebug(
                    "RunRecorder: класс героя не распознан (текст='{Text}'); используется фолбэк Id={Id}.",
                    heroClassText, hc.Id);
                return hc.Id;
            }
        }

        // Защитная ветка: справочник HeroClasses пуст (после сидинга не достижимо).
        _logger.LogWarning("RunRecorder: справочник HeroClasses пуст, класс героя не определён.");
        isPartial = true;
        return 0;
    }

    /// <summary>
    /// Формирует список <see cref="StageRunChest"/> из переданного словаря.
    /// Включает только типы с Count &gt; 0.
    /// </summary>
    private static List<StageRunChest> BuildChests(IReadOnlyDictionary<int, int> chests)
    {
        List<StageRunChest> result = new(chests.Count);
        foreach (KeyValuePair<int, int> kv in chests)
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
}
