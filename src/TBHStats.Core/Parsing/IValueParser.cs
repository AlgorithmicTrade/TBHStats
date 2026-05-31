namespace TBHStats.Core.Parsing;

using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;

/// <summary>
/// Контракт парсинга игровых значений, считанных через OCR:
/// сокращённых чисел (idle-формат), времени этапа и идентификатора этапа.
/// </summary>
public interface IValueParser
{
    /// <summary>
    /// Пытается разобрать сокращённое число в idle-формате.
    /// </summary>
    /// <param name="raw">
    /// Строка OCR, например «1.2K», «3.4M», «5B», «2.5T», «1,234», «999».
    /// Суффиксы K / M / B / T регистронезависимы. Запятая — групповой разделитель.
    /// Внутренние пробелы между числом и суффиксом допускаются («1.2 K»).
    /// </param>
    /// <param name="value">
    /// Разобранное значение, округлённое до целого <see langword="long"/>.
    /// При возврате <see langword="false"/> равно 0.
    /// </param>
    /// <returns>
    /// <see langword="true"/>, если строка успешно разобрана; иначе <see langword="false"/>.
    /// </returns>
    bool TryParseAbbreviatedNumber(string raw, out long value);

    /// <summary>
    /// Пытается разобрать время прохождения этапа в секундах.
    /// </summary>
    /// <param name="raw">
    /// Строка OCR. Поддерживаемые форматы: «SS», «MM:SS», «H:MM:SS».
    /// Ведущие нули допускаются. Минуты и секунды в составном формате — 0..59.
    /// </param>
    /// <param name="seconds">
    /// Разобранное значение в секундах (≥ 0).
    /// При возврате <see langword="false"/> равно 0.
    /// </param>
    /// <returns>
    /// <see langword="true"/>, если строка успешно разобрана; иначе <see langword="false"/>.
    /// </returns>
    bool TryParseStageTimeSeconds(string raw, out int seconds);

    /// <summary>
    /// Пытается разобрать идентификатор текущего этапа из OCR-строки.
    /// </summary>
    /// <param name="raw">
    /// Строка OCR. Гибкий формат: «Act 1 / Normal / 5», «Act1 Nightmare 10», «1-5 Normal».
    /// Сопоставление акта/сложности — по <paramref name="cfg"/> (Key или DisplayName,
    /// регистронезависимо).
    /// </param>
    /// <param name="cfg">
    /// Конфиг игровой механики. Используется для валидации актов, сложностей и диапазона этапов.
    /// </param>
    /// <returns>
    /// <see cref="StageRef"/> при успехе; <see langword="null"/>, если разобрать не удалось
    /// или значения не существуют в конфиге.
    /// </returns>
    StageRef? TryParseStageId(string raw, GameMechanicsConfig cfg);
}
