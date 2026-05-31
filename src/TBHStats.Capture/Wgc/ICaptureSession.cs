using TBHStats.Core.Models;

namespace TBHStats.Capture.Wgc;

/// <summary>
/// Сессия захвата кадров конкретного окна игры через Windows.Graphics.Capture.
/// Управляет машиной состояний захвата: <see cref="CaptureState"/>.
/// </summary>
/// <remarks>
/// Жизненный цикл:
/// <list type="bullet">
///   <item>Создать экземпляр через DI (конструктор принимает <c>IGameWindowTracker</c>).</item>
///   <item>Вызывать <see cref="TryGetFrameAsync"/> в цикле; метод сам управляет состоянием.</item>
///   <item>По завершении работы — <c>await DisposeAsync()</c> для освобождения GPU-ресурсов.</item>
/// </list>
/// </remarks>
public interface ICaptureSession : IAsyncDisposable
{
    /// <summary>
    /// Текущее состояние машины захвата.
    /// Изменение состояния сопровождается событием <see cref="StateChanged"/>.
    /// </summary>
    CaptureState State { get; }

    /// <summary>
    /// Генерируется при каждом переходе между состояниями.
    /// Аргумент — новое состояние после перехода.
    /// </summary>
    event Action<CaptureState> StateChanged;

    /// <summary>
    /// Возвращает очередной кадр содержимого игрового окна.
    /// </summary>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>
    /// Кадр, если состояние <see cref="CaptureState.Capturing"/>;
    /// <see langword="null"/>, если состояние <see cref="CaptureState.Waiting"/> или
    /// <see cref="CaptureState.NotFound"/>. Исключение не выбрасывается при отсутствии
    /// окна или при его свёрнутости — только при отмене через <paramref name="ct"/>.
    /// </returns>
    Task<CapturedFrame?> TryGetFrameAsync(CancellationToken ct);
}
