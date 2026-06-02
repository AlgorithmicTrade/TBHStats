using System.Runtime.InteropServices;

namespace TBHStats.Capture.WindowTracking;

/// <summary>
/// Реализация <see cref="IGameWindowController"/> через Win32 <c>SetWindowPos</c>.
/// Уводит окно игры на координаты (-32000, -32000) — за пределами любого монитора —
/// сохраняя его видимым для DWM и WGC (захват статистики не прерывается).
/// </summary>
public sealed class GameWindowController : IGameWindowController
{
    // Off-screen координаты: Windows разрешает такие значения для top-level окон.
    // -32000 — стандартное значение, которое сама ОС использует для свёрнутых окон.
    private const int OffScreenX = -32000;
    private const int OffScreenY = -32000;

    private readonly object _lock = new();

    private NativeMethods.RECT? _savedRect;
    private IntPtr _hiddenHwnd = IntPtr.Zero;
    private bool _isHidden;

    /// <inheritdoc/>
    public bool IsHidden
    {
        get
        {
            lock (_lock)
                return _isHidden;
        }
    }

    /// <inheritdoc/>
    public bool HideOffScreen(GameWindowHandle window)
    {
        ArgumentNullException.ThrowIfNull(window);

        lock (_lock)
        {
            // Идемпотентность: уже уведено — no-op
            if (_isHidden)
                return true;

            IntPtr hwnd = window.Hwnd;

            if (!NativeMethods.IsWindow(hwnd))
                return false;

            // Сохраняем текущую позицию и размер окна
            if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect))
                return false;

            // Уводим окно за пределы видимой области
            // SWP_NOSIZE    (0x0001) — размер не менять
            // SWP_NOZORDER  (0x0004) — Z-order не трогать
            // SWP_NOACTIVATE(0x0010) — фокус не захватывать
            const uint flags = NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE;

            if (!NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, OffScreenX, OffScreenY, 0, 0, flags))
                return false;

            _savedRect = rect;
            _hiddenHwnd = hwnd;
            _isHidden = true;
            return true;
        }
    }

    /// <inheritdoc/>
    public bool Restore(GameWindowHandle window)
    {
        ArgumentNullException.ThrowIfNull(window);

        lock (_lock)
        {
            // Идемпотентность: не было уведено — no-op
            if (!_isHidden)
                return true;

            IntPtr hwnd = window.Hwnd;

            // Проверяем актуальность хэндла
            if (!NativeMethods.IsWindow(hwnd))
            {
                ResetState();
                return false;
            }

            if (_savedRect is not { } rect)
            {
                ResetState();
                return false;
            }

            const uint flags = NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE;

            if (!NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, rect.Left, rect.Top, 0, 0, flags))
                return false;

            ResetState();
            return true;
        }
    }

    private void ResetState()
    {
        _savedRect = null;
        _hiddenHwnd = IntPtr.Zero;
        _isHidden = false;
    }

    /// <summary>
    /// P/Invoke-объявления user32.dll для управления позицией окна.
    /// </summary>
    private static partial class NativeMethods
    {
        public const uint SWP_NOSIZE     = 0x0001;
        public const uint SWP_NOZORDER   = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint uFlags);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
