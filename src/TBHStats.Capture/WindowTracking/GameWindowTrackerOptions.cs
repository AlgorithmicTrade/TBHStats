namespace TBHStats.Capture.WindowTracking;

/// <summary>
/// Настройки поиска окна игры Task Bar Hero.
/// Передаются в конструктор <see cref="GameWindowTracker"/>.
/// </summary>
public sealed class GameWindowTrackerOptions
{
    // ВАЖНО: порядок объявления статических полей значим. Статические поля C#
    // инициализируются в текстовом порядке, поэтому DefaultTitleHints обязан быть
    // объявлен ДО Default — иначе при инициализации Default = new() поле
    // DefaultTitleHints ещё null, и WindowTitleHints у Default получит null
    // (приводило к NullReferenceException в GameWindowTracker.FindGameWindow).
    // ВАЖНО: НЕ добавлять короткую подсказку "TBH" — она даёт ложные совпадения с
    // собственным окном приложения ("TBHStats") и с окном редактора (например,
    // "… - TBHStats - Visual Studio Code"), которые в Z-order часто стоят выше игры.
    // Реальный заголовок окна игры — "TaskBarHero"; его покрывают точные подсказки ниже.
    private static readonly IReadOnlyList<string> DefaultTitleHints =
        ["Task Bar Hero", "TaskBarHero"];

    /// <summary>
    /// Подстроки заголовка окна (case-insensitive Contains).
    /// Первое совпадение с любой подстрокой считается окном игры.
    /// </summary>
    public IReadOnlyList<string> WindowTitleHints { get; init; } =
        DefaultTitleHints;

    /// <summary>
    /// Настройки по умолчанию: поиск по типичным заголовкам Task Bar Hero.
    /// Точное имя процесса/окна пока не задокументировано производителем игры,
    /// поэтому список конфигурируется извне при необходимости уточнения.
    /// </summary>
    public static readonly GameWindowTrackerOptions Default = new();
}
