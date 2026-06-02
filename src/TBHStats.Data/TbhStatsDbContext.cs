namespace TBHStats.Data;

using Microsoft.EntityFrameworkCore;
using TBHStats.Core.Models;
using TBHStats.Data.Entities;

/// <summary>
/// Основной контекст EF Core для базы данных TBHStats (SQLite).
/// Строка подключения задаётся при регистрации в DI (T010/composition root).
/// Конфигурации сущностей применяются через <see cref="ApplyConfigurationsFromAssembly"/>.
/// </summary>
public sealed class TbhStatsDbContext : DbContext
{
    /// <inheritdoc />
    public TbhStatsDbContext(DbContextOptions<TbhStatsDbContext> options)
        : base(options)
    {
    }

    // ── Справочники (Game Mechanics) ────────────────────────────────────────

    /// <summary>Типы сундуков (brown / blue / red).</summary>
    public DbSet<ChestType> ChestTypes => Set<ChestType>();

    /// <summary>Классы героя.</summary>
    public DbSet<HeroClass> HeroClasses => Set<HeroClass>();

    /// <summary>Вкладки интерфейса игры (9 разделов).</summary>
    public DbSet<Tab> Tabs => Set<Tab>();

    /// <summary>Акты (1..3).</summary>
    public DbSet<Act> Acts => Set<Act>();

    /// <summary>Уровни сложности (normal / nightmare).</summary>
    public DbSet<Difficulty> Difficulties => Set<Difficulty>();

    // ── Доменные сущности ───────────────────────────────────────────────────

    /// <summary>Этапы (3 × 2 × 10 = 60).</summary>
    public DbSet<Stage> Stages => Set<Stage>();

    /// <summary>Забеги этапов (основная история).</summary>
    public DbSet<StageRun> StageRuns => Set<StageRun>();

    /// <summary>Счётчики сундуков по типам за забег.</summary>
    public DbSet<StageRunChest> StageRunChests => Set<StageRunChest>();

    /// <summary>Замеры показателей (живые данные / тренды).</summary>
    public DbSet<MetricSample> MetricSamples => Set<MetricSample>();

    /// <summary>Мгновенные счётчики сундуков на момент замера.</summary>
    public DbSet<MetricSampleChest> MetricSampleChests => Set<MetricSampleChest>();

    /// <summary>Материализованные агрегаты этапов.</summary>
    public DbSet<StageAggregate> StageAggregates => Set<StageAggregate>();

    // ── Конфигурация / состояние ────────────────────────────────────────────

    /// <summary>Калибровки областей считывания (ROI).</summary>
    public DbSet<RoiCalibration> RoiCalibrations => Set<RoiCalibration>();

    /// <summary>Настройки виджета (синглтон).</summary>
    public DbSet<WidgetSettings> WidgetSettings => Set<WidgetSettings>();

    /// <summary>Профиль оптимизации (синглтон).</summary>
    public DbSet<OptimizationProfile> OptimizationProfiles => Set<OptimizationProfile>();

    /// <summary>Геометрия окон (позиция и размер) для восстановления между сессиями (FR-016).</summary>
    public DbSet<WindowPlacement> WindowPlacements => Set<WindowPlacement>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Все IEntityTypeConfiguration<T> из этой сборки применяются автоматически.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TbhStatsDbContext).Assembly);
    }
}
