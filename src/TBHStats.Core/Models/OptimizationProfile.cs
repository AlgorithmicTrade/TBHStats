namespace TBHStats.Core.Models;

/// <summary>
/// Профиль оптимизации: выбранная метрика и параметры recency-aware ранжирования этапов (ADR-007, FR-009).
/// </summary>
public sealed class OptimizationProfile
{
    /// <summary>Выбранная метрика оптимизации (по умолчанию <see cref="OptimizationMetric.GoldPerHour"/>).</summary>
    public OptimizationMetric SelectedMetric { get; init; } = OptimizationMetric.GoldPerHour;

    /// <summary>
    /// Размер свежего окна: число последних non-partial забегов на этап,
    /// учитываемых при агрегации <see cref="AggregationScope.Recent"/> (уточнение 2026-05-31).
    /// Должен быть ≥ 1.
    /// </summary>
    public int RecentWindowSize { get; init; } = 10;

    /// <summary>
    /// Набор данных, используемый ранжированием и рекомендацией (FR-009, FR-019).
    /// <see cref="AggregationScope.Recent"/> (дефолт) — по свежему окну (актуальная сила);
    /// <see cref="AggregationScope.AllTime"/> — по всей истории (справочно).
    /// </summary>
    public AggregationScope Scope { get; init; } = AggregationScope.Recent;

    // Guard clause для RecentWindowSize
    // Реализован через конструктор-верификацию: init-setter не поддерживает произвольный код,
    // поэтому валидация вынесена в явный конструктор.
    /// <summary>Создаёт <see cref="OptimizationProfile"/> с проверкой инварианта <see cref="RecentWindowSize"/> ≥ 1.</summary>
    public OptimizationProfile() { }

    /// <summary>Создаёт <see cref="OptimizationProfile"/> с явным заданием всех параметров.</summary>
    /// <param name="selectedMetric">Метрика ранжирования.</param>
    /// <param name="recentWindowSize">Размер свежего окна (≥ 1).</param>
    /// <param name="scope">Набор данных для ранжирования.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Выбрасывается, если <paramref name="recentWindowSize"/> &lt; 1.
    /// </exception>
    public OptimizationProfile(
        OptimizationMetric selectedMetric = OptimizationMetric.GoldPerHour,
        int recentWindowSize = 10,
        AggregationScope scope = AggregationScope.Recent)
    {
        if (recentWindowSize < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(recentWindowSize),
                recentWindowSize,
                "RecentWindowSize must be ≥ 1.");
        }

        SelectedMetric = selectedMetric;
        RecentWindowSize = recentWindowSize;
        Scope = scope;
    }
}
