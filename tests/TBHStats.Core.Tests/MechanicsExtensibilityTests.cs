namespace TBHStats.Core.Tests;

using FluentAssertions;
using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;
using TBHStats.Core.Optimization;
using Xunit;

/// <summary>
/// Тесты расширяемости игровых механик (SC-010, FR-013, ADR-009).
/// Проверяют, что доменный слой работает с расширенным GameMechanicsConfig
/// без изменения кода — путём замены конфига данными (config-driven, не enum/switch).
/// </summary>
public sealed class MechanicsExtensibilityTests
{
    // ────────────────────────────────────────────────────────────
    // Вспомогательные хелперы
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// Строит расширенный конфиг на базе дефолтного: добавляет новый ChestType (Id=4, "green"),
    /// новый HeroClass ("mage"), новую Tab (Id=10, "event").
    /// </summary>
    private static GameMechanicsConfig BuildExtendedConfig()
    {
        GameMechanicsConfig defaults = GameMechanicsConfig.CreateDefault();

        // Добавляем новый тип сундука с Id=4 (сверх 3 дефолтных)
        var extendedChestTypes = new List<ChestType>(defaults.ChestTypes)
        {
            new ChestType
            {
                Id          = 4,
                Key         = "green",
                DisplayName = "Эпический",
                ColorLabel  = "зелёный",
                SortOrder   = 4,
                IsActive    = true,
            },
        };

        // Добавляем класс героя (по умолчанию список пустой — открываются динамически)
        var extendedHeroClasses = new List<HeroClass>(defaults.HeroClasses)
        {
            new HeroClass
            {
                Id          = 1,
                Key         = "mage",
                DisplayName = "Маг",
                IsActive    = true,
            },
        };

        // Добавляем новую вкладку с Id=10 (сверх 9 дефолтных)
        var extendedTabs = new List<Tab>(defaults.Tabs)
        {
            new Tab
            {
                Id              = 10,
                Key             = "event",
                DisplayName     = "Event",
                RecognitionText = "Event",
                SortOrder       = 10,
                IsActive        = true,
                IsDataSource    = false,
            },
        };

        return new GameMechanicsConfig(
            chestTypes:          extendedChestTypes,
            heroClasses:         extendedHeroClasses,
            tabs:                extendedTabs,
            acts:                defaults.Acts,
            difficulties:        defaults.Difficulties,
            stages:              defaults.Stages,
            fieldSourceBindings: defaults.FieldSourceBindings);
    }

    private static StageRun Run(
        int stageId,
        int durationSeconds,
        long goldGained,
        long xpGained,
        bool isPartial = false,
        IEnumerable<StageRunChest>? chests = null)
        => new StageRun
        {
            StageId         = stageId,
            DurationSeconds = durationSeconds,
            GoldGained      = goldGained,
            XpGained        = xpGained,
            CompletedAtUtc  = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IsPartial       = isPartial,
            Hero            = new HeroSnapshot(1, 10, 1000),
            Chests          = chests is null
                ? new List<StageRunChest>()
                : new List<StageRunChest>(chests),
        };

    private static StageRunChest Chest(int chestTypeId, int count)
        => new StageRunChest { ChestTypeId = chestTypeId, Count = count };

    // ────────────────────────────────────────────────────────────
    // 1. GameMechanicsConfig — расширение справочников
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void ExtendedConfig_ChestTypes_ContainsDefaultPlusNewGreenType()
    {
        // Arrange / Act
        var config = BuildExtendedConfig();

        // Assert: дефолтных 3 + новый "green" = 4
        config.ChestTypes.Should().HaveCount(4);
        config.ChestTypes.Should().Contain(ct => ct.Key == "brown" && ct.Id == 1);
        config.ChestTypes.Should().Contain(ct => ct.Key == "blue"  && ct.Id == 2);
        config.ChestTypes.Should().Contain(ct => ct.Key == "red"   && ct.Id == 3);
        config.ChestTypes.Should().Contain(ct => ct.Key == "green" && ct.Id == 4);
    }

    [Fact]
    public void ExtendedConfig_HeroClasses_ContainsNewMageClass()
    {
        // Arrange / Act
        var config = BuildExtendedConfig();

        // Assert: по умолчанию пустой список, после расширения — один класс
        config.HeroClasses.Should().HaveCount(1);
        config.HeroClasses.Should().Contain(hc => hc.Key == "mage" && hc.Id == 1);
    }

    [Fact]
    public void ExtendedConfig_Tabs_ContainsDefaultPlusNewEventTab()
    {
        // Arrange / Act
        var config = BuildExtendedConfig();

        // Assert: 9 дефолтных + 1 новая "event" = 10
        config.Tabs.Should().HaveCount(10);
        config.Tabs.Should().Contain(t => t.Key == "event" && t.Id == 10);
    }

    [Fact]
    public void ExtendedConfig_StagesAndActs_UnchangedByExtension()
    {
        // Arrange
        var defaults = GameMechanicsConfig.CreateDefault();
        var extended = BuildExtendedConfig();

        // Assert: расширение справочников не затрагивает матрицу этапов/актов
        extended.Stages.Should().HaveCount(defaults.Stages.Count,
            "добавление ChestType/HeroClass/Tab не должно менять матрицу этапов");
        extended.Acts.Should().HaveCount(defaults.Acts.Count);
        extended.Difficulties.Should().HaveCount(defaults.Difficulties.Count);
    }

    // ────────────────────────────────────────────────────────────
    // 2. IGameMechanics.Current / Reload
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void GameMechanics_DefaultCtor_CurrentIsDefaultConfig()
    {
        // Arrange / Act
        var mechanics = new GameMechanics();

        // Assert
        mechanics.Current.ChestTypes.Should().HaveCount(3);
        mechanics.Current.Tabs.Should().HaveCount(9);
        mechanics.Current.HeroClasses.Should().BeEmpty();
    }

    [Fact]
    public void GameMechanics_InitialConfigCtor_CurrentReturnsProvidedConfig()
    {
        // Arrange
        var extendedConfig = BuildExtendedConfig();

        // Act
        var mechanics = new GameMechanics(extendedConfig);

        // Assert
        mechanics.Current.ChestTypes.Should().HaveCount(4);
        mechanics.Current.Tabs.Should().HaveCount(10);
        mechanics.Current.HeroClasses.Should().HaveCount(1);
    }

    [Fact]
    public void GameMechanics_Reload_CurrentUpdatesToExtendedConfig()
    {
        // Arrange
        var mechanics = new GameMechanics(); // дефолт
        var extendedConfig = BuildExtendedConfig();

        // Act
        mechanics.Reload(extendedConfig);

        // Assert: Current отражает новый конфиг сразу после Reload
        mechanics.Current.ChestTypes.Should().HaveCount(4);
        mechanics.Current.Tabs.Should().HaveCount(10);
        mechanics.Current.HeroClasses.Should().HaveCount(1);
    }

    [Fact]
    public void GameMechanics_Reload_WithNullThrows()
    {
        // Arrange
        var mechanics = new GameMechanics();

        // Act / Assert
        var act = () => mechanics.Reload(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ────────────────────────────────────────────────────────────
    // 3. StageAggregateCalculator — чест-рейты для расширенного набора chestTypeIds
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void StageAggregateCalculator_Compute_WithExtendedChestTypeIds_ProducesRatesForAllIds()
    {
        // Arrange: забег с сундуками типов 1, 2, 3, 4 (расширенный конфиг)
        var chests = new[]
        {
            Chest(chestTypeId: 1, count: 5),
            Chest(chestTypeId: 2, count: 3),
            Chest(chestTypeId: 3, count: 1),
            Chest(chestTypeId: 4, count: 2), // новый тип "green"
        };
        var runs = new List<StageRun>
        {
            Run(stageId: 1, durationSeconds: 3600, goldGained: 10_000, xpGained: 5_000, chests: chests),
        };

        // chestTypeIds из расширенного конфига — все 4 типа
        int[] allChestTypeIds = [1, 2, 3, 4];

        var sut = new StageAggregateCalculator();

        // Act
        var agg = sut.Compute(
            stageId: 1,
            runs: runs,
            recentWindowSize: 5,
            chestTypeIds: allChestTypeIds);

        // Assert: ChestRates содержат строки для всех 4 типов (никакого хардкода 1-3)
        agg.ChestRates.Should().HaveCount(4,
            "Calculator должен создавать запись для каждого Id из chestTypeIds, не только 1-3");
        agg.ChestRates.Should().Contain(r => r.ChestTypeId == 4,
            "новый тип 'green' (Id=4) должен присутствовать в результате");

        // Проверяем конкретные темпы для нового типа:
        // count=2 за 3600 сек → 2/3600*3600 = 2.0 в час
        var greenRate = agg.ChestRates.Single(r => r.ChestTypeId == 4);
        greenRate.RatePerHour.Should().BeApproximately(2.0, 1e-6,
            "темп нового типа сундука = count/duration*3600 = 2.0/час");
    }

    [Fact]
    public void StageAggregateCalculator_Compute_DefaultChestTypeIds_RatesAreCorrect()
    {
        // Arrange: два забега с дефолтными типами сундуков Id=1,2,3
        var run1Chests = new[] { Chest(1, 10), Chest(2, 4), Chest(3, 2) };
        var run2Chests = new[] { Chest(1, 8),  Chest(2, 2), Chest(3, 0) };

        var runs = new List<StageRun>
        {
            Run(stageId: 5, durationSeconds: 3600, goldGained: 5_000, xpGained: 2_000, chests: run1Chests),
            Run(stageId: 5, durationSeconds: 1800, goldGained: 2_500, xpGained: 1_000, chests: run2Chests),
        };

        int[] defaultChestTypeIds = [1, 2, 3];
        var sut = new StageAggregateCalculator();

        // Act
        var agg = sut.Compute(5, runs, recentWindowSize: 5, chestTypeIds: defaultChestTypeIds);

        // Assert: chest Id=1: (10+8)/(3600+1800)*3600 = 18/5400*3600 = 12.0/час
        var brownRate = agg.ChestRates.Single(r => r.ChestTypeId == 1);
        brownRate.RatePerHour.Should().BeApproximately(12.0, 1e-6);

        // chest Id=3: (2+0)/(5400)*3600 ≈ 1.333/час
        var redRate = agg.ChestRates.Single(r => r.ChestTypeId == 3);
        redRate.RatePerHour.Should().BeApproximately(2.0 / 5400.0 * 3600.0, 1e-6);
    }

    [Fact]
    public void StageAggregateCalculator_Compute_NewChestTypeNotInAnyRun_RateIsZero()
    {
        // Arrange: забег только с типами 1 и 2; передаём Id=4 в chestTypeIds
        var runs = new List<StageRun>
        {
            Run(stageId: 3, durationSeconds: 3600, goldGained: 7_000, xpGained: 3_000,
                chests: new[] { Chest(1, 6), Chest(2, 2) }),
        };

        int[] extendedChestTypeIds = [1, 2, 4]; // 4 = новый тип, в этом забеге не встречался
        var sut = new StageAggregateCalculator();

        // Act
        var agg = sut.Compute(3, runs, recentWindowSize: 5, chestTypeIds: extendedChestTypeIds);

        // Assert: нет данных по типу 4 → темп = 0.0 (не исключение, не null)
        var greenRate = agg.ChestRates.Single(r => r.ChestTypeId == 4);
        greenRate.RatePerHour.Should().Be(0.0,
            "новый тип сундука, не встречавшийся в забегах, должен давать темп 0, не ошибку");
    }

    [Fact]
    public void StageAggregateCalculator_Compute_EmptyChestTypeIds_NoChestRates()
    {
        // Arrange: конфиг без сундуков (или передаём пустой список)
        var runs = new List<StageRun>
        {
            Run(stageId: 1, durationSeconds: 3600, goldGained: 5_000, xpGained: 2_000),
        };

        var sut = new StageAggregateCalculator();

        // Act
        var agg = sut.Compute(1, runs, recentWindowSize: 5, chestTypeIds: Array.Empty<int>());

        // Assert
        agg.ChestRates.Should().BeEmpty(
            "пустой список chestTypeIds = расширение механик без сундуков, ChestRates пуст");
    }

    // ────────────────────────────────────────────────────────────
    // 4. Config-driven: новый акт в конфиге — StageId парсится корректно
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void ExtendedConfig_NewAct_StagesIncludeNewActStages()
    {
        // Arrange: строим конфиг с 4-м актом (Act 4)
        var defaults = GameMechanicsConfig.CreateDefault();
        var actsWithFour = new List<Act>(defaults.Acts)
        {
            new Act { Id = 4, Number = 4, DisplayName = "Act 4", SortOrder = 4 },
        };

        // Строим матрицу этапов вручную для нового акта (10 × 2 = 20 новых)
        var newStages = new List<Stage>(defaults.Stages);
        int nextId = defaults.Stages.Count + 1;
        foreach (var diff in defaults.Difficulties)
        {
            for (int n = 1; n <= 10; n++)
            {
                newStages.Add(new Stage { Id = nextId++, ActId = 4, DifficultyId = diff.Id, Number = n });
            }
        }

        var extendedConfig = new GameMechanicsConfig(
            chestTypes:          defaults.ChestTypes,
            heroClasses:         defaults.HeroClasses,
            tabs:                defaults.Tabs,
            acts:                actsWithFour,
            difficulties:        defaults.Difficulties,
            stages:              newStages,
            fieldSourceBindings: defaults.FieldSourceBindings);

        // Assert: конфиг содержит 4 акта и 60+20=80 этапов
        extendedConfig.Acts.Should().HaveCount(4);
        extendedConfig.Stages.Should().HaveCount(80);

        // Этапы Act 4 присутствуют
        extendedConfig.Stages.Should().Contain(s => s.ActId == 4,
            "новый акт Act 4 должен присутствовать в матрице этапов");

        // Механика не содержит хардкода на 3 акта — конфиг принимает любое количество
        var mechanics = new GameMechanics(extendedConfig);
        mechanics.Current.Acts.Should().HaveCount(4);
    }

    [Fact]
    public void ExtendedConfig_SoftDeletedChestType_StillPresentInConfig()
    {
        // Arrange: "red" (Id=3) помечен как неактивный (soft-delete), но остаётся в конфиге
        var defaults = GameMechanicsConfig.CreateDefault();
        var chestTypes = defaults.ChestTypes.ToList();
        // Создаём копию с IsActive=false для Id=3 — init-запись нельзя мутировать,
        // поэтому создаём новый объект с нужными значениями
        var inactiveRed = new ChestType
        {
            Id          = 3,
            Key         = "red",
            DisplayName = "Легендарный",
            ColorLabel  = "красный",
            SortOrder   = 3,
            IsActive    = false, // soft-deleted
        };
        chestTypes[2] = inactiveRed;

        var configWithSoftDelete = new GameMechanicsConfig(
            chestTypes:          chestTypes,
            heroClasses:         defaults.HeroClasses,
            tabs:                defaults.Tabs,
            acts:                defaults.Acts,
            difficulties:        defaults.Difficulties,
            stages:              defaults.Stages,
            fieldSourceBindings: defaults.FieldSourceBindings);

        // Assert: конфиг содержит все 3 типа (включая неактивный) — история ссылается на Id
        configWithSoftDelete.ChestTypes.Should().HaveCount(3,
            "soft-delete не удаляет запись из конфига — история сохраняет FK");
        configWithSoftDelete.ChestTypes.Should().Contain(ct => ct.Id == 3 && !ct.IsActive,
            "неактивный тип сундука должен быть доступен для разрешения FK в истории");
    }
}
