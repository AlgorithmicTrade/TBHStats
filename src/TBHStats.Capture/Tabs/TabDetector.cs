using TBHStats.Capture.Ocr;
using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;

namespace TBHStats.Capture.Tabs;

/// <summary>
/// Реализация <see cref="ITabDetector"/>: считывает OCR из ROI активной вкладки
/// и сопоставляет результат с конфигом через <see cref="ITabNameMatcher"/>.
/// </summary>
public sealed class TabDetector : ITabDetector
{
    private readonly IOcrReader _ocr;
    private readonly ITabNameMatcher _matcher;

    /// <summary>
    /// Создаёт экземпляр <see cref="TabDetector"/>.
    /// </summary>
    /// <param name="ocr">Движок OCR для считывания ROI activeTab.</param>
    /// <param name="matcher">Компонент fuzzy-матча распознанного текста с конфигом.</param>
    public TabDetector(IOcrReader ocr, ITabNameMatcher matcher)
    {
        ArgumentNullException.ThrowIfNull(ocr);
        ArgumentNullException.ThrowIfNull(matcher);
        _ocr = ocr;
        _matcher = matcher;
    }

    /// <inheritdoc/>
    public async Task<TabRef?> DetectActiveTabAsync(
        CapturedFrame frame,
        RoiCalibration activeTabRoi,
        GameMechanicsConfig cfg,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        OcrResult ocrResult = await _ocr.ReadAsync(frame, activeTabRoi, ct).ConfigureAwait(false);

        // Если OCR ничего не распознал — возвращаем null (FR-005: не ошибка).
        if (!ocrResult.Recognized)
            return null;

        return _matcher.Match(ocrResult.RawText, cfg);
    }
}
