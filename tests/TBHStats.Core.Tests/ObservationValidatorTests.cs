namespace TBHStats.Core.Tests;

using FluentAssertions;
using TBHStats.Core.Models;
using TBHStats.Core.Parsing;
using Xunit;

/// <summary>
/// TDD red-тесты для <see cref="ObservationValidator"/>.
/// Реализация <c>Validate</c> — заглушка, бросающая <see cref="NotImplementedException"/> (T024).
/// Все тесты ДОЛЖНЫ ПАДАТЬ до реализации T024 — это ожидаемое поведение red-фазы.
/// </summary>
public sealed class ObservationValidatorTests
{
    private const double Threshold = 0.6;

    private static readonly DateTime BaseTime = new(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly ObservationValidator _sut = new();

    // =========================================================================
    // Вспомогательные фабричные методы
    // =========================================================================

    /// <summary>Строит <see cref="RawObservation"/> с заданными полями и уверенностью.</summary>
    private static RawObservation MakeObservation(
        long? gold = null,
        long? xp = null,
        long? xpToLevel = null,
        int? heroLevel = null,
        long? heroDamage = null,
        StageRef? nextLocation = null,
        IReadOnlyDictionary<int, int>? chests = null,
        IReadOnlyDictionary<string, double>? confidence = null,
        DateTime? takenAt = null)
        => new()
        {
            TakenAtUtc       = takenAt ?? BaseTime,
            Gold             = gold,
            Xp               = xp,
            XpToLevel        = xpToLevel,
            HeroLevel        = heroLevel,
            HeroDamage       = heroDamage,
            NextLocation     = nextLocation,
            Chests           = chests ?? new Dictionary<int, int>(),
            PerFieldConfidence = confidence ?? new Dictionary<string, double>(),
        };

    /// <summary>Строит предыдущий надёжный сэмпл для sanity-проверок.</summary>
    private static MetricSample MakePrevReliable(
        long? gold = null,
        long? xp = null,
        int? heroLevel = null)
        => new()
        {
            Id          = 1,
            TakenAtUtc  = BaseTime.AddSeconds(-60),
            IsReliable  = true,
            Gold        = gold,
            Xp          = xp,
            HeroLevel   = heroLevel,
        };

    // =========================================================================
    // Confidence-фильтр: поле принимается / отбрасывается по порогу
    // =========================================================================

    /// <summary>
    /// Gold с уверенностью выше порога — переносится в результат, сэмпл надёжен.
    /// </summary>
    [Fact]
    public void Validate_GoldAboveConfidenceThreshold_GoldPresentAndReliable()
    {
        var obs = MakeObservation(
            gold: 5000L,
            confidence: new Dictionary<string, double> { ["gold"] = 0.9 });

        MetricSample result = _sut.Validate(obs, prevReliable: null, Threshold);

        result.Gold.Should().Be(5000L, because: "уверенность 0.9 >= 0.6, поле принимается");
        result.IsReliable.Should().BeTrue(because: "есть принятое значимое поле Gold без нарушений sanity");
    }

    /// <summary>
    /// Gold с уверенностью ниже порога — отбрасывается, поле = null.
    /// </summary>
    [Fact]
    public void Validate_GoldBelowConfidenceThreshold_GoldIsNull()
    {
        var obs = MakeObservation(
            gold: 5000L,
            confidence: new Dictionary<string, double> { ["gold"] = 0.3 });

        MetricSample result = _sut.Validate(obs, prevReliable: null, Threshold);

        result.Gold.Should().BeNull(because: "уверенность 0.3 < 0.6, поле отбрасывается");
    }

    /// <summary>
    /// Ключ «gold» отсутствует в словаре уверенности — поле считается непринятым.
    /// </summary>
    [Fact]
    public void Validate_GoldConfidenceKeyMissing_GoldIsNull()
    {
        var obs = MakeObservation(
            gold: 5000L,
            confidence: new Dictionary<string, double>());   // ключ «gold» отсутствует

        MetricSample result = _sut.Validate(obs, prevReliable: null, Threshold);

        result.Gold.Should().BeNull(because: "отсутствующий ключ считается неприятым полем");
    }

    /// <summary>
    /// Xp с уверенностью выше порога — переносится в результат.
    /// </summary>
    [Fact]
    public void Validate_XpAboveConfidenceThreshold_XpPresent()
    {
        var obs = MakeObservation(
            xp: 12000L,
            confidence: new Dictionary<string, double> { ["xp"] = 0.85 });

        MetricSample result = _sut.Validate(obs, prevReliable: null, Threshold);

        result.Xp.Should().Be(12000L, because: "уверенность 0.85 >= 0.6");
    }

    /// <summary>
    /// Xp с уверенностью ниже порога — отбрасывается (null).
    /// </summary>
    [Fact]
    public void Validate_XpBelowConfidenceThreshold_XpIsNull()
    {
        var obs = MakeObservation(
            xp: 12000L,
            confidence: new Dictionary<string, double> { ["xp"] = 0.4 });

        MetricSample result = _sut.Validate(obs, prevReliable: null, Threshold);

        result.Xp.Should().BeNull(because: "уверенность 0.4 < 0.6");
    }

    /// <summary>
    /// HeroLevel с уверенностью выше порога — переносится в результат.
    /// </summary>
    [Fact]
    public void Validate_HeroLevelAboveConfidenceThreshold_HeroLevelPresent()
    {
        var obs = MakeObservation(
            heroLevel: 42,
            confidence: new Dictionary<string, double> { ["heroLevel"] = 0.95 });

        MetricSample result = _sut.Validate(obs, prevReliable: null, Threshold);

        result.HeroLevel.Should().Be(42, because: "уверенность 0.95 >= 0.6");
    }

    /// <summary>
    /// HeroDamage с уверенностью выше порога — переносится в результат.
    /// </summary>
    [Fact]
    public void Validate_HeroDamageAboveConfidenceThreshold_HeroDamagePresent()
    {
        var obs = MakeObservation(
            heroDamage: 999_000L,
            confidence: new Dictionary<string, double> { ["heroDamage"] = 0.8 });

        MetricSample result = _sut.Validate(obs, prevReliable: null, Threshold);

        result.HeroDamage.Should().Be(999_000L, because: "уверенность 0.8 >= 0.6");
    }

    /// <summary>
    /// HeroDamage с уверенностью ниже порога — отбрасывается (null).
    /// </summary>
    [Fact]
    public void Validate_HeroDamageBelowConfidenceThreshold_HeroDamageIsNull()
    {
        var obs = MakeObservation(
            heroDamage: 999_000L,
            confidence: new Dictionary<string, double> { ["heroDamage"] = 0.2 });

        MetricSample result = _sut.Validate(obs, prevReliable: null, Threshold);

        result.HeroDamage.Should().BeNull(because: "уверенность 0.2 < 0.6");
    }

    /// <summary>
    /// XpToLevel с уверенностью выше порога — переносится; ключ «xpToLevel».
    /// </summary>
    [Fact]
    public void Validate_XpToLevelAboveConfidenceThreshold_XpToLevelPresent()
    {
        var obs = MakeObservation(
            xpToLevel: 50_000L,
            confidence: new Dictionary<string, double> { ["xpToLevel"] = 0.75 });

        MetricSample result = _sut.Validate(obs, prevReliable: null, Threshold);

        result.XpToLevel.Should().Be(50_000L, because: "уверенность 0.75 >= 0.6, ключ «xpToLevel»");
    }

    /// <summary>
    /// XpToLevel с уверенностью ниже порога — отбрасывается (null).
    /// </summary>
    [Fact]
    public void Validate_XpToLevelBelowConfidenceThreshold_XpToLevelIsNull()
    {
        var obs = MakeObservation(
            xpToLevel: 50_000L,
            confidence: new Dictionary<string, double> { ["xpToLevel"] = 0.5 });

        MetricSample result = _sut.Validate(obs, prevReliable: null, Threshold);

        result.XpToLevel.Should().BeNull(because: "уверенность 0.5 < 0.6");
    }

    // =========================================================================
    // Смешанные confidence: часть полей принята, часть отброшена
    // =========================================================================

    /// <summary>
    /// Gold выше порога, Xp ниже порога — Gold принят, Xp = null, сэмпл надёжен.
    /// </summary>
    [Fact]
    public void Validate_GoldHighConfidenceXpLowConfidence_GoldAcceptedXpNull()
    {
        var obs = MakeObservation(
            gold: 8000L,
            xp: 3000L,
            confidence: new Dictionary<string, double>
            {
                ["gold"] = 0.9,
                ["xp"]   = 0.2,
            });

        MetricSample result = _sut.Validate(obs, prevReliable: null, Threshold);

        result.Gold.Should().Be(8000L);
        result.Xp.Should().BeNull();
        result.IsReliable.Should().BeTrue(because: "Gold принят — есть значимое поле");
    }

    /// <summary>
    /// Все значимые поля (Gold, Xp, HeroLevel) ниже порога → IsReliable = false.
    /// </summary>
    [Fact]
    public void Validate_AllSignificantFieldsBelowThreshold_IsReliableFalse()
    {
        var obs = MakeObservation(
            gold: 5000L,
            xp: 1000L,
            heroLevel: 10,
            confidence: new Dictionary<string, double>
            {
                ["gold"]      = 0.1,
                ["xp"]        = 0.2,
                ["heroLevel"] = 0.3,
            });

        MetricSample result = _sut.Validate(obs, prevReliable: null, Threshold);

        result.IsReliable.Should().BeFalse(because: "все значимые поля отброшены по confidence");
        result.Gold.Should().BeNull();
        result.Xp.Should().BeNull();
        result.HeroLevel.Should().BeNull();
    }

    /// <summary>
    /// Словарь уверенности полностью пуст — все поля отброшены, IsReliable = false.
    /// </summary>
    [Fact]
    public void Validate_EmptyConfidenceDictionary_AllSignificantFieldsNullAndUnreliable()
    {
        var obs = MakeObservation(
            gold: 5000L,
            xp: 1000L,
            confidence: new Dictionary<string, double>());

        MetricSample result = _sut.Validate(obs, prevReliable: null, Threshold);

        result.IsReliable.Should().BeFalse(because: "пустой словарь — нет принятых значимых полей");
        result.Gold.Should().BeNull();
        result.Xp.Should().BeNull();
    }

    // =========================================================================
    // Перенос TakenAtUtc
    // =========================================================================

    /// <summary>
    /// TakenAtUtc из observation точно переносится в результат.
    /// </summary>
    [Fact]
    public void Validate_TakenAtUtc_CopiedFromObservation()
    {
        var specificTime = new DateTime(2025, 9, 15, 8, 30, 0, DateTimeKind.Utc);
        var obs = MakeObservation(
            gold: 100L,
            confidence: new Dictionary<string, double> { ["gold"] = 0.9 },
            takenAt: specificTime);

        MetricSample result = _sut.Validate(obs, prevReliable: null, Threshold);

        result.TakenAtUtc.Should().Be(specificTime, because: "TakenAtUtc должен точно переноситься из observation");
    }

    // =========================================================================
    // NextLocation — переносится без confidence-ключа
    // =========================================================================

    /// <summary>
    /// NextLocation переносится в результат без проверки confidence (нет confidence-ключа).
    /// </summary>
    [Fact]
    public void Validate_NextLocationPresent_CopiedToResult()
    {
        var loc = new StageRef(1, "normal", 6);
        var obs = MakeObservation(
            gold: 1000L,
            nextLocation: loc,
            confidence: new Dictionary<string, double> { ["gold"] = 0.9 });

        MetricSample result = _sut.Validate(obs, prevReliable: null, Threshold);

        result.NextLocation.Should().Be(loc, because: "NextLocation переносится без confidence-фильтра");
    }

    // =========================================================================
    // Золото как расходуемый баланс (убывание легитимно)
    // =========================================================================

    /// <summary>
    /// Gold в observation меньше, чем у prevReliable → легитимная трата (руны/апгрейды/магазин)
    /// → IsReliable = true; сниженный баланс записывается в результат.
    /// </summary>
    [Fact]
    public void Validate_GoldDecreasesFromPrevReliable_LegitimateSpending_IsReliableTrue()
    {
        var prev = MakePrevReliable(gold: 10_000L);
        var obs = MakeObservation(
            gold: 8_000L,   // убывание — легитимная трата
            confidence: new Dictionary<string, double> { ["gold"] = 0.95 });

        MetricSample result = _sut.Validate(obs, prevReliable: prev, Threshold);

        result.IsReliable.Should().BeTrue(because: "убывание Gold = легитимная трата (руны/прокачка/магазин), не нарушение sanity");
        result.Gold.Should().Be(8_000L, because: "фактический (сниженный) баланс записывается в результат");
    }

    /// <summary>
    /// Gold в observation больше, чем у prevReliable → рост → IsReliable = true.
    /// </summary>
    [Fact]
    public void Validate_GoldIncreasesFromPrevReliable_IsReliableTrue()
    {
        var prev = MakePrevReliable(gold: 5_000L);
        var obs = MakeObservation(
            gold: 7_000L,   // рост
            confidence: new Dictionary<string, double> { ["gold"] = 0.95 });

        MetricSample result = _sut.Validate(obs, prevReliable: prev, Threshold);

        result.IsReliable.Should().BeTrue(because: "Gold растёт — сэмпл надёжен");
    }

    /// <summary>
    /// Gold в observation равен prevReliable.Gold (нулевой прирост) → IsReliable = true.
    /// </summary>
    [Fact]
    public void Validate_GoldUnchangedFromPrevReliable_IsReliableTrue()
    {
        var prev = MakePrevReliable(gold: 5_000L);
        var obs = MakeObservation(
            gold: 5_000L,   // без изменений
            confidence: new Dictionary<string, double> { ["gold"] = 0.95 });

        MetricSample result = _sut.Validate(obs, prevReliable: prev, Threshold);

        result.IsReliable.Should().BeTrue(because: "Gold не изменился — сэмпл надёжен");
    }

    /// <summary>
    /// prevReliable = null (первая точка), Gold принят → надёжен.
    /// </summary>
    [Fact]
    public void Validate_NoPrevReliable_GoldAccepted_IsReliableTrue()
    {
        var obs = MakeObservation(
            gold: 3_000L,
            confidence: new Dictionary<string, double> { ["gold"] = 0.9 });

        MetricSample result = _sut.Validate(obs, prevReliable: null, Threshold);

        result.IsReliable.Should().BeTrue(because: "первая точка без prevReliable надёжна при наличии принятого значимого поля");
        result.Gold.Should().Be(3_000L);
    }

    /// <summary>
    /// prevReliable.Gold = null → сравнение невозможно, Gold принят → IsReliable = true.
    /// </summary>
    [Fact]
    public void Validate_PrevReliableGoldNull_GoldDecreaseNotViolation_IsReliableTrue()
    {
        // prevReliable без Gold (null) — нечего сравнивать
        var prev = MakePrevReliable(gold: null);
        var obs = MakeObservation(
            gold: 2_000L,   // любое значение — prev.Gold = null → сравнение невозможно
            confidence: new Dictionary<string, double> { ["gold"] = 0.9 });

        MetricSample result = _sut.Validate(obs, prevReliable: prev, Threshold);

        result.IsReliable.Should().BeTrue(because: "если prev.Gold = null, нет базы для сравнения — сэмпл надёжен");
    }

    /// <summary>
    /// Gold observation не прошёл confidence → поле Gold = null в результате;
    /// IsReliable определяется наличием Xp и HeroLevel.
    /// </summary>
    [Fact]
    public void Validate_GoldFailsConfidence_MonotonicityCheckNotApplied()
    {
        var prev = MakePrevReliable(gold: 10_000L);
        var obs = MakeObservation(
            gold: 1_000L,    // убывание, но confidence ниже порога
            xp: 5_000L,
            heroLevel: 5,
            confidence: new Dictionary<string, double>
            {
                ["gold"]      = 0.1,    // ниже порога — поле отброшено
                ["xp"]        = 0.9,
                ["heroLevel"] = 0.9,
            });

        MetricSample result = _sut.Validate(obs, prevReliable: prev, Threshold);

        result.Gold.Should().BeNull(because: "Gold отброшен по confidence");
        // Xp и HeroLevel приняты → IsReliable = true.
        result.IsReliable.Should().BeTrue(because: "Xp и HeroLevel приняты — есть значимые поля");
    }

    /// <summary>
    /// Gold резко падает после траты (сценарий Rune): prev.Gold=200_000, obs gold=6_553.
    /// Сэмпл надёжен, фактический (сниженный) баланс записан, xp/heroLevel перенесены.
    /// </summary>
    [Fact]
    public void Validate_GoldDropsAfterSpending_SampleStillReliable_AndBalanceRecorded()
    {
        var prev = MakePrevReliable(gold: 200_000L, xp: 1_000_000L, heroLevel: 30);
        var obs = MakeObservation(
            gold: 6_553L,       // резкое падение — игрок потратил золото на руны
            xp: 1_050_000L,
            heroLevel: 30,
            confidence: new Dictionary<string, double>
            {
                ["gold"]      = 0.9,
                ["xp"]        = 0.85,
                ["heroLevel"] = 0.95,
            });

        MetricSample result = _sut.Validate(obs, prevReliable: prev, Threshold);

        result.IsReliable.Should().BeTrue(because: "трата золота на руны — легитимное убывание, сэмпл надёжен");
        result.Gold.Should().Be(6_553L, because: "фактический баланс после траты должен быть записан");
        result.Xp.Should().Be(1_050_000L, because: "Xp валиден и должен быть перенесён");
        result.HeroLevel.Should().Be(30, because: "HeroLevel валиден и должен быть перенесён");
    }

    // =========================================================================
    // EXP-reset при level-up
    // =========================================================================

    /// <summary>
    /// Xp убывает, HeroLevel растёт (level-up) → легитимный сброс EXP → IsReliable = true.
    /// </summary>
    [Fact]
    public void Validate_XpDecreasesWithHeroLevelIncrease_LevelUpNotViolation_IsReliableTrue()
    {
        var prev = MakePrevReliable(xp: 48_000L, heroLevel: 10);
        var obs = MakeObservation(
            xp: 100L,           // сброс после level-up
            heroLevel: 11,      // уровень вырос
            confidence: new Dictionary<string, double>
            {
                ["xp"]        = 0.9,
                ["heroLevel"] = 0.9,
            });

        MetricSample result = _sut.Validate(obs, prevReliable: prev, Threshold);

        result.IsReliable.Should().BeTrue(because: "убывание Xp объяснено level-up (HeroLevel вырос)");
        result.Xp.Should().Be(100L);
        result.HeroLevel.Should().Be(11);
    }

    /// <summary>
    /// Xp убывает, HeroLevel НЕ изменился — необъяснённое убывание;
    /// по контракту remarks явно не специфицирует IsReliable для xp-убывания без level-up,
    /// поэтому тест проверяет только то, что Xp всё равно переносится (поле принято).
    /// </summary>
    [Fact]
    public void Validate_XpDecreasesWithoutLevelUp_XpFieldStillTransferred()
    {
        var prev = MakePrevReliable(xp: 30_000L, heroLevel: 5);
        var obs = MakeObservation(
            xp: 5_000L,     // убывание без level-up
            heroLevel: 5,   // уровень НЕ изменился
            confidence: new Dictionary<string, double>
            {
                ["xp"]        = 0.9,
                ["heroLevel"] = 0.9,
            });

        MetricSample result = _sut.Validate(obs, prevReliable: prev, Threshold);

        // Remarks не специфицирует убывание Xp без level-up как явное нарушение sanity для IsReliable.
        // Тест фиксирует только то, что Xp принят по confidence и перенесён.
        result.Xp.Should().Be(5_000L, because: "Xp прошёл confidence и должен быть перенесён");
    }

    /// <summary>
    /// Gold убывает (трата) И Xp убывает с level-up → оба события легитимны
    /// → IsReliable = true (gold расходуем, level-up = нормальный сброс Xp).
    /// </summary>
    [Fact]
    public void Validate_GoldSpendingWithXpLevelUp_IsReliableTrue()
    {
        var prev = MakePrevReliable(gold: 20_000L, xp: 40_000L, heroLevel: 7);
        var obs = MakeObservation(
            gold: 15_000L,  // убывание Gold — трата на руны/прокачку
            xp: 500L,       // level-up Xp — сброс после повышения уровня
            heroLevel: 8,
            confidence: new Dictionary<string, double>
            {
                ["gold"]      = 0.9,
                ["xp"]        = 0.9,
                ["heroLevel"] = 0.9,
            });

        MetricSample result = _sut.Validate(obs, prevReliable: prev, Threshold);

        result.IsReliable.Should().BeTrue(because: "убывание Gold — легитимная трата; level-up Xp — легитимный сброс; оба события нормальны");
    }

    // =========================================================================
    // Транзиентные сундуки
    // =========================================================================

    /// <summary>
    /// Сундуки из observation копируются в result.Chests как MetricSampleChest.
    /// </summary>
    [Fact]
    public void Validate_ChestsInObservation_CopiedToResultChests()
    {
        var chests = new Dictionary<int, int> { [1] = 3, [2] = 0 };
        var obs = MakeObservation(
            gold: 1000L,
            chests: chests,
            confidence: new Dictionary<string, double> { ["gold"] = 0.9 });

        MetricSample result = _sut.Validate(obs, prevReliable: null, Threshold);

        result.Chests.Should().HaveCount(2, because: "оба типа сундуков должны быть скопированы");
        result.Chests.Should().Contain(c => c.ChestTypeId == 1 && c.Count == 3);
        result.Chests.Should().Contain(c => c.ChestTypeId == 2 && c.Count == 0);
    }

    /// <summary>
    /// Уменьшение Count сундука относительно предыдущего сэмпла (открытие) НЕ делает сэмпл ненадёжным.
    /// </summary>
    [Fact]
    public void Validate_ChestCountDecreasesFromPrev_NotASanityViolation_IsReliableTrue()
    {
        // prevReliable с 5 сундуками типа 1; obs — 0 (открыты)
        var prev = MakePrevReliable(gold: 5_000L);
        prev.Chests = new List<MetricSampleChest>
        {
            new() { ChestTypeId = 1, Count = 5 },
        };

        var obs = MakeObservation(
            gold: 6_000L,   // Gold растёт — монотонность соблюдена
            chests: new Dictionary<int, int> { [1] = 0 },
            confidence: new Dictionary<string, double> { ["gold"] = 0.9 });

        MetricSample result = _sut.Validate(obs, prevReliable: prev, Threshold);

        result.IsReliable.Should().BeTrue(because: "уменьшение Count сундука = открытие, не нарушение sanity");
        result.Chests.Should().Contain(c => c.ChestTypeId == 1 && c.Count == 0);
    }

    /// <summary>
    /// Пустой словарь Chests в observation — result.Chests пуст.
    /// </summary>
    [Fact]
    public void Validate_NoChests_ResultChestsEmpty()
    {
        var obs = MakeObservation(
            gold: 1000L,
            chests: new Dictionary<int, int>(),
            confidence: new Dictionary<string, double> { ["gold"] = 0.9 });

        MetricSample result = _sut.Validate(obs, prevReliable: null, Threshold);

        result.Chests.Should().BeEmpty(because: "исходный словарь сундуков пуст");
    }

    /// <summary>
    /// Несколько типов сундуков, все с Count >= 0 — все копируются корректно.
    /// </summary>
    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 0)]
    [InlineData(3, 12)]
    public void Validate_SingleChestType_CopiedWithCorrectCount(int chestTypeId, int count)
    {
        var obs = MakeObservation(
            gold: 1000L,
            chests: new Dictionary<int, int> { [chestTypeId] = count },
            confidence: new Dictionary<string, double> { ["gold"] = 0.9 });

        MetricSample result = _sut.Validate(obs, prevReliable: null, Threshold);

        result.Chests.Should().ContainSingle(c => c.ChestTypeId == chestTypeId && c.Count == count,
            because: $"ChestTypeId={chestTypeId} с Count={count} должен быть скопирован точно");
    }

    // =========================================================================
    // IsReliable — граничные и комбинированные случаи
    // =========================================================================

    /// <summary>
    /// Только HeroLevel принят (Gold и Xp отсутствуют) → IsReliable = true (HeroLevel — значимое поле).
    /// </summary>
    [Fact]
    public void Validate_OnlyHeroLevelAccepted_IsReliableTrue()
    {
        var obs = MakeObservation(
            heroLevel: 20,
            confidence: new Dictionary<string, double> { ["heroLevel"] = 0.95 });

        MetricSample result = _sut.Validate(obs, prevReliable: null, Threshold);

        result.IsReliable.Should().BeTrue(because: "HeroLevel — значимое поле, его достаточно для IsReliable=true");
        result.HeroLevel.Should().Be(20);
    }

    /// <summary>
    /// Только Xp принят (Gold и HeroLevel отсутствуют) → IsReliable = true.
    /// </summary>
    [Fact]
    public void Validate_OnlyXpAccepted_IsReliableTrue()
    {
        var obs = MakeObservation(
            xp: 25_000L,
            confidence: new Dictionary<string, double> { ["xp"] = 0.8 });

        MetricSample result = _sut.Validate(obs, prevReliable: null, Threshold);

        result.IsReliable.Should().BeTrue(because: "Xp — значимое поле, его достаточно для IsReliable=true");
    }

    /// <summary>
    /// Сэмпл с confidence ровно на границе порога (== threshold) → поле принимается.
    /// </summary>
    [Fact]
    public void Validate_GoldConfidenceExactlyAtThreshold_FieldAccepted()
    {
        var obs = MakeObservation(
            gold: 7_000L,
            confidence: new Dictionary<string, double> { ["gold"] = Threshold }); // ровно 0.6

        MetricSample result = _sut.Validate(obs, prevReliable: null, Threshold);

        result.Gold.Should().Be(7_000L, because: "уверенность == порогу → >=, поле принимается");
    }

    /// <summary>
    /// Observation без каких-либо значимых полей (Gold/Xp/HeroLevel = null) → IsReliable = false.
    /// </summary>
    [Fact]
    public void Validate_NoSignificantFieldsAtAll_IsReliableFalse()
    {
        var obs = MakeObservation(
            // gold/xp/heroLevel = null — поля вообще не задавались
            confidence: new Dictionary<string, double>());

        MetricSample result = _sut.Validate(obs, prevReliable: null, Threshold);

        result.IsReliable.Should().BeFalse(because: "нет ни одного значимого поля — надёжность невозможна");
    }
}
