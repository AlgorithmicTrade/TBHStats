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
    /// Два интервала с level-up в каждом:
    /// Интервал 1: a.Xp=9000, a.XpToLevel=10000, b.Xp=3600, b.Level=a.Level+1 → дельта=4600
    /// Интервал 2: b.Xp=3600, b.XpToLevel=12000, c.Xp=1200, c.Level=b.Level+1 → дельта=10800
    /// Суммарно: (4600+10800)/(3600+3600)*3600 = 15400/7200*3600 = 7700 Xp/час.
    /// </summary>
    [Fact]
    public void ComputeLiveRates_TwoConsecutiveLevelUps_AccumulatesXpCorrectly()
    {
        // Arrange
        var samples = new[]
        {
            MakeSample(BaseUtc,                   xp: 9_000, xpToLevel: 10_000, level: 5),
            MakeSample(BaseUtc.AddSeconds(3600),  xp: 3_600, xpToLevel: 12_000, level: 6),
            MakeSample(BaseUtc.AddSeconds(7200),  xp: 1_200, xpToLevel: 15_000, level: 7)
        };
        // Дельта 1 = (10000-9000)+3600 = 4600
        // Дельта 2 = (12000-3600)+1200 = 9600
        // Темп = (4600+9600)/7200*3600 = 14200/7200*3600 ≈ 7100 Xp/час

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        double expectedDelta = (10_000 - 9_000 + 3_600) + (12_000 - 3_600 + 1_200);
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
    /// Второй: gold+3600, level-up (xpToLevel−xp+newXp=4000), chest type1 +2.
    /// Темп gold = (3600+3600)/7200*3600 = 3600/час.
    /// Темп xp = (3600+4000)/7200*3600 = 3800/час.
    /// Темп chest[1] = (2+2)/7200*3600 = 2/час.
    /// </summary>
    [Fact]
    public void ComputeLiveRates_MixedMetrics_AllRatesComputedIndependently()
    {
        // Arrange
        var t0 = BaseUtc;
        var t1 = BaseUtc.AddSeconds(3600);
        var t2 = BaseUtc.AddSeconds(7200);

        // Интервал 1: без level-up
        // Интервал 2: level-up: b.XpToLevel=10000, b.Xp=6400; c.Level=b.Level+1, c.Xp=400
        // Дельта xp интервал 1 = 3600−0 = 3600
        // Дельта xp интервал 2 = (10000−6400)+400 = 4000
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
                Gold = 3_600, Xp = 3_600, XpToLevel = 10_000, HeroLevel = 5,
                Chests = new List<MetricSampleChest> { new() { ChestTypeId = 1, Count = 2 } }
            },
            new MetricSample
            {
                TakenAtUtc = t2, IsReliable = true,
                Gold = 7_200, Xp = 400, XpToLevel = 15_000, HeroLevel = 6,
                Chests = new List<MetricSampleChest> { new() { ChestTypeId = 1, Count = 4 } }
            }
        };

        // Act
        LiveRates result = _sut.ComputeLiveRates(samples);

        // Assert
        result.GoldPerHour.Should().BeApproximately(3600.0, 1e-6,
            because: "суммарно +7200 золота за 7200 секунд = 3600/час");

        double expectedXpDelta = 3_600 + ((10_000 - 3_600) + 400);
        double expectedXpRate  = expectedXpDelta / 7200.0 * 3600.0;
        result.XpPerHour.Should().BeApproximately(expectedXpRate, 1e-6,
            because: "второй интервал — level-up, компенсируется через XpToLevel");

        result.ChestPerHourByType.Should().ContainKey(1);
        result.ChestPerHourByType[1].Should().BeApproximately(2.0, 1e-6,
            because: "+2 сундука за каждый интервал, суммарно +4 за 7200 с = 2/час");
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
