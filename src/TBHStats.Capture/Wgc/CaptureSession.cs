using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using TBHStats.Core.Models;
using TBHStats.Capture.WindowTracking;

namespace TBHStats.Capture.Wgc;

/// <summary>
/// Реализация <see cref="ICaptureSession"/> поверх Windows.Graphics.Capture (WGC).
/// Захватывает кадры конкретного окна игры по HWND — даже если оно перекрыто другими окнами.
/// </summary>
/// <remarks>
/// Машина состояний (FR-005/005b, R1):
/// <list type="bullet">
///   <item><see cref="CaptureState.NotFound"/> — окно не найдено; <see cref="TryGetFrameAsync"/> возвращает null.</item>
///   <item><see cref="CaptureState.Capturing"/> — окно видимо; WGC захватывает кадры.</item>
///   <item><see cref="CaptureState.Waiting"/> — окно свёрнуто или закрыто; нет валидной поверхности; null-кадр.</item>
/// </list>
/// Перекрытие другим окном НЕ переводит в <see cref="CaptureState.Waiting"/> — WGC захватывает
/// содержимое окна независимо от Z-order.
///
/// WGC-pipeline использует <see cref="Direct3D11CaptureFramePool.CreateFreeThreaded"/>
/// (не требует UI-диспетчера; безопасен для фоновых циклов захвата).
/// При изменении размера окна <c>framePool</c> пересоздаётся через <c>Recreate</c>.
/// </remarks>
public sealed class CaptureSession : ICaptureSession
{
    private readonly IGameWindowTracker _tracker;

    // ── WGC-ресурсы (null до первого Capturing) ──────────────────────────────

    private IDirect3DDevice? _d3dDevice;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _captureSession;
    private GameWindowHandle? _currentWindow;
    private SizeInt32 _poolSize;

    // ── Машина состояний ─────────────────────────────────────────────────────

    private CaptureState _state = CaptureState.NotFound;
    private readonly object _stateLock = new();

    // ── Dispose ──────────────────────────────────────────────────────────────

    private volatile bool _disposed;
    private readonly SemaphoreSlim _resourceLock = new(1, 1);

    // ── Публичный API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Создаёт сессию захвата. Трекер окна передаётся извне (DI).
    /// WGC-ресурсы создаются лениво при первом <see cref="TryGetFrameAsync"/>.
    /// </summary>
    /// <param name="tracker">Трекер окна игры.</param>
    public CaptureSession(IGameWindowTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        _tracker = tracker;
    }

    /// <inheritdoc/>
    public CaptureState State
    {
        get
        {
            lock (_stateLock)
                return _state;
        }
    }

    /// <inheritdoc/>
    public event Action<CaptureState>? StateChanged;

    /// <inheritdoc/>
    public async Task<CapturedFrame?> TryGetFrameAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();

        await _resourceLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await GetFrameInternalAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _resourceLock.Release();
        }
    }

    // ── Внутренняя логика ─────────────────────────────────────────────────────

    /// <summary>
    /// Основная логика получения кадра. Вызывается под <c>_resourceLock</c>.
    /// </summary>
    private async Task<CapturedFrame?> GetFrameInternalAsync(CancellationToken ct)
    {
        // ── 1. Найти/обновить окно ────────────────────────────────────────────

        // Если текущий хендл устарел (HWND закрылся) — сбросить
        if (_currentWindow is not null)
        {
            WindowVisibility vis = _tracker.GetVisibility(_currentWindow);
            if (vis == WindowVisibility.Closed)
            {
                ReleaseWgcResources();
                _currentWindow = null;
                TransitionTo(CaptureState.NotFound);
                return null;
            }
            else if (vis == WindowVisibility.Minimized)
            {
                // Свёрнутое окно → Waiting; ресурсы НЕ освобождаем (авто-возобновление)
                TransitionTo(CaptureState.Waiting);
                return null;
            }
        }
        else
        {
            // Пробуем найти окно
            _currentWindow = _tracker.FindGameWindow();
            if (_currentWindow is null)
            {
                TransitionTo(CaptureState.NotFound);
                return null;
            }

            // Только что нашли — проверяем видимость
            WindowVisibility vis = _tracker.GetVisibility(_currentWindow);
            if (vis == WindowVisibility.Minimized)
            {
                TransitionTo(CaptureState.Waiting);
                return null;
            }
            if (vis == WindowVisibility.Closed)
            {
                _currentWindow = null;
                TransitionTo(CaptureState.NotFound);
                return null;
            }
        }

        // ── 2. Обеспечить активный WGC-pipeline ──────────────────────────────

        SizePx clientSize = _tracker.GetClientSize(_currentWindow);
        if (!clientSize.IsNonEmpty)
        {
            // Размер нулевой — скорее всего окно только что свернулось между вызовами
            TransitionTo(CaptureState.Waiting);
            return null;
        }

        SizeInt32 currentPoolSize = new SizeInt32
        {
            Width  = clientSize.Width,
            Height = clientSize.Height,
        };

        if (_framePool is null || _captureSession is null || _d3dDevice is null)
        {
            // Первый запуск: инициализировать WGC-pipeline
            InitializeWgcPipeline(_currentWindow.Hwnd, currentPoolSize);
        }
        else if (currentPoolSize.Width  != _poolSize.Width ||
                 currentPoolSize.Height != _poolSize.Height)
        {
            // Ресайз окна: пересоздаём framePool
            RecreateFramePool(currentPoolSize);
        }

        TransitionTo(CaptureState.Capturing);

        // ── 3. Получить кадр из framePool ─────────────────────────────────────

        ct.ThrowIfCancellationRequested();
        Direct3D11CaptureFrame? frame = _framePool!.TryGetNextFrame();
        if (frame is null)
        {
            // Кадр ещё не готов — это норма; возвращаем null без изменения состояния
            return null;
        }

        using (frame)
        {
            // Конвертируем поверхность кадра в SoftwareBitmap
            IDirect3DSurface surface = frame.Surface;
            SoftwareBitmap bitmap = await SoftwareBitmap
                .CreateCopyFromSurfaceAsync(surface)
                .AsTask(ct)
                .ConfigureAwait(false);

            // Проверяем, не изменился ли размер после получения кадра
            SizeInt32 frameSize = frame.ContentSize;
            SizePx actualSize = new SizePx(frameSize.Width, frameSize.Height);

            return new CapturedFrame(bitmap, actualSize, DateTimeOffset.UtcNow);
        }
    }

    /// <summary>
    /// Инициализирует D3D11-устройство, framePool и GraphicsCaptureSession.
    /// Вызывается при первом переходе в Capturing.
    /// </summary>
    private void InitializeWgcPipeline(IntPtr hwnd, SizeInt32 size)
    {
        _d3dDevice = Direct3D11Interop.CreateDevice();

        GraphicsCaptureItem item = GraphicsCaptureItemInterop.CreateForWindow(hwnd);

        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _d3dDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            numberOfBuffers: 2,
            size);

        _captureSession = _framePool.CreateCaptureSession(item);

        // Отключаем жёлтую рамку-индикатор захвата (Windows 11, требует SDK 22621+)
        // Если API недоступен на данном билде — игнорируем.
        TryDisableBorderIndicator(_captureSession);

        _captureSession.StartCapture();
        _poolSize = size;
    }

    /// <summary>
    /// Пересоздаёт framePool при изменении размера окна.
    /// </summary>
    private void RecreateFramePool(SizeInt32 newSize)
    {
        _framePool!.Recreate(
            _d3dDevice!,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            numberOfBuffers: 2,
            newSize);

        _poolSize = newSize;
    }

    /// <summary>
    /// Пытается отключить жёлтую рамку-индикатор захвата.
    /// Доступно начиная с Windows 11 SDK 22621. Сбой — не критичен, игнорируется.
    /// </summary>
    private static void TryDisableBorderIndicator(GraphicsCaptureSession session)
    {
        try
        {
            session.IsBorderRequired = false;
        }
        catch (COMException)
        {
            // API недоступен на текущей версии ОС — не критично
        }
        catch (NotSupportedException)
        {
            // Аналогично — некоторые сборки SDK могут не поддерживать
        }
    }

    /// <summary>
    /// Освобождает WGC-ресурсы (session, framePool, device) без сброса хендла окна.
    /// </summary>
    private void ReleaseWgcResources()
    {
        try { _captureSession?.Dispose(); } catch { /* не кидаем при dispose */ }
        _captureSession = null;

        try { _framePool?.Dispose(); } catch { /* не кидаем при dispose */ }
        _framePool = null;

        try { (_d3dDevice as IDisposable)?.Dispose(); } catch { /* не кидаем при dispose */ }
        _d3dDevice = null;
    }

    /// <summary>
    /// Переводит машину состояний в <paramref name="newState"/>,
    /// поднимая событие <see cref="StateChanged"/> только при фактическом изменении.
    /// </summary>
    private void TransitionTo(CaptureState newState)
    {
        bool changed;
        lock (_stateLock)
        {
            changed = _state != newState;
            _state = newState;
        }

        if (changed)
            StateChanged?.Invoke(newState);
    }

    // ── IAsyncDisposable ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Ожидаем освобождения resourceLock перед чисткой
        await _resourceLock.WaitAsync().ConfigureAwait(false);
        try
        {
            ReleaseWgcResources();
            _currentWindow = null;

            lock (_stateLock)
                _state = CaptureState.NotFound;
        }
        finally
        {
            _resourceLock.Release();
            _resourceLock.Dispose();
        }
    }
}
