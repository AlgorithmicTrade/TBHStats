namespace TBHStats.Data.Repositories;

using Microsoft.EntityFrameworkCore;
using TBHStats.Core.Models;
using TBHStats.Core.Optimization;

/// <summary>
/// Репозиторий материализованных агрегатов этапов (FR-008, FR-010).
/// Делегирует вычисление <see cref="IStageAggregateCalculator"/>,
/// а сам отвечает за I/O: загрузку забегов, upsert агрегата, выставление <see cref="StageAggregate.UpdatedAtUtc"/>.
/// </summary>
public sealed class StageAggregateRepository : IStageAggregateRepository
{
    private readonly TbhStatsDbContext _db;
    private readonly IStageAggregateCalculator _calculator;

    /// <param name="db">Контекст EF Core (внедряется через DI).</param>
    /// <param name="calculator">Чистая функция вычисления агрегата (внедряется через DI).</param>
    public StageAggregateRepository(TbhStatsDbContext db, IStageAggregateCalculator calculator)
    {
        _db         = db;
        _calculator = calculator;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StageAggregate>> GetAllAsync(CancellationToken ct)
    {
        return await _db.StageAggregates
            .AsNoTracking()
            .Include(a => a.ChestRates)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RecomputeForStageAsync(int stageId, int recentWindowSize, CancellationToken ct)
    {
        // 1. Загрузить все забеги этапа (без трекинга — read-only вход для калькулятора).
        var runs = await _db.StageRuns
            .AsNoTracking()
            .Where(r => r.StageId == stageId)
            .Include(r => r.Chests)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // 2. Загрузить id активных типов сундуков.
        var chestTypeIds = await _db.ChestTypes
            .Where(c => c.IsActive)
            .OrderBy(c => c.Id)
            .Select(c => c.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // 3. Вычислить агрегат (чистая функция, исключает partial-забеги сама).
        var agg = _calculator.Compute(stageId, runs, recentWindowSize, chestTypeIds);

        // 4. Репозиторий — источник времени; калькулятор UpdatedAtUtc не ставит.
        agg.UpdatedAtUtc = DateTime.UtcNow;

        // 5. Upsert: загрузить существующий агрегат с трекингом для обновления.
        var existing = await _db.StageAggregates
            .Include(a => a.ChestRates)
            .FirstOrDefaultAsync(a => a.StageId == stageId, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            // Первый раз — добавляем весь граф (агрегат + ChestRates).
            _db.StageAggregates.Add(agg);
        }
        else
        {
            // Удаляем старые ChestRates, чтобы избежать конфликтов составного PK.
            _db.Set<StageAggregateChestRate>().RemoveRange(existing.ChestRates);

            // Копируем скалярные поля из нового агрегата в tracked-экземпляр.
            CopyScalarFields(source: agg, target: existing);

            // Добавляем новые ChestRates (StageId уже выставлен калькулятором).
            foreach (var rate in agg.ChestRates)
            {
                existing.ChestRates.Add(rate);
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Вспомогательные методы
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Копирует все скалярные поля из <paramref name="source"/> в <paramref name="target"/>
    /// (tracked-экземпляр в change tracker'е EF).
    /// </summary>
    private static void CopyScalarFields(StageAggregate source, StageAggregate target)
    {
        // All-time
        target.RunCount            = source.RunCount;
        target.AvgGoldPerHour      = source.AvgGoldPerHour;
        target.BestGoldPerHour     = source.BestGoldPerHour;
        target.AvgXpPerHour        = source.AvgXpPerHour;
        target.BestXpPerHour       = source.BestXpPerHour;
        target.AvgDurationSeconds  = source.AvgDurationSeconds;
        target.BestDurationSeconds = source.BestDurationSeconds;
        target.AvgGoldGained       = source.AvgGoldGained;
        target.AvgXpGained         = source.AvgXpGained;
        target.UpdatedAtUtc        = source.UpdatedAtUtc;

        // Recent
        target.RecentRunCount            = source.RecentRunCount;
        target.RecentAvgGoldPerHour      = source.RecentAvgGoldPerHour;
        target.RecentBestGoldPerHour     = source.RecentBestGoldPerHour;
        target.RecentAvgXpPerHour        = source.RecentAvgXpPerHour;
        target.RecentBestXpPerHour       = source.RecentBestXpPerHour;
        target.RecentAvgDurationSeconds  = source.RecentAvgDurationSeconds;
        target.RecentBestDurationSeconds = source.RecentBestDurationSeconds;
        target.RecentAvgGoldGained       = source.RecentAvgGoldGained;
        target.RecentAvgXpGained         = source.RecentAvgXpGained;

        // Power-context (nullable)
        target.RecentHeroLevelMin  = source.RecentHeroLevelMin;
        target.RecentHeroLevelMax  = source.RecentHeroLevelMax;
        target.RecentHeroDamageMin = source.RecentHeroDamageMin;
        target.RecentHeroDamageMax = source.RecentHeroDamageMax;
    }
}
