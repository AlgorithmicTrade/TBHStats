using TBHStats.Core.Models;

namespace TBHStats.Capture;

/// <summary>
/// Детектор завершения этапа: анализирует последовательность наблюдений
/// <see cref="RawObservation"/> и генерирует <see cref="StageCompletionEvent"/>
/// при фиксировании гибели босса этапа (FR-002, data-model §Детекция завершения этапа).
/// </summary>
/// <remarks>
/// <para>
/// Компонент является детерминированной машиной состояний без WinRT-зависимостей.
/// Вызывающий код (оркестратор петли захвата) передаёт по одному наблюдению на кадр.
/// </para>
/// <para>
/// Состояния детектора:
/// <list type="bullet">
///   <item><b>InProgress</b> — этап идёт, босс ещё не появлялся.</item>
///   <item><b>BossEngaged</b> — босс был замечен (<c>BossPresent == true</c>);
///         ожидается его гибель.</item>
/// </list>
/// </para>
/// <para>
/// Переход в <c>BossEngaged</c>: когда <c>observation.BossPresent == true</c>
/// (и/или <c>StageProgress &gt;= 0.99</c>).
/// </para>
/// <para>
/// Сигнал завершения (из состояния <c>BossEngaged</c>):
/// <list type="bullet">
///   <item>Явный переход <c>BossPresent true → false</c> (босс исчез = убит).</item>
///   <item>ИЛИ появление ненулевого <c>StageTimeSeconds</c> (игра показала итоговое время).</item>
/// </list>
/// null-значения <c>BossPresent</c> и <c>StageProgress</c> трактуются как «нет данных»
/// и НЕ меняют состояние — отсутствие поля не равно false и не является сигналом.
/// </para>
/// <para>
/// После выдачи события детектор автоматически возвращается в <c>InProgress</c>,
/// исключая повторный триггер на том же наблюдении (идемпотентность).
/// </para>
/// </remarks>
public interface IStageCompletionDetector
{
    /// <summary>
    /// Подаёт одно наблюдение на обработку.
    /// </summary>
    /// <param name="observation">Наблюдение текущего кадра.</param>
    /// <returns>
    /// <see cref="StageCompletionEvent"/>, если в данном кадре зафиксировано завершение
    /// этапа; <c>null</c>, если завершения нет.
    /// </returns>
    StageCompletionEvent? Observe(RawObservation observation);

    /// <summary>
    /// Сбрасывает внутреннее состояние машины в <c>InProgress</c>.
    /// Вызывается при смене этапа, перезапуске петли захвата или переходе
    /// <see cref="TBHStats.Core.Models.CaptureState"/> в <c>Waiting</c>/<c>NotFound</c>.
    /// </summary>
    void Reset();
}
