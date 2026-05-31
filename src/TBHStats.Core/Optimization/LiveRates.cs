namespace TBHStats.Core.Optimization;

/// <summary>
/// Живые темпы добычи, вычисленные по надёжным интервалам между <c>MetricSample</c>
/// с <c>IsReliable = true</c> (FR-006, FR-005a).
/// </summary>
/// <remarks>
/// Периоды недоступности окна (состояние <c>Waiting</c> / <c>NotFound</c>) не участвуют
/// в расчёте и не занижают темпы («нулевой добычи» не предполагается — FR-005a).
/// </remarks>
/// <param name="GoldPerHour">Золото в час за наблюдаемый период.</param>
/// <param name="XpPerHour">
/// Опыт в час. При вычислении учитывается level-up сброс <c>MetricSample.Xp</c>
/// через компенсацию по <c>XpToLevel</c> предыдущего сэмпла и <c>HeroLevel</c>.
/// </param>
/// <param name="ChestPerHourByType">
/// Темп выпадения сундуков по типам, сундуков/час.
/// Ключ — <c>ChestType.Id</c> из <c>GameMechanicsConfig</c>;
/// значение — количество сундуков данного типа в час.
/// Пустой словарь, если данные о сундуках отсутствуют.
/// </param>
public readonly record struct LiveRates(
    double GoldPerHour,
    double XpPerHour,
    IReadOnlyDictionary<int, double> ChestPerHourByType);
