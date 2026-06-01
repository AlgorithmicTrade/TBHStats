namespace TBHStats.Core.Mechanics;

using TBHStats.Core.Models;

/// <summary>
/// Агрегат игровой конфигурации: справочники сундуков, классов героя, вкладок,
/// актов, сложностей, этапов и привязок источников полей (FR-021, ADR-009).
/// Используется <see cref="IGameMechanics"/> как единый конфиг-объект.
/// </summary>
public sealed class GameMechanicsConfig
{
    /// <summary>Справочник типов сундуков (FR-002, ADR-011).</summary>
    public IReadOnlyList<ChestType> ChestTypes { get; init; }

    /// <summary>Справочник классов героя (ADR-009). По умолчанию — пустой (открыты динамически).</summary>
    public IReadOnlyList<HeroClass> HeroClasses { get; init; }

    /// <summary>Справочник вкладок интерфейса (9 разделов, ADR-008, FR-002b).</summary>
    public IReadOnlyList<Tab> Tabs { get; init; }

    /// <summary>Справочник актов (3 акта, ADR-008).</summary>
    public IReadOnlyList<Act> Acts { get; init; }

    /// <summary>Справочник уровней сложности (normal/nightmare, ADR-008).</summary>
    public IReadOnlyList<Difficulty> Difficulties { get; init; }

    /// <summary>Полная матрица этапов: 3 акта × 2 сложности × 10 = 60 этапов (ADR-008).</summary>
    public IReadOnlyList<Stage> Stages { get; init; }

    /// <summary>Привязки полей к источникам данных (FR-002b).</summary>
    public IReadOnlyList<FieldSourceBinding> FieldSourceBindings { get; init; }

    /// <summary>
    /// Создать экземпляр конфига.
    /// Все коллекции обязательны — передай <see cref="Array.Empty{T}()"/> если список пуст.
    /// </summary>
    public GameMechanicsConfig(
        IReadOnlyList<ChestType> chestTypes,
        IReadOnlyList<HeroClass> heroClasses,
        IReadOnlyList<Tab> tabs,
        IReadOnlyList<Act> acts,
        IReadOnlyList<Difficulty> difficulties,
        IReadOnlyList<Stage> stages,
        IReadOnlyList<FieldSourceBinding> fieldSourceBindings)
    {
        ChestTypes = chestTypes;
        HeroClasses = heroClasses;
        Tabs = tabs;
        Acts = acts;
        Difficulties = difficulties;
        Stages = stages;
        FieldSourceBindings = fieldSourceBindings;
    }

    /// <summary>
    /// Создать дефолтный конфиг игры Task Bar Hero.
    /// Источник истины: data-model «Game UI Map» + «Дефолтные Field Source Bindings».
    /// </summary>
    /// <returns>Полностью заполненный конфиг с 3 сундуками, 9 вкладками, 60 этапами.</returns>
    public static GameMechanicsConfig CreateDefault()
    {
        // ── ChestTypes ────────────────────────────────────────────────────────
        ChestType[] chestTypes =
        [
            new ChestType { Id = 1, Key = "brown", DisplayName = "Базовый",     ColorLabel = "коричневый", SortOrder = 1, IsActive = true, PanelColor = new PanelColor(255, 255, 255) },
            new ChestType { Id = 2, Key = "blue",  DisplayName = "Редкий",      ColorLabel = "синий",      SortOrder = 2, IsActive = true, PanelColor = new PanelColor(190, 220, 238) },
            new ChestType { Id = 3, Key = "red",   DisplayName = "Легендарный", ColorLabel = "красный",    SortOrder = 3, IsActive = true, PanelColor = new PanelColor(236, 133,  41) },
        ];

        // ── Tabs (9 разделов) ─────────────────────────────────────────────────
        Tab[] tabs =
        [
            new Tab { Id = 1, Key = "hero",       DisplayName = "Hero",       RecognitionText = "Hero",       SortOrder = 1, IsActive = true, IsDataSource = true  },
            new Tab { Id = 2, Key = "stash",      DisplayName = "Stash",      RecognitionText = "Stash",      SortOrder = 2, IsActive = true, IsDataSource = false },
            new Tab { Id = 3, Key = "status",     DisplayName = "Status",     RecognitionText = "Status",     SortOrder = 3, IsActive = true, IsDataSource = true  },
            new Tab { Id = 4, Key = "runes",      DisplayName = "Runes",      RecognitionText = "Runes",      SortOrder = 4, IsActive = true, IsDataSource = false },
            new Tab { Id = 5, Key = "cube",       DisplayName = "Cube",       RecognitionText = "Cube",       SortOrder = 5, IsActive = true, IsDataSource = false },
            new Tab { Id = 6, Key = "portal",     DisplayName = "Portal",     RecognitionText = "Portal",     SortOrder = 6, IsActive = true, IsDataSource = true  },
            new Tab { Id = 7, Key = "settings",   DisplayName = "Settings",   RecognitionText = "Settings",   SortOrder = 7, IsActive = true, IsDataSource = false },
            new Tab { Id = 8, Key = "tradeship",  DisplayName = "Trade ship", RecognitionText = "Trade ship", SortOrder = 8, IsActive = true, IsDataSource = false },
            new Tab { Id = 9, Key = "mailbox",    DisplayName = "Mail Box",   RecognitionText = "Mail Box",   SortOrder = 9, IsActive = true, IsDataSource = false },
        ];

        // ── Acts ──────────────────────────────────────────────────────────────
        Act[] acts =
        [
            new Act { Id = 1, Number = 1, DisplayName = "Act 1", SortOrder = 1 },
            new Act { Id = 2, Number = 2, DisplayName = "Act 2", SortOrder = 2 },
            new Act { Id = 3, Number = 3, DisplayName = "Act 3", SortOrder = 3 },
        ];

        // ── Difficulties ──────────────────────────────────────────────────────
        Difficulty[] difficulties =
        [
            new Difficulty { Id = 1, Key = "normal",    DisplayName = "Normal",    SortOrder = 1 },
            new Difficulty { Id = 2, Key = "nightmare", DisplayName = "Nightmare", SortOrder = 2 },
        ];

        // ── Stages: 3 × 2 × 10 = 60 ──────────────────────────────────────────
        Stage[] stages = BuildStages(acts, difficulties);

        // ── HeroClasses (пустой — классы открываются динамически) ────────────
        HeroClass[] heroClasses = [];

        // ── FieldSourceBindings ───────────────────────────────────────────────
        FieldSourceBinding[] baseBindings =
        [
            // Hero tab (Id=1)
            new FieldSourceBinding("gold",          FieldSource.Tab,      TabId: 1),

            // Status tab (Id=3)
            new FieldSourceBinding("xp",            FieldSource.Tab,      TabId: 3),
            new FieldSourceBinding("xpToLevel",     FieldSource.Tab,      TabId: 3),
            new FieldSourceBinding("xpPair",        FieldSource.Tab,      TabId: 3),
            new FieldSourceBinding("heroLevel",     FieldSource.Tab,      TabId: 3),
            new FieldSourceBinding("heroDamage",    FieldSource.Tab,      TabId: 3),
            new FieldSourceBinding("heroClass",     FieldSource.Tab,      TabId: 3),

            // Portal tab (Id=6)
            new FieldSourceBinding("stageId",       FieldSource.Tab,      TabId: 6),

            // MainZone (всегда видно)
            new FieldSourceBinding("stageProgress", FieldSource.MainZone, TabId: null),
            new FieldSourceBinding("stageTime",     FieldSource.MainZone, TabId: null),

            // Базовые ключи сундуков (итоговый семантический ключ; обратная совместимость).
            new FieldSourceBinding("chest:brown",   FieldSource.MainZone, TabId: null),
            new FieldSourceBinding("chest:blue",    FieldSource.MainZone, TabId: null),
            new FieldSourceBinding("chest:red",     FieldSource.MainZone, TabId: null),

            // Зонный детектор сундуков (ADR-023): одна ROI охватывает всю группу плашек.
            // Приоритетный источник chest-данных; per-ROI «chest:*@N»-биндинги сохранены как legacy.
            new FieldSourceBinding("chestZone",     FieldSource.MainZone, TabId: null),

            new FieldSourceBinding("nextLocation",  FieldSource.MainZone, TabId: null),
            new FieldSourceBinding("activeTab",     FieldSource.MainZone, TabId: null),
        ];

        // Калиброванные @N-ключи для каждого активного типа сундука × N ∈ {1,2,3}.
        // Генерируется программно (config-driven, ADR-009): новый тип сундука
        // добавляется только в chestTypes — @N-связки появятся автоматически.
        const int maxSlotCount = 3;
        List<FieldSourceBinding> slotBindings = new(chestTypes.Length * maxSlotCount);
        foreach (ChestType ct in chestTypes)
        {
            if (!ct.IsActive)
                continue;
            for (int n = 1; n <= maxSlotCount; n++)
                slotBindings.Add(new FieldSourceBinding($"chest:{ct.Key}@{n}", FieldSource.MainZone, TabId: null));
        }

        FieldSourceBinding[] bindings = [.. baseBindings, .. slotBindings];

        return new GameMechanicsConfig(
            chestTypes:          chestTypes,
            heroClasses:         heroClasses,
            tabs:                tabs,
            acts:                acts,
            difficulties:        difficulties,
            stages:              stages,
            fieldSourceBindings: bindings);
    }

    /// <summary>
    /// Генерация полной матрицы этапов: 3 акта × 2 сложности × 10 номеров = 60 записей.
    /// Id присваивается детерминированно: (actIndex * diffCount + diffIndex) * stageCount + stageNumber.
    /// </summary>
    private static Stage[] BuildStages(Act[] acts, Difficulty[] difficulties)
    {
        const int stageCount = 10;
        int diffCount = difficulties.Length; // 2
        Stage[] stages = new Stage[acts.Length * diffCount * stageCount]; // 60

        int index = 0;
        foreach (Act act in acts)
        {
            foreach (Difficulty diff in difficulties)
            {
                for (int number = 1; number <= stageCount; number++)
                {
                    stages[index] = new Stage
                    {
                        Id           = index + 1,   // 1..60
                        ActId        = act.Id,
                        DifficultyId = diff.Id,
                        Number       = number,
                    };
                    index++;
                }
            }
        }

        return stages;
    }
}
