// T062-fix — Идентификация типа сундука по цвету фона плашки (ADR-022).
//
// Фикстура: screenshots/chests.jpg (545×241).
// Ground truth: red=1, blue=1, brown=2.
//
// Детектор: ChestDotCounter (реализует IChestPanelAnalyzer) —
//   1. Доминирующий яркий цвет плашки (luminance ≥ 110) → медиана R/G/B.
//   2. Евклидово расстояние до якорей GameMechanicsConfig: brown(255,255,255),
//      blue(190,220,238), red(236,133,41). Толеранс ColorMatchTolerance=70.
//   3. MinBrightCoverage=0.25 — reject тёмной сцены.
//   4. Счёт точек: run-алгоритм по яркости (DarkThreshold=90, MinRunWidthPx=3).
//
// ROI откалиброваны по размеру изображения 545×241 (эмпирически по горизонтальному скану y=90):
//   Вертикальные границы плашек: y≈67..127 → y_norm=67/241≈0.278, h=(127-67)/241≈0.249
//   Red   (orange, x=208..285): x_norm=208/545≈0.382, w=77/545≈0.141
//   Blue  (lightblue, x=301..384): x_norm=301/545≈0.552, w=83/545≈0.152
//   Brown (white, x=397..480): x_norm=397/545≈0.728, w=83/545≈0.152
//   Негатив (тёмный фон слева, x=50..130): x_norm=50/545≈0.092, w=80/545≈0.147
//
// Ключевая проверка бага: ROI с ключом «chest:red@2» над синей плашкой → тип=blue.

using FluentAssertions;
using TBHStats.Capture.Chests;
using TBHStats.Capture.WindowTracking;
using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Xunit;
using Xunit.Abstractions;

namespace TBHStats.Capture.Tests;

/// <summary>
/// Харнесс визуального анализатора плашек сундуков (ADR-022) на реальной фикстуре chests.jpg.
/// Проверяет идентификацию типа по цвету и счёт точек. Без моков и заглушек.
/// </summary>
public sealed class ChestPanelAnalyzerFixturesTests
{
    private readonly ChestDotCounter _analyzer = new();
    private readonly ITestOutputHelper _output;
    private readonly GameMechanicsConfig _cfg = GameMechanicsConfig.CreateDefault();

    // ── Координаты ROI на плашки (нормализованные, 545×241) ─────────────────
    // Вертикаль общая для всех плашек: y≈67..127 → y=67/241, h=60/241

    private const double PanelY = 67.0 / 241.0;  // ≈0.278
    private const double PanelH = 60.0 / 241.0;  // ≈0.249

    // Red (orange): x≈208..285
    private static readonly RoiCalibration RedPanelRoi =
        MakeRoi("chest:red@2",   208.0 / 545.0, PanelY, 77.0 / 545.0, PanelH);

    // Blue (lightblue): x≈301..384
    private static readonly RoiCalibration BluePanelRoi =
        MakeRoi("chest:blue@2",  301.0 / 545.0, PanelY, 83.0 / 545.0, PanelH);

    // Brown (white): x≈397..480
    private static readonly RoiCalibration BrownPanelRoi =
        MakeRoi("chest:brown@2", 397.0 / 545.0, PanelY, 83.0 / 545.0, PanelH);

    // Негатив: тёмный фон слева (x≈50..130), вне плашек
    private static readonly RoiCalibration DarkSceneRoi =
        MakeRoi("chest:red@1",   50.0 / 545.0,  PanelY, 80.0 / 545.0, PanelH);

    // Ключевой регресс: ключ «chest:red@2» над СИНЕЙ плашкой (те же координаты что BluePanelRoi)
    private static readonly RoiCalibration RedKeyOverBlueRoi =
        MakeRoi("chest:red@2",   301.0 / 545.0, PanelY, 83.0 / 545.0, PanelH);

    public ChestPanelAnalyzerFixturesTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ── вспомогательные ──────────────────────────────────────────────────────

    private static RoiCalibration MakeRoi(string fieldKey, double x, double y, double w, double h)
        => new()
        {
            FieldKey  = fieldKey,
            Source    = FieldSource.MainZone,
            X         = x,
            Y         = y,
            W         = w,
            H         = h,
            OcrEngine = OcrEngine.WindowsMediaOcr,
            ParseHint = null,
        };

    private static async Task<(SoftwareBitmap Bitmap, int Width, int Height)> LoadJpgAsync(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", fileName);
        File.Exists(path).Should().BeTrue($"фикстура {fileName} должна существовать в fixtures/");

        StorageFile file = await StorageFile.GetFileFromPathAsync(path);
        using Windows.Storage.Streams.IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);

        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
        SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync();

        return (bitmap, (int)decoder.PixelWidth, (int)decoder.PixelHeight);
    }

    private static CapturedFrame MakeFrame(SoftwareBitmap bitmap, int width, int height)
        => new(bitmap, new SizePx(width, height), DateTimeOffset.UtcNow);

    private async Task<ChestPanelReading> AnalyzeAndLogAsync(CapturedFrame frame, RoiCalibration roi)
    {
        ChestPanelReading result = await _analyzer.AnalyzeChestPanelAsync(frame, roi, _cfg, CancellationToken.None);
        _output.WriteLine(
            $"[{roi.FieldKey}] TypeId={result.ChestTypeId?.ToString() ?? "null"}, " +
            $"DotCount={result.DotCount}, PanelMatch={result.PanelMatch:F3} " +
            $"(ROI x={roi.X:F3} y={roi.Y:F3} w={roi.W:F3} h={roi.H:F3})");
        return result;
    }

    // ── Happy-path: тип определён по цвету, ground truth red=1/blue=1/brown=2 ─

    /// <summary>
    /// Red сундук (orange-фон): тип = red (Id=3), 1 заполненная точка (ground truth).
    /// Ключ ROI («chest:red@2») не влияет на тип — тип из цвета.
    /// </summary>
    [Fact]
    public async Task Red_PanelClassifiedAsRed_OneDot()
    {
        var (bitmap, w, h) = await LoadJpgAsync("chests.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);

        ChestPanelReading result = await AnalyzeAndLogAsync(frame, RedPanelRoi);

        result.ChestTypeId.Should().NotBeNull("red-плашка должна быть распознана");
        ChestType? redType = _cfg.ChestTypes.FirstOrDefault(c => c.Key == "red");
        result.ChestTypeId.Should().Be(redType!.Id, "цвет orange → тип red");
        result.DotCount.Should().Be(1, "red сундук имеет 1 заполненную точку (ground truth)");
        result.PanelMatch.Should().BeGreaterThan(0, "качество совпадения должно быть положительным");
    }

    /// <summary>
    /// Blue сундук (lightblue-фон): тип = blue (Id=2), 1 заполненная точка (ground truth).
    /// </summary>
    [Fact]
    public async Task Blue_PanelClassifiedAsBlue_OneDot()
    {
        var (bitmap, w, h) = await LoadJpgAsync("chests.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);

        ChestPanelReading result = await AnalyzeAndLogAsync(frame, BluePanelRoi);

        result.ChestTypeId.Should().NotBeNull("blue-плашка должна быть распознана");
        ChestType? blueType = _cfg.ChestTypes.FirstOrDefault(c => c.Key == "blue");
        result.ChestTypeId.Should().Be(blueType!.Id, "цвет lightblue → тип blue");
        result.DotCount.Should().Be(1, "blue сундук имеет 1 заполненную точку (ground truth)");
        result.PanelMatch.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Brown сундук (white-фон): тип = brown (Id=1), 2 заполненные точки (ground truth).
    /// </summary>
    [Fact]
    public async Task Brown_PanelClassifiedAsBrown_TwoDots()
    {
        var (bitmap, w, h) = await LoadJpgAsync("chests.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);

        ChestPanelReading result = await AnalyzeAndLogAsync(frame, BrownPanelRoi);

        result.ChestTypeId.Should().NotBeNull("brown-плашка должна быть распознана");
        ChestType? brownType = _cfg.ChestTypes.FirstOrDefault(c => c.Key == "brown");
        result.ChestTypeId.Should().Be(brownType!.Id, "цвет white → тип brown");
        result.DotCount.Should().Be(2, "brown сундук имеет 2 заполненные точки (ground truth)");
        result.PanelMatch.Should().BeGreaterThan(0);
    }

    // ── Регресс-тест бага: ключ не определяет тип ────────────────────────────

    /// <summary>
    /// РЕГРЕСС-ТЕСТ БАГА: ROI с ключом «chest:red@2» над СИНЕЙ плашкой → тип = blue.
    /// Было: фантомный red=1 (из @N-позиционной схемы).
    /// Стало: тип берётся из цвета → blue; фантомного red нет.
    /// </summary>
    [Fact]
    public async Task RegresionBug_RedKeyOverBluePanel_ReturnsBlue()
    {
        var (bitmap, w, h) = await LoadJpgAsync("chests.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);

        ChestPanelReading result = await AnalyzeAndLogAsync(frame, RedKeyOverBlueRoi);

        result.ChestTypeId.Should().NotBeNull(
            "синяя плашка должна распознаться, несмотря на ключ chest:red@2");
        ChestType? blueType = _cfg.ChestTypes.FirstOrDefault(c => c.Key == "blue");
        result.ChestTypeId.Should().Be(blueType!.Id,
            "ключ chest:red@2 НЕ определяет тип: цвет плашки blue → тип blue, не red (фикс бага)");
    }

    // ── Негатив: тёмная сцена → reject ───────────────────────────────────────

    /// <summary>
    /// ROI над тёмным фоном сцены (вне плашек, левый край фикстуры) → ChestTypeId = null (reject).
    /// </summary>
    [Fact]
    public async Task DarkScene_ReturnsNull()
    {
        var (bitmap, w, h) = await LoadJpgAsync("chests.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);

        ChestPanelReading result = await AnalyzeAndLogAsync(frame, DarkSceneRoi);

        result.ChestTypeId.Should().BeNull(
            "тёмный фон сцены вне плашек должен быть отвергнут (brightCoverage < MinBrightCoverage)");
    }

    // ── Smoke: все три ROI на одном кадре, кэш буфера работает ───────────────

    /// <summary>
    /// Smoke: все три плашки на одном кадре → red=1, blue=1, brown=2.
    /// Также проверяет корректность кэша пиксельного буфера.
    /// </summary>
    [Fact]
    public async Task AllThreePanels_OnSameFrame_CorrectTypesAndCounts()
    {
        var (bitmap, w, h) = await LoadJpgAsync("chests.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);

        ChestPanelReading red   = await AnalyzeAndLogAsync(frame, RedPanelRoi);
        ChestPanelReading blue  = await AnalyzeAndLogAsync(frame, BluePanelRoi);
        ChestPanelReading brown = await AnalyzeAndLogAsync(frame, BrownPanelRoi);

        ChestType? redType   = _cfg.ChestTypes.FirstOrDefault(c => c.Key == "red");
        ChestType? blueType  = _cfg.ChestTypes.FirstOrDefault(c => c.Key == "blue");
        ChestType? brownType = _cfg.ChestTypes.FirstOrDefault(c => c.Key == "brown");

        red.ChestTypeId.Should().Be(redType!.Id,   "red-плашка → тип red");
        blue.ChestTypeId.Should().Be(blueType!.Id, "blue-плашка → тип blue");
        brown.ChestTypeId.Should().Be(brownType!.Id, "brown-плашка → тип brown");

        red.DotCount.Should().Be(1,   "red=1 (ground truth)");
        blue.DotCount.Should().Be(1,  "blue=1 (ground truth)");
        brown.DotCount.Should().Be(2, "brown=2 (ground truth)");
    }
}
