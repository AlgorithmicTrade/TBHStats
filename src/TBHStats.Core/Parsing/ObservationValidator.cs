namespace TBHStats.Core.Parsing;

using TBHStats.Core.Models;

/// <summary>
/// Реализация <see cref="IObservationValidator"/>: валидация сырого наблюдения
/// через confidence-порог и sanity-проверки (R4, FR-005/010, T024).
/// </summary>
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

        // ── Sanity: монотонность золота ──
        // Проверяем только если Gold прошёл confidence и prevReliable.Gold задан.
        bool goldViolation = gold.HasValue
                          && prevReliable?.Gold.HasValue == true
                          && gold.Value < prevReliable.Gold!.Value;

        // ── IsReliable ──
        // Значимые поля: Gold, Xp, HeroLevel. Хотя бы одно должно быть принято.
        bool hasSignificantField = gold.HasValue || xp.HasValue || heroLevel.HasValue;
        bool isReliable = hasSignificantField && !goldViolation;

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
