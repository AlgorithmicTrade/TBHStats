namespace TBHStats.Capture.WindowTracking;

/// <summary>
/// Размер области в пикселях (ширина × высота).
/// Используется для передачи размера клиентской области окна игры,
/// чтобы ROI-маппер мог разворачивать нормализованные координаты [0..1] в реальные пиксели.
/// </summary>
public readonly record struct SizePx(int Width, int Height)
{
    /// <summary>Нулевой размер — окно недоступно или невалидно.</summary>
    public static readonly SizePx Empty = new(0, 0);

    /// <summary>True, если размер не нулевой.</summary>
    public bool IsNonEmpty => Width > 0 && Height > 0;
}
