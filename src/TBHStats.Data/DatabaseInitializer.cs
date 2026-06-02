namespace TBHStats.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;
using TBHStats.Data.Repositories;

/// <summary>
/// Bootstrap-сервис для инициализации базы данных TBHStats при старте приложения.
/// Применяет миграции EF Core и выполняет идемпотентный сидинг игровых механик.
/// </summary>
/// <remarks>
/// Вызывается composition root'ом (TBHStats.App) один раз при запуске.
/// Путь к файлу БД вычисляется через <see cref="GetDbPath"/> и передаётся при регистрации
/// <see cref="TbhStatsDbContext"/> в DI.
/// </remarks>
public static class DatabaseInitializer
{
    /// <summary>
    /// Вычисляет абсолютный путь к файлу SQLite-базы данных.
    /// Директория создаётся при первом вызове, если не существует.
    /// </summary>
    /// <returns>Полный путь вида <c>%LOCALAPPDATA%\TBHStats\tbhstats.db</c>.</returns>
    public static string GetDbPath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string dir = Path.Combine(localAppData, "TBHStats");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "tbhstats.db");
    }

    /// <summary>
    /// Возвращает строку подключения SQLite для заданного пути к файлу БД.
    /// </summary>
    /// <param name="dbPath">Абсолютный путь к файлу .db (например, от <see cref="GetDbPath"/>).</param>
    /// <returns>Строка подключения вида <c>Data Source=&lt;path&gt;</c>.</returns>
    public static string GetConnectionString(string dbPath)
        => $"Data Source={dbPath}";

    /// <summary>
    /// Настраивает <see cref="DbContextOptionsBuilder{TContext}"/> для использования SQLite
    /// по заданному пути к файлу БД.
    /// </summary>
    /// <param name="optionsBuilder">Строитель опций контекста.</param>
    /// <param name="dbPath">Абсолютный путь к файлу .db.</param>
    public static void ConfigureSqlite(DbContextOptionsBuilder<TbhStatsDbContext> optionsBuilder, string dbPath)
        => optionsBuilder.UseSqlite(GetConnectionString(dbPath));

    // Поля, требующие предобработки binarize_white (мелкий пиксельный шрифт + яркие эффекты MainZone).
    // verified: только nextLocation. heroLevel читается штатно без бинаризации (T066).
    private const string BinarizeWhiteHint = "binarize_white";

    /// <summary>
    /// Возвращает дефолтный <c>ParseHint</c> для поля ROI-калибровки.
    /// Поле <c>nextLocation</c> требует предобработки <c>binarize_white</c>
    /// (мелкий пиксельный шрифт; verified T066). Все остальные поля — <c>null</c>.
    /// </summary>
    /// <param name="fieldKey">Ключ поля, напр. <c>"nextLocation"</c>, <c>"gold"</c>.</param>
    /// <returns><c>"binarize_white"</c> для <c>nextLocation</c>; иначе <c>null</c>.</returns>
    private static string? DefaultParseHintFor(string fieldKey)
        => string.Equals(fieldKey, "nextLocation", StringComparison.OrdinalIgnoreCase)
            ? BinarizeWhiteHint
            : null;

    /// <summary>
    /// Применяет все ожидающие миграции EF Core и выполняет идемпотентный сидинг игровых механик.
    /// </summary>
    /// <param name="db">Контекст базы данных, сконфигурированный на целевой файл.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <remarks>
    /// Идемпотентность: каждый справочник сидируется только если соответствующая таблица пуста
    /// (проверка <see cref="EntityFrameworkQueryableExtensions.AnyAsync{TSource}"/>).
    /// При повторном запуске приложения данные не дублируются.
    /// </remarks>
    public static async Task InitializeAsync(TbhStatsDbContext db, CancellationToken ct = default)
    {
        // 1. Применить все ожидающие миграции (создаёт файл БД при первом запуске).
        await db.Database.MigrateAsync(ct).ConfigureAwait(false);

        // 2. Идемпотентный сидинг из GameMechanicsConfig.CreateDefault().
        await SeedMechanicsAsync(db, ct).ConfigureAwait(false);

        // 3. Сидинг синглтонов состояния приложения.
        await SeedSingletonsAsync(db, ct).ConfigureAwait(false);

        // 4. Self-heal: проставить binarize_white существующему nextLocation-ROI с пустым хинтом.
        //    (БД создана до фикса; координаты пользователя сохраняем — меняем только хинт.)
        //    Выполняется при каждом запуске — идемпотентно. НЕ перезаписывает явно заданный хинт.
        await HealNextLocationParseHintAsync(db, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Идемпотентный бэкфилл устаревших агрегатов: пересчитывает <see cref="StageAggregate"/>
    /// для этапов, у которых есть забеги с <c>GoldGained &gt; 0</c> или <c>XpGained &gt; 0</c>,
    /// но агрегат имеет <c>AvgGoldGained == 0 &amp;&amp; AvgXpGained == 0</c> (пост-миграционное состояние).
    /// </summary>
    /// <remarks>
    /// Вызывается из composition root (<c>App.xaml.cs</c>) после <see cref="InitializeAsync"/>,
    /// используя тот же DI-scope (доступ к <see cref="IStageAggregateRepository"/>
    /// и <see cref="OptimizationProfile"/>).
    /// Условие самоотключается после первого успешного пересчёта (поля станут &gt; 0).
    /// </remarks>
    /// <param name="db">Контекст EF Core.</param>
    /// <param name="aggregateRepo">Репозиторий агрегатов (для вызова RecomputeForStageAsync).</param>
    /// <param name="recentWindowSize">
    /// Размер свежего окна; передаётся из <see cref="OptimizationProfile.RecentWindowSize"/>.
    /// </param>
    /// <param name="logger">Логгер для сводной info-строки. Может быть <c>null</c>.</param>
    /// <param name="ct">Токен отмены.</param>
    public static async Task BackfillStaleAggregatesAsync(
        TbhStatsDbContext           db,
        IStageAggregateRepository   aggregateRepo,
        int                         recentWindowSize,
        ILogger?                    logger,
        CancellationToken           ct = default)
    {
        // Находим stageId, где есть хотя бы один run с gained>0, но агрегат не пересчитан.
        // Критерий «устаревший»: AvgGoldGained==0 && AvgXpGained==0 при наличии реальных данных.
        var stageIdsWithRuns = await db.StageRuns
            .Where(r => !r.IsPartial && (r.GoldGained > 0 || r.XpGained > 0))
            .Select(r => r.StageId)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (stageIdsWithRuns.Count == 0)
        {
            return;
        }

        var staleStageIds = await db.StageAggregates
            .Where(a => stageIdsWithRuns.Contains(a.StageId)
                        && a.AvgGoldGained == 0.0
                        && a.AvgXpGained == 0.0)
            .Select(a => a.StageId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (staleStageIds.Count == 0)
        {
            return;
        }

        foreach (int stageId in staleStageIds)
        {
            await aggregateRepo
                .RecomputeForStageAsync(stageId, recentWindowSize, ct)
                .ConfigureAwait(false);
        }

        logger?.LogInformation("Backfill: пересчитано {Count} устаревших агрегатов.", staleStageIds.Count);
    }

    // ── Сидинг справочников и этапов ─────────────────────────────────────────

    private static async Task SeedMechanicsAsync(TbhStatsDbContext db, CancellationToken ct)
    {
        GameMechanicsConfig config = GameMechanicsConfig.CreateDefault();

        // ChestTypes
        if (!await db.ChestTypes.AnyAsync(ct).ConfigureAwait(false))
        {
            db.ChestTypes.AddRange(config.ChestTypes);
        }

        // Tabs (9 разделов)
        if (!await db.Tabs.AnyAsync(ct).ConfigureAwait(false))
        {
            db.Tabs.AddRange(config.Tabs);
        }

        // Acts
        if (!await db.Acts.AnyAsync(ct).ConfigureAwait(false))
        {
            db.Acts.AddRange(config.Acts);
        }

        // Difficulties
        if (!await db.Difficulties.AnyAsync(ct).ConfigureAwait(false))
        {
            db.Difficulties.AddRange(config.Difficulties);
        }

        // HeroClasses — по дефолту пустой список (открываются динамически).
        // Сидирование только если пришли данные.
        if (config.HeroClasses.Count > 0 && !await db.HeroClasses.AnyAsync(ct).ConfigureAwait(false))
        {
            db.HeroClasses.AddRange(config.HeroClasses);
        }

        // Stages (60 = 3×2×10) — сидируются после Acts и Difficulties (FK).
        // SaveChanges здесь необходим, чтобы Acts/Difficulties получили Id до вставки Stages.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (!await db.Stages.AnyAsync(ct).ConfigureAwait(false))
        {
            db.Stages.AddRange(config.Stages);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        // RoiCalibrations (дефолтные Field Source Bindings без координат).
        // Координаты X/Y/W/H = 0.0 — заглушки; пользователь калибрует через UI.
        if (!await db.RoiCalibrations.AnyAsync(ct).ConfigureAwait(false))
        {
            foreach (FieldSourceBinding binding in config.FieldSourceBindings)
            {
                db.RoiCalibrations.Add(new RoiCalibration
                {
                    FieldKey  = binding.FieldKey,
                    Source    = binding.Source,
                    TabId     = binding.TabId,
                    X         = 0.0,
                    Y         = 0.0,
                    W         = 0.0,
                    H         = 0.0,
                    OcrEngine = OcrEngine.WindowsMediaOcr,
                    ParseHint = DefaultParseHintFor(binding.FieldKey),
                });
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    // ── Self-heal ROI ParseHint ───────────────────────────────────────────────

    private static async Task HealNextLocationParseHintAsync(TbhStatsDbContext db, CancellationToken ct)
    {
        // Обновляем nextLocation-ROI с пустым/null хинтом через bulk ExecuteUpdateAsync.
        // — RoiCalibration.ParseHint объявлен как init-only (неизменяемая модель Core),
        //   поэтому изменить его через change tracker невозможно — используем прямой SQL-UPDATE.
        // — НЕ трогаем записи с уже заданным (непустым) хинтом: уважаем явный выбор пользователя.
        // — Координаты X/Y/W/H не перечислены в SetProperty — SQLite их не обновляет.
        await db.RoiCalibrations
            .Where(r => r.FieldKey == "nextLocation"
                        && (r.ParseHint == null || r.ParseHint == string.Empty))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(r => r.ParseHint, BinarizeWhiteHint),
                ct)
            .ConfigureAwait(false);
    }

    // ── Сидинг синглтонов (WidgetSettings, OptimizationProfile) ──────────────

    private static async Task SeedSingletonsAsync(TbhStatsDbContext db, CancellationToken ct)
    {
        if (!await db.WidgetSettings.AnyAsync(ct).ConfigureAwait(false))
        {
            db.WidgetSettings.Add(new WidgetSettings
            {
                PosX           = 0.0,
                PosY           = 0.0,
                Width          = 400.0,
                Height         = 300.0,
                AlwaysOnTop    = true,
                Theme          = Theme.System,
                PollIntervalMs = 1500,
            });
        }

        if (!await db.OptimizationProfiles.AnyAsync(ct).ConfigureAwait(false))
        {
            db.OptimizationProfiles.Add(new OptimizationProfile
            {
                SelectedMetric = OptimizationMetric.GoldPerHour,
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
