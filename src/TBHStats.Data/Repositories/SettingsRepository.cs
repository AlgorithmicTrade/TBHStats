namespace TBHStats.Data.Repositories;

using Microsoft.EntityFrameworkCore;
using TBHStats.Core.Models;

/// <summary>
/// Репозиторий настроек приложения: виджет, профиль оптимизации, калибровки ROI.
/// WidgetSettings и OptimizationProfile — singleton-таблицы с shadow PK «Id».
/// RoiCalibrations — replace-all семантика (удаление всех + вставка переданных за одну транзакцию).
/// </summary>
public sealed class SettingsRepository : ISettingsRepository
{
    private readonly TbhStatsDbContext _db;

    /// <param name="db">Контекст EF Core (внедряется через DI).</param>
    public SettingsRepository(TbhStatsDbContext db)
    {
        _db = db;
    }

    // ── WidgetSettings (singleton) ──────────────────────────────────────────

    /// <inheritdoc />
    public async Task<WidgetSettings> GetWidgetSettingsAsync()
    {
        // Синглтон: возвращаем единственную запись; если таблица пуста — дефолтные значения.
        var settings = await _db.WidgetSettings.AsNoTracking().FirstOrDefaultAsync().ConfigureAwait(false);
        return settings ?? new WidgetSettings();
    }

    /// <inheritdoc />
    public async Task SaveWidgetSettingsAsync(WidgetSettings s)
    {
        // Ищем существующую запись (с трекингом, чтобы обновить).
        var existing = await _db.WidgetSettings.FirstOrDefaultAsync().ConfigureAwait(false);
        if (existing is null)
        {
            _db.WidgetSettings.Add(s);
        }
        else
        {
            // Получаем shadow PK существующей записи и переносим на новую.
            int existingId = (int)_db.Entry(existing).Property("Id").CurrentValue!;
            _db.Entry(existing).State = EntityState.Detached;

            _db.WidgetSettings.Attach(s);
            _db.Entry(s).Property("Id").CurrentValue = existingId;
            _db.Entry(s).State = EntityState.Modified;
        }

        await _db.SaveChangesAsync().ConfigureAwait(false);
    }

    // ── OptimizationProfile (singleton) ────────────────────────────────────

    /// <inheritdoc />
    public async Task<OptimizationProfile> GetOptimizationProfileAsync()
    {
        // Синглтон: возвращаем единственную запись; если пусто — дефолт (GoldPerHour).
        var profile = await _db.OptimizationProfiles.AsNoTracking().FirstOrDefaultAsync().ConfigureAwait(false);
        return profile ?? new OptimizationProfile();
    }

    /// <inheritdoc />
    public async Task SaveOptimizationProfileAsync(OptimizationProfile p)
    {
        var existing = await _db.OptimizationProfiles.FirstOrDefaultAsync().ConfigureAwait(false);
        if (existing is null)
        {
            _db.OptimizationProfiles.Add(p);
        }
        else
        {
            int existingId = (int)_db.Entry(existing).Property("Id").CurrentValue!;
            _db.Entry(existing).State = EntityState.Detached;

            _db.OptimizationProfiles.Attach(p);
            _db.Entry(p).Property("Id").CurrentValue = existingId;
            _db.Entry(p).State = EntityState.Modified;
        }

        await _db.SaveChangesAsync().ConfigureAwait(false);
    }

    // ── WindowPlacement (upsert по WindowKey) ──────────────────────────────

    /// <inheritdoc />
    public async Task<WindowPlacement?> GetWindowPlacementAsync(string windowKey, CancellationToken ct = default)
    {
        return await _db.WindowPlacements
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.WindowKey == windowKey, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveWindowPlacementAsync(WindowPlacement placement, CancellationToken ct = default)
    {
        // WindowPlacement — immutable (all init); PK = WindowKey (реальное string-поле).
        // Upsert: если запись с таким WindowKey уже существует — обновляем через Attach+Modified;
        // если нет — добавляем. EF корректно использует WindowKey как PK при Attach/Add.
        bool exists = await _db.WindowPlacements
            .AsNoTracking()
            .AnyAsync(p => p.WindowKey == placement.WindowKey, ct)
            .ConfigureAwait(false);

        if (exists)
        {
            _db.WindowPlacements.Attach(placement);
            _db.Entry(placement).State = EntityState.Modified;
        }
        else
        {
            _db.WindowPlacements.Add(placement);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // ── RoiCalibrations (replace-all) ──────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoiCalibration>> GetRoiCalibrationsAsync()
    {
        return await _db.RoiCalibrations.AsNoTracking().ToListAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveRoiCalibrationsAsync(IReadOnlyList<RoiCalibration> rois)
    {
        // Replace-all: удаляем все существующие и вставляем переданные в одной SaveChanges.
        await _db.RoiCalibrations.ExecuteDeleteAsync().ConfigureAwait(false);

        if (rois.Count > 0)
        {
            // Сбрасываем Id в 0, чтобы EF присвоил новые суррогатные ключи.
            foreach (var roi in rois)
            {
                var fresh = new RoiCalibration
                {
                    FieldKey  = roi.FieldKey,
                    Source    = roi.Source,
                    TabId     = roi.TabId,
                    X         = roi.X,
                    Y         = roi.Y,
                    W         = roi.W,
                    H         = roi.H,
                    OcrEngine = roi.OcrEngine,
                    ParseHint = roi.ParseHint,
                };
                _db.RoiCalibrations.Add(fresh);
            }

            await _db.SaveChangesAsync().ConfigureAwait(false);
        }
    }
}
