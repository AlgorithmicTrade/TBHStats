// T062 — Визуальный подсчёт точек сундуков на реальной фикстуре chests.jpg.
//
// Фикстура: screenshots/chests.jpg (545×241).
// Ground truth (задокументировано пользователем): red=1, blue=1, brown=2.
//
// Детектор: ChestDotCounter — визуальный анализ пикселей без OCR (ADR-021).
// Алгоритм: яркостные пороги → darkFraction по колонкам → run-ы тёмных колонок.
//
// ROI откалиброваны по размеру изображения 545×241 (эмпирически по диагностическому скану):
//   Полоса точек по вертикали: y≈107..119 px → y_norm≈0.444, h_norm≈0.050
//   Red-dots  (левая панель):  x≈218..255 px → x_norm≈0.400, w_norm≈0.068
//     (тёмная точка x=222..227; пустые ячейки x=233..270; рамки не попадают в ROI)
//   Blue-dots (средняя):       x≈316..375 px → x_norm≈0.580, w_norm≈0.109
//     (тёмная точка x=323..328; аналогично red)
//   Brown-dots (правая):       x≈400..440 px → x_norm≈0.734, w_norm≈0.073
//     (тёмные точки x=414..419 и x=425..430; НАЧАЛО после рамки панели x=385..396!
//      brown точки тёмные только 4px из 13 строк ROI → darkFraction≈0.31 ≥ MinDarkColumnFraction=0.3)

using FluentAssertions;
using TBHStats.Capture.Chests;
using TBHStats.Capture.WindowTracking;
using TBHStats.Core.Models;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Xunit;
using Xunit.Abstractions;

namespace TBHStats.Capture.Tests;

/// <summary>
/// Харнесс визуального детектора точек сундуков (T062) на реальной фикстуре chests.jpg.
/// Все тесты работают с реальным <see cref="ChestDotCounter"/> — без моков и заглушек.
/// </summary>
public sealed class ChestDotCounterFixturesTests
{
    private readonly ChestDotCounter _counter = new();
    private readonly ITestOutputHelper _output;

    // ── Точные координаты ROI (нормализованные, подобраны по chests.jpg 545×241) ─
    // Полоса точек по вертикали: y≈107..118 px → 107/241≈0.444, h=11/241≈0.046
    // Добавляем небольшой запас по высоте для надёжности: h=0.055 (≈13px)

    // Red-dots (левая панель, огненный/легендарный сундук):
    //   Иконка центрирована примерно x≈195..290; точки под иконкой ≈ x218..253
    //   218/545≈0.400, ширина 35px/545≈0.064
    private static readonly RoiCalibration RedDotsRoi   = MakeRoi("chest:red@3",   0.400, 0.444, 0.064, 0.055);

    // Blue-dots (средняя панель, синий/редкий сундук):
    //   Иконка центрирована примерно x≈293..388; точки ≈ x316..350
    //   316/545≈0.580, ширина 34px/545≈0.062
    private static readonly RoiCalibration BlueDotsRoi  = MakeRoi("chest:blue@3",  0.580, 0.444, 0.062, 0.055);

    // Brown-dots (правая панель, коричневый/базовый сундук):
    //   Тёмные точки на x=414..419 (dot1) и x=425..430 (dot2).
    //   ROI начинается ПОСЛЕ левой рамки панели (x=385..396) → x=400:
    //   400/545≈0.734, правый край x=440: w=(440-400)/545≈0.073
    //   ВАЖНО: brown точки тёмные только на y=107..110 (4 строки из h=13),
    //   darkFraction≈0.31 — чуть выше MinDarkColumnFraction=0.3.
    private static readonly RoiCalibration BrownDotsRoi = MakeRoi("chest:brown@3", 0.734, 0.444, 0.073, 0.050);

    public ChestDotCounterFixturesTests(ITestOutputHelper output)
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

    private async Task<ChestDotCountResult> CountAndLogAsync(
        CapturedFrame frame,
        RoiCalibration roi)
    {
        ChestDotCountResult result = await _counter.CountFilledDotsAsync(frame, roi, CancellationToken.None);
        _output.WriteLine(
            $"[{roi.FieldKey}] Count={result.Count}, Detected={result.Detected} " +
            $"(ROI x={roi.X:F3} y={roi.Y:F3} w={roi.W:F3} h={roi.H:F3})");
        return result;
    }

    // ── Happy-path: ground truth red=1, blue=1, brown=2 ──────────────────────

    /// <summary>
    /// Red сундук (левая панель, фикстура chests.jpg): должна быть 1 заполненная точка.
    /// Ground truth: пользователь задокументировал red=1.
    /// </summary>
    [Fact]
    public async Task Chests_Red_DetectsOneDot()
    {
        var (bitmap, w, h) = await LoadJpgAsync("chests.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);

        ChestDotCountResult result = await CountAndLogAsync(frame, RedDotsRoi);

        result.Detected.Should().BeTrue("паттерн точек красного сундука должен быть обнаружен");
        result.Count.Should().Be(1, "red сундук в chests.jpg имеет 1 заполненную точку (ground truth)");
    }

    /// <summary>
    /// Blue сундук (средняя панель, фикстура chests.jpg): должна быть 1 заполненная точка.
    /// Ground truth: пользователь задокументировал blue=1.
    /// </summary>
    [Fact]
    public async Task Chests_Blue_DetectsOneDot()
    {
        var (bitmap, w, h) = await LoadJpgAsync("chests.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);

        ChestDotCountResult result = await CountAndLogAsync(frame, BlueDotsRoi);

        result.Detected.Should().BeTrue("паттерн точек синего сундука должен быть обнаружен");
        result.Count.Should().Be(1, "blue сундук в chests.jpg имеет 1 заполненную точку (ground truth)");
    }

    /// <summary>
    /// Brown сундук (правая панель, фикстура chests.jpg): должны быть 2 заполненные точки.
    /// Ground truth: пользователь задокументировал brown=2.
    /// Две точки разделены тонким светлым зазором — run-алгоритм должен разделить их.
    /// </summary>
    [Fact]
    public async Task Chests_Brown_DetectsTwoDots()
    {
        var (bitmap, w, h) = await LoadJpgAsync("chests.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);

        ChestDotCountResult result = await CountAndLogAsync(frame, BrownDotsRoi);

        result.Detected.Should().BeTrue("паттерн точек коричневого сундука должен быть обнаружен");
        result.Count.Should().Be(2, "brown сундук в chests.jpg имеет 2 заполненные точки (ground truth)");
    }

    // ── Smoke: все три вызова на одном кадре ─────────────────────────────────

    /// <summary>
    /// Smoke-тест: все три ROI прогоняются на одном кадре.
    /// Проверяет, что кэш буфера работает корректно и все три результата валидны.
    /// </summary>
    [Fact]
    public async Task Chests_AllThreeOnSameFrame_AllDetected()
    {
        var (bitmap, w, h) = await LoadJpgAsync("chests.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);

        ChestDotCountResult red   = await CountAndLogAsync(frame, RedDotsRoi);
        ChestDotCountResult blue  = await CountAndLogAsync(frame, BlueDotsRoi);
        ChestDotCountResult brown = await CountAndLogAsync(frame, BrownDotsRoi);

        red.Detected.Should().BeTrue();
        blue.Detected.Should().BeTrue();
        brown.Detected.Should().BeTrue();

        red.Count.Should().Be(1,   "red=1 (ground truth)");
        blue.Count.Should().Be(1,  "blue=1 (ground truth)");
        brown.Count.Should().Be(2, "brown=2 (ground truth)");
    }
}
