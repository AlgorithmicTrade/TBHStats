using FluentAssertions;
using TBHStats.Capture.Roi;
using TBHStats.Capture.WindowTracking;
using TBHStats.Core.Models;
using Xunit;

namespace TBHStats.Capture.Tests;

public sealed class RoiMapperTests
{
    private readonly RoiMapper _sut = new();

    // ──────────────────────────────────────────────────────────────────────────
    // 1. Базовое преобразование ToPixels(double, ...)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ToPixels_NormalizedRoi_ReturnsExpectedPixels()
    {
        // roi {X=0.5, Y=0.5, W=0.25, H=0.1} на 800×600
        // px = Round(0.5*800)=400, py = Round(0.5*600)=300,
        // pw = Round(0.25*800)=200, ph = Round(0.1*600)=60
        var result = _sut.ToPixels(0.5, 0.5, 0.25, 0.1, new SizePx(800, 600));

        result.X.Should().Be(400);
        result.Y.Should().Be(300);
        result.Width.Should().Be(200);
        result.Height.Should().Be(60);
    }

    [Fact]
    public void ToPixels_OriginZeroFullSize_ReturnsFullFrame()
    {
        // X=0, Y=0, W=1, H=1 → должен занять весь кадр
        var result = _sut.ToPixels(0.0, 0.0, 1.0, 1.0, new SizePx(1920, 1080));

        result.X.Should().Be(0);
        result.Y.Should().Be(0);
        result.Width.Should().Be(1920);
        result.Height.Should().Be(1080);
    }

    [Theory]
    [InlineData(0.5, 800,  400)]  // 0.5 * 800 = 400.0 → 400
    [InlineData(0.5, 600,  300)]  // 0.5 * 600 = 300.0 → 300
    [InlineData(0.333, 900, 300)] // 0.333 * 900 = 299.7 → Round(299.7, AwayFromZero)=300
    [InlineData(0.1,   600,  60)] // 0.1 * 600 = 60.0 → 60
    public void ToPixels_RoundAwayFromZero_RoundsCorrectly(double fraction, int dimension, int expected)
    {
        // Используем только X-компонент: Y=0, W=fraction, H=1 → смотрим Width
        var result = _sut.ToPixels(0.0, 0.0, fraction, 1.0, new SizePx(dimension, dimension));

        result.Width.Should().Be(expected);
    }

    [Fact]
    public void ToPixels_MidpointRounding_AwayFromZero()
    {
        // 0.5 * 3 = 1.5 → AwayFromZero = 2 (не банкирское округление = 2)
        var result = _sut.ToPixels(0.5, 0.5, 0.5, 0.5, new SizePx(3, 3));

        // px = Round(1.5) = 2, pw = Round(1.5) = 2, pw clamp = min(2, 3-2)=1
        // Важно: проверяем именно X (до клампинга pw)
        result.X.Should().Be(2);
        result.Y.Should().Be(2);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. Инвариантность к масштабу (ключевой тест R3/FR-005b)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ToPixels_ScaleInvariance_800x600_vs_1600x1200()
    {
        // Удвоение размера → удвоение пиксельных координат (в пределах округления ±1px)
        const double x = 0.25, y = 0.3, w = 0.4, h = 0.2;
        var small = _sut.ToPixels(x, y, w, h, new SizePx(800, 600));
        var large = _sut.ToPixels(x, y, w, h, new SizePx(1600, 1200));

        Math.Abs(large.X      - small.X      * 2).Should().BeLessThanOrEqualTo(1);
        Math.Abs(large.Y      - small.Y      * 2).Should().BeLessThanOrEqualTo(1);
        Math.Abs(large.Width  - small.Width  * 2).Should().BeLessThanOrEqualTo(1);
        Math.Abs(large.Height - small.Height * 2).Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void ToPixels_ScaleInvariance_1920x1080_vs_2560x1440()
    {
        // Стандартные игровые разрешения: одни и те же нормализованные доли
        // дают пропорциональные (с точностью до округления) пиксели
        const double x = 0.1, y = 0.05, w = 0.3, h = 0.15;
        var fhd = _sut.ToPixels(x, y, w, h, new SizePx(1920, 1080));
        var qhd = _sut.ToPixels(x, y, w, h, new SizePx(2560, 1440));

        // Нормализованная позиция X/Width должна совпадать с исходной долей ±1px/size
        double xRatioFhd  = (double)fhd.X     / 1920;
        double xRatioQhd  = (double)qhd.X     / 2560;
        double wRatioFhd  = (double)fhd.Width  / 1920;
        double wRatioQhd  = (double)qhd.Width  / 2560;

        xRatioFhd.Should().BeApproximately(xRatioQhd,  precision: 1.0 / 1920);
        wRatioFhd.Should().BeApproximately(wRatioQhd,  precision: 1.0 / 1920);
    }

    [Theory]
    [InlineData(400,  300,  2)]  // 800×600  → 1600×1200 (factor=2)
    [InlineData(960,  540,  2)]  // 1920×1080 → 3840×2160 (factor=2)
    public void ToPixels_ScaleInvariance_DoubledResolution_CoordinatesDouble(
        int baseWidth, int baseHeight, int factor)
    {
        const double x = 0.2, y = 0.4, w = 0.3, h = 0.1;
        var baseResult   = _sut.ToPixels(x, y, w, h, new SizePx(baseWidth, baseHeight));
        var scaledResult = _sut.ToPixels(x, y, w, h, new SizePx(baseWidth * factor, baseHeight * factor));

        Math.Abs(scaledResult.X      - baseResult.X      * factor).Should().BeLessThanOrEqualTo(factor);
        Math.Abs(scaledResult.Y      - baseResult.Y      * factor).Should().BeLessThanOrEqualTo(factor);
        Math.Abs(scaledResult.Width  - baseResult.Width  * factor).Should().BeLessThanOrEqualTo(factor);
        Math.Abs(scaledResult.Height - baseResult.Height * factor).Should().BeLessThanOrEqualTo(factor);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. Клампинг выходящих за пределы координат
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ToPixels_NegativeX_ClampsToZero()
    {
        // x < 0 → Clamp01 → 0 → px = 0
        var result = _sut.ToPixels(-0.1, 0.0, 0.5, 0.5, new SizePx(800, 600));

        result.X.Should().Be(0);
        result.X.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void ToPixels_XPlusWExceedsFrame_WidthClampedToRemainingSpace()
    {
        // X=0.9, W=0.5 → после Clamp01: x=0.9, w=0.5
        // px = Round(0.9*800)=720, pw = Round(0.5*800)=400
        // pw clamp: min(400, 800-720)=80
        var result = _sut.ToPixels(0.9, 0.0, 0.5, 1.0, new SizePx(800, 600));

        result.X.Should().Be(720);
        result.Width.Should().Be(80);   // усечено, не 400
        result.Right.Should().BeLessThanOrEqualTo(800);
    }

    [Fact]
    public void ToPixels_YPlusHExceedsFrame_HeightClampedToRemainingSpace()
    {
        // Y=0.9, H=0.5 → py = Round(0.9*600)=540, ph clamp: min(Round(0.5*600), 600-540)=60
        var result = _sut.ToPixels(0.0, 0.9, 1.0, 0.5, new SizePx(800, 600));

        result.Y.Should().Be(540);
        result.Height.Should().Be(60);  // усечено, не 300
        result.Bottom.Should().BeLessThanOrEqualTo(600);
    }

    [Fact]
    public void ToPixels_XGreaterThanOne_ClampsXToFrameWidth()
    {
        // x > 1 → Clamp01 → 1.0 → px = cw, pw clamp → 0
        var result = _sut.ToPixels(1.5, 0.0, 0.5, 0.5, new SizePx(800, 600));

        result.X.Should().Be(800);
        result.Width.Should().Be(0);    // px=cw → cw-px=0
        result.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ToPixels_NegativeW_ClampsWidthToZero()
    {
        // w < 0 → Clamp01 → 0 → pw = 0
        var result = _sut.ToPixels(0.1, 0.1, -0.3, 0.5, new SizePx(800, 600));

        result.Width.Should().Be(0);
        result.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ToPixels_AllCoordinatesOutOfBounds_DoesNotThrow()
    {
        // Никаких исключений при любых значениях за пределами [0..1]
        var act = () => _sut.ToPixels(-10.0, -10.0, 100.0, 100.0, new SizePx(800, 600));

        act.Should().NotThrow();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. Пустой размер SizePx.Empty
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ToPixels_EmptyClientSize_ReturnsEmptyRect()
    {
        var result = _sut.ToPixels(0.5, 0.5, 0.25, 0.1, SizePx.Empty);

        result.Should().Be(RoiPixelRect.Empty);
        result.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ToPixels_EmptyClientSize_DoesNotThrow()
    {
        var act = () => _sut.ToPixels(0.5, 0.5, 0.25, 0.1, SizePx.Empty);

        act.Should().NotThrow();
    }

    [Fact]
    public void ToPixels_ZeroWidthClientSize_ReturnsEmptyRect()
    {
        // Width=0 → IsNonEmpty=false → Empty
        var result = _sut.ToPixels(0.5, 0.5, 0.25, 0.1, new SizePx(0, 600));

        result.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ToPixels_ZeroHeightClientSize_ReturnsEmptyRect()
    {
        // Height=0 → IsNonEmpty=false → Empty
        var result = _sut.ToPixels(0.5, 0.5, 0.25, 0.1, new SizePx(800, 0));

        result.IsEmpty.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. Round-trip ToPixels → ToNormalized
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_ToPixelsThenToNormalized_ReturnsSimilarFractions()
    {
        // «Круглые» доли на большом размере: погрешность ≤ 1/size
        const double x = 0.25, y = 0.5, w = 0.5, h = 0.25;
        var size = new SizePx(1000, 1000);

        var pixels = _sut.ToPixels(x, y, w, h, size);
        var (nx, ny, nw, nh) = _sut.ToNormalized(pixels, size);

        double tolerance = 1.0 / 1000; // 1 пиксель

        nx.Should().BeApproximately(x, tolerance);
        ny.Should().BeApproximately(y, tolerance);
        nw.Should().BeApproximately(w, tolerance);
        nh.Should().BeApproximately(h, tolerance);
    }

    [Fact]
    public void RoundTrip_ExactFractions_ReturnsPreciseNormalized()
    {
        // Доли, для которых умножение на размер даёт целое число → round-trip без потерь
        // X=0.1, Y=0.2, W=0.3, H=0.4 на 100×100 → px=10,py=20,pw=30,ph=40 (целые)
        var pixels = _sut.ToPixels(0.1, 0.2, 0.3, 0.4, new SizePx(100, 100));
        var (nx, ny, nw, nh) = _sut.ToNormalized(pixels, new SizePx(100, 100));

        nx.Should().BeApproximately(0.1, precision: 1e-10);
        ny.Should().BeApproximately(0.2, precision: 1e-10);
        nw.Should().BeApproximately(0.3, precision: 1e-10);
        nh.Should().BeApproximately(0.4, precision: 1e-10);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. ToPixels(RoiCalibration, ...) — null-проверка и делегирование
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ToPixels_NullRoiCalibration_ThrowsArgumentNullException()
    {
        RoiCalibration? nullRoi = null;

        var act = () => _sut.ToPixels(nullRoi!, new SizePx(800, 600));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToPixels_RoiCalibration_DelegatesToRawOverload()
    {
        // ToPixels(RoiCalibration, ...) должен давать тот же результат, что и ToPixels(double,...)
        var roi = new RoiCalibration { X = 0.5, Y = 0.5, W = 0.25, H = 0.1 };
        var size = new SizePx(800, 600);

        var fromCalibration = _sut.ToPixels(roi, size);
        var fromRaw         = _sut.ToPixels(roi.X, roi.Y, roi.W, roi.H, size);

        fromCalibration.Should().Be(fromRaw);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. ToNormalized — граничные случаи
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ToNormalized_EmptyClientSize_ReturnsAllZeros()
    {
        var rect = new RoiPixelRect(100, 100, 200, 150);
        var result = _sut.ToNormalized(rect, SizePx.Empty);

        result.X.Should().Be(0.0);
        result.Y.Should().Be(0.0);
        result.W.Should().Be(0.0);
        result.H.Should().Be(0.0);
    }

    [Fact]
    public void ToNormalized_KnownPixels_ReturnsCorrectFractions()
    {
        // rect(400,300,200,60) на 800×600 → (0.5, 0.5, 0.25, 0.1)
        var rect = new RoiPixelRect(400, 300, 200, 60);
        var (x, y, w, h) = _sut.ToNormalized(rect, new SizePx(800, 600));

        x.Should().BeApproximately(0.5,  precision: 1e-10);
        y.Should().BeApproximately(0.5,  precision: 1e-10);
        w.Should().BeApproximately(0.25, precision: 1e-10);
        h.Should().BeApproximately(0.1,  precision: 1e-10);
    }

    [Fact]
    public void ToNormalized_ZeroOriginFullRect_ReturnsUnityFractions()
    {
        // rect(0,0,1920,1080) на 1920×1080 → (0, 0, 1, 1)
        var rect = new RoiPixelRect(0, 0, 1920, 1080);
        var (x, y, w, h) = _sut.ToNormalized(rect, new SizePx(1920, 1080));

        x.Should().Be(0.0);
        y.Should().Be(0.0);
        w.Should().Be(1.0);
        h.Should().Be(1.0);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. RoiPixelRect.IsEmpty — граничные случаи
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ToPixels_ValidRoi_ResultIsNotEmpty()
    {
        var result = _sut.ToPixels(0.1, 0.1, 0.5, 0.5, new SizePx(800, 600));

        result.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void ToPixels_VerySmallRoiOnSmallFrame_DoesNotThrow()
    {
        // W и H настолько маленькие, что округление даёт 0 → IsEmpty, но без исключения
        var act = () => _sut.ToPixels(0.0, 0.0, 0.001, 0.001, new SizePx(10, 10));

        act.Should().NotThrow();
    }
}
