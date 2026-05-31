namespace TBHStats.Core.Parsing;

using System.Globalization;
using System.Text.RegularExpressions;
using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;

/// <summary>
/// Парсер игровых значений, считанных через OCR:
/// сокращённых чисел (idle K/M/B/T), времени этапа и идентификатора этапа.
/// </summary>
/// <remarks>
/// Все методы детерминированы, без статического состояния и побочных эффектов.
/// Используется <see cref="CultureInfo.InvariantCulture"/> для числового парсинга.
/// </remarks>
public sealed class ValueParser : IValueParser
{
    // ─── Регулярные выражения (компилируются один раз на тип) ────────────────

    /// <summary>
    /// Разбирает число с опциональным суффиксом: «1.2K», «3.4 M», «5B», «2.5T», «1,234», «999»,
    /// а также полноразмерные числа с пробелом-разделителем разрядов «54 678», «2 285 394»
    /// (реальный формат TBH — европейская локаль, verified на скриншотах, GAME-FACTS §12).
    /// Группы: «digits» — числовая часть (цифры, запятые, точка, пробел/неразрывный пробел
    /// как разделитель разрядов), «suffix» — буква суффикса.
    /// </summary>
    private static readonly Regex AbbreviatedNumberRegex = new(
        @"^\s*(?<digits>[\d,]*\.?\d+)\s*(?<suffix>[KkMmBbTt])?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Пробел-разделитель разрядов МЕЖДУ двумя цифрами: обычный (U+0020),
    /// неразрывный (U+00A0), узкий неразрывный (U+202F).
    /// Удаляется перед числовым парсингом: «2 285 394» → «2285394», «54 678» → «54678».
    /// Lookbehind/lookahead на цифры не затрагивают пробел между числом и суффиксом
    /// («1.2 K») и обрамляющие пробелы (их обрабатывает <c>\s*</c> в основном regex).
    /// </summary>
    private static readonly Regex DigitGroupSeparatorRegex = new(
        "(?<=\\d)\\p{Zs}(?=\\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Разбирает время этапа: «SS», «MM:SS», «H:MM:SS».
    /// Группы: «h» (опц.), «m» (опц.), «s» — обязательная.
    /// </summary>
    private static readonly Regex StageTimeRegex = new(
        @"^\s*(?:(?:(?<h>\d+):)?(?<m>\d{1,2}):)?(?<s>\d{1,2})\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Разбирает все числовые токены (1..99) из строки идентификатора этапа.
    /// </summary>
    private static readonly Regex NumberTokenRegex = new(
        @"\b(\d{1,2})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    // ─── Мультипликаторы суффиксов ───────────────────────────────────────────

    private const decimal MultiplierK = 1_000m;
    private const decimal MultiplierM = 1_000_000m;
    private const decimal MultiplierB = 1_000_000_000m;
    private const decimal MultiplierT = 1_000_000_000_000m;

    // ────────────────────────────────────────────────────────────────────────
    // TryParseAbbreviatedNumber
    // ────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public bool TryParseAbbreviatedNumber(string raw, out long value)
    {
        value = 0L;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        // Полноразмерные числа TBH используют пробел как разделитель разрядов («54 678»,
        // «2 285 394»). Убираем такой разделитель между цифрами, чтобы числовой regex
        // (без пробелов в классе) сматчил полное значение (GAME-FACTS §12, verified в T049).
        raw = DigitGroupSeparatorRegex.Replace(raw, string.Empty);

        Match match = AbbreviatedNumberRegex.Match(raw);
        if (!match.Success)
            return false;

        // Убираем групповые разделители (запятые) — они не являются десятичными в idle-формате.
        string digitsStr = match.Groups["digits"].Value.Replace(",", string.Empty, StringComparison.Ordinal);

        if (!decimal.TryParse(digitsStr, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal number))
            return false;

        // Отрицательные значения невозможны (OCR числа из idle RPG всегда ≥ 0),
        // дополнительно защищаемся от знакового мусора.
        if (number < 0m)
            return false;

        decimal multiplier = 1m;
        string suffixGroup = match.Groups["suffix"].Value;

        if (suffixGroup.Length == 1)
        {
            multiplier = char.ToUpperInvariant(suffixGroup[0]) switch
            {
                'K' => MultiplierK,
                'M' => MultiplierM,
                'B' => MultiplierB,
                'T' => MultiplierT,
                _   => 1m,   // не ожидается при текущем regex, но безопасная ветка
            };
        }

        decimal result = number * multiplier;

        // Проверка диапазона long и приведение.
        if (result > (decimal)long.MaxValue)
            return false;

        value = (long)result;   // decimal→long усекает дробную часть (floor для ≥0)
        return true;
    }

    // ────────────────────────────────────────────────────────────────────────
    // TryParseStageTimeSeconds
    // ────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public bool TryParseStageTimeSeconds(string raw, out int seconds)
    {
        seconds = 0;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        Match match = StageTimeRegex.Match(raw);
        if (!match.Success)
            return false;

        // Группа «s» всегда присутствует при успехе regex.
        if (!int.TryParse(match.Groups["s"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int s))
            return false;

        int m = 0;
        int h = 0;

        Group mGroup = match.Groups["m"];
        if (mGroup.Success)
        {
            if (!int.TryParse(mGroup.Value, NumberStyles.None, CultureInfo.InvariantCulture, out m))
                return false;
        }

        Group hGroup = match.Groups["h"];
        if (hGroup.Success)
        {
            if (!int.TryParse(hGroup.Value, NumberStyles.None, CultureInfo.InvariantCulture, out h))
                return false;
        }

        // Санити-проверка: в составном формате (есть минуты) секунды и минуты — 0..59.
        bool hasMinutes = mGroup.Success;
        if (hasMinutes && (s > 59 || m > 59))
            return false;

        seconds = h * 3600 + m * 60 + s;
        return true;
    }

    // ────────────────────────────────────────────────────────────────────────
    // TryParseStageId
    // ────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public StageRef? TryParseStageId(string raw, GameMechanicsConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(cfg);

        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // ── 1. Пытаемся сопоставить сложность по подстроке (Key/DisplayName) ─
        Difficulty? matchedDifficulty = FindDifficulty(raw, cfg);
        if (matchedDifficulty is null)
            return null;

        // ── 2. Извлекаем все числовые токены из строки ────────────────────────
        MatchCollection numMatches = NumberTokenRegex.Matches(raw);
        if (numMatches.Count == 0)
            return null;

        int[] nums = new int[numMatches.Count];
        for (int i = 0; i < numMatches.Count; i++)
        {
            if (!int.TryParse(numMatches[i].Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out nums[i]))
                return null;
        }

        // ── 3. Извлекаем номер акта и номер этапа ────────────────────────────
        //
        // Стратегия: ищем токены, которые могут быть номером акта (существует в cfg.Acts)
        // и номером этапа (1..MaxStage). Приоритет — первый подходящий токен как акт,
        // последний подходящий токен как этап (чтобы "Act 1 / Normal / 5" работало корректно).

        int maxStageNum = GetMaxStageNumber(cfg, matchedDifficulty);

        int? foundAct   = null;
        int? foundStage = null;

        // Собираем токены-кандидаты на роль акта и этапа.
        foreach (int num in nums)
        {
            if (foundAct is null && IsValidActNumber(num, cfg))
            {
                foundAct = num;
                continue;
            }

            if (num >= 1 && num <= maxStageNum)
            {
                foundStage = num;   // берём последний подходящий
            }
        }

        // Если акт так и не найден — пробуем среди всех токенов повторно
        // (например, строка "Normal 5" без упоминания акта номера).
        // В таком случае акт не определяется и возвращаем null — спецификация
        // требует явного акта в идентификаторе.
        if (foundAct is null || foundStage is null)
            return null;

        // ── 4. Проверяем существование этапа в матрице конфига ─────────────
        if (!StageExistsInConfig(cfg, foundAct.Value, matchedDifficulty, foundStage.Value))
            return null;

        return new StageRef(foundAct.Value, matchedDifficulty.Key, foundStage.Value);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Вспомогательные методы (private)
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ищет сложность в конфиге по совпадению Key или DisplayName (регистронезависимо).
    /// </summary>
    private static Difficulty? FindDifficulty(string raw, GameMechanicsConfig cfg)
    {
        foreach (Difficulty diff in cfg.Difficulties)
        {
            if (raw.Contains(diff.Key, StringComparison.OrdinalIgnoreCase) ||
                raw.Contains(diff.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                return diff;
            }
        }
        return null;
    }

    /// <summary>
    /// Возвращает наибольший номер этапа для заданной сложности из конфига.
    /// </summary>
    private static int GetMaxStageNumber(GameMechanicsConfig cfg, Difficulty difficulty)
    {
        int max = 0;
        foreach (Stage stage in cfg.Stages)
        {
            if (stage.DifficultyId == difficulty.Id && stage.Number > max)
                max = stage.Number;
        }
        return max == 0 ? 10 : max;   // fallback: по spec максимум 10
    }

    /// <summary>
    /// Проверяет, что число <paramref name="num"/> является валидным номером акта в конфиге.
    /// </summary>
    private static bool IsValidActNumber(int num, GameMechanicsConfig cfg)
    {
        foreach (Act act in cfg.Acts)
        {
            if (act.Number == num)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Проверяет, что этап (actNumber, difficulty, stageNumber) существует в матрице конфига.
    /// </summary>
    private static bool StageExistsInConfig(
        GameMechanicsConfig cfg,
        int actNumber,
        Difficulty difficulty,
        int stageNumber)
    {
        // Находим Act.Id по номеру.
        int actId = 0;
        foreach (Act act in cfg.Acts)
        {
            if (act.Number == actNumber)
            {
                actId = act.Id;
                break;
            }
        }
        if (actId == 0)
            return false;

        // Ищем Stage по (ActId, DifficultyId, Number).
        foreach (Stage stage in cfg.Stages)
        {
            if (stage.ActId == actId &&
                stage.DifficultyId == difficulty.Id &&
                stage.Number == stageNumber)
            {
                return true;
            }
        }
        return false;
    }
}
