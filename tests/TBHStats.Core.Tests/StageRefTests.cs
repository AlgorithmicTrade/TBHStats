using FluentAssertions;
using TBHStats.Core.Models;
using Xunit;

namespace TBHStats.Core.Tests;

public sealed class StageRefTests
{
    // -----------------------------------------------------------------------
    // Previous() — хэппи-пути
    // -----------------------------------------------------------------------

    [Fact]
    public void Previous_StageNumberGreaterThanOne_ReturnsSameActAndDecrementedStage()
    {
        // Arrange
        var sut = new StageRef(2, "normal", 5);

        // Act
        StageRef? result = sut.Previous();

        // Assert
        result.Should().NotBeNull();
        result!.Value.ActNumber.Should().Be(2);
        result.Value.DifficultyKey.Should().Be("normal");
        result.Value.StageNumber.Should().Be(4);
    }

    [Fact]
    public void Previous_StageNumberOneWithPreviousAct_ReturnsLastStageOfPreviousAct()
    {
        // Arrange — перенос на предыдущий акт, stagesPerAct по умолчанию 10
        var sut = new StageRef(2, "normal", 1);

        // Act
        StageRef? result = sut.Previous();

        // Assert
        result.Should().NotBeNull();
        result!.Value.ActNumber.Should().Be(1);
        result.Value.DifficultyKey.Should().Be("normal");
        result.Value.StageNumber.Should().Be(10);
    }

    [Fact]
    public void Previous_FirstStageOfFirstAct_ReturnsNull()
    {
        // Arrange — этап 1 первого акта, предыдущего нет
        var sut = new StageRef(1, "normal", 1);

        // Act
        StageRef? result = sut.Previous();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Previous_StageNumberOneHigherActNightmareKey_ReturnsPreviousActWithSameDifficulty()
    {
        // Arrange — сложность «nightmare» должна сохраняться при переносе
        var sut = new StageRef(3, "nightmare", 1);

        // Act
        StageRef? result = sut.Previous();

        // Assert
        result.Should().NotBeNull();
        result!.Value.ActNumber.Should().Be(2);
        result.Value.DifficultyKey.Should().Be("nightmare");
        result.Value.StageNumber.Should().Be(10);
    }

    [Fact]
    public void Previous_LastStageOfAct_ReturnsOneBeforeLastWithinSameAct()
    {
        // Arrange
        var sut = new StageRef(1, "normal", 10);

        // Act
        StageRef? result = sut.Previous();

        // Assert
        result.Should().NotBeNull();
        result!.Value.ActNumber.Should().Be(1);
        result.Value.DifficultyKey.Should().Be("normal");
        result.Value.StageNumber.Should().Be(9);
    }

    // -----------------------------------------------------------------------
    // Previous(stagesPerAct) — кастомный параметр
    // -----------------------------------------------------------------------

    [Fact]
    public void Previous_CustomStagesPerAct_UsesCustomValueForCarryover()
    {
        // Arrange — stagesPerAct = 8
        var sut = new StageRef(2, "normal", 1);

        // Act
        StageRef? result = sut.Previous(stagesPerAct: 8);

        // Assert
        result.Should().NotBeNull();
        result!.Value.ActNumber.Should().Be(1);
        result.Value.DifficultyKey.Should().Be("normal");
        result.Value.StageNumber.Should().Be(8);
    }

    [Fact]
    public void Previous_StagesPerActZero_ReturnsNull()
    {
        // Arrange — stagesPerAct < 1 → null даже при наличии предыдущего акта
        var sut = new StageRef(2, "normal", 1);

        // Act
        StageRef? result = sut.Previous(stagesPerAct: 0);

        // Assert
        result.Should().BeNull();
    }
}
