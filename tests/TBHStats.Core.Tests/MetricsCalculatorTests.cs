namespace TBHStats.Core.Tests;

using FluentAssertions;
using TBHStats.Core.Models;
using TBHStats.Core.Optimization;
using Xunit;

/// <summary>
/// TDD red-тесты для <see cref="MetricsCalculator"/>.
/// Реализация — заглушка (бросает <see cref="NotImplementedException"/>),
/// поэтому все тесты в этом файле ожидаемо ПАДАЮТ до T025.
/// Назначение: зафиксировать ожидаемое поведение FR-006, FR-005a, data-model §Валидация.
/// </summary>
public sealed class MetricsCalculatorTests
{
    private readonly MetricsCalculator _sut = new();

    /// <summary>Базовый UTC-момент для всех тестов. Фиксирован — детерминизм гарантирован.</summary>
    private static readonly DateTime BaseUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // =========================================================================
    // Краевые случаи: пустой и единственный список
    // =========================================================================

    /// <summary>Пустой список → все темпы 0, словарь сундуков пустой.</summary>
    [Fact]
    public void ComputeLiveRates_EmptyList_ReturnsZeroRates()
    {
        // Arrange
        var samples = Array.Empty<MetricSample>();

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        result.GoldPerHour.Should().BeApproximately(0.0, 1e-6);
        result.XpPerHour.Should().BeApproximately(0.0, 1e-6);
        result.ChestPerHourByType.Should().BeEmpty();
    }

    /// <summary>Единственный сэмпл → нет интервала → все темпы 0, словарь сундуков пустой.</summary>
    [Fact]
    public void ComputeLiveRates_SingleSample_ReturnsZeroRates()
    {
        // Arrange
        var samples = new[]
        {
            MakeSample(BaseUtc, gold: 10_000, xp: 500, xpToLevel: 1000, level: 5)
        };

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        result.GoldPerHour.Should().BeApproximately(0.0, 1e-6);
        result.XpPerHour.Should().BeApproximately(0.0, 1e-6);
        result.ChestPerHourByType.Should().BeEmpty();
    }

    // =========================================================================
    // Золото/час — happy path
    // =========================================================================

    /// <summary>
    /// +3600 золота за ровно 3600 секунд → 3600 золото/час.
    /// Проверяет базовую формулу delta/elapsed*3600.
    /// </summary>
    [Fact]
    public void ComputeLiveRates_GoldIncreaseOver3600Seconds_Returns3600GoldPerHour()
    {
        // Arrange
        var samples = new[]
        {
            MakeSample(BaseUtc,                   gold: 10_000),
            MakeSample(BaseUtc.AddSeconds(3600),  gold: 13_600)
        };

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        result.GoldPerHour.Should().BeApproximately(3600.0, 1e-6);
    }

    /// <summary>
    /// +1000 золота за 1800 секунд → 2000 золото/час.
    /// Проверяет дробный расчёт темпа.
    /// </summary>
    [Fact]
    public void ComputeLiveRates_GoldIncreaseOver1800Seconds_Returns2000GoldPerHour()
    {
        // Arrange
        var samples = new[]
        {
            MakeSample(BaseUtc,                   gold: 0),
            MakeSample(BaseUtc.AddSeconds(1800),  gold: 1_000)
        };

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        result.GoldPerHour.Should().BeApproximately(2000.0, 1e-6);
    }

    /// <summary>
    /// Три сэмпла, два интервала: +1800 за 1800 с и +3600 за 3600 с.
    /// Совокупный темп = (1800+3600)/(1800+3600)*3600 = 3600 золото/час.
    /// </summary>
    [Fact]
    public void ComputeLiveRates_ThreeSamples_AggregatesIntervalsCorrectly()
    {
        // Arrange
        // Интервал 1: +1800 золота за 1800 с → темп 3600/час
        // Интервал 2: +3600 золота за 3600 с → темп 3600/час
        // Суммарно: (+1800+3600)/(1800+3600)*3600 = 5400/5400*3600 = 3600/час
        var samples = new[]
        {
            MakeSample(BaseUtc,                   gold: 0),
            MakeSample(BaseUtc.AddSeconds(1800),  gold: 1_800),
            MakeSample(BaseUtc.AddSeconds(5400),  gold: 5_400)
        };

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        result.GoldPerHour.Should().BeApproximately(3600.0, 1e-6);
    }

    /// <summary>
    /// Убывание Gold между двумя сэмплами не даёт отрицательный вклад —
    /// только положительные дельты суммируются.
    /// Результат: только положительный вклад учитывается.
    /// </summary>
    [Fact]
    public void ComputeLiveRates_GoldDecreases_NegativeDeltaIsIgnored()
    {
        // Arrange
        // Интервал 1: gold 0 → 3600 (+3600 за 3600 с) → вклад +3600
        // Интервал 2: gold 3600 → 1000 (убывание) → вклад 0 (игнорируется)
        // Суммарный elapsed = 7200 с; суммарная положительная дельта = 3600
        // Темп = 3600/7200*3600 = 1800 золото/час
        var samples = new[]
        {
            MakeSample(BaseUtc,                   gold: 0),
            MakeSample(BaseUtc.AddSeconds(3600),  gold: 3_600),
            MakeSample(BaseUtc.AddSeconds(7200),  gold: 1_000)
        };

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        result.GoldPerHour.Should().BeApproximately(1800.0, 1e-6);
    }

    /// <summary>
    /// Если Gold null в одном из сэмплов — пара пропускается, не влияет на знаменатель.
    /// </summary>
    [Fact]
    public void ComputeLiveRates_GoldNullInOneSample_PairIsSkippedForGold()
    {
        // Arrange
        // Пара 1: null → 3600 — gold не определён у первого, пара пропускается
        // Пара 2: 3600 → 7200 (+3600 за 3600 с) → 3600 золото/час
        var samples = new[]
        {
            MakeSample(BaseUtc,                   gold: null),
            MakeSample(BaseUtc.AddSeconds(3600),  gold: 3_600),
            MakeSample(BaseUtc.AddSeconds(7200),  gold: 7_200)
        };

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        result.GoldPerHour.Should().BeApproximately(3600.0, 1e-6);
    }

    // =========================================================================
    // Опыт/час — без level-up
    // =========================================================================

    /// <summary>
    /// +7200 Xp за 3600 секунд, уровень не меняется → 7200 Xp/час.
    /// </summary>
    [Fact]
    public void ComputeLiveRates_XpIncreaseNoLevelUp_ReturnsCorrectXpPerHour()
    {
        // Arrange
        var samples = new[]
        {
            MakeSample(BaseUtc,                   xp: 0,    xpToLevel: 10_000, level: 3),
            MakeSample(BaseUtc.AddSeconds(3600),  xp: 7_200, xpToLevel: 10_000, level: 3)
        };

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        result.XpPerHour.Should().BeApproximately(7200.0, 1e-6);
    }

    // =========================================================================
    // Опыт/час — с level-up (FR-005a)
    // =========================================================================

    /// <summary>
    /// Level-up между двумя сэмплами:
    /// a.Xp=8000, a.XpToLevel=10000, b.Xp=2000, b.HeroLevel = a.HeroLevel+1.
    /// Дельта = (10000−8000) + 2000 = 4000 Xp за 3600 с → 4000 Xp/час.
    /// </summary>
    [Fact]
    public void ComputeLiveRates_SingleLevelUp_CompensatesXpResetCorrectly()
    {
        // Arrange
        var samples = new[]
        {
            MakeSample(BaseUtc,                  xp: 8_000, xpToLevel: 10_000, level: 5),
            MakeSample(BaseUtc.AddSeconds(3600), xp: 2_000, xpToLevel: 12_000, level: 6)
        };
        // Ожидаемая дельта = (10000-8000) + 2000 = 4000 Xp за 3600 с = 4000 Xp/час

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        result.XpPerHour.Should().BeApproximately(4000.0, 1e-6);
    }

    /// <summary>
    /// Убывание Xp без смены уровня — должно игнорироваться
    /// (только положительные дельты; уровень не вырос, значит это sanity-событие,
    /// а не level-up → дельта = 0, не отрицательный вклад).
    /// </summary>
    [Fact]
    public void ComputeLiveRates_XpDecreaseWithoutLevelChange_NegativeDeltaIsIgnored()
    {
        // Arrange
        // Интервал 1: xp 0→5000, level 5→5 (+5000 за 3600 с)
        // Интервал 2: xp 5000→2000, level 5→5 (убывание без level-up → игнорируем)
        // Суммарный elapsed = 7200; суммарная положит. дельта = 5000
        // Темп = 5000/7200*3600 = 2500 Xp/час
        var samples = new[]
        {
            MakeSample(BaseUtc,                   xp: 0,     xpToLevel: 10_000, level: 5),
            MakeSample(BaseUtc.AddSeconds(3600),  xp: 5_000, xpToLevel: 10_000, level: 5),
            MakeSample(BaseUtc.AddSeconds(7200),  xp: 2_000, xpToLevel: 10_000, level: 5)
        };

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        result.XpPerHour.Should().BeApproximately(2500.0, 1e-6);
    }

    /// <summary>
    /// Два интервала с level-up в каждом, оба near-full (a.Xp ≥ 0.8·xpToLevel):
    /// Интервал 1: a.Xp=9000, a.XpToLevel=10000 (0.9) → nearFull ✓ → дельта=(10000-9000)+9800=10800
    /// Интервал 2: a.Xp=9800, a.XpToLevel=12000 (0.817) → nearFull ✓ → дельта=(12000-9800)+1200=3400
    /// Суммарно: (10800+3400)/7200*3600 = 14200/7200*3600 ≈ 7100 Xp/час.
    /// </summary>
    [Fact]
    public void ComputeLiveRates_TwoConsecutiveLevelUps_AccumulatesXpCorrectly()
    {
        // Arrange
        // Интервал 1: a.Xp=9000/10000 (0.90 ≥ 0.8) → level-up компенсация применяется
        //   дельта = (10000−9000) + 9800 = 10800
        // Интервал 2: a.Xp=9800/12000 (0.817 ≥ 0.8) → level-up компенсация применяется
        //   дельта = (12000−9800) + 1200 = 3400
        // Темп = (10800+3400)/7200*3600 = 14200/7200*3600 ≈ 7100 Xp/час
        var samples = new[]
        {
            MakeSample(BaseUtc,                   xp: 9_000, xpToLevel: 10_000, level: 5),
            MakeSample(BaseUtc.AddSeconds(3600),  xp: 9_800, xpToLevel: 12_000, level: 6),
            MakeSample(BaseUtc.AddSeconds(7200),  xp: 1_200, xpToLevel: 15_000, level: 7)
        };

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        // Интервал 1: nearFull(9000 ≥ 8000) → (10000−9000)+9800 = 10800
        // Интервал 2: nearFull(9800 ≥ 9600) → (12000−9800)+1200 = 3400
        double expectedDelta = (10_000 - 9_000 + 9_800) + (12_000 - 9_800 + 1_200);
        double expectedRate  = expectedDelta / 7200.0 * 3600.0;
        result.XpPerHour.Should().BeApproximately(expectedRate, 1e-6);
    }

    // =========================================================================
    // Периоды недоступности / большой разрыв (FR-005a)
    // =========================================================================

    /// <summary>
    /// Большой разрыв между двумя надёжными сэмплами (окно было свёрнуто 1 час).
    /// Темп = дельта золота / реальный elapsed. Idle-игра накапливала за это время.
    /// Нулевые промежуточные сэмплы НЕ домысливаются.
    /// +3600 золота за 7200 секунд (два часа) → 1800 золото/час.
    /// </summary>
    [Fact]
    public void ComputeLiveRates_LargeGapBetweenSamples_UsesRealElapsedTime()
    {
        // Arrange
        var samples = new[]
        {
            MakeSample(BaseUtc,                   gold: 0),
            // Разрыв 7200 с (2 часа) — окно было свёрнуто
            MakeSample(BaseUtc.AddSeconds(7200),  gold: 3_600)
        };

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        result.GoldPerHour.Should().BeApproximately(1800.0, 1e-6);
    }

    // =========================================================================
    // Сундуки/час — по типам (ChestPerHourByType)
    // =========================================================================

    /// <summary>
    /// Один тип сундуков, счётчик растёт: 0→4 за 3600 с → 4 сундука/час.
    /// </summary>
    [Fact]
    public void ComputeLiveRates_SingleChestTypeIncreasing_ReturnsCorrectRate()
    {
        // Arrange
        var samples = new[]
        {
            MakeSample(BaseUtc,                  chests: new[] { (typeId: 1, count: 0) }),
            MakeSample(BaseUtc.AddSeconds(3600), chests: new[] { (typeId: 1, count: 4) })
        };

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        result.ChestPerHourByType.Should().ContainKey(1);
        result.ChestPerHourByType[1].Should().BeApproximately(4.0, 1e-6);
    }

    /// <summary>
    /// Два типа сундуков, независимые темпы:
    /// TypeId=1: 0→2 за 3600 с → 2/час; TypeId=2: 0→6 за 3600 с → 6/час.
    /// </summary>
    [Fact]
    public void ComputeLiveRates_TwoChestTypes_EachHasIndependentRate()
    {
        // Arrange
        var samples = new[]
        {
            MakeSample(BaseUtc,
                chests: new[] { (typeId: 1, count: 0), (typeId: 2, count: 0) }),
            MakeSample(BaseUtc.AddSeconds(3600),
                chests: new[] { (typeId: 1, count: 2), (typeId: 2, count: 6) })
        };

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        result.ChestPerHourByType.Should().ContainKey(1);
        result.ChestPerHourByType.Should().ContainKey(2);
        result.ChestPerHourByType[1].Should().BeApproximately(2.0, 1e-6);
        result.ChestPerHourByType[2].Should().BeApproximately(6.0, 1e-6);
    }

    /// <summary>
    /// Открытие сундуков (падение Count) посередине не даёт отрицательный вклад.
    /// Счётчик: 0→4→1 (открыт, осталось 1). Учитывается только дельта 0→4 (+4).
    /// Суммарно за 7200 с: (+4+0) / 7200 * 3600 = 2 сундука/час.
    /// </summary>
    [Fact]
    public void ComputeLiveRates_ChestCountDropsAfterOpen_NegativeDeltaIgnored()
    {
        // Arrange
        // Интервал 1: 0→4 → вклад +4
        // Интервал 2: 4→1 → убывание, вклад 0
        var samples = new[]
        {
            MakeSample(BaseUtc,                  chests: new[] { (typeId: 1, count: 0) }),
            MakeSample(BaseUtc.AddSeconds(3600), chests: new[] { (typeId: 1, count: 4) }),
            MakeSample(BaseUtc.AddSeconds(7200), chests: new[] { (typeId: 1, count: 1) })
        };
        // Темп = 4 / 7200 * 3600 = 2 сундука/час

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        result.ChestPerHourByType.Should().ContainKey(1);
        result.ChestPerHourByType[1].Should().BeApproximately(2.0, 1e-6);
    }

    /// <summary>
    /// Счётчик растёт, потом обнуляется (открытие), потом снова растёт.
    /// 0→3 (+3), 3→0 (открытие, 0), 0→6 (+6). Суммарно за 3 интервала (3×3600с).
    /// Темп = (3+0+6) / 10800 * 3600 = 9/10800*3600 = 3 сундука/час.
    /// </summary>
    [Fact]
    public void ComputeLiveRates_ChestOpenAndRefillPattern_SumsOnlyPositiveDeltas()
    {
        // Arrange
        var samples = new[]
        {
            MakeSample(BaseUtc,                  chests: new[] { (typeId: 2, count: 0) }),
            MakeSample(BaseUtc.AddSeconds(3600), chests: new[] { (typeId: 2, count: 3) }),
            MakeSample(BaseUtc.AddSeconds(7200), chests: new[] { (typeId: 2, count: 0) }),
            MakeSample(BaseUtc.AddSeconds(10800),chests: new[] { (typeId: 2, count: 6) })
        };
        // Делта 1: +3; Дельта 2: -3 → 0; Дельта 3: +6
        // Суммарная дельта = 9 за 10800 с → 9/10800*3600 = 3 сундука/час

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        result.ChestPerHourByType.Should().ContainKey(2);
        result.ChestPerHourByType[2].Should().BeApproximately(3.0, 1e-6);
    }

    /// <summary>
    /// Если ни в одном сэмпле нет данных по сундукам → ChestPerHourByType пустой.
    /// </summary>
    [Fact]
    public void ComputeLiveRates_NoChestData_ReturnsEmptyChestDictionary()
    {
        // Arrange
        var samples = new[]
        {
            MakeSample(BaseUtc,                  gold: 0),
            MakeSample(BaseUtc.AddSeconds(3600), gold: 3_600)
        };

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        result.ChestPerHourByType.Should().BeEmpty();
    }

    // =========================================================================
    // Комплексный сценарий: gold + xp + chests одновременно
    // =========================================================================

    /// <summary>
    /// Два интервала. Первый: gold+3600, xp без level-up +3600, chest type1 +2.
    /// Второй: gold+3600, level-up near-full (b.Xp=9000/10000=0.9 ≥ 0.8), chest type1 +2.
    /// Дельта xp интервал 1 = 3600−0 = 3600
    /// Дельта xp интервал 2 = (10000−9000)+500 = 1500 (near-full → компенсация применяется)
    /// Темп gold = (3600+3600)/7200*3600 = 3600/час.
    /// Темп xp = (3600+1500)/7200*3600 = 2550/час.
    /// Темп chest[1] = (2+2)/7200*3600 = 2/час.
    /// </summary>
    [Fact]
    public void ComputeLiveRates_MixedMetrics_AllRatesComputedIndependently()
    {
        // Arrange
        var t0 = BaseUtc;
        var t1 = BaseUtc.AddSeconds(3600);
        var t2 = BaseUtc.AddSeconds(7200);

        // Интервал 1: b.Xp=3600, b.XpToLevel=10000, b.Level=5 → нет level-up
        //   дельта xp = 3600−0 = 3600
        // Интервал 2: b.Xp=9000, b.XpToLevel=10000, c.Level=6 → level-up
        //   nearFull: 9000 ≥ 10000*0.8=8000 ✓ → компенсация применяется
        //   дельта xp = (10000−9000)+500 = 1500
        var samples = new[]
        {
            new MetricSample
            {
                TakenAtUtc = t0, IsReliable = true,
                Gold = 0, Xp = 0, XpToLevel = 10_000, HeroLevel = 5,
                Chests = new List<MetricSampleChest> { new() { ChestTypeId = 1, Count = 0 } }
            },
            new MetricSample
            {
                TakenAtUtc = t1, IsReliable = true,
                Gold = 3_600, Xp = 9_000, XpToLevel = 10_000, HeroLevel = 5,
                Chests = new List<MetricSampleChest> { new() { ChestTypeId = 1, Count = 2 } }
            },
            new MetricSample
            {
                TakenAtUtc = t2, IsReliable = true,
                Gold = 7_200, Xp = 500, XpToLevel = 15_000, HeroLevel = 6,
                Chests = new List<MetricSampleChest> { new() { ChestTypeId = 1, Count = 4 } }
            }
        };

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        result.GoldPerHour.Should().BeApproximately(3600.0, 1e-6,
            because: "суммарно +7200 золота за 7200 секунд = 3600/час");

        // Интервал 1: 9000−0 = 9000 (нет level-up)
        // Интервал 2: nearFull(9000 ≥ 8000) → (10000−9000)+500 = 1500
        double expectedXpDelta = 9_000 + ((10_000 - 9_000) + 500);
        double expectedXpRate  = expectedXpDelta / 7200.0 * 3600.0;
        result.XpPerHour.Should().BeApproximately(expectedXpRate, 1e-6,
            because: "второй интервал — near-full level-up, компенсируется через XpToLevel");

        result.ChestPerHourByType.Should().ContainKey(1);
        result.ChestPerHourByType[1].Should().BeApproximately(2.0, 1e-6,
            because: "+2 сундука за каждый интервал, суммарно +4 за 7200 с = 2/час");
    }

    // =========================================================================
    // Защита от OCR-выбросов: misread heroLevel при низком xp (near-full guard)
    // =========================================================================

    /// <summary>
    /// «Инкремент уровня» при низком a.Xp — misread heroLevel, а не реальный level-up.
    /// a.Xp=100/10000=0.01 (< 0.8) → nearFull ✗ → компенсация НЕ применяется.
    /// Дельта = b.Xp−a.Xp = 200−100 = 100 → темп = 100/3600*3600 = 100 Xp/час (НЕ ~10000).
    /// </summary>
    [Fact]
    public void ComputeLiveRates_LevelIncrementAtLowXp_NoCompensationApplied()
    {
        // Arrange
        // a.Xp=100, a.XpToLevel=10000 → ratio 0.01, значительно ниже 0.8 → nearFull=false
        // b.Level = a.Level+1 (misread heroLevel), b.Xp=200
        // Без компенсации: дельта = 200−100 = 100 → 100 Xp/час
        // С (неправильной) компенсацией было бы: (10000−100)+200 = 10100 → ~10100 Xp/час (выброс)
        var samples = new[]
        {
            MakeSample(BaseUtc,                  xp: 100,   xpToLevel: 10_000, level: 5),
            MakeSample(BaseUtc.AddSeconds(3600), xp: 200,   xpToLevel: 10_000, level: 6)
        };

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        // Компенсация не применяется → обычная дельта 200−100=100 за 3600 с = 100 Xp/час
        result.XpPerHour.Should().BeApproximately(100.0, 1e-6,
            because: "level+1 при a.Xp=100 (1% XpToLevel) — misread, компенсация level-up не применяется");
        // Проверяем явно, что НЕТ выброса ~10000 Xp/час
        result.XpPerHour.Should().BeLessThan(1000.0,
            because: "компенсация при низком xp инжектировала бы ~xpToLevel в числитель (выброс)");
    }

    /// <summary>
    /// Невозможное чтение: a.Xp > a.XpToLevel — OCR-мусор.
    /// Интервал с невозможным чтением полностью пропускается (не участвует ни в числителе, ни в знаменателе).
    /// Три сэмпла: интервал 1 валиден (+3600 xp за 3600 с), интервал 2 содержит невозможное чтение.
    /// Темп = только по валидному интервалу: 3600/3600*3600 = 3600 Xp/час.
    /// </summary>
    [Fact]
    public void ComputeLiveRates_ImpossibleXpReading_IntervalSkippedEntirely()
    {
        // Arrange
        // Интервал 1: a.Xp=0/10000 (валидно), b.Xp=3600/10000 (валидно) → дельта=3600
        // Интервал 2: b.Xp=3600/10000 (валидно), c.Xp=12000/10000 → c.Xp > c.XpToLevel (невозможно)
        //   → интервал пропускается полностью (xpElapsedSum не увеличивается)
        // Темп = 3600/3600*3600 = 3600 Xp/час (только первый интервал)
        var samples = new[]
        {
            MakeSample(BaseUtc,                   xp: 0,      xpToLevel: 10_000, level: 3),
            MakeSample(BaseUtc.AddSeconds(3600),  xp: 3_600,  xpToLevel: 10_000, level: 3),
            MakeSample(BaseUtc.AddSeconds(7200),  xp: 12_000, xpToLevel: 10_000, level: 3)
            //                                    ^^^ 12000 > 10000 → невозможное чтение, OCR-мусор
        };

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        // Только первый интервал валиден: 3600/3600*3600 = 3600 Xp/час
        result.XpPerHour.Should().BeApproximately(3600.0, 1e-6,
            because: "второй интервал (xp > xpToLevel) пропускается; темп считается только по первому");
    }

    /// <summary>
    /// Near-full level-up компенсируется корректно:
    /// a.Xp=9000, a.XpToLevel=10000 (0.9 ≥ 0.8) → nearFull ✓.
    /// b.Xp=500, b.Level=a.Level+1 → дельта = (10000−9000)+500 = 1500 за 3600 с = 1500 Xp/час.
    /// </summary>
    [Fact]
    public void ComputeLiveRates_NearFullLevelUp_CompensationApplied()
    {
        // Arrange
        // a.Xp=9000/10000=0.9 ≥ 0.8 → nearFull=true; levelUpByOne=true → компенсация применяется
        // дельта = (10000−9000)+500 = 1500 за 3600 с = 1500 Xp/час
        var samples = new[]
        {
            MakeSample(BaseUtc,                  xp: 9_000, xpToLevel: 10_000, level: 5),
            MakeSample(BaseUtc.AddSeconds(3600), xp: 500,   xpToLevel: 12_000, level: 6)
        };

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        // (10000−9000)+500 = 1500 за 3600 с = 1500 Xp/час
        result.XpPerHour.Should().BeApproximately(1500.0, 1e-6,
            because: "a.Xp=9000 — 90% от XpToLevel, nearFull=true, level-up компенсация корректна");
    }

    // =========================================================================
    // Guard: крупная дельта XP в не-level-up-ветке → исключение пары целиком
    // =========================================================================

    /// <summary>
    /// Смена героя на границе сэмплов порождает бессмысленную огромную дельту:
    /// b.Xp(новый герой) − a.Xp(старый герой) = 7 995 000 > a.XpToLevel 10 000.
    /// В не-level-up-ветке положительная дельта > a.XpToLevel — физически невозможна
    /// в пределах одного уровня и трактуется как разрыв: пара исключается ЦЕЛИКОМ
    /// (ни в числитель, ни в знаменатель xpElapsedSum).
    /// Засчитываются только: интервал A (5000 xp) и интервал B (7000 xp).
    /// xpDeltaSum=12_000, xpElapsedSum=7200 → XpPerHour ≈ 6000.0.
    /// </summary>
    [Fact]
    public void ComputeLiveRates_HeroSwitch_SpuriousCrossHeroDeltaExcluded()
    {
        // Arrange
        // t0 → t+3600: герой A, xp 0 → 5000 (дельта 5000, валидная)
        // t+3600 → t+7200: СМЕНА ГЕРОЯ; a.Xp=5000/10000, b.Xp=8_000_000/10_000_000
        //   levelUpByOne = false (50 ≠ 10+1) → не-level-up-ветка
        //   дельта = 7_995_000 > a.XpToLevel 10_000 → РАЗРЫВ, пара исключается целиком
        // t+7200 → t+10800: герой B, xp 8_000_000 → 8_007_000 (дельта 7000, валидная)
        var samples = new[]
        {
            MakeSample(BaseUtc,                   xp: 0,           xpToLevel: 10_000,     level: 10),
            MakeSample(BaseUtc.AddSeconds(3600),  xp: 5_000,       xpToLevel: 10_000,     level: 10),
            MakeSample(BaseUtc.AddSeconds(7200),  xp: 8_000_000,   xpToLevel: 10_000_000, level: 50),
            MakeSample(BaseUtc.AddSeconds(10800), xp: 8_007_000,   xpToLevel: 10_000_000, level: 50)
        };

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        // xpDeltaSum=5000+7000=12000, xpElapsedSum=3600+3600=7200 → 12000/7200*3600 = 6000.0
        result.XpPerHour.Should().BeApproximately(6000.0, 1e-6,
            because: "граничная межгеройская пара (дельта 7_995_000 > a.XpToLevel 10_000) исключается целиком; засчитаны только интервал A (+5000) и интервал B (+7000)");

        result.XpPerHour.Should().BeLessThan(1_000_000,
            because: "без guard'а граничная межгеройская дельта (~7.995e6) навсегда раздула бы числитель и дала бы ~2.67e6 xp/час вместо ~6000");
    }

    /// <summary>
    /// OCR-выброс или многоуровневый misread: уровень не изменился (не-level-up-ветка),
    /// но дельта xp = 77_000 > a.XpToLevel 10_000 — физически невозможна в пределах уровня.
    /// Пара исключается ЦЕЛИКОМ. Засчитывается только первый интервал (+2000 за 3600 с).
    /// xpDeltaSum=2000, xpElapsedSum=3600 → XpPerHour ≈ 2000.0.
    /// </summary>
    [Fact]
    public void ComputeLiveRates_WithinLevelGainExceedsXpToLevel_PairSkipped()
    {
        // Arrange
        // Интервал 1: a.Xp=1000, b.Xp=3000, level=5→5 → дельта 2000 ≤ a.XpToLevel 10000 → валидно
        // Интервал 2: a.Xp=3000/10000, b.Xp=80000/100000, level=5→5
        //   b.Xp ≤ b.XpToLevel (80000 ≤ 100000) → старая плаузибельность проходит,
        //   НО levelUpByOne=false → не-level-up-ветка
        //   дельта = 77_000 > a.XpToLevel 10_000 → РАЗРЫВ, пара исключается целиком
        var samples = new[]
        {
            MakeSample(BaseUtc,                   xp: 1_000,  xpToLevel: 10_000,  level: 5),
            MakeSample(BaseUtc.AddSeconds(3600),  xp: 3_000,  xpToLevel: 10_000,  level: 5),
            MakeSample(BaseUtc.AddSeconds(7200),  xp: 80_000, xpToLevel: 100_000, level: 5)
        };

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        // Только первый интервал: 2000/3600*3600 = 2000.0 xp/час
        result.XpPerHour.Should().BeApproximately(2000.0, 1e-6,
            because: "второй интервал (дельта 77_000 > a.XpToLevel 10_000, уровень не вырос) — разрыв, пара исключается целиком; темп считается только по первому валидному интервалу");
    }

    /// <summary>
    /// Регрессионный барьер: компенсированная level-up-ветка (nearFull + levelUpByOne)
    /// не затрагивается новым guard'ом, даже если компенсированная дельта > a.XpToLevel.
    /// a.Xp=9000/10000 (90% ≥ 80%) → nearFull ✓; b.Level=a.Level+1 → levelUpByOne ✓.
    /// Компенсированная дельта = (10_000−9_000)+9_800 = 10_800 > a.XpToLevel 10_000,
    /// но это ветка level-up — должна засчитываться.
    /// XpPerHour = 10_800/3600*3600 = 10_800.0.
    /// Этот тест ОБЯЗАН оставаться GREEN и до, и после добавления guard'а.
    /// </summary>
    [Fact]
    public void ComputeLiveRates_CompensatedLevelUpDeltaAboveXpToLevel_StillCounted()
    {
        // Arrange
        // a.Xp=9000, a.XpToLevel=10000 → nearFull ✓ (9000 ≥ 8000)
        // b.Level = a.Level+1 = 6 → levelUpByOne ✓
        // Компенсированная дельта = (10000−9000)+9800 = 10800 (> a.XpToLevel 10000, но это level-up ветка)
        var samples = new[]
        {
            MakeSample(BaseUtc,                   xp: 9_000, xpToLevel: 10_000, level: 5),
            MakeSample(BaseUtc.AddSeconds(3600),  xp: 9_800, xpToLevel: 12_000, level: 6)
        };

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        // Компенсированная ветка: дельта = (10000−9000)+9800 = 10800 за 3600 с = 10800 xp/час
        result.XpPerHour.Should().BeApproximately(10_800.0, 1e-6,
            because: "guard применяется ТОЛЬКО к не-level-up-ветке; компенсированная level-up-дельта (nearFull + levelUpByOne) засчитывается всегда, даже если она превышает a.XpToLevel");
    }

    // =========================================================================
    // TDD RED: структурный guard по уровню героя (late-game смена героя)
    // =========================================================================

    /// <summary>
    /// Смена героя в late-game: межгеройская дельта XP МЕНЬШЕ a.XpToLevel,
    /// поэтому существующий магнитудный guard НЕ срабатывает.
    /// Однако Δуровня = +15 (≠ 0 и ≠ +1) — структурный признак разрыва.
    ///
    /// Сэмплы (интервалы 3600 с):
    ///   t0:       xp=10_000_000, xpToLevel=200_000_000, level=80  (герой A)
    ///   t+3600:   xp=11_000_000, xpToLevel=200_000_000, level=80  (герой A: +1_000_000 за 3600 с)
    ///   t+7200:   xp=90_000_000, xpToLevel=300_000_000, level=95  (СМЕНА на героя B; Δlevel=+15)
    ///   t+10800:  xp=91_000_000, xpToLevel=300_000_000, level=95  (герой B: +1_000_000 за 3600 с)
    ///
    /// Граничная пара A2→B1 (t+3600 → t+7200):
    ///   xpDelta = 90M−11M = 79M < a.XpToLevel 200M → магнитудный guard НЕ ловит.
    ///   Δlevel = 95−80 = 15 ≠ 0 и ≠ +1 → структурный разрыв → ДОЛЖНА исключиться.
    ///
    /// Ожидание ПОСЛЕ фикса: засчитаны только интервал A (+1M/3600с) и B (+1M/3600с).
    ///   xpDeltaSum=2_000_000, xpElapsedSum=7200 → XpPerHour ≈ 1_000_000.0.
    ///
    /// TDD RED: до добавления структурного guard'а по уровню этот тест ПАДАЕТ —
    /// граничная пара проходит и даёт XpPerHour ≈ 27_000_000 вместо 1_000_000.
    /// </summary>
    [Fact]
    public void ComputeLiveRates_HeroSwitchInLateGame_LevelJumpExcluded()
    {
        // Arrange
        // Герой A: два сэмпла с реальной дельтой 1_000_000 за 3600 с
        // t+7200: смена на героя B; Δlevel = +15 → магнитудный guard не ловит (79M < 200M),
        //         но структурный guard по уровню должен исключить эту пару целиком
        // Герой B: один интервал с реальной дельтой 1_000_000 за 3600 с
        var samples = new[]
        {
            MakeSample(BaseUtc,                   xp: 10_000_000, xpToLevel: 200_000_000, level: 80),
            MakeSample(BaseUtc.AddSeconds(3600),  xp: 11_000_000, xpToLevel: 200_000_000, level: 80),
            MakeSample(BaseUtc.AddSeconds(7200),  xp: 90_000_000, xpToLevel: 300_000_000, level: 95),
            MakeSample(BaseUtc.AddSeconds(10800), xp: 91_000_000, xpToLevel: 300_000_000, level: 95)
        };

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        // После фикса: xpDeltaSum=2_000_000, xpElapsedSum=7200 → 2_000_000/7200*3600 = 1_000_000
        result.XpPerHour.Should().BeApproximately(1_000_000.0, 1.0,
            because: "структурный guard по уровню должен исключить пару A2→B1 (Δlevel=+15); " +
                     "засчитаны только интервал A (+1M) и B (+1M): 2M/7200*3600 = 1_000_000");

        // Без структурного guard'а: межгеройская дельта 79M/3600с × 3600 ≈ 27_000_000 xp/ч
        // Этот assert ПРОХОДИТ до фикса и ДОЛЖЕН проходить после:
        result.XpPerHour.Should().BeLessThan(5_000_000.0,
            because: "без структурного guard'а по уровню межгеройская дельта 79M даёт ≈27M xp/ч " +
                     "(магнитудный guard не срабатывает, т.к. 79M < a.XpToLevel 200M)");
    }

    // =========================================================================
    // Вспомогательные фабричные методы
    // =========================================================================

    /// <summary>Создаёт надёжный <see cref="MetricSample"/> с указанными полями.</summary>
    private static MetricSample MakeSample(
        DateTime takenAtUtc,
        long?    gold      = null,
        long?    xp        = null,
        long?    xpToLevel = null,
        int?     level     = null,
        (int typeId, int count)[]? chests = null)
    {
        var sample = new MetricSample
        {
            TakenAtUtc = takenAtUtc,
            IsReliable = true,
            Gold       = gold,
            Xp         = xp,
            XpToLevel  = xpToLevel,
            HeroLevel  = level
        };

        if (chests is not null)
        {
            foreach (var (typeId, count) in chests)
            {
                sample.Chests.Add(new MetricSampleChest { ChestTypeId = typeId, Count = count });
            }
        }

        return sample;
    }
}
