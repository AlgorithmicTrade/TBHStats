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
///   <item>Кроп <see cref="SoftwareBitmap"/> к пиксельному прямоугольнику через прямой доступ
///         к пикселям кадра (без BMP-кодека). Полнокадровый буфер кэшируется на уровне кадра
///         (ключ — ссылка <c>ReferenceEquals</c>); sub-rect вырезается построчной копией.</item>
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

    // ── кэш пиксельного буфера кадра ─────────────────────────────────────────
    // Храним только скопированные байты и метаданные; ссылку на SoftwareBitmap —
    // исключительно для идентификации по ReferenceEquals. Не диспозим чужой кадр.
    private SoftwareBitmap? _cachedBitmapKey;
    private byte[]?         _cachedPixels;
    private int             _cachedWidth;
    private int             _cachedHeight;
    private int             _cachedStride;

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

        // Получить или заполнить кэш пиксельного буфера кадра.
        // Копирование буфера выполняется вне lock, чтобы не блокировать конкурентный вызов дольше необходимого.
        byte[] framePixels;
        int    frameWidth;
        int    frameHeight;
        int    frameStride;

        (framePixels, frameWidth, frameHeight, frameStride) = GetOrFillFrameCache(frame.Bitmap);

        // Кроп sub-rect из managed-буфера (синхронно, без кодека)
        using SoftwareBitmap? cropped = CropFromBuffer(
            framePixels, frameWidth, frameHeight, frameStride, pixelRect);
        if (cropped is null)
            return NotRecognized;

        // Апскейл маленьких кропов (Windows.Media.Ocr не читает слишком мелкие изображения).
        SoftwareBitmap ocrInput = await UpscaleForOcrAsync(cropped, WinOcrEngine.MaxImageDimension, ct).ConfigureAwait(false);

        // Конвертировать в формат, требуемый WinOcrEngine (Bgra8 Premultiplied)
        SoftwareBitmap bitmapForOcr = EnsureOcrFormat(ocrInput);
        try
        {
            WinOcrResult winResult = await engine.RecognizeAsync(bitmapForOcr)
                .AsTask(ct)
                .ConfigureAwait(false);

            string rawText = winResult.Text ?? string.Empty;
            bool recognized = !string.IsNullOrWhiteSpace(rawText);
            // Покрытие считаем относительно площади изображения, по которому реально работал OCR.
            double confidence = recognized
                ? ComputeGeometricConfidence(winResult, bitmapForOcr.PixelWidth, bitmapForOcr.PixelHeight)
                : 0.0;

            return new OcrResult(rawText, confidence, recognized);
        }
        finally
        {
            // Освобождаем конвертированный bitmap только если он — новый объект (не тот же, что ocrInput)
            if (!ReferenceEquals(bitmapForOcr, ocrInput))
                bitmapForOcr.Dispose();
            // Освобождаем апскейленный bitmap только если он — новый объект (не тот же, что cropped)
            if (!ReferenceEquals(ocrInput, cropped))
                ocrInput.Dispose();
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

    // ── кэш пиксельного буфера ────────────────────────────────────────────────

    /// <summary>
    /// Возвращает (pixels, width, height, stride) для <paramref name="bitmap"/>.
    /// При промахе — конвертирует в Bgra8 Premultiplied (если нужно), копирует весь буфер
    /// через <c>SoftwareBitmap.CopyToBuffer</c> + managed <c>IBuffer</c>,
    /// сохраняет в кэш. При попадании — возвращает кэшированные данные без копирования.
    /// Доступ к кэшу защищён <c>_engineLock</c> (тот же объект, что и для движка).
    /// </summary>
    private (byte[] pixels, int width, int height, int stride) GetOrFillFrameCache(SoftwareBitmap bitmap)
    {
        lock (_engineLock)
        {
            if (ReferenceEquals(_cachedBitmapKey, bitmap) && _cachedPixels is not null)
                return (_cachedPixels, _cachedWidth, _cachedHeight, _cachedStride);
        }

        // Промах — копируем буфер вне lock (самая длинная часть).
        // Если bitmap не Bgra8 Premultiplied — конвертируем во временный объект.
        SoftwareBitmap? converted = null;
        SoftwareBitmap src = bitmap;

        if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8
            || bitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
        {
            converted = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            src = converted;
        }

        int width   = src.PixelWidth;
        int height  = src.PixelHeight;
        // Bgra8 — 4 байта на пиксель; stride выровнен на 4 байта (всегда width*4 для Bgra8).
        int stride  = width * 4;
        int bufSize = stride * height;

        byte[] pixels = new byte[bufSize];

        // SoftwareBitmap.CopyToBuffer — чистый WinRT-метод без COM-interop/unsafe.
        src.CopyToBuffer(pixels.AsBuffer());

        converted?.Dispose();

        lock (_engineLock)
        {
            _cachedBitmapKey = bitmap;
            _cachedPixels    = pixels;
            _cachedWidth     = width;
            _cachedHeight    = height;
            _cachedStride    = stride;
        }

        return (pixels, width, height, stride);
    }

    /// <summary>
    /// Вырезает sub-rect из managed-буфера пикселей Bgra8 построчной копией.
    /// Возвращает <c>null</c>, если прямоугольник пуст или выходит за границы после клампинга.
    /// Не использует кодек — работает в O(w*h) с одним <c>new SoftwareBitmap</c>.
    /// </summary>
    private static SoftwareBitmap? CropFromBuffer(
        byte[] srcPixels,
        int    srcWidth,
        int    srcHeight,
        int    srcStride,
        RoiPixelRect rect)
    {
        int x = Math.Clamp(rect.X, 0, srcWidth);
        int y = Math.Clamp(rect.Y, 0, srcHeight);
        int w = Math.Clamp(rect.Width,  0, srcWidth  - x);
        int h = Math.Clamp(rect.Height, 0, srcHeight - y);

        if (w <= 0 || h <= 0)
            return null;

        const int bytesPerPixel = 4; // Bgra8
        int dstStride = w * bytesPerPixel;
        byte[] dstPixels = new byte[dstStride * h];

        for (int row = 0; row < h; row++)
        {
            int srcOffset = (y + row) * srcStride + x * bytesPerPixel;
            int dstOffset = row * dstStride;
            System.Buffer.BlockCopy(srcPixels, srcOffset, dstPixels, dstOffset, dstStride);
        }

        SoftwareBitmap cropped = new(BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Premultiplied);
        cropped.CopyFromBuffer(dstPixels.AsBuffer());
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
    /// ∑(wordBoundsArea) / imageArea, clamp [0..1].
    /// </summary>
    /// <param name="ocrResult">Результат <c>WinOcrEngine.RecognizeAsync</c>.</param>
    /// <param name="imageWidth">Ширина изображения, по которому реально работал OCR (после апскейла).</param>
    /// <param name="imageHeight">Высота изображения, по которому реально работал OCR (после апскейла).</param>
    /// <remarks>
    /// Windows.Media.Ocr не предоставляет числовую confidence per-word.
    /// Геометрическое покрытие — аппроксимация: чем больше площади изображения «объяснено»
    /// распознанными словами, тем выше вероятность корректного результата.
    /// Для коротких числовых строк (gold, xp и т.д.) значение близко к 0,1–0,4 при успехе
    /// и равно 0 при отсутствии текста.
    /// Площадь берётся по реальному OCR-входу (после апскейла), чтобы bounding-box'ы
    /// в увеличенных координатах не искажали покрытие относительно исходного ROI.
    /// </remarks>
    private static double ComputeGeometricConfidence(WinOcrResult ocrResult, int imageWidth, int imageHeight)
    {
        double imageArea = (double)imageWidth * imageHeight;
        if (imageArea <= 0)
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

        return Math.Clamp(coveredArea / imageArea, 0.0, 1.0);
    }

    // ── апскейл маленьких кропов ──────────────────────────────────────────────

    /// <summary>Целевой минимум меньшей стороны кропа для надёжного OCR.</summary>
    /// <remarks>
    /// Windows.Media.Ocr возвращает пустой результат на изображениях, у которых меньшая
    /// сторона значительно меньше этого порога (эмпирически подтверждено: gold ≈109×27px,
    /// xp ≈144×20px, heroLevel ≈118×24px — пусто; крупные ROI читаются).
    /// Повышено с 64 до 96: мелкие поля (heroLevel "26"/"27") нестабильно распознавались
    /// при меньшем пороге — больший апскейл повышает надёжность.
    /// </remarks>
    private const int MinOcrDimension = 96;

    /// <summary>
    /// Возвращает версию <paramref name="cropped"/>, увеличенную целочисленным множителем так,
    /// чтобы меньшая сторона была не менее <see cref="MinOcrDimension"/> (Windows.Media.Ocr
    /// не распознаёт слишком маленькие изображения). Если апскейл не нужен — возвращает тот же объект.
    /// Масштаб ограничен <paramref name="maxDimension"/>, чтобы не превысить лимит движка.
    /// </summary>
    private static async Task<SoftwareBitmap> UpscaleForOcrAsync(
        SoftwareBitmap cropped,
        uint maxDimension,
        CancellationToken ct)
    {
        int w = cropped.PixelWidth;
        int h = cropped.PixelHeight;
        int minDim = Math.Min(w, h);

        if (minDim <= 0 || minDim >= MinOcrDimension)
            return cropped;

        int scale = (int)Math.Ceiling((double)MinOcrDimension / minDim);
        int cap = maxDimension > 0 ? (int)maxDimension : 4096;

        while (scale > 1 && ((long)w * scale > cap || (long)h * scale > cap))
            scale--;

        if (scale <= 1)
            return cropped;

        uint sw = (uint)(w * scale);
        uint sh = (uint)(h * scale);

        using InMemoryRandomAccessStream stream = new();

        BitmapEncoder encoder = await BitmapEncoder
            .CreateAsync(BitmapEncoder.BmpEncoderId, stream)
            .AsTask(ct)
            .ConfigureAwait(false);

        encoder.SetSoftwareBitmap(cropped);
        await encoder.FlushAsync()
            .AsTask(ct)
            .ConfigureAwait(false);

        stream.Seek(0);
        BitmapDecoder decoder = await BitmapDecoder
            .CreateAsync(stream)
            .AsTask(ct)
            .ConfigureAwait(false);

        BitmapTransform transform = new()
        {
            ScaledWidth  = sw,
            ScaledHeight = sh,
            InterpolationMode = BitmapInterpolationMode.Fant,
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

        SoftwareBitmap upscaled = new(BitmapPixelFormat.Bgra8, (int)sw, (int)sh, BitmapAlphaMode.Premultiplied);
        upscaled.CopyFromBuffer(pixels.AsBuffer());
        return upscaled;
    }
}
