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
///   <item>Опциональная бинаризация: если <c>roi.ParseHint == "binarize_white"</c> —
///         пороговая бинаризация по яркости (<see cref="BinarizeWhiteThreshold"/>).
///         Пиксели с яркостью ≥ порога → белые; остальные → чёрные.
///         Устраняет цветовые боевые эффекты (синий лёд, жёлтый урон, зелёные полоски),
///         выделяя белый текст на тёмном фоне (применяется для <c>nextLocation</c>).</item>
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

        // Бинаризация: применяется когда ParseHint == "binarize_white".
        // Пиксели с яркостью ≥ BinarizeWhiteThreshold → белые; остальные → чёрные.
        // Устраняет цветовые боевые эффекты (синий лёд, жёлтый урон), изолируя белый текст.
        // Используется для nextLocation (мелкий белый текст рядом с яркими эффектами).
        bool binarize = string.Equals(roi.ParseHint, BinarizeWhiteParseHint, StringComparison.Ordinal);
        using SoftwareBitmap? binarized = binarize ? BinarizeWhite(cropped, BinarizeWhiteThreshold) : null;
        SoftwareBitmap cropSrc = binarized ?? cropped;

        // Паддинг: добавляем однотонный бордюр вокруг очень тесных кропов перед апскейлом.
        // Применяется только при min(w,h) < PaddingThreshold — агрессивный апскейл (×3 и выше)
        // сглаживает тонкие глифы (запятая, точка) у края кропа при Fant-интерполяции.
        // При больших кропах (h ≥ 40px) паддинг не нужен: апскейл умеренный (×1–×2).
        int minCropDim = Math.Min(cropSrc.PixelWidth, cropSrc.PixelHeight);
        using SoftwareBitmap? paddedForScale = minCropDim < PaddingThreshold
            ? AddPadding(cropSrc, OcrPaddingPixels)
            : null;
        SoftwareBitmap scaleSrc = paddedForScale ?? cropSrc;

        // Апскейл маленьких кропов (Windows.Media.Ocr не читает слишком мелкие изображения).
        // Для бинаризованных кропов применяем более агрессивный целевой размер: пиксельный шрифт
        // nextLocation (~15px) требует апскейла до ≥192px (×2 от стандартного MinOcrDimension),
        // чтобы отдельные пиксели глифов не сливались после Fant-интерполяции.
        int targetDim = binarize ? MinOcrDimensionBinarized : MinOcrDimension;
        SoftwareBitmap ocrInput = await UpscaleForOcrAsync(scaleSrc, WinOcrEngine.MaxImageDimension, targetDim, ct).ConfigureAwait(false);

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
            // Освобождаем апскейленный bitmap только если он — новый объект (не тот же, что cropSrc)
            if (!ReferenceEquals(ocrInput, cropSrc))
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

    // ── паддинг перед апскейлом ──────────────────────────────────────────────

    /// <summary>
    /// Количество пикселей однотонного паддинга, добавляемого вокруг очень тесных кропов
    /// (min(w,h) &lt; <see cref="PaddingThreshold"/>) перед апскейлом.
    /// </summary>
    /// <remarks>
    /// Паддинг предотвращает слияние символов у края (запятая, точка, «9» после запятой)
    /// с границей изображения при Fant-сглаживании во время агрессивного апскейла (×3–×5).
    /// Цвет паддинга — тёмный непрозрачный (соответствует фону игрового UI Task Bar Hero).
    /// </remarks>
    private const int OcrPaddingPixels = 6;

    /// <summary>
    /// Порог min(w,h) кропа, ниже которого применяется паддинг.
    /// При min(w,h) ≥ этого значения апскейл ≤ ×2 — паддинг не нужен.
    /// При min(w,h) &lt; этого значения апскейл ≥ ×3 — паддинг защищает граничные глифы.
    /// </summary>
    private const int PaddingThreshold = 40;

    /// <summary>
    /// Создаёт новый <see cref="SoftwareBitmap"/> с однотонным бордюром
    /// шириной <paramref name="pad"/> пикселей вокруг <paramref name="src"/>.
    /// Формат результата — Bgra8 Premultiplied (тот же, что у входного кропа).
    /// Цвет паддинга — полностью прозрачный тёмный (B=0 G=0 R=0 A=255 premult → 0x00 00 00 FF).
    /// </summary>
    private static SoftwareBitmap AddPadding(SoftwareBitmap src, int pad)
    {
        int sw = src.PixelWidth;
        int sh = src.PixelHeight;
        int dw = sw + pad * 2;
        int dh = sh + pad * 2;

        const int bpp = 4; // Bgra8
        int srcStride = sw * bpp;
        int dstStride = dw * bpp;

        byte[] srcPixels = new byte[srcStride * sh];
        src.CopyToBuffer(srcPixels.AsBuffer());

        // Заполняем тёмным непрозрачным цветом (B=0, G=0, R=0, A=255 → pre-mult = 0,0,0,255)
        byte[] dstPixels = new byte[dstStride * dh];
        for (int i = 3; i < dstPixels.Length; i += bpp)
            dstPixels[i] = 0xFF; // alpha = 255

        // Копируем исходный кроп в центр
        for (int row = 0; row < sh; row++)
        {
            int srcOff = row * srcStride;
            int dstOff = (row + pad) * dstStride + pad * bpp;
            System.Buffer.BlockCopy(srcPixels, srcOff, dstPixels, dstOff, srcStride);
        }

        SoftwareBitmap result = new(BitmapPixelFormat.Bgra8, dw, dh, BitmapAlphaMode.Premultiplied);
        result.CopyFromBuffer(dstPixels.AsBuffer());
        return result;
    }

    // ── бинаризация белого текста ─────────────────────────────────────────────

    /// <summary>
    /// Пороговая бинаризация: пиксели с яркостью ≥ <paramref name="threshold"/> → белые;
    /// остальные → чёрные. Выделяет белый текст, устраняя цветовые боевые эффекты.
    /// </summary>
    /// <remarks>
    /// Яркость вычисляется как среднее (R + G + B) / 3 (без гамма-коррекции — достаточно
    /// для практических порогов в пиксельных шрифтах idle RPG).
    /// Входной bitmap — Bgra8 Premultiplied (порядок байт: B, G, R, A).
    /// Premultiplied-режим: R/G/B уже умножены на A/255. При A=255 (непрозрачный)
    /// значения идентичны Straight. Для OCR-фикстур (JPG) alpha всегда 255.
    /// </remarks>
    private static SoftwareBitmap BinarizeWhite(SoftwareBitmap src, int threshold)
    {
        int w = src.PixelWidth;
        int h = src.PixelHeight;
        const int bpp = 4; // Bgra8
        int stride = w * bpp;
        byte[] pixels = new byte[stride * h];
        src.CopyToBuffer(pixels.AsBuffer());

        for (int i = 0; i < pixels.Length; i += bpp)
        {
            int b = pixels[i];
            int g = pixels[i + 1];
            int r = pixels[i + 2];
            // alpha (pixels[i+3]) оставляем без изменений — нужен для Premultiplied-формата
            int brightness = (r + g + b) / 3;
            byte fill = brightness >= threshold ? (byte)255 : (byte)0;
            pixels[i]     = fill; // B
            pixels[i + 1] = fill; // G
            pixels[i + 2] = fill; // R
            // A остаётся как есть
        }

        SoftwareBitmap result = new(BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Premultiplied);
        result.CopyFromBuffer(pixels.AsBuffer());
        return result;
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
    /// Целевой минимум меньшей стороны кропа для бинаризованных полей (ParseHint="binarize_white").
    /// </summary>
    /// <remarks>
    /// Пиксельный bitmap-шрифт nextLocation (~15px высота глифов) требует более агрессивного
    /// апскейла чем обычный текст: при стандартном MinOcrDimension=96 отдельные пиксели букв
    /// сливаются после Fant-интерполяции и OCR не распознаёт глифы.
    /// MinOcrDimensionBinarized=192 (×2 от стандарта) обеспечивает ~4-пиксельные штрихи
    /// после апскейла, что достаточно для Windows.Media.Ocr.
    /// </remarks>
    private const int MinOcrDimensionBinarized = 192;

    /// <summary>
    /// Значение ParseHint, активирующее пороговую бинаризацию белого текста.
    /// </summary>
    /// <remarks>
    /// Используется для полей с белым текстом на фоне с яркими цветными эффектами
    /// (nextLocation — мелкий белый текст рядом с боевыми эффектами в MainZone).
    /// При этом хинте: пиксели с яркостью ≥ <see cref="BinarizeWhiteThreshold"/> → белые;
    /// остальные → чёрные. Убирает синие/жёлтые/зелёные эффекты, сохраняя белый текст.
    /// </remarks>
    public const string BinarizeWhiteParseHint = "binarize_white";

    /// <summary>
    /// Порог яркости (среднее R+G+B / 3) для бинаризации белого текста.
    /// Пиксели с яркостью ≥ этого порога считаются «белыми» (текст), остальные — «чёрными» (фон).
    /// </summary>
    /// <remarks>
    /// Значение 160 выбрано эмпирически: белый текст TBH имеет яркость ≥ 200,
    /// а боевые эффекты (синий лёд, жёлтый урон) — яркость &lt; 140 в их доминирующем канале,
    /// но суммарная яркость часто &lt; 160.
    /// </remarks>
    private const int BinarizeWhiteThreshold = 160;

    /// <summary>
    /// Возвращает версию <paramref name="cropped"/>, увеличенную целочисленным множителем так,
    /// чтобы меньшая сторона была не менее <paramref name="minOcrDimension"/> (Windows.Media.Ocr
    /// не распознаёт слишком маленькие изображения). Если апскейл не нужен — возвращает тот же объект.
    /// Масштаб ограничен <paramref name="maxDimension"/>, чтобы не превысить лимит движка.
    /// </summary>
    private static async Task<SoftwareBitmap> UpscaleForOcrAsync(
        SoftwareBitmap cropped,
        uint maxDimension,
        int minOcrDimension,
        CancellationToken ct)
    {
        int w = cropped.PixelWidth;
        int h = cropped.PixelHeight;
        int minDim = Math.Min(w, h);

        if (minDim <= 0 || minDim >= minOcrDimension)
            return cropped;

        int scale = (int)Math.Ceiling((double)minOcrDimension / minDim);
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
