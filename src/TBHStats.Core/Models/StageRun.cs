namespace TBHStats.Core.Models;

/// <summary>
/// Запись истории: один пройденный (или прерванный) забег этапа (data-model §StageRun).
/// Хранит прирост золота/опыта, контекст героя и перечень сундуков за забег.
/// </summary>
/// <remarks>
/// EF-маппинг (owned entity HeroSnapshot, nav-свойства, PK) конфигурирует T009 во Fluent API.
/// Вычисляемые свойства <see cref="GoldPerHour"/> / <see cref="XpPerHour"/> — get-only,
/// не персистируются.
/// </remarks>
public sealed class StageRun
{
    /// <summary>Суррогатный первичный ключ.</summary>
    public long Id { get; set; }

    /// <summary>FK → <see cref="Stage.Id"/>.</summary>
    public int StageId { get; set; }

    /// <summary>Продолжительность забега в секундах (&gt;0).</summary>
    public int DurationSeconds { get; set; }

    /// <summary>Прирост золота за забег (≥0).</summary>
    public long GoldGained { get; set; }

    /// <summary>Прирост опыта за забег (≥0).</summary>
    public long XpGained { get; set; }

    /// <summary>
    /// Снимок выбранного героя на момент забега (owned value-object).
    /// Контекст сохраняет актуальные значения на момент закрытия забега.
    /// </summary>
    public HeroSnapshot Hero { get; set; } = new HeroSnapshot(0, 1, 0);

    /// <summary>Момент завершения забега (UTC).</summary>
    public DateTime CompletedAtUtc { get; set; }

    /// <summary>
    /// Признак неполного/прерванного забега.
    /// Partial-забеги исключаются из агрегатов «Best/Avg» (FR-010).
    /// </summary>
    public bool IsPartial { get; set; }

    /// <summary>Счётчики сундуков по типам за этот забег.</summary>
    public ICollection<StageRunChest> Chests { get; set; } = new List<StageRunChest>();

    // ──────────────── Вычисляемые (не персистируются) ────────────────

    /// <summary>
    /// Золото в час, вычисленное по <see cref="GoldGained"/> / <see cref="DurationSeconds"/>.
    /// Возвращает 0, если <see cref="DurationSeconds"/> ≤ 0.
    /// </summary>
    public double GoldPerHour =>
        DurationSeconds > 0 ? (double)GoldGained / DurationSeconds * 3600 : 0;

    /// <summary>
    /// Опыт в час, вычисленный по <see cref="XpGained"/> / <see cref="DurationSeconds"/>.
    /// Возвращает 0, если <see cref="DurationSeconds"/> ≤ 0.
    /// </summary>
    public double XpPerHour =>
        DurationSeconds > 0 ? (double)XpGained / DurationSeconds * 3600 : 0;
}
