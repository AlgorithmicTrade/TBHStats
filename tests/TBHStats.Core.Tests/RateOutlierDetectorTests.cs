namespace TBHStats.Core.Tests;

using FluentAssertions;
using TBHStats.Core.Optimization;
using Xunit;

/// <summary>
/// Регрессионные тесты для <see cref="RateOutlierDetector.IsXpRateOutlier"/>.
/// Реализация уже существует — все тесты ожидаемо ЗЕЛЁНЫЕ.
/// Покрывает: отсутствие базы (null/0/отрицательная EMA → false), нормальный темп ниже порога,
/// точная граница (строгое >), floor-доминирование, factor-доминирование,
/// реальные события из лога (одиночный misread OCR и смена этапа).
/// Порог: max(EMA × OutlierFactor, OutlierFloorPerHour) = max(EMA × 6, 5_000_000).
/// </summary>
public sealed class RateOutlierDetectorTests
{
    // =========================================================================
    // НЕ выброс (ожидание false)
    // =========================================================================

    /// <summary>
    /// Тест 1: EMA = null → нет базы для суждения → false.
    /// </summary>
    [Fact]
    public void IsXpRateOutlier_EmaNullHighRaw_ReturnsFalse()
    {
        // Arrange
        double rawXpPerHour = 100_000_000.0;
        double? emaXpPerHour = null;

        // Act
        bool result = RateOutlierDetector.IsXpRateOutlier(rawXpPerHour, emaXpPerHour);

        // Assert
        result.Should().BeFalse(
            because: "emaXpPerHour=null означает «база ещё не установлена»; порог max(EMA×6, 5M) не вычислим → false");
    }

    /// <summary>
    /// Тест 2: EMA = 0.0 → EMA ≤ 0 → нет базы → false.
    /// </summary>
    [Fact]
    public void IsXpRateOutlier_EmaZeroHighRaw_ReturnsFalse()
    {
        // Arrange
        double rawXpPerHour = 100_000_000.0;
        double? emaXpPerHour = 0.0;

        // Act
        bool result = RateOutlierDetector.IsXpRateOutlier(rawXpPerHour, emaXpPerHour);

        // Assert
        result.Should().BeFalse(
            because: "emaXpPerHour=0.0 — EMA ≤ 0, база не установлена (старт/после сброса) → false");
    }

    /// <summary>
    /// Тест 3: EMA = -5.0 (отрицательная) → EMA ≤ 0 → нет базы → false.
    /// </summary>
    [Fact]
    public void IsXpRateOutlier_EmaNegativeHighRaw_ReturnsFalse()
    {
        // Arrange
        double rawXpPerHour = 100_000_000.0;
        double? emaXpPerHour = -5.0;

        // Act
        bool result = RateOutlierDetector.IsXpRateOutlier(rawXpPerHour, emaXpPerHour);

        // Assert
        result.Should().BeFalse(
            because: "emaXpPerHour=-5.0 — отрицательная EMA означает сброс/некорректное состояние; EMA ≤ 0 → false");
    }

    /// <summary>
    /// Тест 4: EMA=1_000_000, raw=3_000_000 → threshold=max(6M,5M)=6M → 3M &lt; 6M → false.
    /// Factor доминирует над floor; raw well within normal range.
    /// </summary>
    [Theory]
    [InlineData(3_000_000.0,   1_000_000.0)]   // threshold = max(6M, 5M) = 6M; 3M < 6M
    [InlineData(4_000_000.0,     500_000.0)]   // threshold = max(3M, 5M) = 5M; 4M < 5M  (floor доминирует)
    public void IsXpRateOutlier_RawBelowThreshold_ReturnsFalse(double rawXpPerHour, double emaXpPerHour)
    {
        // Act
        bool result = RateOutlierDetector.IsXpRateOutlier(rawXpPerHour, emaXpPerHour);

        // Assert
        result.Should().BeFalse(
            because: $"raw={rawXpPerHour:N0}, ema={emaXpPerHour:N0}: " +
                     $"threshold=max(EMA×{RateOutlierDetector.OutlierFactor}, {RateOutlierDetector.OutlierFloorPerHour:N0}); raw не превышает порог строго → false");
    }

    /// <summary>
    /// Тест 6: Точная граница — raw ровно равен threshold → строгое > не выполняется → false.
    /// EMA=2_000_000, raw=12_000_000 → threshold=max(12M,5M)=12M → 12M НЕ > 12M.
    /// </summary>
    [Fact]
    public void IsXpRateOutlier_RawExactlyAtThreshold_ReturnsFalse()
    {
        // Arrange
        double ema = 2_000_000.0;
        double raw = ema * RateOutlierDetector.OutlierFactor; // 12_000_000

        // Act
        bool result = RateOutlierDetector.IsXpRateOutlier(raw, ema);

        // Assert
        result.Should().BeFalse(
            because: $"raw={raw:N0} == threshold=max(EMA×6, 5M)={raw:N0}; условие строгое (>), равенство не является выбросом → false");
    }

    // =========================================================================
    // ВЫБРОС (ожидание true)
    // =========================================================================

    /// <summary>
    /// Тесты 7–8: raw строго выше threshold — factor-доминирование и floor-доминирование.
    /// </summary>
    [Theory]
    [InlineData(13_000_000.0,  2_000_000.0)]   // threshold=max(12M,5M)=12M; 13M > 12M (factor)
    [InlineData( 6_000_000.0,    500_000.0)]   // threshold=max(3M,5M)=5M;  6M > 5M  (floor)
    public void IsXpRateOutlier_RawAboveThreshold_ReturnsTrue(double rawXpPerHour, double emaXpPerHour)
    {
        // Act
        bool result = RateOutlierDetector.IsXpRateOutlier(rawXpPerHour, emaXpPerHour);

        // Assert
        result.Should().BeTrue(
            because: $"raw={rawXpPerHour:N0}, ema={emaXpPerHour:N0}: " +
                     $"threshold=max(EMA×{RateOutlierDetector.OutlierFactor}, {RateOutlierDetector.OutlierFloorPerHour:N0}); raw строго превышает порог → выброс");
    }

    /// <summary>
    /// Тест 9: Реальное событие из лога #1 — одиночный OCR-misread XP mid-run.
    /// EMA=1_771_702, raw=186_677_763 → ratio≈105× — явный выброс.
    /// </summary>
    [Fact]
    public void IsXpRateOutlier_RealLogEvent1_MidRunMisread_ReturnsTrue()
    {
        // Arrange
        double ema = 1_771_702.0;
        double raw = 186_677_763.0;
        // threshold = max(1_771_702 × 6, 5_000_000) = max(10_630_212, 5_000_000) = 10_630_212
        // 186_677_763 >> 10_630_212 → выброс

        // Act
        bool result = RateOutlierDetector.IsXpRateOutlier(raw, ema);

        // Assert
        result.Should().BeTrue(
            because: $"реальный лог #1: raw={raw:N0} ≈ {raw / ema:N0}× EMA={ema:N0}; " +
                     $"threshold=max(EMA×6, 5M)={Math.Max(ema * RateOutlierDetector.OutlierFactor, RateOutlierDetector.OutlierFloorPerHour):N0}; " +
                     $"одиночный OCR-misread XP даёт ложную дельту, которая должна быть отброшена как выброс");
    }

    /// <summary>
    /// Тест 10: Реальное событие из лога #2 — переходный всплеск при смене этапа/панели.
    /// EMA=741_177, raw=388_235_990 → ratio≈524× — явный выброс.
    /// </summary>
    [Fact]
    public void IsXpRateOutlier_RealLogEvent2_StageChangeMisread_ReturnsTrue()
    {
        // Arrange
        double ema = 741_177.0;
        double raw = 388_235_990.0;
        // threshold = max(741_177 × 6, 5_000_000) = max(4_447_062, 5_000_000) = 5_000_000
        // 388_235_990 >> 5_000_000 → выброс

        // Act
        bool result = RateOutlierDetector.IsXpRateOutlier(raw, ema);

        // Assert
        result.Should().BeTrue(
            because: $"реальный лог #2: raw={raw:N0} ≈ {raw / ema:N0}× EMA={ema:N0}; " +
                     $"threshold=max(EMA×6, 5M)={Math.Max(ema * RateOutlierDetector.OutlierFactor, RateOutlierDetector.OutlierFloorPerHour):N0}; " +
                     $"переходный всплеск при смене этапа/панели должен быть отброшен как выброс");
    }
}
