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
    [InlineData("999",          999L)]
    [InlineData("0",            0L)]
    // Европейская локаль TBH: запятая = ДЕСЯТИЧНЫЙ разделитель.
    // «1,234» → «1.234» → decimal 1.234 → усечение → 1 (НЕ 1234)
    [InlineData("1,234",        1L)]
    // Реалистичные игровые значения с десятичной запятой:
    [InlineData("392,8",        392L)]   // «392,8» → 392.8 → 392
    [InlineData("76,0",         76L)]    // «76,0»  → 76.0  → 76
    // Суффиксы с десятичной запятой (европейская запись):
    [InlineData("1,2K",         1200L)]  // «1,2K» → 1.2 × 1000 → 1200
    [InlineData("3,4M",         3_400_000L)] // «3,4M» → 3.4 × 1_000_000
    // Суффиксы с точкой (английская запись) — без изменений:
    [InlineData("1.2K",         1200L)]
    [InlineData("3.4M",         3_400_000L)]
    [InlineData("5B",           5_000_000_000L)]
    [InlineData("2.5T",         2_500_000_000_000L)]
    [InlineData("1.2 K",        1200L)]           // пробел между числом и суффиксом
    [InlineData("1.2k",         1200L)]           // строчный суффикс
    [InlineData("3.4m",         3_400_000L)]      // строчный суффикс
    [InlineData("5b",           5_000_000_000L)]  // строчный суффикс
    [InlineData("2.5t",         2_500_000_000_000L)] // строчный суффикс
    [InlineData("  999  ",      999L)]            // ведущие/хвостовые пробелы
    [InlineData("1000",         1000L)]
    // Пробел как разделитель тысяч — без изменений:
    [InlineData("54 678",       54_678L)]
    [InlineData("2 285 394",    2_285_394L)]
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
    [InlineData("1.2X")]         // неизвестный суффикс
    [InlineData("1.2 Z")]        // неизвестный суффикс с пробелом
    [InlineData("K")]            // только суффикс без числа
    [InlineData(".")]            // неполное число
    // «12,345,678» — две запятые → две точки после Replace → decimal.TryParse возвращает false
    [InlineData("12,345,678")]
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

    [Theory]
    [InlineData("1.9K",  1900L)]   // десятичная точка: 1.9 × 1000 → 1900 (ровно, усечения нет)
    [InlineData("1.9",   1L)]      // десятичная точка: 1.9 → усечение → 1
    [InlineData("1,9",   1L)]      // европейская запятая: 1,9 → 1.9 → усечение → 1
    public void TryParseAbbreviatedNumber_ResultIsTruncated_NotRounded(string raw, long expected)
    {
        // decimal→long усекает дробную часть (floor для ≥ 0), не округляет
        bool result = _sut.TryParseAbbreviatedNumber(raw, out long value);

        result.Should().BeTrue(because: $"«{raw}» является валидным числом");
        value.Should().Be(expected, because: "decimal→long усекает, не округляет");
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

    // =========================================================================
    // TryParseXpPair — happy path
    // =========================================================================

    [Fact]
    public void TryParseXpPair_RealGameFormatWithGroupSeparators_ReturnsTrueAndCorrectValues()
    {
        // "5 530 764 / 6 266 704" — реальный формат TBH: пробел-разделитель разрядов
        bool result = _sut.TryParseXpPair("5 530 764 / 6 266 704", out long current, out long toLevel);

        result.Should().BeTrue(because: "реальный игровой формат с пробелами-разрядами должен парситься");
        current.Should().Be(5_530_764L);
        toLevel.Should().Be(6_266_704L);
    }

    [Fact]
    public void TryParseXpPair_SecondRealGameSample_ReturnsTrueAndCorrectValues()
    {
        // "2 285 394 / 4 739 962" — второй реальный образец из игры
        bool result = _sut.TryParseXpPair("2 285 394 / 4 739 962", out long current, out long toLevel);

        result.Should().BeTrue();
        current.Should().Be(2_285_394L);
        toLevel.Should().Be(4_739_962L);
    }

    [Fact]
    public void TryParseXpPair_NoSpacesAroundSlash_ReturnsTrueAndCorrectValues()
    {
        // "5530764/6266704" — без пробелов вокруг разделителя «/»
        bool result = _sut.TryParseXpPair("5530764/6266704", out long current, out long toLevel);

        result.Should().BeTrue(because: "пробелы вокруг «/» не обязательны");
        current.Should().Be(5_530_764L);
        toLevel.Should().Be(6_266_704L);
    }

    [Fact]
    public void TryParseXpPair_LeadingTrailingSpaces_ReturnsTrueAndCorrectValues()
    {
        // " 100 / 200 " — обрамляющие пробелы снимаются TryParseAbbreviatedNumber
        bool result = _sut.TryParseXpPair(" 100 / 200 ", out long current, out long toLevel);

        result.Should().BeTrue(because: "обрамляющие пробелы не должны мешать парсингу");
        current.Should().Be(100L);
        toLevel.Should().Be(200L);
    }

    [Fact]
    public void TryParseXpPair_KSuffixes_ReturnsTrueAndScaledValues()
    {
        // "1.2K / 3.4K" — суффиксы K обрабатываются через TryParseAbbreviatedNumber
        bool result = _sut.TryParseXpPair("1.2K / 3.4K", out long current, out long toLevel);

        result.Should().BeTrue(because: "суффиксы K/M/B/T поддерживаются в обеих половинах");
        current.Should().Be(1_200L);
        toLevel.Should().Be(3_400L);
    }

    // =========================================================================
    // TryParseXpPair — невалидный ввод → false, current = 0, toLevel = 0
    // =========================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParseXpPair_EmptyOrWhitespace_ReturnsFalseAndZeros(string raw)
    {
        bool result = _sut.TryParseXpPair(raw, out long current, out long toLevel);

        result.Should().BeFalse(because: $"пустая/whitespace строка «{raw}» не содержит пары значений");
        current.Should().Be(0L);
        toLevel.Should().Be(0L);
    }

    [Fact]
    public void TryParseXpPair_NoSlash_ReturnsFalseAndZeros()
    {
        // "123" — нет разделителя «/» → пара неопределена
        bool result = _sut.TryParseXpPair("123", out long current, out long toLevel);

        result.Should().BeFalse(because: "строка без «/» не является XP-парой");
        current.Should().Be(0L);
        toLevel.Should().Be(0L);
    }

    [Fact]
    public void TryParseXpPair_ExtraSlash_ReturnsFalseAndZeros()
    {
        // "5 530 764 / 6 266 704 / 7" — лишний «/» → OCR-шум, защита
        bool result = _sut.TryParseXpPair("5 530 764 / 6 266 704 / 7", out long current, out long toLevel);

        result.Should().BeFalse(because: "строка с более чем одним «/» защищена от OCR-шума");
        current.Should().Be(0L);
        toLevel.Should().Be(0L);
    }

    [Fact]
    public void TryParseXpPair_LeftPartNotNumber_ReturnsFalseAndZeros()
    {
        // "abc / 200" — левая половина не является числом
        bool result = _sut.TryParseXpPair("abc / 200", out long current, out long toLevel);

        result.Should().BeFalse(because: "левая часть не является числом");
        current.Should().Be(0L);
        toLevel.Should().Be(0L);
    }

    [Fact]
    public void TryParseXpPair_RightPartNotNumber_ReturnsFalseAndZeros()
    {
        // "100 / xyz" — правая половина не является числом
        bool result = _sut.TryParseXpPair("100 / xyz", out long current, out long toLevel);

        result.Should().BeFalse(because: "правая часть не является числом");
        current.Should().Be(0L);
        toLevel.Should().Be(0L);
    }

    [Fact]
    public void TryParseXpPair_EmptyLeftPart_ReturnsFalseAndZeros()
    {
        // "/ 200" — левая половина пустая
        bool result = _sut.TryParseXpPair("/ 200", out long current, out long toLevel);

        result.Should().BeFalse(because: "пустая левая половина не является числом");
        current.Should().Be(0L);
        toLevel.Should().Be(0L);
    }

    [Fact]
    public void TryParseXpPair_EmptyRightPart_ReturnsFalseAndZeros()
    {
        // "200 /" — правая половина пустая
        bool result = _sut.TryParseXpPair("200 /", out long current, out long toLevel);

        result.Should().BeFalse(because: "пустая правая половина не является числом");
        current.Should().Be(0L);
        toLevel.Should().Be(0L);
    }

    // =========================================================================
    // TryParseNextLocation — happy path
    // =========================================================================

    [Fact]
    public void TryParseNextLocation_SimpleDash_ReturnsTrueAndCorrectPair()
    {
        // "3-2" — минимальный формат «акт-этап»
        bool result = _sut.TryParseNextLocation("3-2", out int act, out int stage);

        result.Should().BeTrue(because: "«3-2» — корректный формат акт-этап");
        act.Should().Be(3);
        stage.Should().Be(2);
    }

    [Fact]
    public void TryParseNextLocation_ActTenStagesDash_ReturnsTrueAndCorrectPair()
    {
        // "2-10" — двузначный этап
        bool result = _sut.TryParseNextLocation("2-10", out int act, out int stage);

        result.Should().BeTrue(because: "«2-10» — корректный формат с двузначным этапом");
        act.Should().Be(2);
        stage.Should().Be(10);
    }

    [Fact]
    public void TryParseNextLocation_SpacesAroundDash_ReturnsTrueAndCorrectPair()
    {
        // "2 - 10" — пробелы вокруг дефиса допускаются по контракту
        bool result = _sut.TryParseNextLocation("2 - 10", out int act, out int stage);

        result.Should().BeTrue(because: "пробелы вокруг дефиса допускаются");
        act.Should().Be(2);
        stage.Should().Be(10);
    }

    [Fact]
    public void TryParseNextLocation_InBrackets_ReturnsTrueAndCorrectPair()
    {
        // "[3-2]" — пара в квадратных скобках
        bool result = _sut.TryParseNextLocation("[3-2]", out int act, out int stage);

        result.Should().BeTrue(because: "квадратные скобки вокруг пары не мешают парсингу");
        act.Should().Be(3);
        stage.Should().Be(2);
    }

    [Fact]
    public void TryParseNextLocation_EnDash_ReturnsTrueAndCorrectPair()
    {
        // "3–2" — en-dash (U+2013) как разделитель
        bool result = _sut.TryParseNextLocation("3–2", out int act, out int stage);

        result.Should().BeTrue(because: "en-dash является допустимым разделителем");
        act.Should().Be(3);
        stage.Should().Be(2);
    }

    [Fact]
    public void TryParseNextLocation_MinimumValues_ReturnsTrueActOneStageOne()
    {
        // "1-1" — минимальные допустимые значения: act≥1, stage≥1
        bool result = _sut.TryParseNextLocation("1-1", out int act, out int stage);

        result.Should().BeTrue(because: "act=1, stage=1 — минимально допустимая пара");
        act.Should().Be(1);
        stage.Should().Be(1);
    }

    [Fact]
    public void TryParseNextLocation_FirstMatchUsed_WhenMultiplePairsPresent()
    {
        // "2-5 next 3-8" — берётся ПЕРВОЕ вхождение «число-разделитель-число»
        bool result = _sut.TryParseNextLocation("2-5 next 3-8", out int act, out int stage);

        result.Should().BeTrue(because: "должно использоваться первое вхождение пары");
        act.Should().Be(2);
        stage.Should().Be(5);
    }

    // =========================================================================
    // TryParseNextLocation — невалидный ввод → false, (0, 0)
    // =========================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParseNextLocation_EmptyOrWhitespace_ReturnsFalseAndZeros(string raw)
    {
        bool result = _sut.TryParseNextLocation(raw, out int act, out int stage);

        result.Should().BeFalse(because: $"пустая/whitespace строка «{raw}» не содержит пары");
        act.Should().Be(0);
        stage.Should().Be(0);
    }

    [Fact]
    public void TryParseNextLocation_NoSeparator_ReturnsFalseAndZeros()
    {
        // "abc" — нет пары «число-разделитель-число»
        bool result = _sut.TryParseNextLocation("abc", out int act, out int stage);

        result.Should().BeFalse(because: "строка без пары чисел не является локацией");
        act.Should().Be(0);
        stage.Should().Be(0);
    }

    [Fact]
    public void TryParseNextLocation_SingleNumber_ReturnsFalseAndZeros()
    {
        // "3" — есть число, но нет разделителя и второго числа
        bool result = _sut.TryParseNextLocation("3", out int act, out int stage);

        result.Should().BeFalse(because: "одно число без разделителя не является парой акт-этап");
        act.Should().Be(0);
        stage.Should().Be(0);
    }
}
