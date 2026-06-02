// Регресс-тест OCR-поля nextLocation на фикстуре 3-3.jpg (T065-диагностика).
//
// Проблема: nextLocation не читался на этапах 3-3→«3-4», 3-5→«3-6» и т.п.
// Диагноз: пиксельный bitmap-шрифт «3-4» + яркие синие боевые эффекты (лёд, вода)
//   — Windows.Media.Ocr без предобработки возвращает пустой RawText для любого ROI в левом углу.
//   Без бинаризации: все ROI (65×40, 80×49, 110×73, 66×195 px) → Recognized=False, RawText=''.
//   Полный кадр 550×244 читает "Knight has been defeated..." — OCR работает, но «3-4» невидим.
//
// Фикс: бинаризация (ParseHint="binarize_white", реализована в OcrReader.BinarizeWhite).
//   Порог 160: пиксели (R+G+B)/3 >= 160 → белые; остальные → чёрные.
//   + агрессивный апскейл MinOcrDimensionBinarized=192 для пиксельного шрифта.
//   ROI (0.00,0.20,0.15,0.50) + бинаризация → RawText='3-4' → parsed=True, act=3, stage=4.
//
// Фикстура: screenshots/3-3.jpg (550×244) — кроп MainZone на этапе 3-3, nextLocation = «3-4».
// OCR-движок: реальный Windows.Media.Ocr через OcrReader (НЕ мок).

using System.Text.RegularExpressions;
using FluentAssertions;
using TBHStats.Capture;
using TBHStats.Capture.Ocr;
using TBHStats.Capture.WindowTracking;
using TBHStats.Core.Models;
using TBHStats.Core.Parsing;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Xunit;
using Xunit.Abstractions;

namespace TBHStats.Capture.Tests;

/// <summary>
/// Регресс-тест поля nextLocation на фикстуре 3-3.jpg.
/// Проверяет, что бинаризация (OcrReader.BinarizeWhiteParseHint) позволяет OCR
/// распознать «3-4» из пиксельного bitmap-шрифта на фоне ярких боевых эффектов.
/// </summary>
public sealed class NextLocationFixturesTests
{
    private readonly OcrReader _ocr = new();
    private readonly ValueParser _parser = new();
    private readonly ITestOutputHelper _output;

    public NextLocationFixturesTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ── Вспомогательные методы ────────────────────────────────────────────────

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

    private static RoiCalibration Roi(string fieldKey, double x, double y, double w, double h,
        string? parseHint = null)
        => new()
        {
            FieldKey  = fieldKey,
            Source    = FieldSource.MainZone,
            X         = x,
            Y         = y,
            W         = w,
            H         = h,
            OcrEngine = OcrEngine.WindowsMediaOcr,
            ParseHint = parseHint,
        };

    private async Task<OcrResult> ReadAndLogAsync(CapturedFrame frame, RoiCalibration roi, string label = "")
    {
        OcrResult result = await _ocr.ReadAsync(frame, roi, CancellationToken.None);
        int cropW = (int)Math.Round(roi.W * frame.ClientSize.Width);
        int cropH = (int)Math.Round(roi.H * frame.ClientSize.Height);
        _output.WriteLine(
            $"{label}[{roi.FieldKey}] " +
            $"Recognized={result.Recognized} " +
            $"Confidence={result.Confidence:F3} " +
            $"CropPx={cropW}×{cropH} " +
            $"RawText='{result.RawText}'");
        return result;
    }

    private static string NormalizeWs(string text)
        => Regex.Replace(text, @"\s", string.Empty);

    // ─────────────────────────────────────────────────────────────────────────
    // 3-3.jpg (550×244) — MainZone кроп на этапе 3-3, nextLocation = «3-4»
    //
    // Диагноз:
    //   - Без бинаризации: все ROI → Recognized=False (пиксельный шрифт + синие боевые эффекты).
    //   - С ParseHint="binarize_white" (порог 160 + апскейл до 192px):
    //     ROI (0.00,0.20,0.15,0.50) → RawText='3-4' → parsed=True, act=3, stage=4.
    //
    // ROI для калибровки в продакшн-приложении:
    //   x=0.00, y=0.20, w=0.15, h=0.50, ParseHint="binarize_white"
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Регресс-тест T065: OCR на 3-3.jpg с ParseHint="binarize_white" распознаёт «3-4»
    /// и TryParseNextLocation парсит act=3, stage=4.
    /// Фикс диагностирован эмпирически на реальной фикстуре.
    /// </summary>
    [Fact]
    [Trait("Category", "Regression")]
    public async Task MainZone33_NextLocation_WithBinarization_OcrReadsAndParses34()
    {
        var (bitmap, w, h) = await LoadJpgAsync("3-3.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);

        // ROI откалиброван по 3-3.jpg (550×244) с бинаризацией.
        // ParseHint="binarize_white" активирует BinarizeWhite (порог 160) + агрессивный апскейл до 192px.
        // Эмпирически подтверждено: этот ROI даёт Recognized=True, RawText='3-4'.
        RoiCalibration roi = Roi("nextLocation", 0.00, 0.20, 0.15, 0.50,
            OcrReader.BinarizeWhiteParseHint);

        OcrResult result = await ReadAndLogAsync(frame, roi, "REGRESSION ");

        // OCR-уровень: текст распознан
        result.Recognized.Should().BeTrue(
            $"OCR с бинаризацией должен распознать nextLocation «3-4» в 3-3.jpg. " +
            $"RawText='{result.RawText}'");

        // OCR-уровень: текст содержит «3» и «4» с разделителем
        NormalizeWs(result.RawText).Should().MatchRegex(@"3.?4",
            $"нормализованный RawText должен содержать '3' и '4'. Реальный: '{result.RawText}'");

        // Parsing-уровень: TryParseNextLocation успешно разбирает как act=3, stage=4
        bool parsed = _parser.TryParseNextLocation(result.RawText, out int act, out int stage);
        _output.WriteLine($"TryParseNextLocation: parsed={parsed}, act={act}, stage={stage}");

        parsed.Should().BeTrue(
            $"TryParseNextLocation должен успешно парсить RawText='{result.RawText}'.");

        act.Should().Be(3,   $"act из «3-4» должен быть 3; RawText='{result.RawText}'");
        stage.Should().Be(4, $"stage из «3-4» должен быть 4; RawText='{result.RawText}'");
    }
}
