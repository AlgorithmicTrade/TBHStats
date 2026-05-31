using Windows.Graphics.Imaging;
using TBHStats.Capture.WindowTracking;

namespace TBHStats.Capture;

/// <summary>
/// Один кадр содержимого игрового окна, полученный через WGC.
/// Хранит растровое изображение и метаданные, необходимые для развёртки нормализованных ROI.
/// </summary>
/// <remarks>
/// Является общим типом слоя захвата: возвращается <c>ICaptureSession</c> и потребляется OCR,
/// детектором вкладок и экстрактором полей.
/// Реализует <see cref="IDisposable"/> — по завершении работы с кадром вызвать <see cref="Dispose"/>
/// для освобождения <see cref="SoftwareBitmap"/>.
/// </remarks>
public sealed class CapturedFrame : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Растровое изображение кадра (SoftwareBitmap из WGC).
    /// OCR принимает <see cref="SoftwareBitmap"/> напрямую.
    /// </summary>
    public SoftwareBitmap Bitmap { get; }

    /// <summary>
    /// Размер клиентской области окна на момент захвата кадра.
    /// Используется ROI-маппером для развёртки нормализованных координат [0..1] в пиксели.
    /// </summary>
    public SizePx ClientSize { get; }

    /// <summary>Метка времени захвата кадра (UTC).</summary>
    public DateTimeOffset TimestampUtc { get; }

    /// <summary>
    /// Создаёт экземпляр <see cref="CapturedFrame"/>.
    /// </summary>
    /// <param name="bitmap">Растровое изображение кадра; не может быть <c>null</c>.</param>
    /// <param name="clientSize">Размер клиентской области окна на момент захвата.</param>
    /// <param name="timestampUtc">Метка времени захвата (UTC).</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="bitmap"/> равен <c>null</c>.</exception>
    public CapturedFrame(SoftwareBitmap bitmap, SizePx clientSize, DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        Bitmap = bitmap;
        ClientSize = clientSize;
        TimestampUtc = timestampUtc;
    }

    /// <summary>
    /// Освобождает <see cref="SoftwareBitmap"/>.
    /// После вызова <see cref="Dispose"/> использование кадра запрещено.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Bitmap.Dispose();
    }
}
