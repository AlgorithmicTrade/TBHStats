using TBHStats.Core.Models;
using TBHStats.Capture.WindowTracking;

namespace TBHStats.Capture.Roi;

/// <summary>
/// Преобразует нормализованные ROI-координаты [0..1] в пиксели кадра и обратно.
/// Инвариантен к масштабу: одинаковые доли дают пропорциональные пиксельные прямоугольники
/// при любом разрешении окна.
/// </summary>
public interface IRoiMapper
{
    /// <summary>
    /// Разворачивает нормализованный ROI из <see cref="RoiCalibration"/> в пиксели кадра.
    /// </summary>
    /// <param name="roi">Нормализованная конфигурация области интереса.</param>
    /// <param name="clientSize">Актуальный размер клиентской области окна.</param>
    /// <returns>Прямоугольник в пикселях; <see cref="RoiPixelRect.Empty"/> если clientSize пустой.</returns>
    RoiPixelRect ToPixels(RoiCalibration roi, SizePx clientSize);

    /// <summary>
    /// Разворачивает сырые нормализованные координаты в пиксели кадра.
    /// Координаты клампируются к [0..1] перед вычислением.
    /// </summary>
    RoiPixelRect ToPixels(double x, double y, double w, double h, SizePx clientSize);

    /// <summary>
    /// Переводит пиксельный прямоугольник обратно в нормализованные доли.
    /// Используется при калибровке: пользователь рисует прямоугольник в пикселях →
    /// сохраняем доли для <see cref="RoiCalibration"/>.
    /// </summary>
    /// <returns>Нормализованные (X, Y, W, H); все нули если clientSize пустой.</returns>
    (double X, double Y, double W, double H) ToNormalized(RoiPixelRect rect, SizePx clientSize);
}
