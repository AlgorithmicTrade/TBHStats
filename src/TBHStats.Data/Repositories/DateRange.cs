namespace TBHStats.Data.Repositories;

/// <summary>
/// Диапазон времени для выборки сэмплов (UTC).
/// Используется в запросах <see cref="IRunRepository.GetSamplesAsync"/>.
/// </summary>
/// <param name="FromUtc">Начало диапазона (включительно, UTC).</param>
/// <param name="ToUtc">Конец диапазона (включительно, UTC).</param>
public readonly record struct DateRange(DateTime FromUtc, DateTime ToUtc);
