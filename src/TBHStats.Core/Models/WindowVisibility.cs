namespace TBHStats.Core.Models;

/// <summary>
/// Видимость окна игры, как определяется слоем захвата.
/// </summary>
public enum WindowVisibility
{
    /// <summary>Окно видимо на экране (в том числе при частичном перекрытии другим окном).</summary>
    Visible,

    /// <summary>Окно свёрнуто в панель задач.</summary>
    Minimized,

    /// <summary>Окно закрыто (процесс завершился или HWND более недействителен).</summary>
    Closed,
}
