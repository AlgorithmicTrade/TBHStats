namespace TBHStats.Core.Optimization;

using TBHStats.Core.Models;

/// <summary>
/// Контракт вычисления живых темпов добычи по надёжным сэмплам (FR-006, FR-005a).
/// </summary>
public interface IMetricsCalculator
{
    /// <summary>
    /// Вычисляет живые темпы добычи по последовательности достоверных сэмплов.
    /// </summary>
    /// <param name="reliableSamples">
    /// Упорядоченная по времени последовательность сэмплов с <c>IsReliable = true</c>.
    /// Периоды недоступности окна между сэмплами не занижают результат:
    /// темпы считаются только по интервалам между соседними надёжными точками (FR-005a).
    /// </param>
    /// <returns>
    /// Структура <see cref="LiveRates"/> с золотом/час, опытом/час и сундуками/час по типам.
    /// При пустом или единственном сэмпле возвращает нулевые темпы.
    /// </returns>
    LiveRates ComputeLiveRates(IReadOnlyList<MetricSample> reliableSamples);
}
