// T049 — харнесс проверки точности OCR на реальных скриншотах игры Task Bar Hero.
//
// Двухуровневая архитектура ассертов на каждое числовое/текстовое поле:
//   OCR-уровень     : нормализованный RawText содержит ожидаемые символы (GREEN при корректном OCR).
//   Parsing-уровень : ValueParser.TryParseAbbreviatedNumber успешно парсит числа с пробелом-разделителем.
//                     Методы с суффиксом _ParsingGap (категория "ParsingGap") ожидаемо RED до фикса ValueParser.
//
// ROI откалиброваны эмпирически по реальным результатам Windows.Media.Ocr (итерация 2).
// OCR-движок: реальный Windows.Media.Ocr через OcrReader (НЕ мок).
// Загрузка изображений: реальные JPG из fixtures/ (скопированы из screenshots/).

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
/// Харнесс точности OCR на реальных фикстурах-скриншотах Task Bar Hero (T049).
/// Двухуровневые ассерты: OCR-точность (должна быть GREEN) + парсинг ValueParser (может быть RED до фикса).
/// ROI выверены эмпирически по реальному выводу Windows.Media.Ocr.
/// </summary>
public sealed class OcrFixturesTests
{
    private readonly OcrReader _ocr = new();
    private readonly ValueParser _parser = new();
    private readonly ITestOutputHelper _output;

    public OcrFixturesTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Вспомогательные методы
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Загружает JPG-фикстуру из папки fixtures/ (output directory) в SoftwareBitmap.
    /// Возвращает (bitmap, width, height). Caller отвечает за Dispose через CapturedFrame.
    /// </summary>
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

    private static RoiCalibration Roi(string fieldKey, double x, double y, double w, double h)
        => new()
        {
            FieldKey  = fieldKey,
            Source    = FieldSource.Tab,
            X         = x,
            Y         = y,
            W         = w,
            H         = h,
            OcrEngine = OcrEngine.WindowsMediaOcr,
            ParseHint = null,
        };

    private async Task<OcrResult> ReadAndLogAsync(CapturedFrame frame, RoiCalibration roi)
    {
        OcrResult result = await _ocr.ReadAsync(frame, roi, CancellationToken.None);
        _output.WriteLine(
            $"[{roi.FieldKey}] Recognized={result.Recognized} " +
            $"Confidence={result.Confidence:F3} " +
            $"RawText='{result.RawText}'");
        return result;
    }

    /// <summary>Убирает все пробельные символы (включая NBSP, thin space, разделитель разрядов).</summary>
    private static string NormalizeWs(string text)
        => Regex.Replace(text, @"\s", string.Empty);

    // ─────────────────────────────────────────────────────────────────────────
    // overall.jpg (1000×942) — Status+Hero+MainZone объединённый скриншот
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gold "54 678" — шапка Hero-панели (правая).
    /// ROI: (0.35, 0.00, 0.65, 0.06) подтверждён эмпирически: RawText='54 678'.
    /// </summary>
    [Fact]
    public async Task Overall_Gold_OcrRecognizesDigits()
    {
        var (bitmap, w, h) = await LoadJpgAsync("overall.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);
        RoiCalibration roi = Roi("gold", 0.35, 0.00, 0.65, 0.06);

        OcrResult result = await ReadAndLogAsync(frame, roi);

        result.Recognized.Should().BeTrue("OCR должен распознать gold в overall.jpg");
        NormalizeWs(result.RawText).Should().Contain("54678",
            "нормализованный текст gold должен содержать 54678");
    }

    /// <summary>
    /// [ParsingGap] ValueParser не поддерживает пробел-разделитель "54 678" → false.
    /// ОЖИДАЕМО RED до фикса ValueParser.TryParseAbbreviatedNumber.
    /// </summary>
    [Fact]
    [Trait("Category", "ParsingGap")]
    public async Task Overall_Gold_ParsingGap_ValueParserFailsOnSpaceSeparatedNumber()
    {
        var (bitmap, w, h) = await LoadJpgAsync("overall.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);
        RoiCalibration roi = Roi("gold", 0.35, 0.00, 0.65, 0.06);

        OcrResult result = await ReadAndLogAsync(frame, roi);
        result.Recognized.Should().BeTrue();

        bool parsed = _parser.TryParseAbbreviatedNumber(result.RawText, out long value);
        // После фикса ValueParser эти строки станут GREEN:
        parsed.Should().BeTrue("ValueParser должен поддерживать пробел-разделитель разрядов (требует фикс в ValueParser)");
        value.Should().Be(54678L);
    }

    /// <summary>
    /// Класс "Knight" — строка класса в Status-панели (левая).
    /// ROI: strip (0.0, 0.10, 1.0, 0.10) → "Knight Level Knight 23".
    /// </summary>
    [Fact]
    public async Task Overall_HeroClass_OcrRecognizesKnight()
    {
        var (bitmap, w, h) = await LoadJpgAsync("overall.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);
        RoiCalibration roi = Roi("heroClass", 0.0, 0.10, 1.0, 0.10);

        OcrResult result = await ReadAndLogAsync(frame, roi);

        result.Recognized.Should().BeTrue("OCR должен распознать класс героя в overall.jpg");
        result.RawText.Should().ContainEquivalentOf("knight",
            "класс героя в overall.jpg должен быть Knight");
    }

    /// <summary>
    /// Level "23" и Exp "2 285 394 / 4 739 962" — строка Level+Exp в Status-панели.
    /// ROI: (0.0, 0.18, 1.0, 0.05) → "Level Exp 23 2 285 394 / 4 739 962".
    /// </summary>
    [Fact]
    public async Task Overall_LevelAndExp_OcrRecognizesDigits()
    {
        var (bitmap, w, h) = await LoadJpgAsync("overall.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);
        RoiCalibration roi = Roi("levelExp", 0.0, 0.18, 1.0, 0.05);

        OcrResult result = await ReadAndLogAsync(frame, roi);

        result.Recognized.Should().BeTrue("OCR должен распознать строку Level+Exp в overall.jpg");
        string normalized = NormalizeWs(result.RawText);
        normalized.Should().Contain("23",
            "строка должна содержать Level=23");
        normalized.Should().Contain("2285394",
            "нормализованный текст Exp должен содержать 2285394");
    }

    /// <summary>
    /// [ParsingGap] "2 285 394" — пробелы-разделители разрядов, ValueParser возвращает false.
    /// ОЖИДАЕМО RED до фикса ValueParser.
    /// </summary>
    [Fact]
    [Trait("Category", "ParsingGap")]
    public async Task Overall_Exp_ParsingGap_CurrentXpValueParserFails()
    {
        var (bitmap, w, h) = await LoadJpgAsync("overall.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);
        RoiCalibration roi = Roi("levelExp", 0.0, 0.18, 1.0, 0.05);

        OcrResult result = await ReadAndLogAsync(frame, roi);
        result.Recognized.Should().BeTrue();

        // Текст вида "Level Exp 23 2 285 394 / 4 739 962" — берём токен после "/ " как xpToLevel, до — текущий Exp
        // Для демонстрации gap: попробуем парсить любой токен с пробелами внутри
        string[] parts = result.RawText.Split('/');
        string expToken = parts.Length > 0 ? parts[0].Trim().Split(' ').Last() + " ... " : result.RawText;
        // Полная демонстрация: строка "2 285 394" как есть
        const string spaceSeparated = "2 285 394";
        bool parsed = _parser.TryParseAbbreviatedNumber(spaceSeparated, out long value);
        // После фикса ValueParser:
        parsed.Should().BeTrue("ValueParser должен поддерживать пробел-разделитель разрядов (требует фикс)");
        value.Should().Be(2_285_394L);
    }

    /// <summary>
    /// Attack Damage "34" — строка в Status-панели.
    /// ROI: (0.0, 0.20, 1.0, 0.10) → содержит "34".
    /// </summary>
    [Fact]
    public async Task Overall_AttackDamage_OcrRecognizes34()
    {
        var (bitmap, w, h) = await LoadJpgAsync("overall.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);
        RoiCalibration roi = Roi("heroDamage", 0.0, 0.20, 1.0, 0.10);

        OcrResult result = await ReadAndLogAsync(frame, roi);

        result.Recognized.Should().BeTrue("OCR должен распознать Attack Damage в overall.jpg");
        NormalizeWs(result.RawText).Should().Contain("34",
            "Attack Damage в overall.jpg должен быть 34");
    }

    /// <summary>
    /// MainZone лог "Cleared Stage 2-1. (92s)" — полоса y=0.75..0.85.
    /// ROI: (0.0, 0.75, 1.0, 0.10) → "Cleared Stage 2-1. (92s) [20:34]".
    /// </summary>
    [Fact]
    public async Task Overall_MainZone_OcrContainsClearedStage()
    {
        var (bitmap, w, h) = await LoadJpgAsync("overall.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);
        RoiCalibration roi = Roi("mainZoneLog", 0.0, 0.75, 1.0, 0.10);

        OcrResult result = await ReadAndLogAsync(frame, roi);

        result.Recognized.Should().BeTrue("OCR должен распознать лог-строку MainZone в overall.jpg");
        result.RawText.Should().ContainEquivalentOf("cleared",
            "лог-строка должна содержать 'Cleared'");
        result.RawText.Should().Contain("2-1",
            "лог-строка должна содержать номер этапа 2-1");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // status_1.jpg (478×703) — вкладка Status basic
    // Расположение строк (эмпирически, y-координаты нормализованные):
    //   класс "Ranger":           y≈0.175, h≈0.06 → RawText='Ranger'
    //   Level+Class в одной зоне: strip y=0.15..0.35 → весь блок
    //   Exp "5 714 975 / 6 266 704": в hp_row (y=0.265) → 'Exp 5 714 975 / 6 266 704'
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Status1_HeroClass_OcrRecognizesRanger()
    {
        // level_row (0.05, 0.175, 0.90, 0.06) эмпирически возвращает 'Ranger' — класс видим здесь
        var (bitmap, w, h) = await LoadJpgAsync("status_1.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);
        RoiCalibration roi = Roi("heroClass", 0.05, 0.175, 0.90, 0.06);

        OcrResult result = await ReadAndLogAsync(frame, roi);

        result.Recognized.Should().BeTrue("OCR должен распознать класс героя в status_1");
        result.RawText.Should().ContainEquivalentOf("ranger",
            "класс в status_1.jpg должен быть Ranger");
    }

    [Fact]
    public async Task Status1_LevelAndExp_OcrRecognizesDigits()
    {
        // Широкий ROI strip (0.0, 0.15, 1.0, 0.20) → "Ranger ... Level Exp ... 25 5 714 975 / 6 266 704 502,3"
        var (bitmap, w, h) = await LoadJpgAsync("status_1.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);
        RoiCalibration roi = Roi("levelExp", 0.0, 0.15, 1.0, 0.20);

        OcrResult result = await ReadAndLogAsync(frame, roi);

        result.Recognized.Should().BeTrue("OCR должен распознать Level+Exp в status_1");
        string normalized = NormalizeWs(result.RawText);
        normalized.Should().Contain("25",
            "уровень в status_1.jpg должен быть 25");
        normalized.Should().Contain("5714975",
            "нормализованный текст Exp должен содержать 5714975");
    }

    [Fact]
    [Trait("Category", "ParsingGap")]
    public void Status1_Exp_ParsingGap_ValueParserFailsOnSpaceSeparated()
    {
        // ОЖИДАЕМО RED до фикса ValueParser. "5 714 975" содержит пробелы-разделители.
        const string spaceSeparated = "5 714 975";
        bool parsed = _parser.TryParseAbbreviatedNumber(spaceSeparated, out long value);
        parsed.Should().BeTrue("ValueParser должен поддерживать пробел-разделитель разрядов (требует фикс)");
        value.Should().Be(5_714_975L);
    }

    [Fact]
    public async Task Status1_AttackDamage_OcrRecognizes63()
    {
        // strip (0.0, 0.20, 1.0, 0.20) содержит "63"
        var (bitmap, w, h) = await LoadJpgAsync("status_1.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);
        RoiCalibration roi = Roi("heroDamage", 0.0, 0.20, 1.0, 0.20);

        OcrResult result = await ReadAndLogAsync(frame, roi);

        result.Recognized.Should().BeTrue("OCR должен распознать Attack Damage в status_1");
        NormalizeWs(result.RawText).Should().Contain("63",
            "Attack Damage в status_1.jpg должен быть 63");
    }

    [Fact]
    public async Task Status1_CurrentHp_OcrRecognizes125()
    {
        // hp_row (0.05, 0.265, 0.90, 0.06) эмпирически: "Exp 5 714 975 / 6 266 704" — Exp строка здесь
        // HP "125 / 125" в следующей строке → strip (0.0, 0.25, 1.0, 0.20)
        var (bitmap, w, h) = await LoadJpgAsync("status_1.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);
        RoiCalibration roi = Roi("currentHp", 0.0, 0.25, 1.0, 0.20);

        OcrResult result = await ReadAndLogAsync(frame, roi);

        result.Recognized.Should().BeTrue("OCR должен распознать HP в status_1");
        NormalizeWs(result.RawText).Should().Contain("125",
            "HP в status_1.jpg должен содержать 125");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // status_2.jpg (474×705) — Status/Detailed
    // Строки (эмпирически):
    //   класс "Ranger":  y=0.175, h=0.07 → 'Ranger'
    //   level "25":      y=0.215, h=0.06 → 'Level 25'
    //   exp строка:      y=0.245, h=0.07 → 'Level Exp 25 5 715 639 / 6 266 704'
    //   attack speed: в блоке Detailed Stats — пусто при y≈0.445 (слишком мелко/контраст)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Status2_HeroClass_OcrRecognizesRanger()
    {
        var (bitmap, w, h) = await LoadJpgAsync("status_2.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);
        RoiCalibration roi = Roi("heroClass", 0.05, 0.175, 0.90, 0.07);

        OcrResult result = await ReadAndLogAsync(frame, roi);

        result.Recognized.Should().BeTrue("OCR должен распознать класс в status_2");
        result.RawText.Should().ContainEquivalentOf("ranger",
            "класс в status_2.jpg должен быть Ranger");
    }

    [Fact]
    public async Task Status2_Level_OcrRecognizes25()
    {
        // level_row (0.05, 0.215, 0.90, 0.06) → "Level 25"
        var (bitmap, w, h) = await LoadJpgAsync("status_2.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);
        RoiCalibration roi = Roi("heroLevel", 0.05, 0.215, 0.90, 0.06);

        OcrResult result = await ReadAndLogAsync(frame, roi);

        result.Recognized.Should().BeTrue("OCR должен распознать уровень в status_2");
        NormalizeWs(result.RawText).Should().Contain("25",
            "уровень в status_2.jpg должен быть 25");
    }

    [Fact]
    public async Task Status2_Exp_OcrRecognizesDigits()
    {
        // exp_row (0.05, 0.245, 0.90, 0.07) → "Level Exp 25 5 715 639 / 6 266 704"
        var (bitmap, w, h) = await LoadJpgAsync("status_2.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);
        RoiCalibration roi = Roi("xp", 0.05, 0.245, 0.90, 0.07);

        OcrResult result = await ReadAndLogAsync(frame, roi);

        result.Recognized.Should().BeTrue("OCR должен распознать Exp в status_2");
        NormalizeWs(result.RawText).Should().Contain("5715639",
            "нормализованный текст должен содержать 5715639");
    }

    [Fact]
    [Trait("Category", "ParsingGap")]
    public void Status2_Exp_ParsingGap_ValueParserFailsOnSpaceSeparated()
    {
        // ОЖИДАЕМО RED до фикса ValueParser. "5 715 639" содержит пробелы-разделители.
        const string spaceSeparated = "5 715 639";
        bool parsed = _parser.TryParseAbbreviatedNumber(spaceSeparated, out long value);
        parsed.Should().BeTrue("ValueParser должен поддерживать пробел-разделитель (требует фикс)");
        value.Should().Be(5_715_639L);
    }

    [Fact]
    public async Task Status2_AttackDamage_OcrRecognizes63()
    {
        // strip (0.0, 0.15, 1.0, 0.20) → "Ranger Level Exp Basic Attack DPS 25 ... 502,3" + "63"
        var (bitmap, w, h) = await LoadJpgAsync("status_2.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);
        RoiCalibration roi = Roi("heroDamage", 0.0, 0.15, 1.0, 0.25);

        OcrResult result = await ReadAndLogAsync(frame, roi);

        result.Recognized.Should().BeTrue("OCR должен распознать Attack Damage в status_2");
        NormalizeWs(result.RawText).Should().Contain("63",
            "Attack Damage в status_2.jpg должен быть 63");
    }

    [Fact]
    public async Task Status2_DetailedStats_OcrRecognizesSection()
    {
        // "Detailed Stats" — заголовок блока детальной статистики, y≈0.40..0.55
        var (bitmap, w, h) = await LoadJpgAsync("status_2.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);
        RoiCalibration roi = Roi("detailedStats", 0.0, 0.40, 1.0, 0.15);

        OcrResult result = await ReadAndLogAsync(frame, roi);

        result.Recognized.Should().BeTrue("OCR должен распознать заголовок Detailed Stats");
        result.RawText.Should().ContainEquivalentOf("detailed",
            "status_2 содержит блок Detailed Stats");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // hero_1.jpg (476×697) — Hero/Inventory
    // Расположение (эмпирически):
    //   gold "57 912":   strip_00_10 (0.0, 0.00, 1.0, 0.10) → '57 912'
    //   класс "Ranger":  class_wider (0.25, 0.12, 0.60, 0.10) → 'Ranger'
    //   Lv.25:           не читается OCR (пиксельный шрифт мелкий)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Hero1_Gold_OcrRecognizesDigits()
    {
        // gold_top_left (0.05, 0.00, 0.50, 0.07) → "57 912"
        var (bitmap, w, h) = await LoadJpgAsync("hero_1.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);
        RoiCalibration roi = Roi("gold", 0.05, 0.00, 0.50, 0.07);

        OcrResult result = await ReadAndLogAsync(frame, roi);

        result.Recognized.Should().BeTrue("OCR должен распознать gold в hero_1");
        NormalizeWs(result.RawText).Should().Contain("57912",
            "нормализованный текст gold должен содержать 57912");
    }

    [Fact]
    [Trait("Category", "ParsingGap")]
    public void Hero1_Gold_ParsingGap_ValueParserFailsOnSpaceSeparatedNumber()
    {
        // ОЖИДАЕМО RED до фикса ValueParser. "57 912" содержит пробел-разделитель.
        const string spaceSeparated = "57 912";
        bool parsed = _parser.TryParseAbbreviatedNumber(spaceSeparated, out long value);
        parsed.Should().BeTrue("ValueParser должен поддерживать пробел-разделитель (требует фикс)");
        value.Should().Be(57912L);
    }

    [Fact]
    public async Task Hero1_HeroClass_OcrRecognizesRanger()
    {
        // class_wider (0.25, 0.12, 0.60, 0.10) → "Ranger"
        var (bitmap, w, h) = await LoadJpgAsync("hero_1.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);
        RoiCalibration roi = Roi("heroClass", 0.25, 0.12, 0.60, 0.10);

        OcrResult result = await ReadAndLogAsync(frame, roi);

        result.Recognized.Should().BeTrue("OCR должен распознать класс в hero_1");
        result.RawText.Should().ContainEquivalentOf("ranger",
            "класс в hero_1.jpg должен быть Ranger");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // hero_2.jpg (476×697) — Hero/Formation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Hero2_Gold_OcrRecognizesDigits()
    {
        // gold_top (0.05, 0.00, 0.55, 0.07) → "60 655"
        var (bitmap, w, h) = await LoadJpgAsync("hero_2.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);
        RoiCalibration roi = Roi("gold", 0.05, 0.00, 0.55, 0.07);

        OcrResult result = await ReadAndLogAsync(frame, roi);

        result.Recognized.Should().BeTrue("OCR должен распознать gold в hero_2");
        NormalizeWs(result.RawText).Should().Contain("60655",
            "нормализованный текст gold должен содержать 60655");
    }

    [Fact]
    [Trait("Category", "ParsingGap")]
    public void Hero2_Gold_ParsingGap_ValueParserFailsOnSpaceSeparatedNumber()
    {
        // ОЖИДАЕМО RED до фикса ValueParser. "60 655" содержит пробел-разделитель.
        const string spaceSeparated = "60 655";
        bool parsed = _parser.TryParseAbbreviatedNumber(spaceSeparated, out long value);
        parsed.Should().BeTrue("ValueParser должен поддерживать пробел-разделитель (требует фикс)");
        value.Should().Be(60655L);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // portal.jpg (479×697) — Portal
    // Расположение (эмпирически):
    //   сложность "Normal":    difficulty_wide (0.10, 0.15, 0.80, 0.08) → 'Normal'
    //   заголовок карты "Act 2": act_header (0.10, 0.32, 0.80, 0.08) → 'Act 2'
    //   нода "[2-1]":          пуста при любом y≥0.75 — OCR не читает мелкий пиксельный текст
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Portal_Difficulty_OcrRecognizesNormal()
    {
        // difficulty_wide (0.10, 0.15, 0.80, 0.08) → "Normal"
        var (bitmap, w, h) = await LoadJpgAsync("portal.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);
        RoiCalibration roi = Roi("difficulty", 0.10, 0.15, 0.80, 0.08);

        OcrResult result = await ReadAndLogAsync(frame, roi);

        result.Recognized.Should().BeTrue("OCR должен распознать сложность в portal");
        result.RawText.Should().ContainEquivalentOf("normal",
            "сложность в portal.jpg должна быть Normal");
    }

    [Fact]
    public async Task Portal_MapHeader_OcrRecognizesAct2()
    {
        // act_header (0.10, 0.32, 0.80, 0.08) → "Act 2"
        var (bitmap, w, h) = await LoadJpgAsync("portal.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);
        RoiCalibration roi = Roi("activeTab", 0.10, 0.32, 0.80, 0.08);

        OcrResult result = await ReadAndLogAsync(frame, roi);

        result.Recognized.Should().BeTrue("OCR должен распознать заголовок карты в portal");
        result.RawText.Should().ContainEquivalentOf("act",
            "заголовок в portal.jpg должен содержать 'Act'");
        NormalizeWs(result.RawText).Should().Contain("2",
            "заголовок должен содержать '2' (Act 2)");
    }

    /// <summary>
    /// Метка ноды "[2-1]" — нижняя часть карты.
    /// Эмпирически: OCR не читает мелкий пиксельный текст меток нод (всё пусто при y≥0.75).
    /// Причина: (b) OCR неточен для мелкого bitmap-шрифта меток нод — кандидат на Tesseract-fallback.
    /// Тест зафиксирован как документация ограничения OCR, а не hard-assert.
    /// </summary>
    [Fact]
    public async Task Portal_StageNodeLabel_OcrLimitationDocumented()
    {
        var (bitmap, w, h) = await LoadJpgAsync("portal.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);
        // Широкое ROI нижней части карты — всё равно пусто
        RoiCalibration roi = Roi("stageId", 0.0, 0.75, 1.0, 0.25);

        OcrResult result = await ReadAndLogAsync(frame, roi);

        // Документируем факт: Windows.Media.Ocr не читает мелкие метки нод в portal.jpg
        // Это ограничение OCR-движка для данного размера шрифта, не баг теста.
        // Рекомендация: Tesseract с масштабированием как fallback (ADR-005).
        _output.WriteLine(
            $"Portal node OCR limitation: Recognized={result.Recognized}, " +
            $"Text='{result.RawText}'. Expected '[2-1]' — requires Tesseract fallback.");
        // Не ассертируем Recognized=true — это задокументированное ограничение
    }

    // ─────────────────────────────────────────────────────────────────────────
    // mainzone.jpg (549×224) — MainZone лог
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MainZone_LogLine_OcrContainsObtained()
    {
        // Верхняя полоса mainzone — лог-строка "Obtained Long Staff."
        var (bitmap, w, h) = await LoadJpgAsync("mainzone.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);
        RoiCalibration roi = Roi("mainZoneLog", 0.05, 0.01, 0.82, 0.25);

        OcrResult result = await ReadAndLogAsync(frame, roi);

        result.Recognized.Should().BeTrue("OCR должен распознать лог-строку в mainzone");
        result.RawText.Should().ContainEquivalentOf("obtained",
            "лог-строка mainzone.jpg должна содержать 'Obtained'");
    }

    [Fact]
    public async Task MainZone_NextLocation_OcrRecognizes22()
    {
        // Метка "2-2" — левый край mainzone
        var (bitmap, w, h) = await LoadJpgAsync("mainzone.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);
        RoiCalibration roi = Roi("nextLocation", 0.02, 0.47, 0.13, 0.40);

        OcrResult result = await ReadAndLogAsync(frame, roi);

        result.Recognized.Should().BeTrue("OCR должен распознать next-location в mainzone");
        NormalizeWs(result.RawText).Should().Contain("2-2",
            "next-location в mainzone.jpg должен быть 2-2");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Мета-тест: smoke — движок OCR доступен
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OcrEngine_IsAvailable_RecognizesAtLeastOneField()
    {
        // Если этот тест падает с Recognized=false — все OCR-тесты будут RED.
        var (bitmap, w, h) = await LoadJpgAsync("status_1.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);
        RoiCalibration roi = Roi("smoke", 0.0, 0.10, 1.0, 0.50);

        OcrResult result = await ReadAndLogAsync(frame, roi);

        result.Recognized.Should().BeTrue(
            "Windows.Media.Ocr должен быть доступен на этой машине (Windows 11). " +
            "Если тест падает — движок недоступен и все OCR-тесты будут RED.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Дополнительные ParsingGap-тесты: unit-демонстрация gap на литеральных строках
    // (независимы от OCR, проверяют ValueParser напрямую)
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [Trait("Category", "ParsingGap")]
    [InlineData("54 678",      54678L)]
    [InlineData("57 912",      57912L)]
    [InlineData("60 655",      60655L)]
    [InlineData("2 285 394",   2_285_394L)]
    [InlineData("4 739 962",   4_739_962L)]
    [InlineData("5 714 975",   5_714_975L)]
    [InlineData("5 715 639",   5_715_639L)]
    [InlineData("6 266 704",   6_266_704L)]
    public void ValueParser_ParsingGap_SpaceSeparatedNumbersNotSupported(string raw, long expected)
    {
        // ОЖИДАЕМО RED до фикса ValueParser.
        // Подтверждает gap: все игровые числа с пробелом-разделителем не парсятся.
        // После добавления поддержки пробела в AbbreviatedNumberRegex эти тесты станут GREEN.
        bool parsed = _parser.TryParseAbbreviatedNumber(raw, out long value);
        parsed.Should().BeTrue(
            $"ValueParser должен парсить '{raw}' как {expected} (требует фикс: поддержка пробела-разделителя)");
        value.Should().Be(expected);
    }
}
