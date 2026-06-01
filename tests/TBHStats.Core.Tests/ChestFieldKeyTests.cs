namespace TBHStats.Core.Tests;

using FluentAssertions;
using TBHStats.Core.Parsing;
using Xunit;

/// <summary>
/// Unit-тесты для <see cref="ChestFieldKey"/>.
/// Используются только реальные объекты — без моков и фейков.
/// </summary>
public sealed class ChestFieldKeyTests
{
    // =========================================================================
    // Happy path — базовые ключи (без @N)
    // =========================================================================

    [Fact]
    public void TryParse_BasicKey_ReturnsTrueWithNullSlotCount()
    {
        bool result = ChestFieldKey.TryParse("chest:brown", out string chestKey, out int? slotCount);

        result.Should().BeTrue(because: "\"chest:brown\" является корректным базовым ключом сундука");
        chestKey.Should().Be("brown");
        slotCount.Should().BeNull(because: "базовый ключ без @N должен давать null slotCount");
    }

    // =========================================================================
    // Happy path — ключи с @N (калиброванные)
    // =========================================================================

    [Theory]
    [InlineData("chest:blue@2",  "blue",  2)]
    [InlineData("chest:red@3",   "red",   3)]
    [InlineData("chest:brown@1", "brown", 1)]
    public void TryParse_KeyWithSlotSuffix_ReturnsTrueWithCorrectValues(
        string input, string expectedKey, int expectedSlot)
    {
        bool result = ChestFieldKey.TryParse(input, out string chestKey, out int? slotCount);

        result.Should().BeTrue(because: $"\"{input}\" является корректным ключом с суффиксом @N");
        chestKey.Should().Be(expectedKey);
        slotCount.Should().Be(expectedSlot);
    }

    // =========================================================================
    // Невалидный ввод — false + пустая строка + null slotCount
    // =========================================================================

    [Theory]
    [InlineData("gold",              "нет префикса chest:")]
    [InlineData("",                  "пустая строка")]
    [InlineData("chest:",            "chestKey пуст после префикса")]
    [InlineData("chest:@2",          "chestKey пуст перед @")]
    [InlineData("chest:brown@",      "nPart пуст после @")]
    [InlineData("chest:brown@0",     "N=0 < 1")]
    [InlineData("chest:brown@x",     "N не является числом")]
    [InlineData("chest:brown@-1",    "NumberStyles.None отклоняет знак минус")]
    [InlineData("chest:brown@2x",    "суффикс содержит нечисловые символы")]
    [InlineData("Chest:brown",       "заглавная C не совпадает при Ordinal-сравнении")]
    public void TryParse_InvalidInput_ReturnsFalseAndEmptyChestKey(string input, string because)
    {
        bool result = ChestFieldKey.TryParse(input, out string chestKey, out int? slotCount);

        result.Should().BeFalse(because: because);
        chestKey.Should().BeEmpty(because: "при неудаче chestKey должен быть пустой строкой");
        slotCount.Should().BeNull(because: "при неудаче slotCount должен быть null");
    }

    // =========================================================================
    // Явная проверка out-параметров для ключевых граничных кейсов
    // =========================================================================

    [Fact]
    public void TryParse_EmptyChestPartAfterPrefix_ChestKeyIsEmpty()
    {
        bool result = ChestFieldKey.TryParse("chest:", out string chestKey, out int? slotCount);

        result.Should().BeFalse();
        chestKey.Should().BeEmpty();
        slotCount.Should().BeNull();
    }

    [Fact]
    public void TryParse_UpperCasePrefix_ReturnsFalseOrdinalSensitive()
    {
        // Регистр важен: "Chest:brown" не начинается с "chest:" при StringComparison.Ordinal
        bool result = ChestFieldKey.TryParse("Chest:brown", out string chestKey, out int? slotCount);

        result.Should().BeFalse(because: "StringComparison.Ordinal: 'C' ≠ 'c'");
        chestKey.Should().BeEmpty();
        slotCount.Should().BeNull();
    }

    [Fact]
    public void TryParse_SlotCountZero_ReturnsFalse()
    {
        // N=0 отклоняется условием n < 1
        bool result = ChestFieldKey.TryParse("chest:brown@0", out string chestKey, out int? slotCount);

        result.Should().BeFalse(because: "N=0 нарушает ограничение N ≥ 1");
        chestKey.Should().BeEmpty();
        slotCount.Should().BeNull();
    }

    [Fact]
    public void TryParse_SlotCountOne_ReturnsTrueSlotCountOne()
    {
        // Граничный случай: минимально допустимый @N = 1
        bool result = ChestFieldKey.TryParse("chest:brown@1", out string chestKey, out int? slotCount);

        result.Should().BeTrue();
        chestKey.Should().Be("brown");
        slotCount.Should().Be(1);
    }
}
