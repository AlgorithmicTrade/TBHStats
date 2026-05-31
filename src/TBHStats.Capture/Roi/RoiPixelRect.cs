namespace TBHStats.Capture.Roi;

/// <summary>
/// Прямоугольник в пикселях кадра, полученный развёрткой нормализованного ROI.
/// </summary>
public readonly record struct RoiPixelRect(int X, int Y, int Width, int Height)
{
    /// <summary>Правый край (X + Width).</summary>
    public int Right => X + Width;

    /// <summary>Нижний край (Y + Height).</summary>
    public int Bottom => Y + Height;

    /// <summary>True, если прямоугольник пустой (нулевой или отрицательный размер).</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Пустой прямоугольник (начало координат, нулевой размер).</summary>
    public static readonly RoiPixelRect Empty = new(0, 0, 0, 0);
}
