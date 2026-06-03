using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TBHStats.Core.Models;
using TBHStats.Data.Repositories;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TBHStats_App.Views;

/// <summary>
/// Host window for the stage comparison page (T041, T068 Phase 5).
/// Implements:
///   - borderless title bar (OverlappedPresenter.SetBorderAndTitleBar hasTitleBar:false)
///   - Win32 drag via WM_NCLBUTTONDOWN on HeaderBorder
///   - geometry persistence: key "compare", loaded OnFirstActivated, saved with 500 ms debounce
/// </summary>
public sealed partial class CompareHostWindow : Window
{
    private const string WindowKey = "compare";

    private readonly ILogger<CompareHostWindow> _logger;

    // ── Win32 P/Invoke for drag without title bar ────────────────────────────

    [LibraryImport("user32.dll")]
    private static partial void ReleaseCapture();

    [LibraryImport("user32.dll")]
    private static partial IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WmNclbuttondown = 0x00A1;
    private const int  HtCaption       = 0x0002;

    public CompareHostWindow()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<CompareHostWindow>>();

        // ── Borderless title bar (same pattern as WidgetWindow) ──────────────
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
            // Keep maximize/minimize enabled for a larger resizable window.
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
        }

        // Default size; may be overridden by persisted placement in OnFirstActivated.
        AppWindow.Resize(new SizeInt32(820, 600));
        AppWindow.Title = "TBHStats — Stage Comparison";
        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Navigate to CompareView after InitializeComponent (frame is ready).
        CompareFrame.Navigate(typeof(CompareView));

        // Load persisted geometry on first activation.
        Activated += OnFirstActivated;

        // Persist geometry on position/size changes.
        AppWindow.Changed += OnAppWindowChanged;
    }

    // ─── Borderless drag ─────────────────────────────────────────────────────

    /// <summary>
    /// PointerPressed on header bar — start system drag via Win32 WM_NCLBUTTONDOWN.
    /// Guard skips the event when the ✕ button (or any child button) is pressed.
    /// </summary>
    private void OnHeaderPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsWithinButton(e.OriginalSource as DependencyObject)) return;

        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ReleaseCapture();
        SendMessageW(hwnd, WmNclbuttondown, new IntPtr(HtCaption), IntPtr.Zero);
    }

    /// <summary>
    /// Returns true if <paramref name="source"/> or any visual ancestor is a <see cref="Button"/>.
    /// </summary>
    private static bool IsWithinButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button) return true;
            source = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    // ─── Close button ────────────────────────────────────────────────────────

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    // ─── First activation: load persisted placement ─────────────────────────

    private bool _settingsApplied;

    private async void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_settingsApplied) return;
        _settingsApplied = true;

        Activated -= OnFirstActivated;

        try
        {
            WindowPlacement? wp = await LoadAsync().ConfigureAwait(true);
            if (wp is not null && wp.Width > 0 && wp.Height > 0)
            {
                AppWindow.Resize(new SizeInt32((int)wp.Width, (int)wp.Height));

                if (wp.PosX != 0 || wp.PosY != 0)
                    AppWindow.Move(new Windows.Graphics.PointInt32((int)wp.PosX, (int)wp.PosY));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load CompareHostWindow placement on activation.");
        }
    }

    // ─── Persist geometry on change (500 ms debounce) ───────────────────────

    private bool _savePending;

    private async void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPositionChange && !args.DidSizeChange) return;
        if (_savePending) return;
        _savePending = true;

        try
        {
            await Task.Delay(500).ConfigureAwait(true);

            WindowPlacement placement = new()
            {
                WindowKey = WindowKey,
                PosX      = AppWindow.Position.X,
                PosY      = AppWindow.Position.Y,
                Width     = AppWindow.Size.Width,
                Height    = AppWindow.Size.Height,
            };

            await SaveAsync(placement).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save CompareHostWindow placement.");
        }
        finally
        {
            _savePending = false;
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static async Task<WindowPlacement?> LoadAsync()
    {
        await using AsyncServiceScope scope = App.Services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<ISettingsRepository>()
            .GetWindowPlacementAsync(WindowKey)
            .ConfigureAwait(false);
    }

    private static async Task SaveAsync(WindowPlacement placement)
    {
        await using AsyncServiceScope scope = App.Services.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<ISettingsRepository>()
            .SaveWindowPlacementAsync(placement)
            .ConfigureAwait(false);
    }
}
