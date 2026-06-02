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
/// <param name="Xp">Последнее надёжно считанное накопленное значение опыта. Null, если нет данных.</param>
/// <param name="XpToLevel">Последнее надёжно считанное значение опыта, необходимого до следующего уровня. Null, если нет данных.</param>
/// <param name="HeroLevel">Последний надёжно считанный уровень героя. Null, если нет данных.</param>
/// <param name="HeroClass">Текст класса героя (нормализованный из OCR). Null, если нет данных.</param>
/// <param name="HeroDamage">Последний надёжно считанный урон героя. Null, если нет данных.</param>
/// <param name="Stage">Идентификатор текущего этапа (из NextLocation или StageId). Null, если нет данных.</param>
/// <param name="LastReliableUtc">Метка времени последнего надёжного замера (UTC). Null, если ни одного надёжного замера не было.</param>
/// <param name="IsStale">
/// Данные устарели: либо <see cref="State"/> не является <see cref="CaptureState.Capturing"/>,
/// либо последний надёжный замер был более <c>StaleThresholdSeconds</c> секунд назад.
/// </param>
/// <param name="Chests">
/// Текущие счётчики сундуков по типу (ChestType.Id → количество) из последнего надёжного сэмпла.
/// Счётчики транзиентны: отражают значения последнего OCR-кадра, не накапливаются между итерациями.
/// Никогда не null — при отсутствии данных возвращается пустой словарь.
/// </param>
/// <param name="StageProgress">
/// Прогресс текущего этапа ∈ [0..1] (последнее известное значение из визуального детектора).
/// Null, если данные ещё не поступали.
/// </param>
/// <param name="BossPresent">
/// Признак активного боя с боссом этапа (последнее известное значение). Null, если нет данных.
/// </param>
/// <param name="StageElapsedSeconds">
/// Время на текущем этапе в секундах (сегментный таймер; сбрасывается при смене этапа). Null, если нет данных.
/// </param>
/// <param name="LastCompletedStageSeconds">
/// Длительность предыдущей ПРОЙДЕННОЙ (по боссу) попытки в секундах; показывается в скобках для сравнения.
/// Null до первого прохождения.
/// </param>
/// <param name="LastCompletedStageGold">
/// Прирост золота за последний ПРОЙДЕННЫЙ (по боссу) сегмент этапа.
/// Null до первого прохождения.
/// </param>
/// <param name="LastCompletedStageXp">
/// Прирост опыта за последний ПРОЙДЕННЫЙ (по боссу) сегмент этапа.
/// Null до первого прохождения.
/// </param>
/// <param name="SessionGoldGained">
/// Суммарный положительный прирост золота за сессию (с момента запуска виджета).
/// Учитываются только положительные дельты между надёжными кадрами; траты (отрицательные дельты) игнорируются.
/// </param>
/// <param name="SessionXpGained">
/// Суммарный прирост опыта за сессию с компенсацией level-up.
/// Формула идентична сегментной: при повышении уровня учитывается добор до конца предыдущего уровня.
/// </param>
/// <param name="SessionStagesCompleted">
/// Количество этапов, завершённых по боссу за сессию.
/// Инкрементируется только при наблюдаемом прохождении (бос + начало этапа видено).
/// </param>
/// <param name="SessionLevelsGained">
/// Количество уровней героя, полученных за сессию.
/// </param>
/// <param name="SessionElapsedSeconds">
/// Время работы в секундах с момента старта оркестратора (старт сессии).
/// Тикает непрерывно, включая периоды Waiting/NotFound.
/// </param>
public sealed record LiveStatsSnapshot(
    CaptureState State,
    LiveRates Rates,
    long? Gold,
    long? Xp,
    long? XpToLevel,
    int? HeroLevel,
    string? HeroClass,
    long? HeroDamage,
    StageRef? Stage,
    double? StageProgress,
    bool? BossPresent,
    int? StageElapsedSeconds,
    int? LastCompletedStageSeconds,
    long? LastCompletedStageGold,
    long? LastCompletedStageXp,
    DateTime? LastReliableUtc,
    bool IsStale,
    IReadOnlyDictionary<int, int> Chests,
    long SessionGoldGained,
    long SessionXpGained,
    int SessionStagesCompleted,
    int SessionLevelsGained,
    int SessionElapsedSeconds)
{
    /// <summary>
    /// Начальный снимок: <see cref="CaptureState.NotFound"/>, нет данных, IsStale=true.
    /// Используется как начальное значение <see cref="IStatsOrchestrator.Current"/> до первой итерации.
    /// </summary>
    public static readonly LiveStatsSnapshot Empty = new(
        State:                        CaptureState.NotFound,
        Rates:                        new LiveRates(0, 0, new Dictionary<int, double>()),
        Gold:                         null,
        Xp:                           null,
        XpToLevel:                    null,
        HeroLevel:                    null,
        HeroClass:                    null,
        HeroDamage:                   null,
        Stage:                        null,
        StageProgress:                null,
        BossPresent:                  null,
        StageElapsedSeconds:          null,
        LastCompletedStageSeconds:    null,
        LastCompletedStageGold:       null,
        LastCompletedStageXp:         null,
        LastReliableUtc:              null,
        IsStale:                      true,
        Chests:                       new Dictionary<int, int>(),
        SessionGoldGained:            0,
        SessionXpGained:              0,
        SessionStagesCompleted:       0,
        SessionLevelsGained:          0,
        SessionElapsedSeconds:        0);
}
