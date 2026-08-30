using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using HardwareInspector.Core.Models.Components;

namespace HardwareInspector.App.Services;

/// <summary>
/// Đặt một cửa sổ lên đúng màn hình vật lý.
///
/// Không dùng thuộc tính Left/Top của WPF vì chúng tính theo đơn vị độc lập thiết bị,
/// còn toạ độ lấy từ EnumDisplaySettings là pixel vật lý. Khi hai màn có tỉ lệ DPI
/// khác nhau, phép quy đổi rất dễ sai và cửa sổ nhảy sang màn bên cạnh.
/// SetWindowPos nhận thẳng pixel vật lý nên tránh được toàn bộ vấn đề đó.
/// </summary>
internal static class MonitorPositioner
{
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpShowWindow = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    public static bool MoveTo(Window window, DisplayInfo target)
    {
        if (target.WidthPx <= 0 || target.HeightPx <= 0) return false;

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero) return false;

        // Phải ở trạng thái Normal thì SetWindowPos mới có tác dụng.
        window.WindowState = WindowState.Normal;

        var moved = SetWindowPos(handle, nint.Zero,
            target.PositionX, target.PositionY,
            target.WidthPx, target.HeightPx,
            SwpNoZOrder | SwpShowWindow);

        if (!moved) return false;

        // Maximize sau khi đã nằm đúng màn: WPF sẽ phủ kín đúng màn hình đó.
        window.Dispatcher.BeginInvoke(
            new Action(() => window.WindowState = WindowState.Maximized),
            System.Windows.Threading.DispatcherPriority.Loaded);

        return true;
    }
}
