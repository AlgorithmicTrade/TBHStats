using System.Runtime.InteropServices;
using Windows.Graphics.Capture;

namespace TBHStats.Capture.Wgc;

/// <summary>
/// Interop-хелпер для создания <see cref="GraphicsCaptureItem"/> из нативного HWND
/// через COM-интерфейс <c>IGraphicsCaptureItemInterop</c>.
/// </summary>
/// <remarks>
/// Windows.Graphics.Capture не предоставляет управляемого конструктора по HWND
/// из unpackaged-приложения. Вместо этого получаем activation factory
/// (через <c>IActivationFactory</c> / <c>As&lt;IGraphicsCaptureItemInterop&gt;</c>)
/// и вызываем <c>CreateForWindow</c>.
///
/// COM-интерфейс <c>IGraphicsCaptureItemInterop</c>:
/// GUID = {3628E81B-3CAC-4C60-B7F4-23CE0E0C3356},
/// метод CreateForWindow(HWND, REFIID, ppv**).
/// </remarks>
internal static class GraphicsCaptureItemInterop
{
    // IID_IGraphicsCaptureItemInterop: {3628E81B-3CAC-4C60-B7F4-23CE0E0C3356}
    private static readonly Guid IID_IGraphicsCaptureItemInterop =
        new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");

    // IID_GraphicsCaptureItem (WinRT runtimeclass):
    // {79C3F95B-31F7-4EC2-A464-632EF5D30760}
    private static readonly Guid IID_GraphicsCaptureItem =
        new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        /// <summary>
        /// Создать GraphicsCaptureItem из HWND (unpackaged desktop).
        /// vtable-slot 3 (0-based): после QueryInterface, AddRef, Release.
        /// </summary>
        void CreateForWindow(
            [In] IntPtr window,
            [In] ref Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);

        void CreateForMonitor(
            [In] IntPtr monitor,
            [In] ref Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);
    }

    /// <summary>
    /// Создаёт <see cref="GraphicsCaptureItem"/> для указанного HWND игрового окна.
    /// </summary>
    /// <param name="hwnd">Дескриптор окна.</param>
    /// <returns><see cref="GraphicsCaptureItem"/> для данного HWND.</returns>
    /// <exception cref="COMException">
    /// Если HWND недействителен или WGC interop недоступен.
    /// </exception>
    internal static GraphicsCaptureItem CreateForWindow(IntPtr hwnd)
    {
        // Получаем activation factory GraphicsCaptureItem через WinRT activation
        // и QI до IGraphicsCaptureItemInterop
        var factory = WindowsRuntimeMarshal.GetActivationFactory(typeof(GraphicsCaptureItem));
        var interop = (IGraphicsCaptureItemInterop)factory;

        Guid iid = IID_GraphicsCaptureItem;
        interop.CreateForWindow(hwnd, ref iid, out object ppv);

        if (ppv is not GraphicsCaptureItem item)
            throw new InvalidCastException(
                $"IGraphicsCaptureItemInterop.CreateForWindow не вернул GraphicsCaptureItem (HWND=0x{hwnd:X}).");

        return item;
    }

    // ── WindowsRuntimeMarshal helper ─────────────────────────────────────────

    /// <summary>
    /// Thin wrapper: получает activation factory WinRT-класса через RoGetActivationFactory.
    /// Используем встроенный <see cref="System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeMarshal"/>,
    /// доступный при TFM net8.0-windows10.0.22621.0.
    /// </summary>
    private static class WindowsRuntimeMarshal
    {
        [DllImport("combase.dll", ExactSpelling = true, PreserveSig = false)]
        private static extern void RoGetActivationFactory(
            [MarshalAs(UnmanagedType.HString)] string activatableClassId,
            ref Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out object factory);

        private static readonly Guid IID_IActivationFactory =
            new("00000035-0000-0000-C000-000000000046");

        internal static object GetActivationFactory(Type winRtType)
        {
            // Полное имя WinRT runtime class
            string classId = winRtType.FullName
                ?? throw new ArgumentException($"Тип {winRtType} не имеет FullName.");

            Guid iid = IID_IActivationFactory;
            RoGetActivationFactory(classId, ref iid, out object factory);
            return factory;
        }
    }
}
