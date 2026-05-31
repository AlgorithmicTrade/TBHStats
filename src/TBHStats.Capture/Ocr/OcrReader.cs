using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using TBHStats.Core.Models;
using TBHStats.Capture.Roi;

// Псевдонимы для устранения конфликта имён между TBHStats.Core.Models.OcrEngine и Windows.Media.Ocr.OcrEngine
using WinOcrEngine = Windows.Media.Ocr.OcrEngine;
using WinOcrLine   = Windows.Media.Ocr.OcrLine;
using WinOcrWord   = Windows.Media.Ocr.OcrWord;
using WinOcrResult = Windows.Media.Ocr.OcrResult;

namespace TBHStats.Capture.Ocr;

/// <summary>
/// Реализует <see cref="IOcrReader"/> поверх встроенного движка <c>Windows.Media.Ocr</c>.
/// </summary>
/// <remarks>
/// Алгоритм:
/// <list type="number">
///   <item>ROI → пиксели через <see cref="RoiMapper"/>.</item>
///   <item>Кроп <see cref="SoftwareBitmap"/> к пиксельному прямоугольнику через
///         <c>BitmapEncoder</c>/<c>BitmapDecoder</c> + <c>BitmapBounds</c>.</item>
///   <item>Распознавание кропа через <c>WinOcrEngine.RecognizeAsync</c>.</item>
///   <item>Confidence вычисляется как геометрическое покрытие: отношение суммарной площади
///         bounding-box'ов слов к площади ROI (clamp [0..1]).</item>
/// </list>
///
/// Движок создаётся лениво при первом вызове и кэшируется. Если движок недоступен
/// (<c>TryCreateFromUserProfileLanguages</c> и <c>TryCreateFromLanguage("en")</c> оба вернули null),
/// метод возвращает «не распознано» без исключения.
/// </remarks>
public sealed class OcrReader : IOcrReader
{
    private readonly RoiMapper _mapper = new();

    // Лениво инициализируемый WinRT-движок.
    // null после инициализации означает недоступность движка (оба TryCreate вернули null).
    private WinOcrEngine? _engine;
    private bool _engineInitialized;
    private readonly object _engineLock = new();

    // ── публичный API ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<OcrResult> ReadAsync(CapturedFrame frame, RoiCalibration roi, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(roi);

        ct.ThrowIfCancellationRequested();

        WinOcrEngine? engine = GetOrCreateEngine();
        if (engine is null)
            return NotRecognized;

        // Развернуть нормализованный ROI в пиксели текущего кадра
        RoiPixelRect pixelRect = _mapper.ToPixels(roi, frame.ClientSize);
        if (pixelRect.IsEmpty)
            return NotRecognized;

        // Кроп SoftwareBitmap к ROI-прямоугольнику
        using SoftwareBitmap? cropped = await CropAsync(frame.Bitmap, pixelRect, ct).ConfigureAwait(false);
        if (cropped is null)
            return NotRecognized;

        // Конвертировать в формат, требуемый WinOcrEngine (Bgra8 Premultiplied)
        SoftwareBitmap bitmapForOcr = EnsureOcrFormat(cropped);
        try
        {
            WinOcrResult winResult = await engine.RecognizeAsync(bitmapForOcr)
                .AsTask(ct)
                .ConfigureAwait(false);

            string rawText = winResult.Text ?? string.Empty;
            bool recognized = !string.IsNullOrWhiteSpace(rawText);
            double confidence = recognized
                ? ComputeGeometricConfidence(winResult, pixelRect)
                : 0.0;

            return new OcrResult(rawText, confidence, recognized);
        }
        finally
        {
            // Освобождаем конвертированный bitmap только если он — новый объект (не тот же, что cropped)
            if (!ReferenceEquals(bitmapForOcr, cropped))
                bitmapForOcr.Dispose();
        }
    }

    // ── вспомогательные методы ────────────────────────────────────────────────

    private static readonly OcrResult NotRecognized = new(string.Empty, 0.0, false);

    /// <summary>
    /// Возвращает кэшированный движок OCR или создаёт его при первом обращении.
    /// Возвращает <c>null</c>, если оба метода создания вернули null (движок недоступен).
    /// </summary>
    private WinOcrEngine? GetOrCreateEngine()
    {
        if (_engineInitialized)
            return _engine;

        lock (_engineLock)
        {
            if (_engineInitialized)
                return _engine;

            _engine = WinOcrEngine.TryCreateFromUserProfileLanguages()
                   ?? WinOcrEngine.TryCreateFromLanguage(new Language("en"));
            _engineInitialized = true;
        }

        return _engine;
    }

    /// <summary>
    /// Обрезает <paramref name="source"/> до <paramref name="rect"/> через кодек в памяти.
    /// Использует <c>BitmapEncoder</c>/<c>BitmapDecoder</c> с <c>BitmapTransform.Bounds</c>
    /// для извлечения суб-региона без ручного попиксельного копирования.
    /// Возвращает <c>null</c>, если кроп не удался.
    /// </summary>
    private static async Task<SoftwareBitmap?> CropAsync(
        SoftwareBitmap source,
        RoiPixelRect rect,
        CancellationToken ct)
    {
        // Клампинг к фактическим размерам исходного bitmap
        int srcW = source.PixelWidth;
        int srcH = source.PixelHeight;

        int x = Math.Clamp(rect.X, 0, srcW);
        int y = Math.Clamp(rect.Y, 0, srcH);
        int w = Math.Clamp(rect.Width, 0, srcW - x);
        int h = Math.Clamp(rect.Height, 0, srcH - y);

        if (w <= 0 || h <= 0)
            return null;

        ct.ThrowIfCancellationRequested();

        using InMemoryRandomAccessStream stream = new();

        // Шаг 1: записать исходный bitmap в поток через BmpEncoder
        BitmapEncoder encoder = await BitmapEncoder
            .CreateAsync(BitmapEncoder.BmpEncoderId, stream)
            .AsTask(ct)
            .ConfigureAwait(false);

        encoder.SetSoftwareBitmap(source);
        await encoder.FlushAsync()
            .AsTask(ct)
            .ConfigureAwait(false);

        // Шаг 2: декодировать с BitmapTransform.Bounds → получить суб-регион
        stream.Seek(0);
        BitmapDecoder decoder = await BitmapDecoder
            .CreateAsync(stream)
            .AsTask(ct)
            .ConfigureAwait(false);

        BitmapTransform transform = new()
        {
            Bounds = new BitmapBounds
            {
                X      = (uint)x,
                Y      = (uint)y,
                Width  = (uint)w,
                Height = (uint)h,
            }
        };

        PixelDataProvider pixelData = await decoder
            .GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage)
            .AsTask(ct)
            .ConfigureAwait(false);

        byte[] pixels = pixelData.DetachPixelData();

        SoftwareBitmap cropped = new(BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Premultiplied);
        cropped.CopyFromBuffer(pixels.AsBuffer());
        return cropped;
    }

    /// <summary>
    /// Возвращает bitmap в формате, требуемом <c>WinOcrEngine</c> (Bgra8, Premultiplied).
    /// Если исходный bitmap уже в нужном формате — возвращает тот же объект без копии.
    /// Иначе — создаёт новый через <see cref="SoftwareBitmap.Convert"/>.
    /// </summary>
    private static SoftwareBitmap EnsureOcrFormat(SoftwareBitmap bitmap)
    {
        if (bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8
            && bitmap.BitmapAlphaMode == BitmapAlphaMode.Premultiplied)
        {
            return bitmap;
        }

        return SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }

    /// <summary>
    /// Вычисляет эвристику достоверности OCR как геометрическое покрытие:
    /// ∑(wordBoundsArea) / roiArea, clamp [0..1].
    /// </summary>
    /// <remarks>
    /// Windows.Media.Ocr не предоставляет числовую confidence per-word.
    /// Геометрическое покрытие — аппроксимация: чем больше площади ROI «объяснено»
    /// распознанными словами, тем выше вероятность корректного результата.
    /// Для коротких числовых строк (gold, xp и т.д.) значение близко к 0,1–0,4 при успехе
    /// и равно 0 при отсутствии текста.
    /// </remarks>
    private static double ComputeGeometricConfidence(WinOcrResult ocrResult, RoiPixelRect roiRect)
    {
        double roiArea = (double)roiRect.Width * roiRect.Height;
        if (roiArea <= 0)
            return 0.0;

        double coveredArea = 0.0;
        foreach (WinOcrLine line in ocrResult.Lines)
        {
            foreach (WinOcrWord word in line.Words)
            {
                Rect b = word.BoundingRect;
                coveredArea += b.Width * b.Height;
            }
        }

        return Math.Clamp(coveredArea / roiArea, 0.0, 1.0);
    }
}
