// ADR-023 — Зонная локализация плашек сундуков по цвету + счёт точек по рядам.
//
// Два теста на реальных фикстурах (без моков). Тестируют ЗОННЫЙ АНАЛИЗАТОР НАПРЯМУЮ.
// ПРИМЕЧАНИЕ: обнаружение сундуков в рабочем пайплайне ВРЕМЕННО ОТКЛЮЧЕНО
// (см. FieldExtractor.ChestDetectionEnabled, T062 backlog). Анализатор и эти тесты
// сохранены как база для будущей доработки (multi-row 6+, плотные ряды, живой масштаб).
//
// 1. chests.jpg (545×241): red=1, blue=1, brown=2
//    Три плашки рядом: red(orange) x≈208..285, blue(lightblue) x≈301..384, brown(white) x≈397..480.
//    Зонная ROI: x≈0.358, y≈0.228, w≈0.541, h≈0.311.
//
// 2. main.jpg (549×232): blue=3, brown=3; red ОТСУТСТВУЕТ (нет красной плашки).
//    Две плашки рядом: blue x≈255..360, brown x≈378..437.
//    Зонная ROI: x≈0.446, y≈0.207, w≈0.364, h≈0.302.

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
/// Харнесс зонного анализатора плашек сундуков (ADR-023) на двух реальных фикстурах.
/// Проверяет локализацию по цвету и счёт точек. Без моков и заглушек.
/// </summary>
public sealed class ChestZoneAnalyzerFixturesTests
{
    private readonly ChestZoneAnalyzer _analyzer = new();
    private readonly ITestOutputHelper _output;
    private readonly GameMechanicsConfig _cfg = GameMechanicsConfig.CreateDefault();

    public ChestZoneAnalyzerFixturesTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ── ROI для chests.jpg (545×241) ─────────────────────────────────────────
    private static readonly RoiCalibration ChestsJpgZoneRoi = MakeRoi(
        "chestZone",
        x: 195.0 / 545.0,
        y:  55.0 / 241.0,
        w: 295.0 / 545.0,
        h:  75.0 / 241.0);

    // ── ROI для main.jpg (549×232) ────────────────────────────────────────────
    private static readonly RoiCalibration MainJpgZoneRoi = MakeRoi(
        "chestZone",
        x: 245.0 / 549.0,
        y:  48.0 / 232.0,
        w: 200.0 / 549.0,
        h:  70.0 / 232.0);

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

    private async Task<IReadOnlyDictionary<int, int>> AnalyzeAndLogAsync(
        CapturedFrame frame,
        RoiCalibration roi,
        string fixtureName)
    {
        IReadOnlyDictionary<int, int> result = await _analyzer
            .AnalyzeZoneAsync(frame, roi, _cfg, CancellationToken.None);

        foreach ((int typeId, int count) in result)
        {
            string key = _cfg.ChestTypes.FirstOrDefault(c => c.Id == typeId)?.Key ?? typeId.ToString();
            _output.WriteLine($"[{fixtureName}] typeId={typeId} ({key}), dots={count}");
        }

        _output.WriteLine($"[{fixtureName}] total types found: {result.Count}");
        return result;
    }

    // ── Тест 1: chests.jpg — три плашки, red=1/blue=1/brown=2 ─────────────────

    /// <summary>
    /// Зонный анализатор на chests.jpg: должны найтись все три типа — red=1, blue=1, brown=2.
    /// </summary>
    [Fact]
    public async Task ChestsJpg_ZoneRoi_FindsAllThreeTypes_CorrectCounts()
    {
        var (bitmap, w, h) = await LoadJpgAsync("chests.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);

        IReadOnlyDictionary<int, int> result = await AnalyzeAndLogAsync(frame, ChestsJpgZoneRoi, "chests.jpg");

        int redId   = _cfg.ChestTypes.First(c => c.Key == "red").Id;
        int blueId  = _cfg.ChestTypes.First(c => c.Key == "blue").Id;
        int brownId = _cfg.ChestTypes.First(c => c.Key == "brown").Id;

        result.Should().ContainKey(redId,   "красная плашка присутствует в chests.jpg");
        result.Should().ContainKey(blueId,  "синяя плашка присутствует в chests.jpg");
        result.Should().ContainKey(brownId, "коричневая плашка присутствует в chests.jpg");

        result[redId].Should().Be(1,   "red=1 (ground truth chests.jpg)");
        result[blueId].Should().Be(1,  "blue=1 (ground truth chests.jpg)");
        result[brownId].Should().Be(2, "brown=2 (ground truth chests.jpg)");
    }

    // ── Тест 2: main.jpg — две плашки, blue=3/brown=3, red отсутствует ─────────

    /// <summary>
    /// Зонный анализатор на main.jpg: синяя и коричневая плашки, каждая с 3 точками.
    /// Красная плашка отсутствует → typeId red НЕ должен входить в результат.
    /// </summary>
    [Fact]
    public async Task MainJpg_ZoneRoi_FindsTwoTypes_RedAbsent()
    {
        var (bitmap, w, h) = await LoadJpgAsync("main.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);

        IReadOnlyDictionary<int, int> result = await AnalyzeAndLogAsync(frame, MainJpgZoneRoi, "main.jpg");

        int redId   = _cfg.ChestTypes.First(c => c.Key == "red").Id;
        int blueId  = _cfg.ChestTypes.First(c => c.Key == "blue").Id;
        int brownId = _cfg.ChestTypes.First(c => c.Key == "brown").Id;

        result.Should().ContainKey(blueId,  "синяя плашка присутствует в main.jpg");
        result.Should().ContainKey(brownId, "коричневая плашка присутствует в main.jpg");

        result.Should().NotContainKey(redId,
            "красная плашка ОТСУТСТВУЕТ в main.jpg — в зоне только blue и brown");

        result[blueId].Should().Be(3,  "blue=3 (ground truth main.jpg)");
        result[brownId].Should().Be(3, "brown=3 (ground truth main.jpg)");
    }
}
