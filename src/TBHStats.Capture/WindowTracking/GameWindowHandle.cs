namespace TBHStats.Capture.WindowTracking;

/// <summary>
/// Обёртка над нативным окном игры. Хранит HWND, идентификатор процесса и заголовок окна
/// на момент обнаружения. Используется как непрозрачный токен — не хранить между перезапусками.
/// </summary>
public sealed class GameWindowHandle
{
    /// <summary>Нативный дескриптор окна (HWND).</summary>
    public IntPtr Hwnd { get; }

    /// <summary>PID процесса-владельца окна.</summary>
    public int ProcessId { get; }

    /// <summary>Заголовок окна на момент обнаружения.</summary>
    public string Title { get; }

    internal GameWindowHandle(IntPtr hwnd, int processId, string title)
    {
        Hwnd = hwnd;
        ProcessId = processId;
        Title = title;
    }

    /// <inheritdoc/>
    public override string ToString() => $"HWND=0x{Hwnd:X} PID={ProcessId} Title=\"{Title}\"";
}
