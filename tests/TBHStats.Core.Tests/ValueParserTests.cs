namespace TBHStats.Core.Tests;

using FluentAssertions;
using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;
using TBHStats.Core.Parsing;
using Xunit;

/// <summary>
/// Unit-тесты для <see cref="ValueParser"/>.
/// Используются только реальные объекты — без моков и фейков.
/// </summary>
public sealed class ValueParserTests
{
    private readonly ValueParser _sut = new();
    private readonly GameMechanicsConfig _cfg = GameMechanicsConfig.CreateDefault();

    // =========================================================================
    // TryParseAbbreviatedNumber — happy path
    // =========================================================================

    [Theory]
    [InlineData("999",        999L)]
    [InlineData("0",          0L)]
    [InlineData("1,234",      1234L)]
    [InlineData("12,345,678", 12_345_678L)]
    [InlineData("1.2K",       1200L)]
    [InlineData("3.4M",       3_400_000L)]
    [InlineData("5B",         5_000_000_000L)]
    [InlineData("2.5T",       2_500_000_000_000L)]
    [InlineData("1.2 K",      1200L)]           // пробел между числом и суффиксом
    [InlineData("1.2k",       1200L)]           // строчный суффикс
    [InlineData("3.4m",       3_400_000L)]      // строчный суффикс
    [InlineData("5b",         5_000_000_000L)]  // строчный суффикс
    [InlineData("2.5t",       2_500_000_000_000L)] // строчный суффикс
    [InlineData("  999  ",    999L)]            // ведущие/хвостовые пробелы
    [InlineData("1000",       1000L)]
    public void TryParseAbbreviatedNumber_ValidInput_ReturnsTrueAndCorrectValue(string raw, long expected)
    {
        bool result = _sut.TryParseAbbreviatedNumber(raw, out long value);

        result.Should().BeTrue(because: $"строка '{raw}' является валидным idle-числом");
        value.Should().Be(expected);
    }

    [Fact]
    public void TryParseAbbreviatedNumber_IntegerWithoutSuffix_ReturnsExactValue()
    {
        bool result = _sut.TryParseAbbreviatedNumber("42", out long value);

        result.Should().BeTrue();
        value.Should().Be(42L);
    }

    [Fact]
    public void TryParseAbbreviatedNumber_LargeValidValue_DoesNotOverflow()
    {
        // 9000T = 9 000 × 10^12 = 9×10^15, меньше long.MaxValue (~9.22×10^18)
        bool result = _sut.TryParseAbbreviatedNumber("9000T", out long value);

        result.Should().BeTrue();
        value.Should().Be(9_000_000_000_000_000L);
    }

    // =========================================================================
    // TryParseAbbreviatedNumber — невалидный ввод → false, value = 0
    // =========================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1.2X")]   // неизвестный суффикс
    [InlineData("1.2 Z")]  // неизвестный суффикс с пробелом
    [InlineData("K")]      // только суффикс без числа
    [InlineData(".")]      // неполное число
    public void TryParseAbbreviatedNumber_InvalidInput_ReturnsFalse(string raw)
    {
        bool result = _sut.TryParseAbbreviatedNumber(raw, out long value);

        result.Should().BeFalse(because: $"строка '{raw}' не является валидным idle-числом");
        value.Should().Be(0L);
    }

    [Fact]
    public void TryParseAbbreviatedNumber_NegativeNumber_ReturnsFalse()
    {
        // Regex не допускает знак минус в шаблоне, поэтому "-5" не совпадает с regex
        bool result = _sut.TryParseAbbreviatedNumber("-5", out long value);

        result.Should().BeFalse(because: "отрицательные значения недопустимы в idle-формате");
        value.Should().Be(0L);
    }

    [Fact]
    public void TryParseAbbreviatedNumber_NullInput_ReturnsFalse()
    {
        // IsNullOrWhiteSpace(null) == true → ранний выход с false
        bool result = _sut.TryParseAbbreviatedNumber(null!, out long value);

        result.Should().BeFalse(because: "null считается пустым вводом");
        value.Should().Be(0L);
    }

    [Fact]
    public void TryParseAbbreviatedNumber_OverflowLong_ReturnsFalse()
    {
        // 10000000T = 10^19 > long.MaxValue (~9.22×10^18) → переполнение
        bool result = _sut.TryParseAbbreviatedNumber("10000000T", out long value);

        result.Should().BeFalse(because: "значение превышает диапазон long");
        value.Should().Be(0L);
    }

    [Fact]
    public void TryParseAbbreviatedNumber_ResultIsTruncated_NotRounded()
    {
        // 1.9K = 1900 (decimal->long усекает, не округляет)
        bool result = _sut.TryParseAbbreviatedNumber("1.9K", out long value);

        result.Should().BeTrue();
        value.Should().Be(1900L);
    }

    // =========================================================================
    // TryParseStageTimeSeconds — happy path
    // =========================================================================

    [Theory]
    [InlineData("0",       0)]
    [InlineData("45",      45)]
    [InlineData("59",      59)]
    [InlineData("00:00",   0)]
    [InlineData("12:34",   754)]     // 12*60 + 34 = 754
    [InlineData("00:01",   1)]
    [InlineData("59:59",   3599)]
    [InlineData("1:02:03", 3723)]    // 1*3600 + 2*60 + 3 = 3723
    [InlineData("0:00:00", 0)]
    [InlineData("2:00:00", 7200)]
    public void TryParseStageTimeSeconds_ValidInput_ReturnsTrueAndCorrectSeconds(string raw, int expectedSeconds)
    {
        bool result = _sut.TryParseStageTimeSeconds(raw, out int seconds);

        result.Should().BeTrue(because: $"строка '{raw}' является валидным временем этапа");
        seconds.Should().Be(expectedSeconds);
    }

    [Fact]
    public void TryParseStageTimeSeconds_SingleSegmentNoMinutes_AllowsAnySeconds()
    {
        // В формате «SS» (только секунды, без двоеточия) sanity-check не применяется.
        // Значение "99" разбирается как 99 секунд — это ОК (нет группы m).
        bool result = _sut.TryParseStageTimeSeconds("99", out int seconds);

        result.Should().BeTrue(because: "в формате SS sanity-check не применяется — нет группы минут");
        seconds.Should().Be(99);
    }

    // =========================================================================
    // TryParseStageTimeSeconds — невалидный ввод → false, seconds = 0
    // =========================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]
    [InlineData("1:2:3:4")]      // слишком много сегментов
    [InlineData("1:60")]         // секунды 60 > 59 → sanity fail (есть минутная группа)
    [InlineData("99:99")]        // и минуты, и секунды > 59 → sanity fail
    [InlineData("-1")]           // отрицательное не проходит regex
    public void TryParseStageTimeSeconds_InvalidInput_ReturnsFalse(string raw)
    {
        bool result = _sut.TryParseStageTimeSeconds(raw, out int seconds);

        result.Should().BeFalse(because: $"строка '{raw}' не является валидным временем");
        seconds.Should().Be(0);
    }

    [Fact]
    public void TryParseStageTimeSeconds_NullInput_ReturnsFalse()
    {
        bool result = _sut.TryParseStageTimeSeconds(null!, out int seconds);

        result.Should().BeFalse();
        seconds.Should().Be(0);
    }

    [Fact]
    public void TryParseStageTimeSeconds_MinutesExceed59InCompositeFormat_ReturnsFalse()
    {
        // "1:60:00" — минуты = 60 > 59 → sanity fail
        bool result = _sut.TryParseStageTimeSeconds("1:60:00", out int seconds);

        result.Should().BeFalse(because: "минуты 60 > 59 нарушают sanity-проверку");
        seconds.Should().Be(0);
    }

    // =========================================================================
    // TryParseStageId — happy path (реальный GameMechanicsConfig.CreateDefault())
    // =========================================================================

    [Fact]
    public void TryParseStageId_ActSlashNormalSlashStage_ParsesCorrectly()
    {
        // "Act 1 / Normal / 5" → числа [1, 5]; 1 — акт, 5 — этап
        StageRef? result = _sut.TryParseStageId("Act 1 / Normal / 5", _cfg);

        result.Should().NotBeNull();
        result!.Value.ActNumber.Should().Be(1);
        result.Value.DifficultyKey.Should().Be("normal");
        result.Value.StageNumber.Should().Be(5);
    }

    [Fact]
    public void TryParseStageId_DashFormatNormalMiddle_ParsesCorrectly()
    {
        // "1-5 Normal" → числа [1, 5]; 1 — акт, 5 — этап
        // Символ "-" не является word-boundary \b-разделителем между цифрами?
        // Regex \b(\d{1,2})\b: "1-5" — "1" и "5" оба обрамлены \b → оба совпадут.
        StageRef? result = _sut.TryParseStageId("1-5 Normal", _cfg);

        result.Should().NotBeNull();
        result!.Value.ActNumber.Should().Be(1);
        result.Value.DifficultyKey.Should().Be("normal");
        result.Value.StageNumber.Should().Be(5);
    }

    [Fact]
    public void TryParseStageId_NightmareActPrefixFirst_ParsesCorrectly()
    {
        // "Nightmare Act 2 / 10" → числа [2, 10]; 2 — акт, 10 — этап
        StageRef? result = _sut.TryParseStageId("Nightmare Act 2 / 10", _cfg);

        result.Should().NotBeNull();
        result!.Value.ActNumber.Should().Be(2);
        result.Value.DifficultyKey.Should().Be("nightmare");
        result.Value.StageNumber.Should().Be(10);
    }

    [Fact]
    public void TryParseStageId_ActNormalStageInline_ParsesCorrectly()
    {
        // "Act 3 Normal 1" → числа [3, 1]; 3 — акт, 1 — этап
        StageRef? result = _sut.TryParseStageId("Act 3 Normal 1", _cfg);

        result.Should().NotBeNull();
        result!.Value.ActNumber.Should().Be(3);
        result.Value.DifficultyKey.Should().Be("normal");
        result.Value.StageNumber.Should().Be(1);
    }

    [Fact]
    public void TryParseStageId_Act2Nightmare_ParsesCorrectly()
    {
        // "Act 2 / Nightmare / 7" → ActNumber=2, DifficultyKey="nightmare", StageNumber=7
        StageRef? result = _sut.TryParseStageId("Act 2 / Nightmare / 7", _cfg);

        result.Should().NotBeNull();
        result!.Value.ActNumber.Should().Be(2);
        result.Value.DifficultyKey.Should().Be("nightmare");
        result.Value.StageNumber.Should().Be(7);
    }

    [Fact]
    public void TryParseStageId_CaseInsensitiveDifficulty_ParsesCorrectly()
    {
        // "Act 1 / NORMAL / 3" — сложность регистронезависима
        StageRef? result = _sut.TryParseStageId("Act 1 / NORMAL / 3", _cfg);

        result.Should().NotBeNull();
        result!.Value.DifficultyKey.Should().Be("normal");
        result.Value.StageNumber.Should().Be(3);
    }

    // =========================================================================
    // TryParseStageId — невалидный ввод → null
    // =========================================================================

    [Fact]
    public void TryParseStageId_EmptyString_ReturnsNull()
    {
        StageRef? result = _sut.TryParseStageId("", _cfg);

        result.Should().BeNull(because: "пустая строка не содержит данных");
    }

    [Fact]
    public void TryParseStageId_WhitespaceOnly_ReturnsNull()
    {
        StageRef? result = _sut.TryParseStageId("   ", _cfg);

        result.Should().BeNull();
    }

    [Fact]
    public void TryParseStageId_NoDifficultyKeyword_ReturnsNull()
    {
        // "Act 1 / 5" — нет ключевого слова сложности → FindDifficulty вернёт null
        StageRef? result = _sut.TryParseStageId("Act 1 / 5", _cfg);

        result.Should().BeNull(because: "без ключевого слова сложности парсер не может определить DifficultyKey");
    }

    [Fact]
    public void TryParseStageId_NonExistentAct_ReturnsNull()
    {
        // "Act 9 / Normal / 5" — акт 9 отсутствует в конфиге (только 1..3)
        StageRef? result = _sut.TryParseStageId("Act 9 / Normal / 5", _cfg);

        result.Should().BeNull(because: "акт 9 не существует в конфиге");
    }

    [Fact]
    public void TryParseStageId_StageNumberOutOfRange_ReturnsNull()
    {
        // "Act 1 / Normal / 99" — \b(\d{1,2})\b совпадает только с 1-2 цифрами,
        // "99" — двузначное число, попадёт как токен, но maxStageNum=10, 99>10 → не попадёт в foundStage
        // При этом foundAct=1 (из "1"), foundStage=null → вернёт null
        StageRef? result = _sut.TryParseStageId("Act 1 / Normal / 99", _cfg);

        result.Should().BeNull(because: "этап 99 находится вне допустимого диапазона 1..10");
    }

    [Fact]
    public void TryParseStageId_Garbage_ReturnsNull()
    {
        StageRef? result = _sut.TryParseStageId("garbage text with no meaning", _cfg);

        result.Should().BeNull();
    }

    [Fact]
    public void TryParseStageId_NullRaw_ThrowsArgumentNullException()
    {
        // ArgumentNullException.ThrowIfNull(raw) в реализации
        Action act = () => _sut.TryParseStageId(null!, _cfg);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("raw");
    }

    [Fact]
    public void TryParseStageId_NullConfig_ThrowsArgumentNullException()
    {
        // ArgumentNullException.ThrowIfNull(cfg) в реализации
        Action act = () => _sut.TryParseStageId("Act 1 / Normal / 5", null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("cfg");
    }

    [Fact]
    public void TryParseStageId_OnlyDifficultyNoNumbers_ReturnsNull()
    {
        // "Normal" — сложность найдена, но нет числовых токенов → нет акта/этапа → null
        StageRef? result = _sut.TryParseStageId("Normal", _cfg);

        result.Should().BeNull(because: "без числовых токенов нельзя определить акт и этап");
    }
}
