namespace TBHStats.Core.Models;

/// <summary>
/// Метрика оптимизации — определяет цель ранжирования этапов (FR-009).
/// По умолчанию используется <see cref="GoldPerHour"/>.
/// </summary>
public enum OptimizationMetric
{
    /// <summary>Золото в час — цель по умолчанию (Q2=A).</summary>
    GoldPerHour,

    /// <summary>Опыт в час.</summary>
    XpPerHour,
}
