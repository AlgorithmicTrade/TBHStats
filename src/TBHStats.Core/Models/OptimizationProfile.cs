namespace TBHStats.Core.Models;

/// <summary>
/// Профиль оптимизации: выбранная метрика ранжирования этапов (ADR-007, FR-009).
/// </summary>
public sealed class OptimizationProfile
{
    /// <summary>Выбранная метрика оптимизации (по умолчанию <see cref="OptimizationMetric.GoldPerHour"/>).</summary>
    public OptimizationMetric SelectedMetric { get; init; } = OptimizationMetric.GoldPerHour;
}
