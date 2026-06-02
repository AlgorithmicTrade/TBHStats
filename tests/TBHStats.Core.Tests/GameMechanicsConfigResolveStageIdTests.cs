namespace TBHStats.Core.Tests;

using FluentAssertions;
using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;
using Xunit;

/// <summary>
/// Тесты резолвера StageId в <see cref="GameMechanicsConfig.ResolveStageId"/>
/// (T063 Phase 3, ADR-009, config-driven справочники).
/// Используется реальный <see cref="GameMechanicsConfig.CreateDefault()"/> — без моков.
/// </summary>
public sealed class GameMechanicsConfigResolveStageIdTests
{
    private readonly GameMechanicsConfig _cfg = GameMechanicsConfig.CreateDefault();

    // Получаем ожидаемый Id прямо из конфига, чтобы тест не хардкодил магические числа
    // и оставался валидным при смене порядка BuildStages.

    [Fact]
    public void ResolveStageId_Act1_Normal_Stage1_ReturnsFirstStageId()
    {
        // Arrange: Act1/normal/1 — первый этап в дефолтном конфиге
        Stage expected = _cfg.Stages.First(s =>
            s.ActId        == _cfg.Acts.First(a => a.Number == 1).Id &&
            s.DifficultyId == _cfg.Difficulties.First(d => d.Key == "normal").Id &&
            s.Number       == 1);

        var stageRef = new StageRef(ActNumber: 1, DifficultyKey: "normal", StageNumber: 1);

        // Act
        int? result = _cfg.ResolveStageId(stageRef);

        // Assert
        result.Should().Be(expected.Id,
            "Act1/normal/1 должен резолвиться в первый Stage дефолтного конфига");
    }

    [Fact]
    public void ResolveStageId_Act1_Normal_Stage10_ReturnsCorrectId()
    {
        // Arrange: последний этап акта 1 / normal
        Stage expected = _cfg.Stages.First(s =>
            s.ActId        == _cfg.Acts.First(a => a.Number == 1).Id &&
            s.DifficultyId == _cfg.Difficulties.First(d => d.Key == "normal").Id &&
            s.Number       == 10);

        var stageRef = new StageRef(1, "normal", 10);

        // Act
        int? result = _cfg.ResolveStageId(stageRef);

        // Assert
        result.Should().Be(expected.Id, "Act1/normal/10 должен корректно резолвиться");
    }

    [Fact]
    public void ResolveStageId_Act2_Normal_Stage5_ReturnsCorrectId()
    {
        // Arrange: середина акта 2 / normal
        Stage expected = _cfg.Stages.First(s =>
            s.ActId        == _cfg.Acts.First(a => a.Number == 2).Id &&
            s.DifficultyId == _cfg.Difficulties.First(d => d.Key == "normal").Id &&
            s.Number       == 5);

        var stageRef = new StageRef(2, "normal", 5);

        // Act
        int? result = _cfg.ResolveStageId(stageRef);

        // Assert
        result.Should().Be(expected.Id, "Act2/normal/5 должен корректно резолвиться");
    }

    [Fact]
    public void ResolveStageId_Act3_Normal_Stage1_ReturnsCorrectId()
    {
        // Arrange: первый этап акта 3 / normal
        Stage expected = _cfg.Stages.First(s =>
            s.ActId        == _cfg.Acts.First(a => a.Number == 3).Id &&
            s.DifficultyId == _cfg.Difficulties.First(d => d.Key == "normal").Id &&
            s.Number       == 1);

        var stageRef = new StageRef(3, "normal", 1);

        // Act
        int? result = _cfg.ResolveStageId(stageRef);

        // Assert
        result.Should().Be(expected.Id, "Act3/normal/1 должен корректно резолвиться");
    }

    [Fact]
    public void ResolveStageId_Act1_Nightmare_Stage1_ReturnsCorrectId()
    {
        // Arrange: Act1/nightmare/1 — сложность nightmare
        Stage expected = _cfg.Stages.First(s =>
            s.ActId        == _cfg.Acts.First(a => a.Number == 1).Id &&
            s.DifficultyId == _cfg.Difficulties.First(d => d.Key == "nightmare").Id &&
            s.Number       == 1);

        var stageRef = new StageRef(1, "nightmare", 1);

        // Act
        int? result = _cfg.ResolveStageId(stageRef);

        // Assert
        result.Should().Be(expected.Id, "Act1/nightmare/1 должен корректно резолвиться");
    }

    [Fact]
    public void ResolveStageId_Act2_Nightmare_Stage7_ReturnsCorrectId()
    {
        // Arrange: Act2/nightmare/7
        Stage expected = _cfg.Stages.First(s =>
            s.ActId        == _cfg.Acts.First(a => a.Number == 2).Id &&
            s.DifficultyId == _cfg.Difficulties.First(d => d.Key == "nightmare").Id &&
            s.Number       == 7);

        var stageRef = new StageRef(2, "nightmare", 7);

        // Act
        int? result = _cfg.ResolveStageId(stageRef);

        // Assert
        result.Should().Be(expected.Id, "Act2/nightmare/7 должен корректно резолвиться");
    }

    [Fact]
    public void ResolveStageId_DifficultyKeyCaseInsensitive_ReturnsCorrectId()
    {
        // Arrange: DifficultyKey "NORMAL" (верхний регистр) — должен матчиться OrdinalIgnoreCase
        Stage expected = _cfg.Stages.First(s =>
            s.ActId        == _cfg.Acts.First(a => a.Number == 1).Id &&
            s.DifficultyId == _cfg.Difficulties.First(d => d.Key == "normal").Id &&
            s.Number       == 3);

        var stageRef = new StageRef(1, "NORMAL", 3);

        // Act
        int? result = _cfg.ResolveStageId(stageRef);

        // Assert
        result.Should().Be(expected.Id, "Регистр DifficultyKey должен игнорироваться (OrdinalIgnoreCase)");
    }

    [Fact]
    public void ResolveStageId_UnknownActNumber_ReturnsNull()
    {
        // Arrange: ActNumber=99 — такого акта нет в дефолтном конфиге
        var stageRef = new StageRef(99, "normal", 1);

        // Act
        int? result = _cfg.ResolveStageId(stageRef);

        // Assert
        result.Should().BeNull("несуществующий ActNumber должен давать null");
    }

    [Fact]
    public void ResolveStageId_UnknownDifficultyKey_ReturnsNull()
    {
        // Arrange: DifficultyKey="bogus" — такой сложности нет
        var stageRef = new StageRef(1, "bogus", 1);

        // Act
        int? result = _cfg.ResolveStageId(stageRef);

        // Assert
        result.Should().BeNull("несуществующий DifficultyKey должен давать null");
    }
}
