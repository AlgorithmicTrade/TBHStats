using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;
using TBHStats.Capture.Roi;

namespace TBHStats.Capture.Chests;

/// <summary>
/// Реализует <see cref="IChestDotCounter"/> и <see cref="IChestPanelAnalyzer"/>:
/// определяет тип сундука по цвету фона плашки и считает заполненные точки (ADR-022).
/// </summary>
/// <remarks>
/// Алгоритм (единый для обоих интерфейсов):
/// <list type="number">
///   <item>Развернуть нормализованный ROI в пиксели кадра.</item>
///   <item>Скопировать пиксельный буфер (Bgra8) — кэш разделяется на уровне экземпляра
///         для переиспользования при нескольких ROI на одном кадре.</item>
///   <item>Классификация типа (<see cref="IChestPanelAnalyzer"/> путь):
///     <list type="bullet">
///       <item>Собрать пиксели с luminance &gt; <see cref="BrightPanelThreshold"/> (фон плашки;
///             тёмный спрайт/точки/сцена исключаются).</item>
///       <item>Вычислить медиану R/G/B по ярким пикселям — <c>panelColor</c>.</item>
///       <item>Если доля ярких пикселей &lt; <see cref="MinBrightCoverage"/> или
///             минимальное евклидово расстояние до якорей &gt; <see cref="ColorMatchTolerance"/>
///             — reject (<c>ChestTypeId=null</c>).</item>
///       <item>Иначе выбрать ближайший тип (наименьшее расстояние).</item>
///     </list>
///   </item>
///   <item>Счёт точек (оба интерфейса):
///     <list type="bullet">
///       <item>Для каждой вертикальной колонки: доля тёмных пикселей (luminance &lt; <see cref="DarkThreshold"/>).</item>
///       <item>Колонка «тёмная» при <c>darkFraction ≥ <see cref="MinDarkColumnFraction"/></c>.</item>
///       <item>Непрерывный run тёмных колонок шириной ≥ <see cref="MinRunWidthPx"/> = одна заполненная точка.</item>
///     </list>
///   </item>
/// </list>
///
/// Пороги счёта точек выверены эмпирически на фикстуре <c>screenshots/chests.jpg</c> (545×241):
/// ground truth red=1, blue=1, brown=2. Тёмные точки: luminance 9–86; пустые ячейки: 210–255.
///
/// Пороги классификации цвета выверены по якорям GameMechanicsConfig.CreateDefault():
/// red=(236,133,41), blue=(190,220,238), brown=(255,255,255).
/// Расстояния между якорями: orange↔white≈190, lightblue↔white≈75, orange↔lightblue≈210.
/// Толеранс <see cref="ColorMatchTolerance"/> = 70 надёжно разделяет все три типа и отвергает
/// тёмную сцену (медианный цвет тёмного фона не попадает в диапазон ярких пикселей плашки;
/// reject обеспечивается фильтром <see cref="MinBrightCoverage"/>).
/// </remarks>
public sealed class ChestDotCounter : IChestDotCounter, IChestPanelAnalyzer
{
    private readonly RoiMapper _mapper = new();

    // ── пороги яркости (luminance = 0.299R + 0.587G + 0.114B) ───────────────

    /// <summary>
    /// Порог яркости: пиксель считается «тёмным» (внутри заполненной точки),
    /// если его luminance строго меньше этого значения.
    /// </summary>
    /// <remarks>
    /// Эмпирически: заполненные точки всех трёх типов имеют luminance 9–86
    /// (red ~9–40, blue ~24–40, brown ~40–86). Фоновая панель — 130–205.
    /// Порог 90 оставляет запас от самой яркой тёмной точки (brown≈86) до панели (≥130).
    /// </remarks>
    private const int DarkThreshold = 90;

    /// <summary>
    /// Порог яркости: пиксель считается «светлым» (пустая незаполненная ячейка или фон),
    /// если его luminance строго больше этого значения.
    /// </summary>
    /// <remarks>
    /// Эмпирически: незаполненные ячейки (белые квадраты вместимости) имеют luminance 210–255.
    /// Порог 210 надёжно отделяет их от заполненных точек и панели.
    /// Наличие хотя бы одного «светлого» пикселя в ROI = признак валидной панели сундука
    /// (для обратной совместимости <see cref="IChestDotCounter.CountFilledDotsAsync"/>).
    /// </remarks>
    private const int BrightThreshold = 210;

    /// <summary>
    /// Порог яркости для сбора ярких пикселей плашки при классификации цвета (ADR-022).
    /// Пиксель считается частью фона плашки, если luminance ≥ этого значения.
    /// </summary>
    /// <remarks>
    /// Значение 110 включает фон плашек brown/blue/red (lum ~130–255) и белые пустые ячейки
    /// (lum ~210–255), но исключает тёмный спрайт иконки (lum ~9–86), тёмные точки (~9–86)
    /// и тёмную сцену MainZone вне плашек (lum &lt; ~100).
    /// Медиана по ярким пикселям устойчива к спрайту/точкам: они уже исключены фильтром.
    /// </remarks>
    private const int BrightPanelThreshold = 110;

    /// <summary>
    /// Минимальная доля «тёмных» пикселей в вертикальной колонке ROI,
    /// при которой колонка считается «тёмной» (внутри заполненной точки).
    /// </summary>
    /// <remarks>
    /// Значение 0.3 означает: в колонке должно быть ≥30% тёмных пикселей.
    /// Эмпирически по фикстуре chests.jpg (545×241):
    ///   - Red/blue точки: 6 тёмных строк из 13 строк ROI → darkFraction≈0.46 ≥ 0.3 ✓
    ///   - Brown точки: 4 тёмных строки из 13 строк ROI → darkFraction≈0.31 ≥ 0.3 ✓
    ///     (brown точки отображаются только в верхних ~4px панели, в отличие от red/blue ~6px)
    /// Тёмные рамки панелей отсекаются правильной настройкой x-диапазона ROI (не ROI должен
    /// включать рамки), а не только этим порогом.
    /// Фоновые средние пиксели (lum=130–160) не считаются «тёмными» (DarkThreshold=90) →
    /// не влияют на darkFraction.
    /// </remarks>
    private const double MinDarkColumnFraction = 0.3;

    /// <summary>
    /// Минимальная ширина (в колонках/пикселях) непрерывного run тёмных колонок,
    /// чтобы он считался «заполненной точкой».
    /// </summary>
    /// <remarks>
    /// Эмпирически: одна точка занимает ~5–6px по ширине. Порог 4 отсекает
    /// артефакты шириной 1–3px (тени рамок краёв плашки) и не «съедает» реальные точки.
    /// Brown точки: 6px (≥4 ✓). Red/blue точки: ~5–6px (≥4 ✓).
    /// Рамки краёв плашек red/blue: ~3px → отвергаются (ширина &lt; 4 ✓).
    /// Предыдущее значение 3 позволяло ложному run рамки давать фантомную точку.
    /// </remarks>
    private const int MinRunWidthPx = 4;

    // ── пороги классификации цвета плашки (ADR-022) ──────────────────────────

    /// <summary>
    /// Максимально допустимое евклидово расстояние RGB между медианным цветом плашки
    /// и ближайшим якорным цветом типа сундука для успешной классификации.
    /// </summary>
    /// <remarks>
    /// Расстояния между якорями (ADR-022):
    ///   - orange(236,133,41) ↔ white(255,255,255): ≈ 190
    ///   - lightblue(190,220,238) ↔ white(255,255,255): ≈ 75
    ///   - orange(236,133,41) ↔ lightblue(190,220,238): ≈ 210
    /// Порог 70 надёжно разделяет все три типа (минимальное межъякорное расстояние / 2 ≈ 37,5,
    /// порог 70 выбран с запасом на вариацию освещения/JPEG-артефакты).
    /// Тёмная сцена MainZone отвергается ещё раньше фильтром MinBrightCoverage.
    /// </remarks>
    private const double ColorMatchTolerance = 70.0;

    /// <summary>
    /// Минимальная доля ярких пикселей (luminance ≥ <see cref="BrightPanelThreshold"/>)
    /// в ROI для признания наличия плашки.
    /// </summary>
    /// <remarks>
    /// При значении 0.25: плашка должна покрывать ≥25% площади ROI яркими пикселями.
    /// Тёмная сцена MainZone (lum &lt;~100) не набирает этого порога → reject.
    /// Плашки с небольшим спрайтом: белая/голубая/оранжевая область занимает ~60–80% ROI
    /// (спрайт иконки + тёмные точки ≈ 20–40%) → brightCoverage ≥ 0.5 ≫ порога.
    /// Значение 0.25 обеспечивает запас при крупном спрайте или неточном ROI.
    /// </remarks>
    private const double MinBrightCoverage = 0.25;

    /// <summary>
    /// Доля от высоты ROI, используемая для счёта точек: берётся только нижняя полоса.
    /// </summary>
    /// <remarks>
    /// Ряд точек расположен в нижней части плашки сундука (~35% высоты).
    /// Верхние ~65% занимает спрайт иконки — он содержит тёмные области, которые
    /// run-алгоритм ошибочно посчитал бы за точки при использовании полного ROI.
    /// Значение 0.38 (нижние 38%) — эмпирически по chests.jpg (545×241):
    ///   - Плашки y≈67..127 (60px); ряд точек y≈104..127 (23px ≈ 38%).
    ///   - Спрайт иконки y≈67..103 (37px ≈ 62%) — исключается.
    /// </remarks>
    private const double DotStripFraction = 0.38;

    // ── кэш буфера кадра ─────────────────────────────────────────────────────
    // Хранит скопированные байты последнего обработанного кадра для переиспользования
    // при нескольких вызовах на одном кадре (например, для red/blue/brown сундуков подряд).

    private SoftwareBitmap? _cachedBitmapKey;
    private byte[]?         _cachedPixels;
    private int             _cachedWidth;
    private int             _cachedHeight;
    private int             _cachedStride;
    private readonly object _cacheLock = new();

    // ── IChestDotCounter (legacy — обратная совместимость) ────────────────────

    /// <inheritdoc/>
    public Task<ChestDotCountResult> CountFilledDotsAsync(
        CapturedFrame frame,
        RoiCalibration roi,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(roi);

        ct.ThrowIfCancellationRequested();

        RoiPixelRect pixelRect = _mapper.ToPixels(roi, frame.ClientSize);
        if (pixelRect.IsEmpty)
            return Task.FromResult(new ChestDotCountResult(0, false));

        (byte[] pixels, int frameWidth, int frameHeight, int frameStride) = GetOrFillFrameCache(frame.Bitmap);

        ChestDotCountResult result = CountDots(pixels, frameWidth, frameHeight, frameStride, pixelRect);

        return Task.FromResult(result);
    }

    // ── IChestPanelAnalyzer ───────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<ChestPanelReading> AnalyzeChestPanelAsync(
        CapturedFrame frame,
        RoiCalibration roi,
        GameMechanicsConfig cfg,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(roi);
        ArgumentNullException.ThrowIfNull(cfg);

        ct.ThrowIfCancellationRequested();

        RoiPixelRect pixelRect = _mapper.ToPixels(roi, frame.ClientSize);
        if (pixelRect.IsEmpty)
            return Task.FromResult(new ChestPanelReading(null, 0, 0.0));

        (byte[] pixels, int frameWidth, int frameHeight, int frameStride) = GetOrFillFrameCache(frame.Bitmap);

        ChestPanelReading result = AnalyzePanel(pixels, frameWidth, frameHeight, frameStride, pixelRect, cfg);

        return Task.FromResult(result);
    }

    // ── алгоритм: классификация цвета плашки + счёт точек ────────────────────

    /// <summary>
    /// Анализирует ROI: классифицирует тип сундука по цвету фона плашки, затем считает точки.
    /// </summary>
    private static ChestPanelReading AnalyzePanel(
        byte[] pixels,
        int frameWidth,
        int frameHeight,
        int frameStride,
        RoiPixelRect rect,
        GameMechanicsConfig cfg)
    {
        int x0 = Math.Clamp(rect.X,      0, frameWidth);
        int y0 = Math.Clamp(rect.Y,      0, frameHeight);
        int x1 = Math.Clamp(rect.Right,  0, frameWidth);
        int y1 = Math.Clamp(rect.Bottom, 0, frameHeight);

        int roiWidth  = x1 - x0;
        int roiHeight = y1 - y0;

        if (roiWidth <= 0 || roiHeight <= 0)
            return new ChestPanelReading(null, 0, 0.0);

        // ── Шаг 1: собрать яркие пиксели для медианы цвета плашки ────────────
        // Пиксели с luminance >= BrightPanelThreshold = фон плашки (без тёмного спрайта/точек).
        // Используем списки значений каналов для медианы (медиана устойчива к выбросам спрайта).

        int totalPixels = roiWidth * roiHeight;
        List<byte> brightR = new(totalPixels / 2);
        List<byte> brightG = new(totalPixels / 2);
        List<byte> brightB = new(totalPixels / 2);

        const int bpp = 4; // Bgra8: B, G, R, A

        for (int row = y0; row < y1; row++)
        {
            for (int col = x0; col < x1; col++)
            {
                int offset = row * frameStride + col * bpp;
                byte b = pixels[offset];
                byte g = pixels[offset + 1];
                byte r = pixels[offset + 2];

                double lum = 0.299 * r + 0.587 * g + 0.114 * b;

                if (lum >= BrightPanelThreshold)
                {
                    brightR.Add(r);
                    brightG.Add(g);
                    brightB.Add(b);
                }
            }
        }

        double brightCoverage = (double)brightR.Count / totalPixels;

        if (brightCoverage < MinBrightCoverage)
        {
            // Слишком мало ярких пикселей — нет плашки (тёмная сцена или пустой ROI)
            return new ChestPanelReading(null, 0, 0.0);
        }

        // ── Шаг 2: медианный цвет плашки ────────────────────────────────────
        brightR.Sort();
        brightG.Sort();
        brightB.Sort();

        int medIdx = brightR.Count / 2;
        double panelR = brightR[medIdx];
        double panelG = brightG[medIdx];
        double panelB = brightB[medIdx];

        // ── Шаг 3: классификация типа по евклидову расстоянию до якорей ──────
        int bestTypeId    = -1;
        double bestDist   = double.MaxValue;

        foreach (Core.Models.ChestType ct in cfg.ChestTypes)
        {
            if (!ct.IsActive || ct.PanelColor is null)
                continue;

            Core.Models.PanelColor anchor = ct.PanelColor.Value;

            double dR = panelR - anchor.R;
            double dG = panelG - anchor.G;
            double dB = panelB - anchor.B;
            double dist = Math.Sqrt(dR * dR + dG * dG + dB * dB);

            if (dist < bestDist)
            {
                bestDist   = dist;
                bestTypeId = ct.Id;
            }
        }

        if (bestTypeId < 0 || bestDist > ColorMatchTolerance)
        {
            // Ни один якорь не подошёл по толерансу
            return new ChestPanelReading(null, 0, 0.0);
        }

        // ── Шаг 4: вычислить PanelMatch ──────────────────────────────────────
        // PanelMatch = 1 - dist / (ColorMatchTolerance * 2), зажатое в [0..1].
        // При dist=0 → 1.0; при dist=ColorMatchTolerance → 0.5;
        // компонент brightCoverage учитывается как дополнительный вес надёжности.
        double panelMatch = Math.Clamp(1.0 - bestDist / (ColorMatchTolerance * 2.0), 0.0, 1.0)
                            * Math.Clamp(brightCoverage, 0.0, 1.0);

        // ── Шаг 5: подсчёт тёмных точек в нижней полосе плашки ──────────────
        // Ряд точек расположен в нижних ~DotStripFraction высоты плашки.
        // Верхние ~62% занимает спрайт иконки — содержит тёмные пиксели,
        // которые run-алгоритм посчитал бы как ложные точки.
        int dotStripH  = Math.Max(1, (int)Math.Round(roiHeight * DotStripFraction, MidpointRounding.AwayFromZero));
        int dotStripY0 = y1 - dotStripH; // нижняя полоса: [y1-dotStripH .. y1]
        RoiPixelRect dotRect = new(x0, dotStripY0, roiWidth, dotStripH);
        ChestDotCountResult dotResult = CountDots(pixels, frameWidth, frameHeight, frameStride, dotRect);

        return new ChestPanelReading(bestTypeId, dotResult.Count, panelMatch);
    }

    // ── алгоритм подсчёта точек ──────────────────────────────────────────────

    /// <summary>
    /// Анализирует ROI-полосу: считает заполненные тёмные точки и определяет валидность паттерна.
    /// </summary>
    private static ChestDotCountResult CountDots(
        byte[] pixels,
        int frameWidth,
        int frameHeight,
        int frameStride,
        RoiPixelRect rect)
    {
        int x0 = Math.Clamp(rect.X,      0, frameWidth);
        int y0 = Math.Clamp(rect.Y,      0, frameHeight);
        int x1 = Math.Clamp(rect.Right,  0, frameWidth);
        int y1 = Math.Clamp(rect.Bottom, 0, frameHeight);

        int roiHeight = y1 - y0;
        int roiWidth  = x1 - x0;

        if (roiWidth <= 0 || roiHeight <= 0)
            return new ChestDotCountResult(0, false);

        bool hasBrightPixel = false; // признак наличия светлой незаполненной ячейки
        int  filledDots     = 0;
        int  runWidth       = 0;     // текущая ширина run тёмных колонок
        bool inRun          = false;

        const int bpp = 4; // Bgra8: B, G, R, A

        for (int col = x0; col < x1; col++)
        {
            int darkCount   = 0;
            int brightCount = 0;

            for (int row = y0; row < y1; row++)
            {
                int offset = row * frameStride + col * bpp;

                int b = pixels[offset];
                int g = pixels[offset + 1];
                int r = pixels[offset + 2];

                double lum = 0.299 * r + 0.587 * g + 0.114 * b;

                if (lum < DarkThreshold)
                    darkCount++;
                else if (lum > BrightThreshold)
                {
                    brightCount++;
                    hasBrightPixel = true;
                }
            }

            double darkFraction = roiHeight > 0 ? (double)darkCount / roiHeight : 0.0;
            bool isDarkColumn = darkFraction >= MinDarkColumnFraction;

            if (isDarkColumn)
            {
                runWidth++;
                inRun = true;
            }
            else
            {
                if (inRun && runWidth >= MinRunWidthPx)
                    filledDots++;
                runWidth = 0;
                inRun = false;
            }
        }

        // Закрываем незавершённый run в конце ROI
        if (inRun && runWidth >= MinRunWidthPx)
            filledDots++;

        bool detected = hasBrightPixel || filledDots > 0;

        return new ChestDotCountResult(filledDots, detected);
    }

    // ── кэш пиксельного буфера ────────────────────────────────────────────────

    /// <summary>
    /// Возвращает (pixels, width, height, stride) для <paramref name="bitmap"/>.
    /// При промахе конвертирует в Bgra8 Premultiplied и копирует весь буфер.
    /// При попадании возвращает кэшированные данные.
    /// </summary>
    private (byte[] pixels, int width, int height, int stride) GetOrFillFrameCache(SoftwareBitmap bitmap)
    {
        lock (_cacheLock)
        {
            if (ReferenceEquals(_cachedBitmapKey, bitmap) && _cachedPixels is not null)
                return (_cachedPixels, _cachedWidth, _cachedHeight, _cachedStride);
        }

        // Промах — копируем буфер вне lock
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
        int stride  = width * 4; // Bgra8: 4 байта на пиксель
        byte[] pixelBytes = new byte[stride * height];

        src.CopyToBuffer(pixelBytes.AsBuffer());
        converted?.Dispose();

        lock (_cacheLock)
        {
            _cachedBitmapKey = bitmap;
            _cachedPixels    = pixelBytes;
            _cachedWidth     = width;
            _cachedHeight    = height;
            _cachedStride    = stride;
        }

        return (pixelBytes, width, height, stride);
    }
}
