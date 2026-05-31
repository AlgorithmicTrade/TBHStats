using System;
using System.Linq;
using FluentAssertions;
using TBHStats.Capture.WindowTracking;
using Xunit;

namespace TBHStats.Capture.Tests;

/// <summary>
/// Регрессионные тесты <see cref="GameWindowTrackerOptions"/> и базовой устойчивости
/// <see cref="GameWindowTracker"/>.
/// </summary>
/// <remarks>
/// Главный регресс: порядок инициализации статических полей. Если статическое поле
/// <c>Default</c> объявлено ДО <c>DefaultTitleHints</c>, то <c>Default.WindowTitleHints</c>
/// получает <see langword="null"/> (поле-источник ещё не инициализировано в текстовом порядке),
/// что приводило к <see cref="System.NullReferenceException"/> в
/// <see cref="GameWindowTracker.FindGameWindow"/> на каждом кадре и вечному статусу «Игра не найдена».
/// </remarks>
public sealed class GameWindowTrackerOptionsTests
{
    [Fact]
    public void Default_WindowTitleHints_IsNotNullOrEmpty()
    {
        GameWindowTrackerOptions.Default.WindowTitleHints
            .Should().NotBeNull("Default не должен зависеть от порядка статической инициализации")
            .And.NotBeEmpty();
    }

    [Fact]
    public void Default_WindowTitleHints_ContainsExpectedHints()
    {
        GameWindowTrackerOptions.Default.WindowTitleHints
            .Should().Contain("TaskBarHero")
            .And.Contain("Task Bar Hero");
    }

    [Fact]
    public void Default_WindowTitleHints_HasNoNullElements()
    {
        GameWindowTrackerOptions.Default.WindowTitleHints
            .Should().NotContainNulls();
    }

    [Theory]
    [InlineData("TBHStats")]                                              // собственное окно приложения
    [InlineData("t051-acceptance-checklist.md - TBHStats - Visual Studio Code")] // окно редактора
    public void Default_WindowTitleHints_DoNotFalseMatchOwnAppOrEditor(string foreignTitle)
    {
        // Регресс: слишком короткая подсказка ("TBH") давала ложное совпадение с окном
        // самого приложения / редактора, и захватывалось не то окно (пустые значения в виджете).
        bool anyMatch = GameWindowTrackerOptions.Default.WindowTitleHints
            .Any(h => foreignTitle.Contains(h, StringComparison.OrdinalIgnoreCase));

        anyMatch.Should().BeFalse(
            "подсказки заголовка не должны совпадать с окнами, не являющимися игрой Task Bar Hero");
    }

    [Fact]
    public void ParameterlessCtor_FindGameWindow_DoesNotThrow()
    {
        // Конструктор по умолчанию использует GameWindowTrackerOptions.Default.
        // До исправления static-init порядка здесь возникал NullReferenceException
        // внутри EnumWindows-колбэка при переборе реальных окон системы.
        var sut = new GameWindowTracker();

        Action act = () => sut.FindGameWindow();

        act.Should().NotThrow();
    }
}
