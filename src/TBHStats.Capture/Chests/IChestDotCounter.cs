using TBHStats.Core.Models;

namespace TBHStats.Capture.Chests;

/// <summary>
/// Визуальный детектор заполненных точек сундука в ROI-полосе кадра.
/// </summary>
/// <remarks>
/// Точки под иконкой каждого типа сундука в MainZone — ГРАФИЧЕСКИЕ (не текст):
/// OCR их не читает. Данный детектор анализирует пиксели ROI-полосы напрямую
/// и считает число заполненных (тёмных) ячеек-точек.
/// </remarks>
public interface IChestDotCounter
{
    /// <summary>
    /// Подсчитывает число заполненных (тёмных) точек сундука в указанной ROI-полосе кадра.
    /// </summary>
    /// <param name="frame">Захваченный кадр с <see cref="Windows.Graphics.Imaging.SoftwareBitmap"/>.</param>
    /// <param name="roi">Нормализованные координаты [0..1] полосы точек данного типа сундука.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>
    /// <see cref="ChestDotCountResult"/> с числом заполненных точек и флагом обнаружения паттерна.
    /// Если <see cref="ChestDotCountResult.Detected"/> равен <c>false</c> — ROI не содержит
    /// валидного паттерна точек (например, тёмный игровой фон без панели); счёт не достоверен.
    /// </returns>
    Task<ChestDotCountResult> CountFilledDotsAsync(CapturedFrame frame, RoiCalibration roi, CancellationToken ct);
}
