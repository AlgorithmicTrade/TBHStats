namespace TBHStats.Capture.WindowTracking;

/// <summary>
/// Настройки поиска окна игры Task Bar Hero.
/// Передаются в конструктор <see cref="GameWindowTracker"/>.
/// </summary>
public sealed class GameWindowTrackerOptions
{
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

    private static readonly IReadOnlyList<string> DefaultTitleHints =
        ["Task Bar Hero", "TaskBarHero", "TBH"];
}
