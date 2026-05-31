using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;

namespace TBHStats.Capture.Wgc;

/// <summary>
/// Вспомогательный interop-хелпер для создания <see cref="IDirect3DDevice"/>
/// из нативного D3D11-устройства поверх DXGI.
/// </summary>
/// <remarks>
/// Реализует цепочку:
/// <list type="number">
///   <item><c>D3D11CreateDevice</c> (d3d11.dll) → <c>ID3D11Device</c>.</item>
///   <item>QI до <c>IDXGIDevice</c>.</item>
///   <item><c>CreateDirect3D11DeviceFromDXGIDevice</c> (d3d11.dll) → WinRT-объект <c>IInspectable</c>.</item>
///   <item>Маршалинг в управляемый <see cref="IDirect3DDevice"/> через <c>IDirect3DDxgiInterfaceAccess</c>
///       (Marshal.GetObjectForIUnknown).</item>
/// </list>
/// Требует <c>AllowUnsafeBlocks</c> в проекте (unsafe блоки для QI через IntPtr).
/// </remarks>
internal static class Direct3D11Interop
{
    // ── P/Invoke ─────────────────────────────────────────────────────────────────

    [DllImport("d3d11.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void D3D11CreateDevice(
        IntPtr pAdapter,
        uint driverType,
        IntPtr software,
        uint flags,
        IntPtr pFeatureLevels,
        uint featureLevels,
        uint sdkVersion,
        out IntPtr ppDevice,
        IntPtr pFeatureLevel,
        IntPtr ppImmediateContext);

    [DllImport("d3d11.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void CreateDirect3D11DeviceFromDXGIDevice(
        IntPtr dxgiDevice,
        out IntPtr graphicsDevice);

    // ── Константы D3D ──────────────────────────────────────────────────────────

    /// <summary>D3D_DRIVER_TYPE_HARDWARE = 1</summary>
    private const uint D3D_DRIVER_TYPE_HARDWARE = 1;

    /// <summary>D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20 — требуется для DXGI interop / WGC.</summary>
    private const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;

    /// <summary>D3D11_SDK_VERSION = 7</summary>
    private const uint D3D11_SDK_VERSION = 7;

    // ── IID'ы ─────────────────────────────────────────────────────────────────

    /// <summary>IID_IDXGIDevice: {54ec77fa-1377-44e6-8c32-88fd5f44c84c}</summary>
    private static readonly Guid IID_IDXGIDevice =
        new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    /// <summary>IID_IInspectable: {AF86E2E0-B12D-4c6a-9C5A-D7AA65101E90}</summary>
    private static readonly Guid IID_IInspectable =
        new("AF86E2E0-B12D-4c6a-9C5A-D7AA65101E90");

    // ── Публичный API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Создаёт аппаратное D3D11-устройство и оборачивает его в WinRT-тип <see cref="IDirect3DDevice"/>,
    /// совместимый с <c>Direct3D11CaptureFramePool</c>.
    /// </summary>
    /// <returns>WinRT-обёртка над D3D11-устройством.</returns>
    /// <exception cref="COMException">Если D3D11/DXGI недоступны или QI провалился.</exception>
    internal static IDirect3DDevice CreateDevice()
    {
        // Создаём аппаратное D3D11-устройство с флагом BGRA_SUPPORT (обязателен для WGC/DXGI interop)
        D3D11CreateDevice(
            pAdapter: IntPtr.Zero,
            driverType: D3D_DRIVER_TYPE_HARDWARE,
            software: IntPtr.Zero,
            flags: D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            pFeatureLevels: IntPtr.Zero,
            featureLevels: 0,
            sdkVersion: D3D11_SDK_VERSION,
            ppDevice: out IntPtr d3dDevicePtr,
            pFeatureLevel: IntPtr.Zero,
            ppImmediateContext: IntPtr.Zero);

        try
        {
            // QI ID3D11Device → IDXGIDevice
            // Локальная копия IID требуется: readonly static не может быть ref-аргументом вне static ctor
            Guid dxgiDeviceIid = IID_IDXGIDevice;
            int hr = Marshal.QueryInterface(d3dDevicePtr, ref dxgiDeviceIid, out IntPtr dxgiDevicePtr);
            Marshal.ThrowExceptionForHR(hr);

            try
            {
                // Создаём WinRT-обёртку IDirect3DDevice из IDXGIDevice
                CreateDirect3D11DeviceFromDXGIDevice(dxgiDevicePtr, out IntPtr inspectablePtr);

                try
                {
                    // Маршалируем IInspectable → управляемый IDirect3DDevice
                    object? obj = Marshal.GetObjectForIUnknown(inspectablePtr);
                    if (obj is not IDirect3DDevice device)
                        throw new InvalidCastException(
                            "CreateDirect3D11DeviceFromDXGIDevice не вернул IDirect3DDevice.");
                    return device;
                }
                finally
                {
                    Marshal.Release(inspectablePtr);
                }
            }
            finally
            {
                Marshal.Release(dxgiDevicePtr);
            }
        }
        finally
        {
            Marshal.Release(d3dDevicePtr);
        }
    }
}
