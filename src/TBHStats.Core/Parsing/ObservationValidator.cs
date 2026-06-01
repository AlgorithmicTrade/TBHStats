namespace TBHStats.Core.Parsing;

using TBHStats.Core.Models;

/// <summary>
/// Реализация <see cref="IObservationValidator"/>: валидация сырого наблюдения
/// через confidence-порог (R4, FR-005/010, T024).
/// </summary>
/// <remarks>
/// Золото — <b>расходуемый баланс</b>: игрок тратит его на прокачку рун, апгрейды и магазин,
/// поэтому общий баланс легитимно убывает. Убывание золота <b>не является нарушением sanity</b>
/// и не делает сэмпл ненадёжным. Темп «золото/час» считается только по положительным дельтам
/// (заработок) в <c>MetricsCalculator</c> — траты на убыль не влияют.
/// </remarks>
public sealed class ObservationValidator : IObservationValidator
{
    /// <inheritdoc/>
    public MetricSample Validate(
        RawObservation observation,
        MetricSample? prevReliable,
        double confidenceThreshold)
    {
        // ── Confidence-фильтр: принимаем поле только если ключ присутствует и значение >= порога ──
        long?  gold      = Accept(observation.PerFieldConfidence, "gold",       confidenceThreshold) ? observation.Gold      : null;
        long?  xp        = Accept(observation.PerFieldConfidence, "xp",         confidenceThreshold) ? observation.Xp        : null;
        long?  xpToLevel = Accept(observation.PerFieldConfidence, "xpToLevel",  confidenceThreshold) ? observation.XpToLevel : null;
        int?   heroLevel = Accept(observation.PerFieldConfidence, "heroLevel",  confidenceThreshold) ? observation.HeroLevel : null;
        long?  heroDamage= Accept(observation.PerFieldConfidence, "heroDamage", confidenceThreshold) ? observation.HeroDamage: null;

        // ── IsReliable ──
        // Значимые поля: Gold, Xp, HeroLevel. Хотя бы одно должно быть принято.
        // Золото — расходуемый баланс (тратится на руны/прокачку/магазин), а НЕ монотонный
        // кумулятив. Убывание золота легитимно и НЕ делает сэмпл ненадёжным (иначе виджет
        // замораживается после траты, пока баланс не дорастёт обратно — наблюдалось при
        // открытии вкладки Rune). Темп «золото/час» считает только положительные дельты
        // (заработок) — см. MetricsCalculator; убыль игнорируется, поэтому трата золота
        // не искажает темп.
        bool hasSignificantField = gold.HasValue || xp.HasValue || heroLevel.HasValue;
        bool isReliable = hasSignificantField;

        // ── Сундуки: копируем ключ→MetricSampleChest ──
        var chests = new List<MetricSampleChest>(observation.Chests.Count);
        foreach (KeyValuePair<int, int> kv in observation.Chests)
        {
            chests.Add(new MetricSampleChest { ChestTypeId = kv.Key, Count = kv.Value });
        }

        return new MetricSample
        {
            TakenAtUtc   = observation.TakenAtUtc,
            StageId      = null,                       // резолвится позже
            Gold         = gold,
            Xp           = xp,
            XpToLevel    = xpToLevel,
            HeroLevel    = heroLevel,
            HeroDamage   = heroDamage,
            NextLocation = observation.NextLocation,   // переносится без confidence-ключа
            IsReliable   = isReliable,
            Chests       = chests,
        };
    }

    /// <summary>
    /// Возвращает <see langword="true"/>, если ключ присутствует в словаре уверенности
    /// и его значение &gt;= <paramref name="threshold"/>.
    /// </summary>
    private static bool Accept(
        IReadOnlyDictionary<string, double> confidence,
        string key,
        double threshold)
        => confidence.TryGetValue(key, out double value) && value >= threshold;
}
