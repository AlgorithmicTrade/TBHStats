using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;
using TBHStats.Capture.Roi;

namespace TBHStats.Capture.Progress;

/// <summary>
/// Реализует <see cref="IStageProgressReader"/>: визуальная детекция прогрессбара этапа
/// по цвету заливки горизонтального бара в правом-нижнем углу MainZone (ADR-024).
/// </summary>
/// <remarks>
/// Алгоритм (высото-устойчивый — не зависит от точной высоты ROI):
/// <list type="number">
///   <item>Развернуть нормализованный ROI в пиксели через <see cref="RoiMapper"/>.</item>
///   <item>Получить Bgra8-буфер кадра (кэш по <c>ReferenceEquals</c>, lock).</item>
///   <item>Для каждой вертикальной колонки ROI подсчитать <b>абсолютное</b> число фиолетовых /
///     синих / тёмных пикселей. Колонка классифицируется по преобладающему цвету при достижении
///     порога <see cref="MinColoredPixelsPerColumn"/> цветных пикселей. Абсолютный порог (а не доля
///     высоты) делает детекцию устойчивой к ROI, нарисованному «на глаз» выше тонкой цветной полоски
///     (заливка ≈6px, а ROI может быть выше — доля упала бы ниже порога, а абсолютный счёт — нет).</item>
///   <item>Если <c>blueColumns / total ≥ <see cref="BlueFractionThreshold"/></c> →
///     BossPresent=true, Progress=1.0.</item>
///   <item>Иначе: BossPresent=false, Progress = Clamp(purpleFraction × 0.95, 0.0, 0.95).</item>
///   <item>Если в ROI нет ни одной валидной (цветной/тёмной) колонки → Progress=null, BossPresent=null.</item>
/// </list>
///
/// Цветовые пороги выверены на четырёх фикстурах:
/// <list type="bullet">
///   <item><c>screenshots/progress_begin.jpg</c> (554×243): бар пустой, y≈205..212 → progress≈0.</item>
///   <item><c>screenshots/progress_half.jpg</c> (548×247): фиолетовый x[504..540], rows 208..213 → ~53%.</item>
///   <item><c>screenshots/progress_full.jpg</c> (559×236): фиолетовый x[482..550], rows 198..203 → 100% пути → progress=0.95.</item>
///   <item><c>screenshots/progress_stagebossfight.jpg</c> (552×247): синий x[475..543], rows 206..212 → BossPresent=true, progress=1.0.</item>
/// </list>
/// </remarks>
public sealed class StageProgressReader : IStageProgressReader
{
    private readonly RoiMapper _mapper = new();

    // ── Цветовая модель (относительная, не зависит от насыщенности) ──────────
    //
    // Якоря заливки сильно отличаются по насыщенности между JPEG-фикстурами и живым
    // WGC-кадром: фикстурный фиолетовый ≈ (165,77,213), живой ≈ (130,98,150) — заметно
    // менее синий. Абсолютные пороги (B>150 и т.п.) на живом баре отсекали тусклый/градиентный
    // фиолетовый по краю заливки → недосчёт прогресса. Поэтому цвет различается по ОТНОСИТЕЛЬНОЙ
    // структуре каналов, инвариантной к яркости/насыщенности:
    //   • фиолетовый (мадженто-violet): R и B заметно выше G; зелёный — минимальный канал.
    //   • синий (бирюзово-cyan боя с боссом): B>G и G заметно выше R; красный — минимальный канал.
    // Разделитель фиолетовый↔синий: у фиолетового R>G, у синего R<G (взаимоисключающе).

    /// <summary>
    /// Минимальное превышение R и B над G (по каждому каналу), при котором не-тёмный пиксель
    /// классифицируется как фиолетовый (заливка пути).
    /// </summary>
    /// <remarks>
    /// Живой фиолетовый по диагностике: средний не-тёмный RGB заливки ≈ (129,98,126) →
    /// R−G≈31, B−G≈28; у тусклого/градиентного края меньше. Фикстурный (165,77,213):
    /// R−G≈88, B−G≈136. Порог 12 ловит и яркий фикстурный, и тусклый живой фиолетовый,
    /// при этом отсекает серый/нейтральный (R≈G≈B) и тёмный трек (исключён по luminance).
    /// </remarks>
    private const int PurpleChannelMargin = 12;

    /// <summary>
    /// Минимальное превышение G над R для синего пикселя (признак, отличающий синий от фиолетового).
    /// </summary>
    /// <remarks>
    /// Синий якорь (95,199,255): G−R≈104. Живой синий (бирюзовый) тоже даёт G≫R.
    /// Порог 25 надёжно отделяет синий (R — минимальный канал) от фиолетового (R>G).
    /// </remarks>
    private const int BlueGreenOverRedMargin = 25;

    /// <summary>
    /// Минимальное превышение B над G для синего пикселя.
    /// </summary>
    /// <remarks>
    /// Синий якорь (95,199,255): B−G≈56. Порог 5 (B лишь чуть выше G — у бирюзового
    /// B и G близки) с опорой на основной разделитель <see cref="BlueGreenOverRedMargin"/>.
    /// </remarks>
    private const int BlueBlueOverGreenMargin = 5;

    // ── Агрегационные пороги ─────────────────────────────────────────────────

    /// <summary>
    /// Минимальное АБСОЛЮТНОЕ число пикселей нужного цвета в колонке ROI,
    /// при котором колонка классифицируется как «фиолетовая» / «синяя» / «тёмная».
    /// </summary>
    /// <remarks>
    /// Цветная заливка бара имеет высоту ≈6..8px. Абсолютный порог (а не доля высоты ROI)
    /// делает детекцию устойчивой к ROI, нарисованному «на глаз»: даже если ROI выше полоски
    /// (напр. 20px), залитая колонка всё равно содержит ≈6 цветных пикселей ≥ 3 → засчитывается.
    /// Порог 3 отсекает одиночные JPEG/краевые артефакты (1–2px), но захватывает реальную полоску.
    /// На фикстурах залитые колонки дают ≈6 цветных px ≥ 3 ✓; пустые — 0.
    /// </remarks>
    private const int MinColoredPixelsPerColumn = 3;

    /// <summary>
    /// Порог luminance, ниже которого пиксель считается «тёмным» (пустой трек бара).
    /// </summary>
    /// <remarks>Тёмный трек по фикстурам: luminance 4..23. Порог 40 с запасом.</remarks>
    private const double DarkLumThreshold = 40.0;

    /// <summary>
    /// Минимальная доля синих колонок от ширины ROI, при которой диагностируется BossPresent=true.
    /// </summary>
    /// <remarks>
    /// При бое с боссом (progress_stagebossfight.jpg): синий ≈97% колонок.
    /// При обычном прогрессе синих колонок 0 (или единичные артефакты ≤2%).
    /// Порог 0.20 (20%) надёжно разделяет эти случаи с большим зазором.
    /// </remarks>
    private const double BlueFractionThreshold = 0.20;

    /// <summary>
    /// Значение прогресса для «конца пути»: полностью фиолетовый бар и бой с боссом.
    /// </summary>
    /// <remarks>
    /// По игровой механике этап НЕ пройден, пока не убит босс. Полная фиолетовая полоса
    /// (стрелка дошла до левого края) = конец пути ≈ 95%; впереди ещё бой с боссом, который
    /// тоже держится на 95% (синяя заливка). 100% наступает только в момент завершения этапа
    /// (босс убит → бар сбрасывается). Прогресс пути масштабируется в [0..<see cref="BossProgressValue"/>].
    /// </remarks>
    private const double BossProgressValue = 0.95;

    /// <summary>
    /// Сырая доля закрашенного трека (фиол / (фиол+тёмн)), соответствующая ПУСТОМУ бару.
    /// </summary>
    /// <remarks>
    /// У бара есть «мёртвые зоны» на концах: стрелка-маркер прогресса (с мадженто-кончиком)
    /// даёт ненулевую «фиолетовую» долю даже при пустом баре. По живой диагностике пустой бар
    /// даёт сырую долю ≈ 0.13 (ф12/т80). Рабочий диапазон сырой доли ремапится в [0..1]:
    /// всё ≤ этого значения = 0% прогресса.
    /// </remarks>
    private const double RawTrackEmpty = 0.12;

    /// <summary>
    /// Сырая доля закрашенного трека, соответствующая ПОЛНОСТЬЮ закрашенному бару (конец пути).
    /// </summary>
    /// <remarks>
    /// Десатурированный край заливки у стрелки не дотягивает по цвету → у полного бара сырая доля
    /// ≈ 0.85 (ф78/т14), а не 1.0. Рабочий диапазон [<see cref="RawTrackEmpty"/>..<see cref="RawTrackFull"/>]
    /// линейно ремапится в [0..1] и масштабируется на <see cref="BossProgressValue"/> = полный бар → 95%.
    /// Значения откалиброваны по живой диагностике (полный бар: сырая доля ≈0.837..0.848,
    /// колебание ф77..78); фикстурные сырые доли (0 / 0.51 / 0.96) ремапятся в (0 / 0.54 / 1.0)
    /// →×0.95 — в допусках фикстур-тестов.
    /// </remarks>
    private const double RawTrackFull = 0.84;

    /// <summary>
    /// Минимальная доля валидных (цветных или тёмных) колонок от ширины ROI
    /// для признания ROI читаемым баром. Если меньше — Progress=null, BossPresent=null.
    /// </summary>
    /// <remarks>
    /// При корректно откалиброванном ROI в кадре ≥90% колонок принадлежат бару (фиолетовые,
    /// синие или тёмный трек). Порог 0.05 отсекает полностью невалидные ROI (нет ни одной
    /// бар-подобной колонки — напр. ROI указывает на светлую сцену вне бара).
    /// </remarks>
    private const double MinValidBarFraction = 0.05;

    // ── кэш буфера кадра ──────────────────────────────────────────────────────

    private SoftwareBitmap? _cachedBitmapKey;
    private byte[]?         _cachedPixels;
    private int             _cachedWidth;
    private int             _cachedHeight;
    private int             _cachedStride;
    private readonly object _cacheLock = new();

    // ── IStageProgressReader ──────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<StageProgressReading> ReadAsync(
        CapturedFrame frame,
        RoiCalibration roi,
        GameMechanicsConfig cfg,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(roi);
        ArgumentNullException.ThrowIfNull(cfg);

        ct.ThrowIfCancellationRequested();

        StageProgressDiagnostic diag = Diagnose(frame, roi);
        return Task.FromResult(new StageProgressReading(diag.Progress, diag.BossPresent));
    }

    /// <inheritdoc/>
    public Task<StageProgressDiagnostic> DiagnoseAsync(
        CapturedFrame frame,
        RoiCalibration roi,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(roi);

        ct.ThrowIfCancellationRequested();

        return Task.FromResult(Diagnose(frame, roi));
    }

    // ── основной алгоритм ─────────────────────────────────────────────────────

    /// <summary>
    /// Полный анализ ROI: классификация колонок, прогресс/босс и диагностические счётчики.
    /// </summary>
    private StageProgressDiagnostic Diagnose(CapturedFrame frame, RoiCalibration roi)
    {
        RoiPixelRect rect = _mapper.ToPixels(roi, frame.ClientSize);
        if (rect.IsEmpty)
            return StageProgressDiagnostic.Empty;

        (byte[] pixels, int frameWidth, int frameHeight, int frameStride) = GetOrFillFrameCache(frame.Bitmap);

        const int bpp = 4; // Bgra8

        int x0 = Math.Clamp(rect.X,      0, frameWidth);
        int y0 = Math.Clamp(rect.Y,      0, frameHeight);
        int x1 = Math.Clamp(rect.Right,  0, frameWidth);
        int y1 = Math.Clamp(rect.Bottom, 0, frameHeight);

        int roiW = x1 - x0;
        int roiH = y1 - y0;

        if (roiW <= 0 || roiH <= 0)
            return StageProgressDiagnostic.Empty;

        int purpleColumns = 0;
        int blueColumns   = 0;
        int darkColumns   = 0;

        // Образец цвета заливки: средний R/G/B по ВСЕМ не-тёмным пикселям ROI (а не только
        // по уже-распознанным фиолетовым/синим). Это позволяет на экране калибровки увидеть
        // ИСТИННЫЙ цвет заливки живой игры, даже если предикат IsPurple/IsBlue его не поймал
        // (диагностика рассогласования цветовых порогов с реальным рендером).
        long sumR = 0, sumG = 0, sumB = 0;
        long nonDarkPx = 0;

        for (int col = x0; col < x1; col++)
        {
            int purplePx = 0;
            int bluePx   = 0;
            int darkPx   = 0;

            for (int row = y0; row < y1; row++)
            {
                int offset = row * frameStride + col * bpp;
                byte b = pixels[offset];
                byte g = pixels[offset + 1];
                byte r = pixels[offset + 2];

                double lum = 0.299 * r + 0.587 * g + 0.114 * b;
                bool isDark = lum < DarkLumThreshold;

                // Тёмные пиксели НЕ классифицируются по цвету: относительные предикаты могут
                // сработать на тёмно-фиолетовом тоне пустого трека и ложно «заполнить» бар.
                if (!isDark && IsPurple(r, g, b))
                {
                    purplePx++;
                    sumR += r; sumG += g; sumB += b; nonDarkPx++;
                }
                else if (!isDark && IsBlue(r, g, b))
                {
                    bluePx++;
                    sumR += r; sumG += g; sumB += b; nonDarkPx++;
                }
                else if (isDark)
                {
                    darkPx++;
                }
                else
                {
                    // не-тёмный, но не цветной (серый/нейтральный край, стрелка) — в образец цвета
                    sumR += r; sumG += g; sumB += b; nonDarkPx++;
                }
            }

            // Классификация по преобладающему цвету с абсолютным порогом (высото-устойчиво).
            if (purplePx >= MinColoredPixelsPerColumn && purplePx >= bluePx)
                purpleColumns++;
            else if (bluePx >= MinColoredPixelsPerColumn)
                blueColumns++;
            else if (darkPx >= MinColoredPixelsPerColumn)
                darkColumns++;
            // Прочие колонки (ни цвета, ни тёмного трека) — не учитываются
        }

        int validColumns = purpleColumns + blueColumns + darkColumns;
        int otherColumns = roiW - validColumns;

        int sampleR = nonDarkPx > 0 ? (int)(sumR / nonDarkPx) : 0;
        int sampleG = nonDarkPx > 0 ? (int)(sumG / nonDarkPx) : 0;
        int sampleB = nonDarkPx > 0 ? (int)(sumB / nonDarkPx) : 0;

        // Guard: если ROI совсем не похож на бар — не читаем
        if ((double)validColumns / roiW < MinValidBarFraction)
            return new StageProgressDiagnostic(
                roiW, roiH, purpleColumns, blueColumns, darkColumns, otherColumns,
                sampleR, sampleG, sampleB, Progress: null, BossPresent: null);

        double blueFraction = (double)blueColumns / roiW;

        double? progress;
        bool? bossPresent;

        if (blueFraction >= BlueFractionThreshold)
        {
            // Бой с боссом (синяя заливка): этап ещё НЕ пройден (босс жив) → держим 0.95.
            // 100% наступает только в момент завершения (босс убит → бар сбрасывается в пустой).
            progress    = BossProgressValue;
            bossPresent = true;
        }
        else
        {
            // Прогресс пути = доля ЗАКРАШЕННОГО трека: фиол / (фиол + тёмный трек).
            // Нормировка по треку (а не по всей ширине ROI) устойчива к лишним колонкам ROI:
            // белая стрелка, светлые края бара и паддинг попадают в «прочие» и исключаются из
            // знаменателя — поэтому полностью фиолетовый бар даёт ≈1.0 → 0.95 независимо от того,
            // насколько точно ROI обрезан по краям. Заливка растёт справа налево.
            int trackColumns = purpleColumns + darkColumns;
            double purpleTrackFraction = trackColumns > 0
                ? (double)purpleColumns / trackColumns
                : 0.0;

            // Ремап «мёртвых зон» бара: сырой рабочий диапазон [RawTrackEmpty..RawTrackFull] → [0..1].
            // Убирает ненулевую заливку пустого бара (стрелка-маркер) и недозаполнение полного
            // (десатурированный край у стрелки), давая чистые 0% (пусто) и 95% (полный).
            double normalized = (purpleTrackFraction - RawTrackEmpty) / (RawTrackFull - RawTrackEmpty);
            progress    = Math.Clamp(normalized, 0.0, 1.0) * BossProgressValue;
            bossPresent = false;
        }

        return new StageProgressDiagnostic(
            roiW, roiH, purpleColumns, blueColumns, darkColumns, otherColumns,
            sampleR, sampleG, sampleB, progress, bossPresent);
    }

    // ── пиксельные предикаты цвета ────────────────────────────────────────────

    /// <summary>
    /// Проверяет, является ли пиксель (r,g,b) фиолетовым (заливка пути прогрессбара).
    /// Относительный признак (инвариант к насыщенности): R и B заметно выше G
    /// ((R−G) и (B−G) ≥ <see cref="PurpleChannelMargin"/>); зелёный — минимальный канал.
    /// Отличие от синего: у фиолетового R&gt;G (у синего R&lt;G). Тёмный трек исключается
    /// вызывающим кодом по luminance.
    /// </summary>
    private static bool IsPurple(byte r, byte g, byte b) =>
        (r - g) >= PurpleChannelMargin
        && (b - g) >= PurpleChannelMargin;

    /// <summary>
    /// Проверяет, является ли пиксель (r,g,b) синим (бой с боссом).
    /// Относительный признак: B&gt;G (на <see cref="BlueBlueOverGreenMargin"/>) и G заметно выше R
    /// (на <see cref="BlueGreenOverRedMargin"/>) — красный минимальный канал (бирюзово-cyan).
    /// Взаимоисключающе с <see cref="IsPurple"/> по сравнению R↔G.
    /// </summary>
    private static bool IsBlue(byte r, byte g, byte b) =>
        (b - g) >= BlueBlueOverGreenMargin
        && (g - r) >= BlueGreenOverRedMargin;

    // ── кэш пиксельного буфера ────────────────────────────────────────────────

    /// <summary>
    /// Возвращает (pixels, width, height, stride) для <paramref name="bitmap"/>.
    /// При промахе конвертирует в Bgra8 Premultiplied и копирует весь буфер.
    /// При попадании возвращает кэшированные данные (ключ — ReferenceEquals).
    /// </summary>
    private (byte[] pixels, int width, int height, int stride) GetOrFillFrameCache(SoftwareBitmap bitmap)
    {
        lock (_cacheLock)
        {
            if (ReferenceEquals(_cachedBitmapKey, bitmap) && _cachedPixels is not null)
                return (_cachedPixels, _cachedWidth, _cachedHeight, _cachedStride);
        }

        SoftwareBitmap? converted = null;
        SoftwareBitmap src = bitmap;

        if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8
            || bitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
        {
            converted = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            src = converted;
        }

        int width  = src.PixelWidth;
        int height = src.PixelHeight;
        int stride = width * 4;
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
