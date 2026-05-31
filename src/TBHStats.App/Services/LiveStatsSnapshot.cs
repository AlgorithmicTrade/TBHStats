using TBHStats.Core.Models;
using TBHStats.Core.Optimization;

namespace TBHStats.App.Services;

/// <summary>
/// Снимок живой статистики, публикуемый <see cref="IStatsOrchestrator"/> на каждой итерации петли.
/// Используется ViewModel (T029) для биндинга в UI без привязки к UI-диспетчеру внутри сервиса.
/// </summary>
/// <param name="State">
/// Текущее состояние машины захвата.
/// Если <see cref="CaptureState.NotFound"/> — окно игры не найдено;
/// <see cref="CaptureState.Waiting"/> — окно свёрнуто/закрыто, данные могут быть устаревшими;
/// <see cref="CaptureState.Capturing"/> — данные актуальны.
/// </param>
/// <param name="Rates">
/// Живые темпы добычи, вычисленные по надёжным интервалам (FR-006, FR-005a).
/// При отсутствии достаточного числа надёжных сэмплов — нулевые темпы.
/// </param>
/// <param name="Gold">Последнее надёжно считанное кумулятивное золото. Null, если нет данных.</param>
/// <param name="HeroLevel">Последний надёжно считанный уровень героя. Null, если нет данных.</param>
/// <param name="HeroClass">Текст класса героя (нормализованный из OCR). Null, если нет данных.</param>
/// <param name="HeroDamage">Последний надёжно считанный урон героя. Null, если нет данных.</param>
/// <param name="Stage">Идентификатор текущего этапа (из NextLocation или StageId). Null, если нет данных.</param>
/// <param name="LastReliableUtc">Метка времени последнего надёжного замера (UTC). Null, если ни одного надёжного замера не было.</param>
/// <param name="IsStale">
/// Данные устарели: либо <see cref="State"/> не является <see cref="CaptureState.Capturing"/>,
/// либо последний надёжный замер был более <c>StaleThresholdSeconds</c> секунд назад.
/// </param>
public sealed record LiveStatsSnapshot(
    CaptureState State,
    LiveRates Rates,
    long? Gold,
    int? HeroLevel,
    string? HeroClass,
    long? HeroDamage,
    StageRef? Stage,
    DateTime? LastReliableUtc,
    bool IsStale)
{
    /// <summary>
    /// Начальный снимок: <see cref="CaptureState.NotFound"/>, нет данных, IsStale=true.
    /// Используется как начальное значение <see cref="IStatsOrchestrator.Current"/> до первой итерации.
    /// </summary>
    public static readonly LiveStatsSnapshot Empty = new(
        State:          CaptureState.NotFound,
        Rates:          new LiveRates(0, 0, new Dictionary<int, double>()),
        Gold:           null,
        HeroLevel:      null,
        HeroClass:      null,
        HeroDamage:     null,
        Stage:          null,
        LastReliableUtc: null,
        IsStale:        true);
}
