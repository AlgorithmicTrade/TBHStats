// T052 — замер латентности установившегося режима: «кадр + OCR одной ROI».
//
// Методика:
//   1. Warmup: 2 прогревочных вызова ReadAsync ВНЕ замера (ленивая инициализация WinOcrEngine —
//              её стоимость не должна входить в показатель установившейся латентности).
//   2. Замер: N_ITERATIONS = 10 вызовов ReadAsync, время через Stopwatch per-call.
//   3. CapturedFrame загружается один раз до цикла (загрузка JPG — фикстурный оверхед,
//              в реальном потоке кадр уже в памяти из WGC).
//   4. Метрики: min, max, median, p95 в мс; вывод через ITestOutputHelper.
//   5. Ассерт — regression bound median < 1000 мс (generous, стабилен на CI/слабой машине).
//              SC-цель <150 мс логируется отдельно в аутпуте для визуального сравнения.
//
// SC-цель (spec): латентность одного прохода «захват кадра + OCR одной ROI» < ~150 мс.
// Ассерт 1000 мс — защита от грубой регрессии; 150 мс — эмпирический ориентир,
// не жёсткий CI-gate (Windows.Media.Ocr на медленном CI может выйти за 150 мс без регрессии).
//
// Idle CPU и возобновление ≤5с НЕ покрываются — требуют живого приложения (T051).

using System.Diagnostics;
using FluentAssertions;
using TBHStats.Capture;
using TBHStats.Capture.Ocr;
using TBHStats.Capture.WindowTracking;
using TBHStats.Core.Models;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Xunit;
using Xunit.Abstractions;

namespace TBHStats.Capture.Tests;

/// <summary>
/// Тесты латентности установившегося режима OcrReader.ReadAsync (T052).
/// Измеряет N вызовов ReadAsync после warmup, логирует min/max/median/p95.
/// </summary>
public sealed class OcrLatencyTests
{
    // ── константы измерения ────────────────────────────────────────────────────

    /// <summary>Число прогревочных вызовов (инициализация WinOcrEngine).</summary>
    private const int WarmupCount = 2;

    /// <summary>Число измерительных итераций.</summary>
    private const int MeasureCount = 10;

    /// <summary>
    /// Generous regression bound для median (мс).
    /// SC-цель <150 мс; 1000 мс — защита от грубой регрессии на CI/слабой машине.
    /// </summary>
    private const double RegressionBoundMedianMs = 1000.0;

    /// <summary>
    /// SC-цель из spec (мс). Используется только для логирования — не жёсткий ассерт.
    /// </summary>
    private const double ScTargetMs = 150.0;

    // ── зависимости ───────────────────────────────────────────────────────────

    private readonly OcrReader _ocr = new();
    private readonly ITestOutputHelper _output;

    public OcrLatencyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ── вспомогательные методы ────────────────────────────────────────────────

    /// <summary>
    /// Загружает JPG-фикстуру из выходной папки тестов (fixtures/) в SoftwareBitmap.
    /// </summary>
    private static async Task<(SoftwareBitmap Bitmap, int Width, int Height)> LoadJpgAsync(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", fileName);
        File.Exists(path).Should().BeTrue($"фикстура '{fileName}' должна существовать в fixtures/");

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

    /// <summary>
    /// Вычисляет медиану и p95 из отсортированного списка значений.
    /// </summary>
    private static (double Median, double P95) ComputePercentiles(double[] sortedMs)
    {
        int n = sortedMs.Length;
        double median = n % 2 == 0
            ? (sortedMs[n / 2 - 1] + sortedMs[n / 2]) / 2.0
            : sortedMs[n / 2];

        // p95: индекс ceil(0.95 * n) - 1, но не выходим за границу
        int p95Idx = (int)Math.Ceiling(0.95 * n) - 1;
        p95Idx = Math.Clamp(p95Idx, 0, n - 1);
        double p95 = sortedMs[p95Idx];

        return (median, p95);
    }

    private void LogLatencyStats(string label, double[] sortedMs)
    {
        (double median, double p95) = ComputePercentiles(sortedMs);
        double min = sortedMs[0];
        double max = sortedMs[^1];

        _output.WriteLine($"=== OcrReader.ReadAsync latency ({label}) ===");
        _output.WriteLine($"  Iterations : {sortedMs.Length}");
        _output.WriteLine($"  Min        : {min:F1} ms");
        _output.WriteLine($"  Max        : {max:F1} ms");
        _output.WriteLine($"  Median     : {median:F1} ms");
        _output.WriteLine($"  p95        : {p95:F1} ms");
        _output.WriteLine($"  SC target  : < {ScTargetMs} ms  (эмпирический ориентир из spec)");
        _output.WriteLine($"  Met SC?    : {(median < ScTargetMs ? "YES" : "NO (выше 150 мс, не регрессия CI)")}");
        _output.WriteLine($"  Regression bound (ассерт): median < {RegressionBoundMedianMs} ms");
    }

    // ── тест 1: установившаяся латентность ReadAsync (основной ассерт) ────────

    /// <summary>
    /// Основной тест латентности: измеряет установившуюся латентность OcrReader.ReadAsync
    /// по одной числовой ROI (gold в hero_1.jpg) после warmup.
    ///
    /// Загрузка кадра из JPG выполняется один раз до цикла — фикстурный оверхед.
    /// В замер включён только ReadAsync (crop + OCR), что соответствует реальному
    /// установившемуся режиму (кадр приходит из WGC уже в памяти).
    ///
    /// Ассерт: median < 1000 мс (regression bound).
    /// SC-цель spec: < 150 мс — логируется для визуального сравнения.
    /// </summary>
    [Fact]
    public async Task ReadAsync_SteadyState_MedianBelowRegressionBound()
    {
        // Arrange — загружаем кадр один раз до замера
        var (bitmap, w, h) = await LoadJpgAsync("hero_1.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);

        // gold ROI в hero_1.jpg (эмпирически выверена в T049)
        RoiCalibration roi = Roi("gold", 0.05, 0.00, 0.50, 0.07);

        // Warmup — инициализируем WinOcrEngine вне замера
        _output.WriteLine($"Выполняем {WarmupCount} warmup-итераций (инициализация WinOcrEngine)...");
        for (int i = 0; i < WarmupCount; i++)
        {
            OcrResult warmupResult = await _ocr.ReadAsync(frame, roi, CancellationToken.None);
            _output.WriteLine($"  warmup [{i + 1}]: Recognized={warmupResult.Recognized}, " +
                              $"Text='{warmupResult.RawText}'");
        }

        // Act — замеряем установившуюся латентность
        _output.WriteLine($"\nЗамеряем {MeasureCount} итераций...");
        double[] elapsedMs = new double[MeasureCount];
        Stopwatch sw = new();

        for (int i = 0; i < MeasureCount; i++)
        {
            sw.Restart();
            OcrResult result = await _ocr.ReadAsync(frame, roi, CancellationToken.None);
            sw.Stop();

            elapsedMs[i] = sw.Elapsed.TotalMilliseconds;
            _output.WriteLine($"  iter [{i + 1,2}]: {elapsedMs[i]:F1} ms  " +
                              $"Recognized={result.Recognized}  Text='{result.RawText}'");
        }

        // Статистика
        double[] sorted = elapsedMs.Order().ToArray();
        LogLatencyStats("hero_1.jpg / gold ROI", sorted);

        (double median, double p95) = ComputePercentiles(sorted);

        // Assert — regression bound (generous для CI/слабой машины)
        median.Should().BeLessThan(RegressionBoundMedianMs,
            $"медиана латентности OcrReader.ReadAsync ({median:F1} мс) должна быть ниже " +
            $"regression bound {RegressionBoundMedianMs} мс. SC-цель spec: < {ScTargetMs} мс.");
    }

    // ── тест 2: установившаяся латентность на status_1.jpg / xp ROI ──────────

    /// <summary>
    /// Повторяет замер на другой фикстуре (status_1.jpg, xp ROI) для проверки
    /// инвариантности латентности относительно размера ROI и содержимого кадра.
    /// </summary>
    [Fact]
    public async Task ReadAsync_SteadyState_StatusFixture_MedianBelowRegressionBound()
    {
        // Arrange
        var (bitmap, w, h) = await LoadJpgAsync("status_1.jpg");
        using CapturedFrame frame = MakeFrame(bitmap, w, h);

        // Широкий ROI strip (level+exp), эмпирически выверен в T049
        RoiCalibration roi = Roi("levelExp", 0.0, 0.15, 1.0, 0.20);

        // Warmup
        _output.WriteLine($"Warmup {WarmupCount}x для status_1.jpg / levelExp ROI...");
        for (int i = 0; i < WarmupCount; i++)
            await _ocr.ReadAsync(frame, roi, CancellationToken.None);

        // Act
        double[] elapsedMs = new double[MeasureCount];
        Stopwatch sw = new();

        for (int i = 0; i < MeasureCount; i++)
        {
            sw.Restart();
            OcrResult result = await _ocr.ReadAsync(frame, roi, CancellationToken.None);
            sw.Stop();

            elapsedMs[i] = sw.Elapsed.TotalMilliseconds;
            _output.WriteLine($"  iter [{i + 1,2}]: {elapsedMs[i]:F1} ms  " +
                              $"Recognized={result.Recognized}");
        }

        double[] sorted = elapsedMs.Order().ToArray();
        LogLatencyStats("status_1.jpg / levelExp ROI", sorted);

        (double median, _) = ComputePercentiles(sorted);

        // Assert
        median.Should().BeLessThan(RegressionBoundMedianMs,
            $"медиана латентности ({median:F1} мс) должна быть ниже regression bound " +
            $"{RegressionBoundMedianMs} мс. SC-цель spec: < {ScTargetMs} мс.");
    }

    // ── тест 3 (справочный): полный цикл загрузка-JPG + OCR ──────────────────

    /// <summary>
    /// Справочный тест: измеряет полный цикл «загрузка JPG из файла + OCR».
    /// В основной ассерт NOT включён (это не установившийся режим —
    /// в реальности кадр из WGC уже в памяти). Логируется для справки.
    ///
    /// Ассерт: полный цикл завершается менее чем за 5000 мс (smoke, против полного зависания).
    /// </summary>
    [Fact]
    public async Task ReadAsync_FullCycleWithJpgLoad_CompletesWithinSmokeBound()
    {
        RoiCalibration roi = Roi("gold", 0.05, 0.00, 0.50, 0.07);

        // Warmup вне замера (инициализация движка)
        {
            var (bmpW, wW, hW) = await LoadJpgAsync("hero_1.jpg");
            using CapturedFrame warmupFrame = MakeFrame(bmpW, wW, hW);
            for (int i = 0; i < WarmupCount; i++)
                await _ocr.ReadAsync(warmupFrame, roi, CancellationToken.None);
        }

        // Замер полного цикла (включая загрузку JPG)
        const int fullCycleCount = 5;
        double[] elapsedMs = new double[fullCycleCount];
        Stopwatch sw = new();

        for (int i = 0; i < fullCycleCount; i++)
        {
            sw.Restart();

            var (bitmap, width, height) = await LoadJpgAsync("hero_1.jpg");
            using CapturedFrame frame = MakeFrame(bitmap, width, height);
            await _ocr.ReadAsync(frame, roi, CancellationToken.None);

            sw.Stop();
            elapsedMs[i] = sw.Elapsed.TotalMilliseconds;
            _output.WriteLine($"  full-cycle [{i + 1}]: {elapsedMs[i]:F1} ms");
        }

        double[] sorted = elapsedMs.Order().ToArray();
        (double median, double p95) = ComputePercentiles(sorted);

        _output.WriteLine($"\n=== Полный цикл (JPG load + OCR) ===");
        _output.WriteLine($"  Iterations : {sorted.Length}");
        _output.WriteLine($"  Min        : {sorted[0]:F1} ms");
        _output.WriteLine($"  Max        : {sorted[^1]:F1} ms");
        _output.WriteLine($"  Median     : {median:F1} ms");
        _output.WriteLine($"  p95        : {p95:F1} ms");
        _output.WriteLine($"  [Справочно] Загрузка JPG включена в замер. " +
                          $"В реальном потоке кадр в памяти — этот overhead отсутствует.");

        // Smoke-ассерт: полный цикл не должен зависать (5000 мс)
        const double smokeBoundMs = 5000.0;
        median.Should().BeLessThan(smokeBoundMs,
            $"полный цикл (JPG + OCR) медиана {median:F1} мс должна быть ниже smoke bound {smokeBoundMs} мс");
    }
}
