namespace TBHStats.Core.Models;

/// <summary>
/// Конфигурация области интереса (ROI) для считывания одного поля (R2, R3).
/// Координаты хранятся как нормализованные доли клиентской области ∈ [0..1].
/// Per-ROI выбор OCR-движка.
/// </summary>
public sealed class RoiCalibration
{
    /// <summary>Суррогатный первичный ключ.</summary>
    public int Id { get; init; }

    /// <summary>
    /// Ключ считываемого поля.
    /// Примеры: "gold", "xp", "xpToLevel", "stageTime", "heroLevel",
    /// "heroDamage", "heroClass", "stageId", "stageProgress",
    /// "nextLocation", "chest:brown", "chest:blue", "chest:red", "activeTab".
    /// </summary>
    public string FieldKey { get; init; } = string.Empty;

    /// <summary>Источник поля: <see cref="FieldSource.MainZone"/> или <see cref="FieldSource.Tab"/>.</summary>
    public FieldSource Source { get; init; }

    /// <summary>
    /// FK → <see cref="Tab.Id"/>.
    /// Заполняется только если <see cref="Source"/> == <see cref="FieldSource.Tab"/>;
    /// поле читается лишь когда указанная вкладка активна.
    /// </summary>
    public int? TabId { get; init; }

    /// <summary>Левый край ROI: нормализованная доля ширины клиентской области [0..1].</summary>
    public double X { get; init; }

    /// <summary>Верхний край ROI: нормализованная доля высоты клиентской области [0..1].</summary>
    public double Y { get; init; }

    /// <summary>Ширина ROI: нормализованная доля ширины клиентской области [0..1].</summary>
    public double W { get; init; }

    /// <summary>Высота ROI: нормализованная доля высоты клиентской области [0..1].</summary>
    public double H { get; init; }

    /// <summary>OCR-движок, используемый для этого ROI.</summary>
    public OcrEngine OcrEngine { get; init; }

    /// <summary>
    /// Необязательная подсказка парсеру OCR (например, whitelist цифр и суффиксов K/M/B/T).
    /// null — без ограничений.
    /// </summary>
    public string? ParseHint { get; init; }
}
