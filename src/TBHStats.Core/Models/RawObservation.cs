namespace TBHStats.Core.Models;

/// <summary>
/// Сырое наблюдение одного кадра — DTO, производимый <c>IFieldExtractor</c> (слой Capture, T023)
/// и потребляемый <c>IObservationValidator</c> в доменном ядре (T024).
/// </summary>
/// <remarks>
/// <para>
/// Все OCR-производные поля nullable: значение отсутствует, если не было считано
/// с достаточной уверенностью, либо активная вкладка не предоставляет это поле (FR-002b).
/// </para>
/// <para>
/// Поля с <c>Source=Tab</c> присутствуют только если соответствующая вкладка активна;
/// иначе поле = null (не ошибка — сохраняется последнее достоверное значение).
/// </para>
/// <para>
/// <see cref="Chests"/> — мгновенные транзиентные «точки» под иконкой каждого типа сундука
/// в MainZone. Падение до 0 = открытие сундука, НЕ потеря (data-model §Валидация).
/// </para>
/// </remarks>
public sealed class RawObservation
{
    /// <summary>Момент снятия кадра (UTC).</summary>
    public DateTime TakenAtUtc { get; init; }

    /// <summary>
    /// Кумулятивное золото на момент кадра.
    /// Null, если поле не считано или вкладка «hero» не была активна.
    /// </summary>
    public long? Gold { get; init; }

    /// <summary>
    /// EXP героя в пределах текущего уровня (сбрасывается при level-up).
    /// Null, если поле не считано или вкладка «status» не была активна.
    /// </summary>
    public long? Xp { get; init; }

    /// <summary>
    /// EXP, необходимый для перехода на следующий уровень.
    /// Требуется для корректного расчёта опыт/час при level-up (FR-005a).
    /// Null, если поле не считано.
    /// </summary>
    public long? XpToLevel { get; init; }

    /// <summary>
    /// Время прохождения текущего этапа в секундах.
    /// Появляется в MainZone после убийства босса этапа.
    /// Null, если поле не считано.
    /// </summary>
    public int? StageTimeSeconds { get; init; }

    /// <summary>
    /// Прогресс текущего этапа ∈ [0.0..1.0].
    /// В конце этапа прогресс = 1.0 и появляется босс.
    /// Null, если поле не считано.
    /// </summary>
    public double? StageProgress { get; init; }

    /// <summary>
    /// Признак присутствия босса этапа в MainZone.
    /// Убийство босса = завершение этапа (сигнал сегментации забегов — FR-002).
    /// Null, если поле не считано.
    /// </summary>
    public bool? BossPresent { get; init; }

    /// <summary>
    /// Уровень героя на момент кадра.
    /// Null, если поле не считано или вкладка «status» не была активна.
    /// </summary>
    public int? HeroLevel { get; init; }

    /// <summary>
    /// Урон героя на момент кадра (Attack Damage из вкладки «status» по умолчанию).
    /// Null, если поле не считано.
    /// </summary>
    public long? HeroDamage { get; init; }

    /// <summary>
    /// Текст класса героя, считанный OCR (до нормализации по HeroClass.Key).
    /// Null, если поле не считано.
    /// </summary>
    public string? HeroClassText { get; init; }

    /// <summary>
    /// Текст идентификатора этапа, считанный OCR из вкладки «portal».
    /// Требует парсинга через <c>IValueParser.TryParseStageId</c>.
    /// Null, если поле не считано или вкладка «portal» не была активна.
    /// </summary>
    public string? StageText { get; init; }

    /// <summary>
    /// Следующая локация (current+1), считанная из MainZone.
    /// Null на максимально открытой локации (поле отсутствует в игре).
    /// </summary>
    public StageRef? NextLocation { get; init; }

    /// <summary>
    /// Активная вкладка, распознанная <c>ITabDetector</c> в этом кадре.
    /// Null, если вкладка не определена достоверно (уверенность ниже порога).
    /// </summary>
    public TabRef? ActiveTab { get; init; }

    /// <summary>
    /// Мгновенные транзиентные «точки» сундуков в MainZone по типам.
    /// Ключ — <c>ChestType.Id</c> из <c>GameMechanicsConfig</c>;
    /// значение — текущее число «точек» под иконкой типа (≥0).
    /// Падение к 0 = открытие сундука, НЕ потеря (data-model §Валидация).
    /// По умолчанию пустой словарь (не null).
    /// </summary>
    public IReadOnlyDictionary<int, int> Chests { get; init; }
        = new Dictionary<int, int>();

    /// <summary>
    /// Уверенность OCR-распознавания по ключу поля.
    /// Стандартные ключи: «gold», «xp», «xpToLevel», «heroLevel», «heroDamage»,
    /// «heroClass», «stageId», «stageTime», «stageProgress», «bossPresent», «nextLocation», «activeTab».
    /// Значение ∈ [0.0..1.0]; ключ отсутствует, если поле не читалось в данном кадре.
    /// По умолчанию пустой словарь (не null).
    /// </summary>
    public IReadOnlyDictionary<string, double> PerFieldConfidence { get; init; }
        = new Dictionary<string, double>();
}
