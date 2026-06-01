using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;
using TBHStats.Capture.Roi;

namespace TBHStats.Capture.Chests;

/// <summary>
/// Реализует <see cref="IChestZoneAnalyzer"/>: зонная локализация плашек сундуков
/// по цвету колонок + масштабонезависимый счёт точек по рядам (ADR-023).
/// </summary>
/// <remarks>
/// Алгоритм:
/// <list type="number">
///   <item>Кроп зоны (Bgra8); кэш буфера разделяется между вызовами на одном кадре.</item>
///   <item><b>Горизонтальная сегментация:</b> для каждой вертикальной колонки зоны собираются
///     яркие пиксели (luminance ≥ <see cref="BrightPanelThreshold"/>), вычисляется медианный
///     R/G/B → классификация к ближайшему якорю <see cref="ChestType.PanelColor"/> (евклидово
///     расстояние ≤ <see cref="ColorMatchTolerance"/>; доля ярких пикселей ≥
///     <see cref="MinBrightColumnCoverage"/>). Серия смежных колонок одного типа ≥
///     <see cref="MinPanelWidthFraction"/> ширины зоны = один сегмент-плашка.</item>
///   <item><b>Счёт точек по рядам (multi-row):</b> в нижнем поясе сегмента (нижние
///     <see cref="DotZoneFraction"/> от высоты зоны) для каждой строки запускается
///     run-алгоритм → число точек в строке. Consecutive строки с ненулевым счётом
///     образуют полосу-ряд; ширина run-а ∈ [minRunPx, maxRunPx] = одна точка (спрайт
///     и рамка дают run > maxRunPx → 0 точек → не образуют полосу). Итог по плашке =
///     сумма мод (по полосам-рядам). Поддерживает до 5 точек в ряду + второй ряд при 6+.</item>
/// </list>
///
/// Пороги выверены на трёх фикстурах:
/// <list type="bullet">
///   <item><c>screenshots/chests.jpg</c> (545×241): red=1, blue=1, brown=2.</item>
///   <item><c>screenshots/main.jpg</c> (549×232): blue=3, brown=3, red отсутствует.</item>
///   <item><c>screenshots/calibration.jpg</c> (755×327): red=1, blue=4, brown=6 (два ряда).</item>
/// </list>
/// </remarks>
public sealed class ChestZoneAnalyzer : IChestZoneAnalyzer
{
    private readonly RoiMapper _mapper = new();

    // ── пороги яркости ────────────────────────────────────────────────────────

    /// <summary>
    /// Порог яркости для определения «тёмного» пикселя (заполненная точка).
    /// </summary>
    /// <remarks>
    /// Заполненные точки всех трёх типов: luminance 9–86
    /// (red ≈9–40, blue ≈24–40, brown ≈40–86).
    /// Фон плашки: luminance 130–255. Запас: порог 110 захватывает тёмные точки
    /// brown (lum≈80–90), не путая с фоном плашки (≥130).
    /// </remarks>
    private const int DarkThreshold = 110;

    /// <summary>
    /// Порог яркости для пикселей фона плашки при цветовой классификации колонок.
    /// Пиксель включается в медианный расчёт, если luminance ≥ этого значения.
    /// </summary>
    /// <remarks>
    /// Значение 120 включает фон всех трёх типов плашек (lum 130–255)
    /// и белые пустые ячейки (lum 210–255), но исключает тёмный спрайт/точки (9–86)
    /// и тёмную сцену вне плашек (lum &lt;100).
    /// </remarks>
    private const int BrightPanelThreshold = 120;

    // ── пороги сегментации колонок ────────────────────────────────────────────

    /// <summary>
    /// Максимальное евклидово расстояние RGB между медианным цветом колонки и якорём
    /// для успешной классификации к типу сундука.
    /// </summary>
    /// <remarks>
    /// Расстояния между якорями (из GameMechanicsConfig.CreateDefault()):
    ///   orange(236,133,41) ↔ white(255,255,255): ≈190
    ///   lightblue(190,220,238) ↔ white(255,255,255): ≈75
    ///   orange(236,133,41) ↔ lightblue(190,220,238): ≈210
    /// Расстояния между якорями:
    ///   orange(236,133,41) ↔ white(255,255,255): ≈190
    ///   lightblue(190,220,238) ↔ white(255,255,255): ≈75
    ///   orange(236,133,41) ↔ lightblue(190,220,238): ≈210
    /// Порог 90 охватывает широкий диапазон JPEG-артефактов для всех трёх типов плашек.
    /// Минимальное межъякорное расстояние lightblue(190,220,238)↔white(255,255,255) = ≈75.
    /// При tolerance=90 разделение типов обеспечивается тем, что реальные медианы каждого типа
    /// значительно ближе к своему якорю, чем к соседнему: dist(blue_median, blue_anchor) ≈ 40–87,
    /// dist(blue_median, white_anchor) ≈ 90–130 > 90 → white не ложно-позитивен.
    /// </remarks>
    private const double ColorMatchTolerance = 90.0;

    /// <summary>
    /// Минимальная доля ярких пикселей (luminance ≥ <see cref="BrightPanelThreshold"/>)
    /// в вертикальной колонке для включения её в сегментацию как колонки плашки.
    /// </summary>
    /// <remarks>
    /// Значение 0.3: в колонке должно быть ≥30% ярких пикселей.
    /// Тёмный разделитель/сцена не набирает этого порога → классифицируется как «нет плашки».
    /// При частично-тёмных колонках краёв плашки (~50% тёмный спрайт) порог обеспечивает
    /// достаточный запас (~50% ярких ≥ 30%).
    /// </remarks>
    private const double MinBrightColumnCoverage = 0.30;

    /// <summary>
    /// Минимальная ширина сегмента как доля от ширины зоны для признания его плашкой.
    /// </summary>
    /// <remarks>
    /// Для зоны шириной ~300px (охватывает 3 плашки по ~80px каждая):
    ///   - минимальная плашка 80px / 300px ≈ 0.08 → порог 0.06 с запасом.
    /// Тёмные разделители (1–5px) / спрайтовые засветы (~10px) отсекаются.
    /// </remarks>
    private const double MinPanelWidthFraction = 0.06;

    // ── пороги счёта точек ────────────────────────────────────────────────────

    /// <summary>
    /// Доля от высоты зоны, используемая для поиска строк точек (нижний пояс).
    /// </summary>
    /// <remarks>
    /// Нижний пояс берётся от дна зоны. Фикстуры (высота зоны ≈70–75px):
    ///   - chests.jpg: зона y=55..130 (75px). Нижние 32% = 24px → dotY0_frame=106. Точки y=107..118 ✓.
    ///   - main.jpg: зона y=48..118 (70px). Нижние 32% = 22px → dotY0_frame=96. Точки y=99..108 ✓.
    ///   - calibration.jpg: зона y≈100..172 (72px). Нижние 32% = 23px → dotY0_frame=149.
    ///     Ряд 1 точек y≈142–149 → строка y=149 (1 строка, ndark≈40 → darkFraction≈0.38 > 0.04) ✓.
    ///     Ряд 2 точек y≈154–161 → полностью попадает ✓.
    ///     Разрыв [150..153] = 4 строки > MaxBandGapRows=3 → два отдельных ряда → 5+1=6 ✓.
    /// Значение 0.32 исключает спрайт иконки (выше нижнего пояса) из диапазона поиска точек.
    /// </remarks>
    private const double DotZoneFraction = 0.32;

    /// <summary>
    /// Минимальная доля тёмных пикселей в горизонтальной строке (по ширине сегмента),
    /// необходимая для того чтобы строка считалась «кандидатом на ряд точек».
    /// </summary>
    /// <remarks>
    /// Строка с 2 точками (chests.jpg, 12/83): darkFraction ≈ 0.145.
    /// Строка ряда2 calibration.jpg (1 точка, 23/126): darkFraction ≈ 0.183.
    /// Зазор calibration.jpg (JPEG/оверлей, 16/126): darkFraction ≈ 0.127.
    /// Чисто светлый фон → darkFraction ≈ 0.
    /// Порог 0.13 отсекает зазор/шум calibration (0.127 &lt; 0.13) и artефакты chests (0.06 &lt; 0.13),
    /// включая строки реальных точек (0.145+ ✓).
    /// </remarks>
    private const double MinDarkRowFraction = 0.13;

    /// <summary>
    /// Верхняя граница доли тёмных пикселей в строке для включения в run-алгоритм.
    /// Строки с долей ≥ этого порога — тёмный спрайт иконки — исключаются.
    /// </summary>
    /// <remarks>
    /// Тёмная нижняя рамка плашки и тёмный фон сцены: darkFraction ≈ 0.50–1.0.
    /// Строка из 5 полных точек (ряд1 при 5+ сундуков): darkFraction = 5×8px / 104px ≈ 0.38.
    /// Строка из 1 точки (ряд2): darkFraction ≈ 0.07.
    /// Порог 0.45 надёжно разделяет строки точек (≤0.38) от тёмной рамки/фона (≥0.50).
    /// </remarks>
    private const double MaxDarkRowFraction = 0.45;

    /// <summary>
    /// Максимальный разрыв (число строк с 0 точками) внутри одной полосы-ряда,
    /// который ещё не разделяет её на два ряда.
    /// </summary>
    /// <remarks>
    /// JPEG-артефакты и пиксельные зазоры ячейки точки (1–2px) порождают строки с 0 точками
    /// внутри физического ряда точек. Без допуска один ряд дробится на несколько полос,
    /// завышая итог.
    /// Значение 3: разрыв ≤ 3 строки = один ряд; разрыв > 3 строки = отдельный ряд.
    /// Используется при группировке строк в полосы-ряды в <see cref="CountDotsInSegment"/>.
    /// </remarks>
    private const int MaxBandGapRows = 3;

    /// <summary>
    /// Ожидаемое максимальное число точек в одном ряду.
    /// Используется для оценки шага ячейки: estimatedCellWidth = segmentWidth / MaxDotsPerRow.
    /// </summary>
    /// <remarks>
    /// По игровой механике: до 5 точек в ряду. Значение 5 даёт
    /// estimatedCellWidth ≈ segmentWidth / 5 для плашки с максимально заполненным рядом.
    /// </remarks>
    private const int ExpectedMaxDotsPerRow = 5;

    /// <summary>
    /// Минимальная ширина run тёмных колонок как доля от оценённой ширины одной ячейки
    /// (<see cref="ExpectedMaxDotsPerRow"/>).
    /// </summary>
    /// <remarks>
    /// estimatedCellWidth = segmentWidth / ExpectedMaxDotsPerRow.
    /// minRunPx = estimatedCellWidth * MinRunCellWidthFraction.
    ///   - chests.jpg blue ≈83px: ячейка ≈16.6px, minRunPx ≈ 16.6 × 0.15 = 2.5px → 2px; точка ≈6px ✓
    ///   - chests.jpg brown ≈83px: аналогично; brown точка ≈6px ✓
    ///   - main.jpg blue ≈105px: ячейка ≈21px, minRunPx ≈ 21 × 0.15 = 3.15px → 3px; точка ≈4–6px ✓
    ///     (JPEG-артефакты могут сузить отдельные run-ы до 3–4px)
    ///   - main.jpg brown ≈59px: ячейка ≈11.8px, minRunPx ≈ 1.8px → 1px; brown точка ≈6px ✓
    /// Значение 0.15 надёжно захватывает точки 3–6px при разных масштабах.
    /// Ложные узкие run-ы (1–2px, шум) отсекаются minRunPx=2–3px; слишком широкие — maxRunPx.
    /// </remarks>
    private const double MinRunCellWidthFraction = 0.15;

    /// <summary>
    /// Максимальная ширина run тёмных колонок как доля от ширины сегмента
    /// для признания его точкой (не спрайтом/рамкой).
    /// </summary>
    /// <remarks>
    /// Применяется формула: maxRunPx = min(segW × MaxRunSegmentWidthFraction, MaxRunAbsolutePx).
    /// Абсолютный cap <see cref="MaxRunAbsolutePx"/> = 15px гарантирует, что широкие тёмные
    /// области (тёмный хвост иконки ≥16px, зелёная рамка оверлея) отсекаются при любом segW,
    /// а реальные точки (≤15px) считаются во всех фикстурах.
    /// </remarks>
    private const double MaxRunSegmentWidthFraction = 0.35;

    /// <summary>
    /// Абсолютный cap ширины run для признания его точкой (не спрайтом/рамкой).
    /// </summary>
    /// <remarks>
    /// При больших сегментах (calibration.jpg brown ≈125px):
    ///   maxRunPx = min(125 × 0.35 = 43, 15) = 15.
    ///   Тёмный хвост иконки/оверлея run 16px > 15 → отсекается ✓.
    ///   Реальные точки 7–8px ≤ 15 ✓.
    /// При малых сегментах (chests.jpg red ≈65px):
    ///   maxRunPx = min(65 × 0.35 = 22, 15) = 15.
    ///   Точки 8px ≤ 15 ✓; текст-run 3px → через minRunPx (тоже отсекается если < 2).
    /// Значение 15 выбрано по фикстурам: ширина иконки-run минимум 16px, ширина точки максимум 12px.
    /// </remarks>
    private const int MaxRunAbsolutePx = 15;

    // ── кэш буфера кадра ──────────────────────────────────────────────────────

    private SoftwareBitmap? _cachedBitmapKey;
    private byte[]?         _cachedPixels;
    private int             _cachedWidth;
    private int             _cachedHeight;
    private int             _cachedStride;
    private readonly object _cacheLock = new();

    // ── IChestZoneAnalyzer ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<IReadOnlyDictionary<int, int>> AnalyzeZoneAsync(
        CapturedFrame frame,
        RoiCalibration zoneRoi,
        GameMechanicsConfig cfg,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(zoneRoi);
        ArgumentNullException.ThrowIfNull(cfg);

        ct.ThrowIfCancellationRequested();

        RoiPixelRect zoneRect = _mapper.ToPixels(zoneRoi, frame.ClientSize);
        if (zoneRect.IsEmpty)
            return Task.FromResult<IReadOnlyDictionary<int, int>>(new Dictionary<int, int>());

        (byte[] pixels, int frameWidth, int frameHeight, int frameStride) = GetOrFillFrameCache(frame.Bitmap);

        IReadOnlyDictionary<int, int> result = AnalyzeZone(
            pixels, frameWidth, frameHeight, frameStride, zoneRect, cfg);

        return Task.FromResult(result);
    }

    // ── основной алгоритм ─────────────────────────────────────────────────────

    /// <summary>
    /// Сегментирует зону на плашки по цвету колонок, затем считает точки в каждой.
    /// </summary>
    private static IReadOnlyDictionary<int, int> AnalyzeZone(
        byte[] pixels,
        int frameWidth,
        int frameHeight,
        int frameStride,
        RoiPixelRect zoneRect,
        GameMechanicsConfig cfg)
    {
        int x0 = Math.Clamp(zoneRect.X,      0, frameWidth);
        int y0 = Math.Clamp(zoneRect.Y,      0, frameHeight);
        int x1 = Math.Clamp(zoneRect.Right,  0, frameWidth);
        int y1 = Math.Clamp(zoneRect.Bottom, 0, frameHeight);

        int zoneW = x1 - x0;
        int zoneH = y1 - y0;

        if (zoneW <= 0 || zoneH <= 0)
            return new Dictionary<int, int>();

        // Активные типы с якорным цветом
        ChestType[] activeTypes = cfg.ChestTypes
            .Where(c => c.IsActive && c.PanelColor.HasValue)
            .ToArray();

        if (activeTypes.Length == 0)
            return new Dictionary<int, int>();

        // Шаг 1: для каждой колонки зоны — тип плашки или -1 (нет плашки)
        int[] colType = ClassifyColumns(pixels, frameWidth, frameStride, x0, y0, x1, y1, zoneW, zoneH, activeTypes);

        // Шаг 2: сегментация — смежные колонки одного типа → сегмент
        List<(int typeId, int segX0, int segX1)> segments = BuildSegments(colType, x0, zoneW);

        // Шаг 3: фильтр по минимальной ширине
        int minPanelPx = (int)Math.Max(1.0, zoneW * MinPanelWidthFraction);
        List<(int typeId, int segX0, int segX1)> validSegments =
            segments.Where(s => (s.segX1 - s.segX0) >= minPanelPx).ToList();

        if (validSegments.Count == 0)
            return new Dictionary<int, int>();

        // Шаг 4: для каждого сегмента — счёт точек в нижнем поясе
        // Нижний пояс: [y1 - dotZoneH .. y1]
        int dotZoneH = Math.Max(1, (int)Math.Round(zoneH * DotZoneFraction, MidpointRounding.AwayFromZero));
        int dotY0 = y1 - dotZoneH;

        Dictionary<int, int> result = [];

        foreach ((int typeId, int segX0, int segX1) in validSegments)
        {
            // При нескольких сегментах одного типа (дубликат) берём максимальный счёт
            int dotCount = CountDotsInSegment(
                pixels, frameStride,
                segX0, segX1, dotY0, y1,
                dotZoneH);

            if (!result.TryGetValue(typeId, out int prev) || dotCount > prev)
                result[typeId] = dotCount;
        }

        return result;
    }

    // ── классификация колонок ─────────────────────────────────────────────────

    /// <summary>
    /// Для каждой горизонтальной колонки зоны определяет ChestTypeId или -1 («нет плашки»).
    /// </summary>
    private static int[] ClassifyColumns(
        byte[] pixels,
        int frameWidth,
        int frameStride,
        int x0, int y0, int x1, int y1,
        int zoneW, int zoneH,
        ChestType[] activeTypes)
    {
        const int bpp = 4; // Bgra8

        int[] colType = new int[zoneW];

        for (int col = x0; col < x1; col++)
        {
            // Собрать яркие пиксели колонки
            List<byte> brightR = [];
            List<byte> brightG = [];
            List<byte> brightB = [];

            for (int row = y0; row < y1; row++)
            {
                int offset = row * frameStride + col * bpp;
                byte b = pixels[offset];
                byte g = pixels[offset + 1];
                byte r = pixels[offset + 2];

                double lum = 0.299 * r + 0.587 * g + 0.114 * b;
                // Исключаем зелёные пиксели (оверлеи калибровочного UI, тексты подписей):
                // зелёно-доминирующий g > r+30 && g > b+20 не является фоном плашки.
                // Пороги 30/20 выбраны так, чтобы захватить оверлей calibration.jpg (G-R≈56, G-B≈25)
                // и не исключать нормальные пиксели плашек (white, orange, lightblue).
                bool isGreenOverlay = g > r + 30 && g > b + 20;
                if (lum >= BrightPanelThreshold && !isGreenOverlay)
                {
                    brightR.Add(r);
                    brightG.Add(g);
                    brightB.Add(b);
                }
            }

            double brightFraction = (double)brightR.Count / zoneH;

            if (brightFraction < MinBrightColumnCoverage)
            {
                // Тёмный разделитель / сцена вне плашек
                colType[col - x0] = -1;
                continue;
            }

            // Медиана R/G/B по ярким пикселям
            brightR.Sort();
            brightG.Sort();
            brightB.Sort();

            int medIdx = brightR.Count / 2;
            double medR = brightR[medIdx];
            double medG = brightG[medIdx];
            double medB = brightB[medIdx];

            // Найти ближайший якорь
            int bestTypeId = -1;
            double bestDist = double.MaxValue;

            foreach (ChestType ct in activeTypes)
            {
                PanelColor anchor = ct.PanelColor!.Value; // activeTypes уже отфильтрованы

                double dR = medR - anchor.R;
                double dG = medG - anchor.G;
                double dB = medB - anchor.B;
                double dist = Math.Sqrt(dR * dR + dG * dG + dB * dB);

                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestTypeId = ct.Id;
                }
            }

            colType[col - x0] = (bestDist <= ColorMatchTolerance) ? bestTypeId : -1;
        }

        return colType;
    }

    // ── сегментация ───────────────────────────────────────────────────────────

    /// <summary>
    /// Строит список смежных сегментов по массиву типов колонок.
    /// Каждый сегмент — максимальный непрерывный блок одного typeId (не -1).
    /// </summary>
    private static List<(int typeId, int segX0, int segX1)> BuildSegments(int[] colType, int zoneX0, int zoneW)
    {
        List<(int typeId, int segX0, int segX1)> segments = [];

        int i = 0;
        while (i < zoneW)
        {
            if (colType[i] == -1)
            {
                i++;
                continue;
            }

            int typeId = colType[i];
            int start = i;
            while (i < zoneW && colType[i] == typeId)
                i++;
            // Сегмент в координатах кадра
            segments.Add((typeId, zoneX0 + start, zoneX0 + i));
        }

        return segments;
    }

    // ── счёт точек в сегменте ─────────────────────────────────────────────────

    /// <summary>
    /// Считает заполненные точки в нижнем поясе сегмента плашки.
    /// Поддерживает несколько горизонтальных рядов точек (при 6+ сундуков — два ряда по 5).
    /// </summary>
    /// <remarks>
    /// Алгоритм агрегированно-по-рядного счёта (multi-row aggregate):
    /// <list type="number">
    ///   <item>Для каждой горизонтальной строки нижнего пояса определяем, является ли строка
    ///     «активной» (кандидатом на ряд точек): darkFraction ∈ [<see cref="MinDarkRowFraction"/>,
    ///     <see cref="MaxDarkRowFraction"/>).</item>
    ///   <item>Consecutive активные строки (разделённые полосами неактивных) образуют
    ///     горизонтальный ряд точек. Разрыв ≤ <see cref="MaxBandGapRows"/> строк внутри ряда
    ///     допускается (JPEG-артефакты).</item>
    ///   <item>Для каждого ряда строим агрегированный горизонтальный профиль: для каждой
    ///     колонки сегмента суммируем тёмные пиксели по всем активным строкам ряда; нормируем
    ///     на число активных строк. Колонка считается «тёмной» при нормированной доле ≥ 0.5.</item>
    ///   <item>Run-алгоритм по агрегированному профилю: непрерывный run тёмных колонок шириной
    ///     ∈ [minRunPx, maxRunPx] = одна точка. Агрегирование нивелирует JPEG-шум случайных
    ///     тёмных пикселей в отдельных строках.</item>
    ///   <item>Суммируем счёты точек по всем рядам = итог по плашке.</item>
    /// </list>
    /// Масштабонезависимость: minRunPx = (segW / <see cref="ExpectedMaxDotsPerRow"/>)
    /// × <see cref="MinRunCellWidthFraction"/>. maxRunPx = segW × <see cref="MaxRunSegmentWidthFraction"/>.
    /// </remarks>
    private static int CountDotsInSegment(
        byte[] pixels,
        int frameStride,
        int segX0, int segX1,
        int dotY0, int dotY1,
        int dotZoneH)
    {
        const int bpp = 4;

        int segW = segX1 - segX0;
        if (segW <= 0 || dotZoneH <= 0)
            return 0;

        // Масштабонезависимые пороги ширины run.
        // maxRunPx ограничен абсолютным cap MaxRunAbsolutePx для отсечения широких run-ов
        // тёмного хвоста иконки/оверлея (≥16px) при любом segW.
        double estimatedCellWidth = (double)segW / ExpectedMaxDotsPerRow;
        int minRunPx = Math.Max(1, (int)Math.Floor(estimatedCellWidth * MinRunCellWidthFraction));
        int maxRunPxFraction = Math.Max(minRunPx, (int)Math.Floor(segW * MaxRunSegmentWidthFraction));
        int maxRunPx = Math.Min(maxRunPxFraction, MaxRunAbsolutePx);

        int rowCount = dotY1 - dotY0;
        if (rowCount <= 0)
            return 0;

        // Шаг 1: для каждой строки нижнего пояса определить, является ли она «активной»
        // (кандидат на ряд точек): darkFraction ∈ [MinDarkRowFraction, MaxDarkRowFraction).
        // Хранить массив тёмных пикселей по колонкам для каждой активной строки (для агрегирования).
        bool[] rowActive = new bool[rowCount];
        int[] darkTotalPerCol = new int[segW]; // общий счётчик тёмных px по колонкам

        for (int ri = 0; ri < rowCount; ri++)
        {
            int row = dotY0 + ri;
            int rowDarkTotal = 0;
            int[] colDark = new int[segW];

            for (int col = segX0; col < segX1; col++)
            {
                int offset = row * frameStride + col * bpp;
                double lum = 0.299 * pixels[offset + 2] + 0.587 * pixels[offset + 1] + 0.114 * pixels[offset];
                if (lum < DarkThreshold)
                {
                    rowDarkTotal++;
                    colDark[col - segX0] = 1;
                }
            }

            double rowFraction = (double)rowDarkTotal / segW;
            rowActive[ri] = rowFraction >= MinDarkRowFraction && rowFraction < MaxDarkRowFraction;
            if (rowActive[ri])
                for (int ci = 0; ci < segW; ci++)
                    darkTotalPerCol[ci] += colDark[ci];
        }

        // Шаг 2: найти горизонтальные ряды-полосы (блоки активных строк с зазором ≤ MaxBandGapRows)
        // и для каждого ряда запустить run-алгоритм по агрегированному профилю.
        int totalDots = 0;
        int bandRowStart = -1; // первая строка текущей полосы-ряда
        int bandActiveRows = 0; // число активных строк в текущей полосе
        int[] bandDarkPerCol = new int[segW]; // агрегат по активным строкам текущей полосы
        int gapLen = 0;

        for (int ri = 0; ri <= rowCount; ri++)
        {
            bool active = ri < rowCount && rowActive[ri];

            if (active)
            {
                if (bandRowStart < 0)
                    bandRowStart = ri;
                gapLen = 0;
                bandActiveRows++;
                for (int ci = 0; ci < segW; ci++)
                {
                    int row = dotY0 + ri;
                    int offset = row * frameStride + (segX0 + ci) * bpp;
                    double lum = 0.299 * pixels[offset + 2] + 0.587 * pixels[offset + 1] + 0.114 * pixels[offset];
                    if (lum < DarkThreshold)
                        bandDarkPerCol[ci]++;
                }
            }
            else if (bandRowStart >= 0)
            {
                gapLen++;
                bool endOfSearch = ri == rowCount;
                bool gapTooLarge = gapLen > MaxBandGapRows;

                if (endOfSearch || gapTooLarge)
                {
                    // Полоса завершена: запустить run-алгоритм по агрегированному профилю
                    if (bandActiveRows > 0)
                    {
                        int dots = CountRunsInProfile(bandDarkPerCol, segW, bandActiveRows, minRunPx, maxRunPx);
                        totalDots += dots;
                    }
                    // Сброс состояния
                    bandRowStart = -1;
                    bandActiveRows = 0;
                    Array.Clear(bandDarkPerCol, 0, segW);
                    gapLen = 0;
                }
            }
        }

        return totalDots;
    }

    /// <summary>
    /// Запускает run-алгоритм по агрегированному профилю тёмных пикселей.
    /// Колонка «тёмная» если доля тёмных пикселей среди <paramref name="activeRows"/> строк ≥ 0.5.
    /// Возвращает число run-ов шириной ∈ [<paramref name="minRunPx"/>, <paramref name="maxRunPx"/>].
    /// </summary>
    private static int CountRunsInProfile(int[] darkPerCol, int segW, int activeRows, int minRunPx, int maxRunPx)
    {
        int dots = 0;
        int runWidth = 0;

        for (int ci = 0; ci < segW; ci++)
        {
            bool isDark = (double)darkPerCol[ci] / activeRows >= 0.5;
            if (isDark)
            {
                runWidth++;
            }
            else
            {
                if (runWidth >= minRunPx && runWidth <= maxRunPx)
                    dots++;
                runWidth = 0;
            }
        }
        if (runWidth >= minRunPx && runWidth <= maxRunPx)
            dots++;

        return dots;
    }

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
