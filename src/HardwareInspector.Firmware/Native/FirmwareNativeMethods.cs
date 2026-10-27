using System.Runtime.InteropServices;

namespace HardwareInspector.Firmware.Native;

/// <summary>
/// P/Invoke tới API firmware của Windows. Đây là đường duy nhất đọc được bảng SMBIOS thô
/// (danh sách khe RAM, khe mở rộng, cổng trên bo) mà không cần driver kernel.
/// </summary>
internal static partial class FirmwareNativeMethods
{
    // 'RSMB' viết ngược do Windows nhận provider signature theo little-endian.
    internal const uint ProviderRsmb = 0x52534D42;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial uint GetSystemFirmwareTable(
        uint firmwareTableProviderSignature,
        uint firmwareTableID,
        [Out] byte[]? pFirmwareTableBuffer,
        uint bufferSize);
}
