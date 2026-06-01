namespace TBHStats.Core.Models;

/// <summary>
/// Лёгкий value-объект, однозначно идентифицирующий этап игры.
/// Уникальность определяется тройкой (<see cref="ActNumber"/>, <see cref="DifficultyKey"/>, <see cref="StageNumber"/>):
/// один и тот же номер этапа на разных актах/сложностях — это РАЗНЫЕ этапы (data-model edge case).
/// </summary>
/// <param name="ActNumber">Номер акта (≥1).</param>
/// <param name="DifficultyKey">Ключ сложности, например «normal» или «nightmare» (непустая строка).</param>
/// <param name="StageNumber">Номер этапа внутри акта/сложности (≥1, текущий диапазон 1..10).</param>
public readonly record struct StageRef(int ActNumber, string DifficultyKey, int StageNumber)
{
    /// <summary>Номер акта (≥1).</summary>
    public int ActNumber { get; init; } = ValidateActNumber(ActNumber);

    /// <summary>Ключ сложности (непустая строка, например «normal»/«nightmare»).</summary>
    public string DifficultyKey { get; init; } = ValidateDifficultyKey(DifficultyKey);

    /// <summary>Номер этапа внутри акта/сложности (≥1).</summary>
    public int StageNumber { get; init; } = ValidateStageNumber(StageNumber);

    private static int ValidateActNumber(int value)
    {
        if (value < 1)
            throw new ArgumentOutOfRangeException(nameof(ActNumber), value, "ActNumber должен быть ≥ 1.");
        return value;
    }

    private static string ValidateDifficultyKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("DifficultyKey не должен быть пустым или состоять только из пробелов.", nameof(DifficultyKey));
        return value;
    }

    private static int ValidateStageNumber(int value)
    {
        if (value < 1)
            throw new ArgumentOutOfRangeException(nameof(StageNumber), value, "StageNumber должен быть ≥ 1.");
        return value;
    }

    /// <summary>
    /// Возвращает ПРЕДЫДУЩИЙ этап (для вычисления текущего из nextLocation: current = nextLocation − 1)
    /// с переносом «этап 1 данного акта → предыдущий акт, этап <paramref name="stagesPerAct"/>».
    /// </summary>
    /// <param name="stagesPerAct">Число этапов в акте (по умолчанию 10, ADR-008).</param>
    /// <returns>
    /// Предыдущий <see cref="StageRef"/>; либо <see langword="null"/>, если предыдущего нет
    /// (этап 1 первого акта) или <paramref name="stagesPerAct"/> &lt; 1.
    /// </returns>
    public StageRef? Previous(int stagesPerAct = 10)
    {
        if (StageNumber > 1)
            return new StageRef(ActNumber, DifficultyKey, StageNumber - 1);

        // StageNumber == 1 → последний этап предыдущего акта (если акт есть).
        if (ActNumber > 1 && stagesPerAct >= 1)
            return new StageRef(ActNumber - 1, DifficultyKey, stagesPerAct);

        return null; // 1-1: предыдущего этапа нет
    }

    /// <summary>
    /// Возвращает строковое представление в формате «Act{ActNumber}/{DifficultyKey}/{StageNumber}»,
    /// например «Act1/normal/5».
    /// </summary>
    public override string ToString() => $"Act{ActNumber}/{DifficultyKey}/{StageNumber}";
}
