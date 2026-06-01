namespace TBHStats.Core.Tests;

using FluentAssertions;
using TBHStats.Core.Parsing;
using Xunit;

/// <summary>
/// Unit-тесты для <see cref="ChestLayoutResolver"/>.
/// Используются только реальные объекты — без моков и фейков.
/// Условные id типов: brown=1, blue=2, red=3.
/// </summary>
public sealed class ChestLayoutResolverTests
{
    private readonly ChestLayoutResolver _sut = new();

    // =========================================================================
    // Кейс 1 — пустой список
    // =========================================================================

    [Fact]
    public void Resolve_EmptyList_ReturnsChosenZeroAndEmptyCounts()
    {
        ChestLayoutResolution result = _sut.Resolve(Array.Empty<ChestLayoutReading>());

        result.ChosenSlotCount.Should().Be(0);
        result.Counts.Should().BeEmpty();
    }

    // =========================================================================
    // Кейс 2 — N=1, один тип
    // =========================================================================

    [Fact]
    public void Resolve_SingleTypeSingleSlot_ReturnsChosenOne()
    {
        // readings: brown=1@1, count=4 → litCount=1==N=1 → точное совпадение
        var readings = new[]
        {
            new ChestLayoutReading(ChestTypeId: 1, SlotCount: 1, Count: 4),
        };

        ChestLayoutResolution result = _sut.Resolve(readings);

        result.ChosenSlotCount.Should().Be(1);
        result.Counts.Should().BeEquivalentTo(new Dictionary<int, int> { [1] = 4 });
    }

    // =========================================================================
    // Кейс 3 — N=2, два горящих типа плюс погасшие соседних раскладок
    // =========================================================================

    [Fact]
    public void Resolve_TwoTypesWithSlot2_ReturnsChosenTwoIgnoresDarkReadings()
    {
        // Горящие: brown@2=5, blue@2=3 → litCount=2==N=2 → точное совпадение
        // Погасшие соседей: brown@1=0, brown@3=0, blue@3=0 → не влияют
        var readings = new[]
        {
            new ChestLayoutReading(ChestTypeId: 1, SlotCount: 2, Count: 5),
            new ChestLayoutReading(ChestTypeId: 2, SlotCount: 2, Count: 3),
            new ChestLayoutReading(ChestTypeId: 1, SlotCount: 1, Count: 0),
            new ChestLayoutReading(ChestTypeId: 1, SlotCount: 3, Count: 0),
            new ChestLayoutReading(ChestTypeId: 2, SlotCount: 3, Count: 0),
        };

        ChestLayoutResolution result = _sut.Resolve(readings);

        result.ChosenSlotCount.Should().Be(2);
        result.Counts.Should().BeEquivalentTo(new Dictionary<int, int> { [1] = 5, [2] = 3 });
    }

    // =========================================================================
    // Кейс 4 — N=3, три горящих типа
    // =========================================================================

    [Fact]
    public void Resolve_ThreeTypesWithSlot3_ReturnsChosenThree()
    {
        // brown@3=6, blue@3=2, red@3=1 → litCount=3==N=3 → точное совпадение
        var readings = new[]
        {
            new ChestLayoutReading(ChestTypeId: 1, SlotCount: 3, Count: 6),
            new ChestLayoutReading(ChestTypeId: 2, SlotCount: 3, Count: 2),
            new ChestLayoutReading(ChestTypeId: 3, SlotCount: 3, Count: 1),
        };

        ChestLayoutResolution result = _sut.Resolve(readings);

        result.ChosenSlotCount.Should().Be(3);
        result.Counts.Should().HaveCount(3);
        result.Counts.Should().BeEquivalentTo(new Dictionary<int, int> { [1] = 6, [2] = 2, [3] = 1 });
    }

    // =========================================================================
    // Кейс 5 — неоднозначность: оба N=1 и N=2 дают точное совпадение
    //          Приоритет большему N → выбирается N=2
    // =========================================================================

    [Fact]
    public void Resolve_BothSlot1AndSlot2ExactMatch_PrefersLargerN()
    {
        // N=1: brown@1=4 → litCount=1==1 → точное совпадение
        // N=2: brown@2=3, blue@2=1 → litCount=2==2 → точное совпадение
        // Перебор от большего N: N=2 обрабатывается первым → выбирается N=2
        var readings = new[]
        {
            new ChestLayoutReading(ChestTypeId: 1, SlotCount: 1, Count: 4),
            new ChestLayoutReading(ChestTypeId: 1, SlotCount: 2, Count: 3),
            new ChestLayoutReading(ChestTypeId: 2, SlotCount: 2, Count: 1),
        };

        ChestLayoutResolution result = _sut.Resolve(readings);

        result.ChosenSlotCount.Should().Be(2);
        result.Counts.Should().BeEquivalentTo(new Dictionary<int, int> { [1] = 3, [2] = 1 });
    }

    // =========================================================================
    // Кейс 6 — fallback: нет N с точным litCount==N
    // =========================================================================

    [Fact]
    public void Resolve_NoExactMatchWithPartiallyLitSlot3_FallbackChoosesLargestN()
    {
        // N=3: brown@3=5, blue@3=2, red@3=0 → litCount=2≠3 → не точное совпадение
        // N=2: brown@2=0 → litCount=0 → не горит
        // Fallback: N=3 имеет litCount=2 > N=2 litCount=0 → Chosen=3
        // Counts содержит только горящие: {1:5, 2:2}, red исключается (Count=0)
        var readings = new[]
        {
            new ChestLayoutReading(ChestTypeId: 1, SlotCount: 3, Count: 5),
            new ChestLayoutReading(ChestTypeId: 2, SlotCount: 3, Count: 2),
            new ChestLayoutReading(ChestTypeId: 3, SlotCount: 3, Count: 0),
            new ChestLayoutReading(ChestTypeId: 1, SlotCount: 2, Count: 0),
        };

        ChestLayoutResolution result = _sut.Resolve(readings);

        result.ChosenSlotCount.Should().Be(3);
        result.Counts.Should().BeEquivalentTo(new Dictionary<int, int> { [1] = 5, [2] = 2 });
        result.Counts.Should().NotContainKey(3, because: "red имеет Count=0 и не входит в Counts");
    }

    // =========================================================================
    // Кейс 7 — все нули → ChosenSlotCount=0, Counts пуст
    // =========================================================================

    [Fact]
    public void Resolve_AllCountsZero_ReturnsChosenZeroAndEmptyCounts()
    {
        var readings = new[]
        {
            new ChestLayoutReading(ChestTypeId: 1, SlotCount: 1, Count: 0),
            new ChestLayoutReading(ChestTypeId: 1, SlotCount: 2, Count: 0),
            new ChestLayoutReading(ChestTypeId: 2, SlotCount: 2, Count: 0),
        };

        ChestLayoutResolution result = _sut.Resolve(readings);

        result.ChosenSlotCount.Should().Be(0);
        result.Counts.Should().BeEmpty();
    }

    // =========================================================================
    // Кейс 8 — Count==0 исключается из Counts (fallback с одним горящим)
    // =========================================================================

    [Fact]
    public void Resolve_PartiallyLitSlot2_CountsExcludeZeroEntries()
    {
        // N=2: brown@2=4 (горит), blue@2=0 (погас) → litCount=1≠2 → не точное совпадение
        // Fallback: N=2 litCount=1 > 0 → Chosen=2
        // Counts содержит только горящий: {1:4}, тип 2 (blue) не входит
        var readings = new[]
        {
            new ChestLayoutReading(ChestTypeId: 1, SlotCount: 2, Count: 4),
            new ChestLayoutReading(ChestTypeId: 2, SlotCount: 2, Count: 0),
        };

        ChestLayoutResolution result = _sut.Resolve(readings);

        result.ChosenSlotCount.Should().Be(2);
        result.Counts.Should().BeEquivalentTo(new Dictionary<int, int> { [1] = 4 });
        result.Counts.Should().NotContainKey(2, because: "blue имеет Count=0 и не входит в Counts");
    }

    // =========================================================================
    // Кейс 9 — дубликат ChestTypeId в одной раскладке: берётся последнее значение
    // =========================================================================

    [Fact]
    public void Resolve_DuplicateChestTypeIdSameSlot_LastValueWins()
    {
        // N=2: brown@2 встречается дважды (count=4, затем count=7), blue@2=3
        // BuildLitMap: map[1]=4 → map[1]=7 (перезапись последним); map[2]=3
        // litCount=2 (уникальных горящих типов)==N=2 → точное совпадение
        // Chosen=2, Counts={1:7, 2:3}
        var readings = new[]
        {
            new ChestLayoutReading(ChestTypeId: 1, SlotCount: 2, Count: 4),
            new ChestLayoutReading(ChestTypeId: 1, SlotCount: 2, Count: 7),
            new ChestLayoutReading(ChestTypeId: 2, SlotCount: 2, Count: 3),
        };

        ChestLayoutResolution result = _sut.Resolve(readings);

        result.ChosenSlotCount.Should().Be(2);
        result.Counts.Should().BeEquivalentTo(new Dictionary<int, int> { [1] = 7, [2] = 3 },
            because: "при дублирующемся ChestTypeId берётся последнее по порядку значение (count=7, а не 4)");
    }
}
