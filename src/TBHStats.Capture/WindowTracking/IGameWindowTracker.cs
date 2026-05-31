using TBHStats.Core.Models;

namespace TBHStats.Capture.WindowTracking;

/// <summary>
/// Поиск и отслеживание окна игры Task Bar Hero.
/// Используется слоем захвата перед созданием WGC-сессии (FR-001, FR-005b).
/// </summary>
public interface IGameWindowTracker
{
    /// <summary>
    /// Найти окно процесса игры (по заголовку или имени процесса).
    /// Возвращает <see langword="null"/>, если подходящее окно не найдено.
    /// </summary>
    GameWindowHandle? FindGameWindow();

    /// <summary>
    /// Определить текущую видимость окна:
    /// <list type="bullet">
    ///   <item><see cref="WindowVisibility.Visible"/> — окно существует и не свёрнуто (включая частично перекрытое);</item>
    ///   <item><see cref="WindowVisibility.Minimized"/> — окно свёрнуто в панель задач;</item>
    ///   <item><see cref="WindowVisibility.Closed"/> — HWND более недействителен.</item>
    /// </list>
    /// Перекрытие другим окном НЕ меняет статус — WGC захватывает содержимое независимо от перекрытия.
    /// </summary>
    WindowVisibility GetVisibility(GameWindowHandle window);

    /// <summary>
    /// Получить размер клиентской области окна в пикселях.
    /// Возвращает <see cref="SizePx.Empty"/> (0×0) если HWND недействителен — не бросает исключение.
    /// </summary>
    SizePx GetClientSize(GameWindowHandle window);
}
