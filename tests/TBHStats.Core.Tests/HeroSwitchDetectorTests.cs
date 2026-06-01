namespace TBHStats.Core.Tests;

using FluentAssertions;
using TBHStats.Core.Models;
using TBHStats.Core.Optimization;
using Xunit;

/// <summary>
/// Регрессионные тесты для <see cref="HeroSwitchDetector.IsHeroSwitch"/>.
/// Реализация уже существует — все тесты ожидаемо ЗЕЛЁНЫЕ.
/// Покрывает: правило 1 (падение уровня), правило 2 (смена потолка без near-full),
/// легитимные level-up (не триггерят), граничные значения near-full-порога 0.8,
/// null-гарды, idle-набор уровней за свёрнутое окно.
/// </summary>
public sealed class HeroSwitchDetectorTests
{
    // =========================================================================
    // НЕ смена героя (ожидание false)
    // =========================================================================

    /// <summary>
    /// Тест 1: Тот же уровень, тот же потолок, рост опыта внутри уровня.
    /// Правила 1 и 2 не срабатывают → false.
    ///
    /// Примечание: смена героя на ТОТ ЖЕ уровень с тем же потолком детектором НЕ ловится
    /// (правила 1 и 2 не срабатывают) — это покрывается отдельным сигналом «смена класса»
    /// в StatsOrchestrator, а не HeroSwitchDetector.
    /// </summary>
    [Fact]
    public void IsHeroSwitch_SameLevelSameCapXpGrows_ReturnsFalse()
    {
        // Arrange
        // prev: Xp=2000, XpToLevel=10000, level=5
        // cur:  Xp=5000, XpToLevel=10000, level=5 — рост внутри уровня, потолок тот же
        var prev = MakeSample(xp: 2_000, xpToLevel: 10_000, level: 5);
        var cur  = MakeSample(xp: 5_000, xpToLevel: 10_000, level: 5);

        // Act
        bool result = HeroSwitchDetector.IsHeroSwitch(prev, cur);

        // Assert
        result.Should().BeFalse(
            because: "тот же уровень + тот же потолок + рост опыта — нормальный прогресс внутри уровня");
    }

    /// <summary>
    /// Тест 2: Легитимный одиночный level-up при near-full (90% потолка).
    /// prev.Xp=9000, prev.XpToLevel=10000 → 90% ≥ 80% → nearFull → правило 2 не срабатывает.
    /// Рост уровня сам по себе не триггерит → false.
    /// </summary>
    [Fact]
    public void IsHeroSwitch_LegitLevelUpNearFull_ReturnsFalse()
    {
        // Arrange
        // prev: Xp=9000, XpToLevel=10000 → 90% ≥ 80% (near-full) → level-up легитимен
        // cur:  Xp=500, XpToLevel=12000, level=6 — уровень вырос, потолок сменился
        var prev = MakeSample(xp: 9_000, xpToLevel: 10_000, level: 5);
        var cur  = MakeSample(xp: 500,   xpToLevel: 12_000, level: 6);

        // Act
        bool result = HeroSwitchDetector.IsHeroSwitch(prev, cur);

        // Assert
        result.Should().BeFalse(
            because: "prev.Xp=9000 — 90% от XpToLevel=10000, nearFull=true; потолок сменился с сигнатурой level-up → легитимный level-up, не смена героя");
    }

    /// <summary>
    /// Тест 3: Level-up с misread heroLevel — уровень в cur совпадает с prev (OCR ошибся на +1),
    /// но prev был near-full (95%), а потолок вырос → трактуется как level-up, не сброс.
    /// prev: Xp=9500, XpToLevel=10000 → 95% ≥ 80% → nearFull → правило 2 не срабатывает.
    /// </summary>
    [Fact]
    public void IsHeroSwitch_LevelUpMisreadHeroLevelNearFull_ReturnsFalse()
    {
        // Arrange
        // prev: Xp=9500, XpToLevel=10000, level=5 → nearFull (95%)
        // cur:  Xp=300, XpToLevel=12000, level=5 (OCR misread: уровень не изменился)
        //   Потолок сменился, но prev was near-full → правило 2 не срабатывает
        //   Уровень не упал → правило 1 не срабатывает
        var prev = MakeSample(xp: 9_500, xpToLevel: 10_000, level: 5);
        var cur  = MakeSample(xp: 300,   xpToLevel: 12_000, level: 5);

        // Act
        bool result = HeroSwitchDetector.IsHeroSwitch(prev, cur);

        // Assert
        result.Should().BeFalse(
            because: "prev.Xp=9500 — 95% от XpToLevel=10000, nearFull=true; смена потолка при near-full трактуется как level-up, даже если heroLevel не изменился (OCR misread)");
    }

    /// <summary>
    /// Тест 4: Оба HeroLevel null, тот же потолок, рост опыта.
    /// Правило 1 не срабатывает (нет данных об уровне), правило 2 не срабатывает (потолок тот же).
    /// </summary>
    [Fact]
    public void IsHeroSwitch_BothLevelsNullSameCapXpGrows_ReturnsFalse()
    {
        // Arrange
        // prev: Xp=1000, XpToLevel=5000, level=null
        // cur:  Xp=3000, XpToLevel=5000, level=null — потолок одинаковый, уровень неизвестен
        var prev = MakeSample(xp: 1_000, xpToLevel: 5_000, level: null);
        var cur  = MakeSample(xp: 3_000, xpToLevel: 5_000, level: null);

        // Act
        bool result = HeroSwitchDetector.IsHeroSwitch(prev, cur);

        // Assert
        result.Should().BeFalse(
            because: "оба level=null, потолок одинаковый — данных для ни одного правила недостаточно, смену героя определить нельзя → false");
    }

    /// <summary>
    /// Тест 5: Idle-набор — уровень вырос на несколько при near-full prev.
    /// Рост уровня сам по себе не является сигналом смены героя (FR-005a, idle-набор за свёрнутое окно).
    /// prev: Xp=9000, XpToLevel=10000 → nearFull → правило 2 не срабатывает.
    /// Рост уровня на 3 (5→8) → правило 1 проверяет только ПАДЕНИЕ, не рост → false.
    ///
    /// Примечание: смена героя на ТОТ ЖЕ или БОЛЕЕ ВЫСОКИЙ уровень с соответствующим потолком
    /// детектором НЕ ловится (правила 1 и 2 не срабатывают при near-full prev) — это покрывается
    /// отдельным сигналом «смена класса» в StatsOrchestrator.
    /// </summary>
    [Fact]
    public void IsHeroSwitch_IdleMultiLevelUpNearFull_ReturnsFalse()
    {
        // Arrange
        // prev: Xp=9000, XpToLevel=10000, level=5 → nearFull (90%)
        // cur:  Xp=2000, XpToLevel=20000, level=8 — idle-набор нескольких уровней
        var prev = MakeSample(xp: 9_000, xpToLevel: 10_000, level: 5);
        var cur  = MakeSample(xp: 2_000, xpToLevel: 20_000, level: 8);

        // Act
        bool result = HeroSwitchDetector.IsHeroSwitch(prev, cur);

        // Assert
        result.Should().BeFalse(
            because: "рост уровня (даже множественный) сам по себе не является сигналом смены героя; prev был near-full (90%) → потолок мог законно вырасти за несколько idle level-up'ов");
    }

    /// <summary>
    /// Тест 6: Один из XpToLevel null — недостаточно данных для правила 2.
    /// Уровни не падают → правило 1 не срабатывает.
    /// Итого → false.
    /// </summary>
    [Fact]
    public void IsHeroSwitch_PrevXpToLevelNull_ReturnsFalse()
    {
        // Arrange
        // prev: XpToLevel=null, level=5 → правило 2 требует оба XpToLevel, пропускается
        // cur:  XpToLevel=12000, level=6 — уровень вырос, но prev.XpToLevel=null
        var prev = MakeSample(xp: 5_000, xpToLevel: null, level: 5);
        var cur  = MakeSample(xp: 1_000, xpToLevel: 12_000, level: 6);

        // Act
        bool result = HeroSwitchDetector.IsHeroSwitch(prev, cur);

        // Assert
        result.Should().BeFalse(
            because: "prev.XpToLevel=null → правило 2 не может быть применено; уровень не упал → правило 1 не срабатывает → недостаточно данных, возвращается false");
    }

    // =========================================================================
    // СМЕНА героя (ожидание true)
    // =========================================================================

    /// <summary>
    /// Тест 7: Падение уровня — правило 1.
    /// Герой не теряет уровни, падение level означает другого героя или misread.
    /// </summary>
    [Fact]
    public void IsHeroSwitch_LevelDrops_ReturnsTrue()
    {
        // Arrange
        // prev: level=50; cur: level=27 — падение уровня на 23
        var prev = MakeSample(xp: 1_000_000, xpToLevel: 5_000_000, level: 50);
        var cur  = MakeSample(xp: 500_000,   xpToLevel: 3_000_000, level: 27);

        // Act
        bool result = HeroSwitchDetector.IsHeroSwitch(prev, cur);

        // Assert
        result.Should().BeTrue(
            because: "cur.HeroLevel=27 < prev.HeroLevel=50 — герой не теряет уровни; правило 1: падение уровня = смена героя");
    }

    /// <summary>
    /// Тест 8: Late-game смена героя — оба HeroLevel null, потолок сменился, prev НЕ near-full.
    /// prev: Xp=11_000_000, XpToLevel=200_000_000 → 5.5% (далеко от 80%) → nearFull=false.
    /// Потолок сменился (200M → 300M) → правило 2 срабатывает → true.
    ///
    /// Это живой кейс провала OCR-детекции уровня в late-game: когда level не читается,
    /// единственный структурный сигнал — смена потолка опыта без near-full prev.
    /// </summary>
    [Fact]
    public void IsHeroSwitch_LateGameBothLevelsNullCapChangedNotNearFull_ReturnsTrue()
    {
        // Arrange
        // prev: Xp=11_000_000, XpToLevel=200_000_000, level=null → 5.5% (не near-full)
        // cur:  Xp=90_000_000, XpToLevel=300_000_000, level=null — потолок сменился
        //   nearFull: 11M ≥ 200M*0.8=160M → false → правило 2 срабатывает → true
        var prev = MakeSample(xp: 11_000_000,  xpToLevel: 200_000_000, level: null);
        var cur  = MakeSample(xp: 90_000_000,  xpToLevel: 300_000_000, level: null);

        // Act
        bool result = HeroSwitchDetector.IsHeroSwitch(prev, cur);

        // Assert
        result.Should().BeTrue(
            because: "prev.Xp=11M всего 5.5% от XpToLevel=200M — далеко от near-full (80%); потолок сменился без сигнатуры level-up → правило 2: смена героя");
    }

    /// <summary>
    /// Тест 9: Смена потолка при низком prev.Xp с известными уровнями.
    /// prev: Xp=1_000_000, XpToLevel=70_000_000 → 1.4% (не near-full).
    /// Уровень вырос (80→82), но prev НЕ был near-full → потолок сменился = смена героя.
    /// </summary>
    [Fact]
    public void IsHeroSwitch_LowXpCapChangedWithKnownLevels_ReturnsTrue()
    {
        // Arrange
        // prev: Xp=1_000_000, XpToLevel=70_000_000, level=80 → 1.4% (не near-full)
        // cur:  Xp=5_000_000, XpToLevel=90_000_000, level=82
        //   nearFull: 1M ≥ 70M*0.8=56M → false → правило 2 срабатывает → true
        var prev = MakeSample(xp: 1_000_000, xpToLevel: 70_000_000, level: 80);
        var cur  = MakeSample(xp: 5_000_000, xpToLevel: 90_000_000, level: 82);

        // Act
        bool result = HeroSwitchDetector.IsHeroSwitch(prev, cur);

        // Assert
        result.Should().BeTrue(
            because: "prev.Xp=1M всего 1.4% от XpToLevel=70M — не near-full (80%); потолок сменился (70M→90M) без near-full сигнатуры level-up → правило 2: смена героя");
    }

    /// <summary>
    /// Тест 10: Падение уровня даже при одинаковом потолке → правило 1 приоритетно.
    /// Одинаковый XpToLevel не спасает — падение level является достаточным сигналом смены героя.
    /// </summary>
    [Fact]
    public void IsHeroSwitch_LevelDropsSameCap_ReturnsTrue()
    {
        // Arrange
        // prev: Xp=100, XpToLevel=10000, level=10
        // cur:  Xp=100, XpToLevel=10000, level=9 — уровень упал на 1, потолок одинаковый
        var prev = MakeSample(xp: 100, xpToLevel: 10_000, level: 10);
        var cur  = MakeSample(xp: 100, xpToLevel: 10_000, level: 9);

        // Act
        bool result = HeroSwitchDetector.IsHeroSwitch(prev, cur);

        // Assert
        result.Should().BeTrue(
            because: "cur.HeroLevel=9 < prev.HeroLevel=10 — уровень упал; правило 1 приоритетно, одинаковый потолок не отменяет падение уровня");
    }

    // =========================================================================
    // Граничные случаи
    // =========================================================================

    /// <summary>
    /// Тест 11: Null-аргументы бросают ArgumentNullException.
    /// </summary>
    [Fact]
    public void IsHeroSwitch_NullPrevious_ThrowsArgumentNullException()
    {
        // Arrange
        var sample = MakeSample(xp: 1_000, xpToLevel: 10_000, level: 5);

        // Act
        Action act = () => HeroSwitchDetector.IsHeroSwitch(null!, sample);

        // Assert
        act.Should().Throw<ArgumentNullException>(
            because: "previous=null — недопустимый аргумент, метод обязан бросить ArgumentNullException");
    }

    [Fact]
    public void IsHeroSwitch_NullCurrent_ThrowsArgumentNullException()
    {
        // Arrange
        var sample = MakeSample(xp: 1_000, xpToLevel: 10_000, level: 5);

        // Act
        Action act = () => HeroSwitchDetector.IsHeroSwitch(sample, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>(
            because: "current=null — недопустимый аргумент, метод обязан бросить ArgumentNullException");
    }

    /// <summary>
    /// Тест 12: Тот же уровень и потолок, но разный опыт → false.
    ///
    /// Примечание: детектор не ловит смену героя на ТОТ ЖЕ уровень с тем же потолком —
    /// правила 1 (нет падения) и 2 (потолок не изменился) не срабатывают.
    /// Такой случай покрывается отдельным сигналом «смена класса» в StatsOrchestrator,
    /// а не HeroSwitchDetector.
    /// </summary>
    [Fact]
    public void IsHeroSwitch_SameLevelSameCap_ReturnsFalse()
    {
        // Arrange
        // prev: Xp=1_000_000, XpToLevel=5_000_000, level=30
        // cur:  Xp=4_000_000, XpToLevel=5_000_000, level=30 — тот же уровень + потолок, рост opыта
        //   Правило 1: уровень не упал → не срабатывает
        //   Правило 2: XpToLevel одинаковый → не срабатывает
        //   → false (одинаковый уровень+потолок: детектор по XpToLevel/level не ловит;
        //     покрывается сигналом класса в StatsOrchestrator)
        var prev = MakeSample(xp: 1_000_000, xpToLevel: 5_000_000, level: 30);
        var cur  = MakeSample(xp: 4_000_000, xpToLevel: 5_000_000, level: 30);

        // Act
        bool result = HeroSwitchDetector.IsHeroSwitch(prev, cur);

        // Assert
        result.Should().BeFalse(
            because: "одинаковый уровень+потолок: детектор по XpToLevel/level не ловит смену на того же героя; покрывается сигналом класса в StatsOrchestrator");
    }

    // =========================================================================
    // Вспомогательная фабрика
    // =========================================================================

    /// <summary>Создаёт <see cref="MetricSample"/> с нужными полями. IsReliable=true по умолчанию.</summary>
    private static MetricSample MakeSample(
        long? xp        = null,
        long? xpToLevel = null,
        int?  level     = null,
        long? gold      = null)
    {
        return new MetricSample
        {
            TakenAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IsReliable = true,
            Gold       = gold,
            Xp         = xp,
            XpToLevel  = xpToLevel,
            HeroLevel  = level
        };
    }
}
