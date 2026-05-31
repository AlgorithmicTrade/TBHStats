using TBHStats.Core.Models;
using TBHStats.Capture.WindowTracking;

namespace TBHStats.Capture.Roi;

/// <summary>
/// Преобразует нормализованные ROI-координаты [0..1] в пиксели кадра и обратно.
/// Детерминировано, без статического состояния, потокобезопасно.
/// </summary>
public sealed class RoiMapper : IRoiMapper
{
    /// <inheritdoc/>
    public RoiPixelRect ToPixels(RoiCalibration roi, SizePx clientSize)
    {
        ArgumentNullException.ThrowIfNull(roi);
        return ToPixels(roi.X, roi.Y, roi.W, roi.H, clientSize);
    }

    /// <inheritdoc/>
    public RoiPixelRect ToPixels(double x, double y, double w, double h, SizePx clientSize)
    {
        if (!clientSize.IsNonEmpty)
            return RoiPixelRect.Empty;

        int cw = clientSize.Width;
        int ch = clientSize.Height;

        // Клампинг входных долей к [0..1]
        double cx = Clamp01(x);
        double cy = Clamp01(y);
        double cw01 = Clamp01(w);
        double ch01 = Clamp01(h);

        int px = RoundAwayFromZero(cx * cw);
        int py = RoundAwayFromZero(cy * ch);
        int pw = RoundAwayFromZero(cw01 * cw);
        int ph = RoundAwayFromZero(ch01 * ch);

        // Клампинг итогового прямоугольника по границам кадра
        px = Math.Clamp(px, 0, cw);
        py = Math.Clamp(py, 0, ch);
        pw = Math.Clamp(pw, 0, cw - px);
        ph = Math.Clamp(ph, 0, ch - py);

        return new RoiPixelRect(px, py, pw, ph);
    }

    /// <inheritdoc/>
    public (double X, double Y, double W, double H) ToNormalized(RoiPixelRect rect, SizePx clientSize)
    {
        if (!clientSize.IsNonEmpty)
            return (0.0, 0.0, 0.0, 0.0);

        double cw = clientSize.Width;
        double ch = clientSize.Height;

        return (
            X: rect.X / cw,
            Y: rect.Y / ch,
            W: rect.Width / cw,
            H: rect.Height / ch
        );
    }

    // ── вспомогательные ──────────────────────────────────────────────────────

    private static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);

    private static int RoundAwayFromZero(double value) =>
        (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
