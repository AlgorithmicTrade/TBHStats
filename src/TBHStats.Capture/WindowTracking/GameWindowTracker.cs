using System.Runtime.InteropServices;
using System.Text;
using TBHStats.Core.Models;

namespace TBHStats.Capture.WindowTracking;

/// <summary>
/// Реализация <see cref="IGameWindowTracker"/> поверх Win32 P/Invoke (user32.dll).
/// Ищет окно игры Task Bar Hero по подстрокам заголовка (case-insensitive Contains).
/// Конфигурируется через <see cref="GameWindowTrackerOptions"/>.
/// </summary>
public sealed class GameWindowTracker : IGameWindowTracker
{
    private readonly IReadOnlyList<string> _windowTitleHints;

    /// <summary>
    /// Создать трекер с настройками по умолчанию:
    /// заголовок ищется по подстрокам <c>["Task Bar Hero", "TaskBarHero", "TBH"]</c>.
    /// </summary>
    public GameWindowTracker()
        : this(GameWindowTrackerOptions.Default) { }

    /// <summary>
    /// Создать трекер с явными настройками.
    /// </summary>
    /// <param name="options">Настройки поиска окна.</param>
    public GameWindowTracker(GameWindowTrackerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _windowTitleHints = options.WindowTitleHints;
    }

    /// <inheritdoc/>
    public GameWindowHandle? FindGameWindow()
    {
        GameWindowHandle? found = null;

        NativeMethods.EnumWindowsProc callback = (hwnd, _) =>
        {
            // Пропускаем невидимые top-level окна
            if (!NativeMethods.IsWindowVisible(hwnd))
                return true;

            // Читаем заголовок окна
            int length = NativeMethods.GetWindowTextLengthW(hwnd);
            if (length <= 0)
                return true;

            var sb = new StringBuilder(length + 1);
            NativeMethods.GetWindowTextW(hwnd, sb, sb.Capacity);
            string title = sb.ToString();

            if (string.IsNullOrWhiteSpace(title))
                return true;

            // Матчинг: case-insensitive Contains по любой из подсказок
            foreach (string hint in _windowTitleHints)
            {
                if (title.Contains(hint, StringComparison.OrdinalIgnoreCase))
                {
                    NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                    found = new GameWindowHandle(hwnd, (int)pid, title);
                    return false; // остановить перечисление
                }
            }

            return true;
        };

        NativeMethods.EnumWindows(callback, IntPtr.Zero);
        return found;
    }

    /// <inheritdoc/>
    public WindowVisibility GetVisibility(GameWindowHandle window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!NativeMethods.IsWindow(window.Hwnd))
            return WindowVisibility.Closed;

        if (NativeMethods.IsIconic(window.Hwnd))
            return WindowVisibility.Minimized;

        return WindowVisibility.Visible;
    }

    /// <inheritdoc/>
    public SizePx GetClientSize(GameWindowHandle window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!NativeMethods.IsWindow(window.Hwnd))
            return SizePx.Empty;

        if (!NativeMethods.GetClientRect(window.Hwnd, out NativeMethods.RECT rect))
            return SizePx.Empty;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        return width > 0 && height > 0
            ? new SizePx(width, height)
            : SizePx.Empty;
    }

    /// <summary>
    /// P/Invoke-объявления user32.dll.
    /// </summary>
    private static partial class NativeMethods
    {
        // EnumWindows передаёт управляемый делегат — LibraryImport не поддерживает делегаты
        // напрямую без unsafe, поэтому здесь используется классический DllImport.
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = false)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        public static extern int GetWindowTextLengthW(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = false)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = false)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = false)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = false)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = false)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

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
