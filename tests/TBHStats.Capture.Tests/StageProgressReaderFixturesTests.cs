// ADR-024 — Визуальный детектор прогрессбара этапа по цвету заливки (StageProgressReader).
//
// Четыре теста на реальных фикстурах (без моков). Тестируют StageProgressReader напрямую.
//
// Фикстуры (screenshots/*.jpg, копируются в fixtures/ при сборке):
//   1. progress_begin.jpg (554×243): пустой тёмный трек. Ожидание: Progress≈0.0, BossPresent=false.
//   2. progress_half.jpg (548×247): фиолетовый правый участок бара (~50%). Ожидание: Progress∈[0.35..0.65], BossPresent=false.
//   3. progress_full.jpg (559×236): бар полностью фиолетовый. Ожидание: Progress≥0.85, BossPresent=false.
//   4. progress_stagebossfight.jpg (552×247): бар полностью синий. Ожидание: BossPresent=true, Progress=1.0.
//
// ROI откалиброваны по реальным пикселям каждой фикстуры (см. столбцовый дамп ниже).
// Ключевой инвариант: ROI по ширине = ПОЛНЫЙ трек бара (тёмный + цветной),
// иначе purpleFraction = purpleColumns/roiWidth будет неверной.
//
// begin  (554×243): тёмный трек x=475..543, y=206..211 → x=475/554, w=69/554, y=206/243, h=6/243
//   purpleColumns=0, darkColumns=69 → purpleFraction=0 → progress=0.0
//
// half   (548×247): трек x=469..540, y=208..213 → x=469/548, w=72/548, y=208/247, h=6/247
//   тёмных x=469..503 (35 col), фиолетовых x=504..540 (37 col)
//   purpleFraction=37/72≈0.514 → progress=0.514×0.95≈0.488
//
// full   (559×236): трек x=479..550, y=198..203 → x=479/559, w=72/559, y=198/236, h=6/236
//   тёмных x=479..481 (3 col), фиолетовых x=482..550 (69 col)
//   purpleFraction=69/72≈0.958 → clamp → progress=0.95
//
// boss   (552×247): синий x=475..543, y=206..212 → x=475/552, w=69/552, y=206/247, h=7/247
//   blueColumns=69/69 → blueFraction≈1.0 ≥ 0.20 → BossPresent=true, Progress=1.0

using FluentAssertions;
using TBHStats.Capture.Progress;
using TBHStats.Capture.WindowTracking;
using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Xunit;
using Xunit.Abstractions;

namespace TBHStats.Capture.Tests;

/// <summary>
/// Харнесс визуального детектора прогрессбара этапа (ADR-024) на четырёх реальных фикстурах.
/// Проверяет детекцию пустого, частичного, полного прогресса и боссового боя. Без моков.
/// </summary>
public sealed class StageProgressReaderFixturesTests
{
    private readonly StageProgressReader _sut = new();
    private readonly ITestOutputHelper _output;
    private readonly GameMechanicsConfig _cfg = GameMechanicsConfig.CreateDefault();

    public StageProgressReaderFixturesTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ── ROI для progress_begin.jpg (554×243) ──────────────────────────────────
    // Тёмный трек x=475..543 (69 col), y=206..211 (6 rows).
    // purpleColumns=0 → purpleFraction=0.0 → progress=0.0
    private static readonly RoiCalibration BeginRoi = MakeRoi(
        x: 475.0 / 554.0,
        y: 206.0 / 243.0,
        w:  69.0 / 554.0,
        h:   6.0 / 243.0);

    // ── ROI для progress_half.jpg (548×247) ───────────────────────────────────
    // Полный трек x=469..540 (72 col), y=208..213 (6 rows).
    // Тёмных 35 col (x=469..503), фиолетовых 37 col (x=504..540).
    // purpleFraction=37/72≈0.514 → progress≈0.488
    private static readonly RoiCalibration HalfRoi = MakeRoi(
        x: 469.0 / 548.0,
        y: 208.0 / 247.0,
        w:  72.0 / 548.0,
        h:   6.0 / 247.0);

    // ── ROI для progress_full.jpg (559×236) ───────────────────────────────────
    // Трек x=479..550 (72 col), y=198..203 (6 rows).
    // Тёмных 3 col (x=479..481), фиолетовых 69 col (x=482..550).
    // purpleFraction=69/72≈0.958 → clamp → progress=0.95
    private static readonly RoiCalibration FullRoi = MakeRoi(
        x: 479.0 / 559.0,
        y: 198.0 / 236.0,
        w:  72.0 / 559.0,
        h:   6.0 / 236.0);

    // ── ROI для progress_stagebossfight.jpg (552×247) ─────────────────────────
    // Синий трек x=475..543 (69 col), y=206..212 (7 rows).
    // blueColumns=69 → blueFraction≈1.0 ≥ 0.20 → BossPresent=true, Progress=1.0
    private static readonly RoiCalibration BossRoi = MakeRoi(
        x: 475.0 / 552.0,
        y: 206.0 / 247.0,
        w:  69.0 / 552.0,
        h:   7.0 / 247.0);

    // ── Тест 1: пустой бар → Progress≈0.0, BossPresent=false ────────────────

    /// <summary>
    /// progress_begin.jpg: тёмный трек, фиолетового нет → Progress должен быть ≤ 0.1, BossPresent=false.
    /// </summary>
    [Fact]
    public async Task Begin_EmptyBar_ProgressNearZero_NoBoss()
    {
        (SoftwareBitmap bitmap, int w, int h) = await LoadJpgAsync("progress_begin.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);

        StageProgressReading result = await _sut.ReadAsync(frame, BeginRoi, _cfg, CancellationToken.None);

        _output.WriteLine($"[progress_begin.jpg] Progress={result.Progress:F4}, BossPresent={result.BossPresent}");

        result.Progress.Should().NotBeNull("ROI валидна и накрывает реальный трек бара");
        result.BossPresent.Should().NotBeNull("ROI валидна и накрывает реальный трек бара");

        result.Progress!.Value.Should().BeInRange(0.0, 0.10,
            "бар пуст (все колонки тёмные) → purpleFraction=0 → progress=0.0");
        result.BossPresent!.Value.Should().Be(false,
            "синего нет в пустом баре");
    }

    // ── Тест 2: половина бара фиолетовая → Progress ∈ [0.35..0.65], BossPresent=false ──

    /// <summary>
    /// progress_half.jpg: ~37/72 колонок фиолетовые → Progress ≈ 0.49, BossPresent=false.
    /// </summary>
    [Fact]
    public async Task Half_PurpleFill_ProgressAroundHalf_NoBoss()
    {
        (SoftwareBitmap bitmap, int w, int h) = await LoadJpgAsync("progress_half.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);

        StageProgressReading result = await _sut.ReadAsync(frame, HalfRoi, _cfg, CancellationToken.None);

        _output.WriteLine($"[progress_half.jpg] Progress={result.Progress:F4}, BossPresent={result.BossPresent}");

        result.Progress.Should().NotBeNull("ROI валидна");
        result.BossPresent.Should().NotBeNull("ROI валидна");

        result.Progress!.Value.Should().BeInRange(0.35, 0.65,
            "около половины колонок фиолетовые → purpleFraction≈0.51 → progress≈0.49");
        result.BossPresent!.Value.Should().Be(false,
            "синего нет — заливка фиолетовая (путь этапа)");
    }

    // ── Тест 3: бар полностью фиолетовый → Progress≥0.85, BossPresent=false ──

    /// <summary>
    /// progress_full.jpg: ~69/72 колонок фиолетовые → Progress=0.95 (clamped), BossPresent=false.
    /// </summary>
    [Fact]
    public async Task Full_AllPurple_ProgressNear095_NoBoss()
    {
        (SoftwareBitmap bitmap, int w, int h) = await LoadJpgAsync("progress_full.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);

        StageProgressReading result = await _sut.ReadAsync(frame, FullRoi, _cfg, CancellationToken.None);

        _output.WriteLine($"[progress_full.jpg] Progress={result.Progress:F4}, BossPresent={result.BossPresent}");

        result.Progress.Should().NotBeNull("ROI валидна");
        result.BossPresent.Should().NotBeNull("ROI валидна");

        result.Progress!.Value.Should().BeInRange(0.85, 0.95,
            "69/72 колонок фиолетовые → purpleFraction≈0.958 → clamped progress=0.95");
        result.BossPresent!.Value.Should().Be(false,
            "синего нет — бар полностью фиолетовый, бой с боссом ещё не начался");
    }

    // ── Тест 4: бар синий (бой с боссом) → BossPresent=true, Progress=0.95 ───

    /// <summary>
    /// progress_stagebossfight.jpg: все 69 колонок синие → BossPresent=true, Progress=0.95
    /// (этап не пройден, пока босс не убит; 100% — только в момент завершения).
    /// </summary>
    [Fact]
    public async Task BossFight_BlueBar_BossPresentTrue_Progress095()
    {
        (SoftwareBitmap bitmap, int w, int h) = await LoadJpgAsync("progress_stagebossfight.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);

        StageProgressReading result = await _sut.ReadAsync(frame, BossRoi, _cfg, CancellationToken.None);

        _output.WriteLine($"[progress_stagebossfight.jpg] Progress={result.Progress:F4}, BossPresent={result.BossPresent}");

        result.Progress.Should().NotBeNull("ROI валидна");
        result.BossPresent.Should().NotBeNull("ROI валидна");

        result.BossPresent!.Value.Should().Be(true,
            "синий бар → blueFraction≈1.0 ≥ порог 0.20 → BossPresent=true");
        result.Progress!.Value.Should().BeApproximately(0.95, 0.001,
            "при бое с боссом этап ещё не пройден → детектор держит Progress=0.95");
    }

    // ── вспомогательные ──────────────────────────────────────────────────────

    private static RoiCalibration MakeRoi(double x, double y, double w, double h)
        => new()
        {
            FieldKey  = "stageProgress",
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
}
