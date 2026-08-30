using System.Runtime.InteropServices;

namespace HardwareInspector.Collectors.Native;

/// <summary>
/// API liệt kê thiết bị hiển thị của Windows.
///
/// Cách đúng để tìm màn hình là đi từ card xuất hình xuống: EnumDisplayDevices ở mức
/// thứ nhất trả về từng adapter (\\.\DISPLAY1, \\.\DISPLAY2...), mỗi adapter tương ứng
/// một cổng xuất. Gọi tiếp EnumDisplayDevices trên tên adapter đó sẽ ra màn hình
/// đang cắm vào cổng ấy. Nhờ vậy cắm thêm màn rời là thấy ngay, và biết nó nằm ở cổng nào.
/// </summary>
internal static class DisplayNativeMethods
{
    internal const int EnumCurrentSettings = -1;
    internal const uint EddGetDeviceInterfaceName = 0x00000001;

    // DISPLAY_DEVICE.StateFlags
    internal const uint DisplayDeviceAttachedToDesktop = 0x00000001;
    internal const uint DisplayDevicePrimaryDevice = 0x00000004;
    internal const uint DisplayDeviceMirroringDriver = 0x00000008;
    internal const uint DisplayDeviceActive = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayDevice
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayDevicesW(
        string? lpDevice, uint iDevNum, ref DisplayDevice lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplaySettingsW(
        string? lpszDeviceName, int iModeNum, ref DevMode lpDevMode);

    internal static DisplayDevice NewDisplayDevice() =>
        new()
        {
            cb = Marshal.SizeOf<DisplayDevice>(),
            DeviceName = string.Empty,
            DeviceString = string.Empty,
            DeviceID = string.Empty,
            DeviceKey = string.Empty
        };

    internal static DevMode NewDevMode() =>
        new()
        {
            dmSize = (ushort)Marshal.SizeOf<DevMode>(),
            dmDeviceName = string.Empty,
            dmFormName = string.Empty
        };
}
