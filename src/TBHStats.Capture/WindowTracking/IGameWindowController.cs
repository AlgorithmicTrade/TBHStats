namespace TBHStats.Capture.WindowTracking;

/// <summary>
/// Управление позицией окна игры (window-management): увод за пределы
/// видимой области экрана и возврат на исходную позицию.
/// </summary>
/// <remarks>
/// <para>
/// Окно остаётся <c>Visible</c> (не сворачивается, не скрывается) — DWM продолжает
/// компоновать его поверхность, поэтому WGC-захват и сбор статистики <b>не прерываются</b>
/// (в отличие от <c>SW_MINIMIZE</c>/<c>SW_HIDE</c>, которые убирают окно из композиции DWM).
/// </para>
/// <para>
/// Observe-only по вводу сохраняется: <c>SetWindowPos</c> перемещает окно,
/// но не инжектирует клавиши, мышь или сообщения в игровой процесс.
/// </para>
/// </remarks>
public interface IGameWindowController
{
    /// <summary>
    /// <see langword="true"/>, если окно сейчас уведено за экран этим контроллером.
    /// </summary>
    bool IsHidden { get; }

    /// <summary>
    /// Увести окно за пределы видимой области экрана (<c>SetWindowPos</c> на off-screen
    /// координаты), запомнив исходную позицию для последующего <see cref="Restore"/>.
    /// </summary>
    /// <remarks>
    /// Идемпотентно: повторный вызов при <see cref="IsHidden"/> == <see langword="true"/> —
    /// no-op, возвращает <see langword="true"/>. Размер, Z-order и фокус окна не изменяются.
    /// </remarks>
    /// <param name="window">Дескриптор окна игры, полученный от <see cref="IGameWindowTracker"/>.</param>
    /// <returns>
    /// <see langword="true"/> при успехе;
    /// <see langword="false"/>, если окно не найдено или недоступно.
    /// </returns>
    bool HideOffScreen(GameWindowHandle window);

    /// <summary>
    /// Вернуть окно на исходную позицию, сохранённую в момент вызова <see cref="HideOffScreen"/>.
    /// </summary>
    /// <remarks>
    /// Идемпотентно: если окно не было уведено — no-op, возвращает <see langword="true"/>.
    /// </remarks>
    /// <param name="window">Дескриптор окна игры.</param>
    /// <returns>
    /// <see langword="true"/> при успехе;
    /// <see langword="false"/>, если окно не найдено или недоступно.
    /// </returns>
    bool Restore(GameWindowHandle window);
}
