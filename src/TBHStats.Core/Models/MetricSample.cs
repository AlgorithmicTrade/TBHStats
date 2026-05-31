namespace TBHStats.Core.Models;

/// <summary>
/// Один замер показателей игры в момент времени (data-model §MetricSample).
/// Используется для вычисления живых темпов по интервалам между надёжными точками.
/// </summary>
/// <remarks>
/// <para>
/// Темпы (золото/час, опыт/час) вычисляются по дельтам между соседними
/// <see cref="IsReliable"/> = true сэмплами. Периоды недоступности окна
/// не трактуются как «нулевая добыча» (FR-005a).
/// </para>
/// <para>
/// <see cref="Xp"/> — EXP внутри текущего уровня; сбрасывается при level-up.
/// При вычислении опыт/час инкремент уровня компенсируется через
/// <see cref="XpToLevel"/> предыдущего сэмпла + <see cref="HeroLevel"/> (data-model §Валидация).
/// </para>
/// <para>
/// EF-маппинг (owned StageRef?, owned ChestRates, PK) настраивает T009 через Fluent API.
/// </para>
/// </remarks>
public sealed class MetricSample
{
    /// <summary>Суррогатный первичный ключ.</summary>
    public long Id { get; set; }

    /// <summary>Момент снятия замера (UTC).</summary>
    public DateTime TakenAtUtc { get; set; }

    /// <summary>FK → <see cref="Stage.Id"/> (nullable — этап может быть не определён).</summary>
    public int? StageId { get; set; }

    /// <summary>
    /// Кумулятивное золото на момент замера.
    /// Null, если поле не было считано с достаточной уверенностью.
    /// </summary>
    public long? Gold { get; set; }

    /// <summary>
    /// EXP героя в пределах текущего уровня (сбрасывается при level-up).
    /// Null, если поле не считано.
    /// </summary>
    public long? Xp { get; set; }

    /// <summary>
    /// EXP, необходимый для перехода на следующий уровень.
    /// Требуется для корректного расчёта опыт/час при level-up (FR-005a).
    /// Null, если поле не считано.
    /// </summary>
    public long? XpToLevel { get; set; }

    /// <summary>
    /// Признак достоверности замера: прошёл confidence-порог + sanity-проверку (R4).
    /// Только достоверные сэмплы участвуют в расчёте темпов (FR-005a).
    /// </summary>
    public bool IsReliable { get; set; }

    /// <summary>Уровень героя на момент замера. Null, если не считан.</summary>
    public int? HeroLevel { get; set; }

    /// <summary>Урон героя на момент замера. Null, если не считан.</summary>
    public long? HeroDamage { get; set; }

    /// <summary>
    /// Следующая локация из MainZone (current+1).
    /// Null на максимально открытой локации (поле отсутствует в игре).
    /// Owned value-object; EF-маппинг настраивает T009.
    /// </summary>
    public StageRef? NextLocation { get; set; }

    /// <summary>
    /// Мгновенные счётчики точек сундуков в MainZone по типам.
    /// Транзиентные данные: обнуляются при открытии сундуков (не являются накопленным итогом).
    /// </summary>
    public ICollection<MetricSampleChest> Chests { get; set; } = new List<MetricSampleChest>();
}
