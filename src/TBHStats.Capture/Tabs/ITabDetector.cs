using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;

namespace TBHStats.Capture.Tabs;

/// <summary>
/// Определяет активную вкладку игрового интерфейса по кадру захвата (FR-002a).
/// Выполняет OCR ROI «activeTab», нормализует распознанный текст и сопоставляет его
/// с записями <see cref="Tab"/> из конфигурации механик через <see cref="ITabNameMatcher"/>.
/// </summary>
public interface ITabDetector
{
    /// <summary>
    /// Определяет активную вкладку по кадру захвата.
    /// </summary>
    /// <param name="frame">Кадр содержимого игрового окна.</param>
    /// <param name="activeTabRoi">
    /// Конфигурация ROI, описывающего область с названием активной вкладки.
    /// Передаётся вызывающим оркестратором (FieldKey == "activeTab").
    /// </param>
    /// <param name="cfg">
    /// Конфигурация механик игры; используется для получения списка вкладок.
    /// </param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>
    /// <see cref="TabRef"/> идентифицирующий активную вкладку с уверенностью распознавания,
    /// если вкладка определена; <c>null</c>, если OCR не распознал текст или
    /// матч с конфигом не прошёл (FR-005: не ошибка).
    /// </returns>
    Task<TabRef?> DetectActiveTabAsync(
        CapturedFrame frame,
        RoiCalibration activeTabRoi,
        GameMechanicsConfig cfg,
        CancellationToken ct);
}
