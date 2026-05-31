using CommunityToolkit.Mvvm.ComponentModel;
using TBHStats.Core.Models;

namespace TBHStats.App.ViewModels;

/// <summary>
/// Изменяемая observable-обёртка над <see cref="RoiCalibration"/> для редактирования
/// в <see cref="CalibrationViewModel"/>.
/// Каждый экземпляр соответствует одной ROI-записи в списке.
/// </summary>
public sealed partial class RoiCalibrationItem : ObservableObject
{
    /// <summary>Суррогатный ключ (0 — новая запись, ещё не сохранённая).</summary>
    public int Id { get; set; }

    /// <summary>Ключ считываемого поля (например, «gold», «xp», «chest:brown»).</summary>
    [ObservableProperty]
    private string _fieldKey = string.Empty;

    /// <summary>Источник поля: MainZone или Tab.</summary>
    [ObservableProperty]
    private FieldSource _source;

    /// <summary>FK → Tab.Id (null, если Source == MainZone).</summary>
    [ObservableProperty]
    private int? _tabId;

    /// <summary>Левый край ROI: нормализованная доля [0..1].</summary>
    [ObservableProperty]
    private double _x;

    /// <summary>Верхний край ROI: нормализованная доля [0..1].</summary>
    [ObservableProperty]
    private double _y;

    /// <summary>Ширина ROI: нормализованная доля [0..1].</summary>
    [ObservableProperty]
    private double _w;

    /// <summary>Высота ROI: нормализованная доля [0..1].</summary>
    [ObservableProperty]
    private double _h;

    /// <summary>OCR-движок для данного ROI.</summary>
    [ObservableProperty]
    private OcrEngine _ocrEngine;

    /// <summary>Подсказка парсеру OCR (null — без ограничений).</summary>
    [ObservableProperty]
    private string? _parseHint;

    // ──────────────────────────────────────────────────────────────
    // Conversion
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Создать <see cref="RoiCalibrationItem"/> из доменной записи.
    /// </summary>
    public static RoiCalibrationItem FromDomain(RoiCalibration roi) => new()
    {
        Id        = roi.Id,
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

    /// <summary>
    /// Конвертировать в доменную запись. Координаты клампируются к [0..1].
    /// </summary>
    public RoiCalibration ToDomain() => new()
    {
        Id        = Id,
        FieldKey  = FieldKey,
        Source    = Source,
        TabId     = Source == FieldSource.Tab ? TabId : null,
        X         = Clamp01(X),
        Y         = Clamp01(Y),
        W         = Clamp01(W),
        H         = Clamp01(H),
        OcrEngine = OcrEngine,
        ParseHint = string.IsNullOrWhiteSpace(ParseHint) ? null : ParseHint.Trim(),
    };

    private static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;
}
