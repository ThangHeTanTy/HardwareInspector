using System.Runtime.InteropServices;

namespace HardwareInspector.Firmware.Native;

/// <summary>
/// P/Invoke tới các API firmware của Windows. Đây là đường duy nhất đọc được
/// bảng SMBIOS thô và danh sách bảng ACPI mà không cần driver kernel.
/// </summary>
internal static partial class FirmwareNativeMethods
{
    // 'RSMB' và 'ACPI' viết ngược do Windows nhận provider signature theo little-endian.
    internal const uint ProviderRsmb = 0x52534D42; // 'RSMB'
    internal const uint ProviderAcpi = 0x41435049; // 'ACPI'
    internal const uint ProviderFirm = 0x4649524D; // 'FIRM'

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial uint GetSystemFirmwareTable(
        uint firmwareTableProviderSignature,
        uint firmwareTableID,
        [Out] byte[]? pFirmwareTableBuffer,
        uint bufferSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial uint EnumSystemFirmwareTables(
        uint firmwareTableProviderSignature,
        [Out] byte[]? pFirmwareTableEnumBuffer,
        uint bufferSize);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint GetFirmwareEnvironmentVariableExW(
        string lpName,
        string lpGuid,
        [Out] byte[]? pBuffer,
        uint nSize,
        out uint pdwAttributes);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool LookupPrivilegeValueW(string? lpSystemName, string lpName, out long luid);

    [StructLayout(LayoutKind.Sequential)]
    internal struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public long Luid;
        public uint Attributes;
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AdjustTokenPrivileges(
        nint tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TokenPrivileges newState,
        uint bufferLength,
        nint previousState,
        nint returnLength);

    internal const uint TokenAdjustPrivileges = 0x0020;
    internal const uint TokenQuery = 0x0008;
    internal const uint SePrivilegeEnabled = 0x0002;

    /// <summary>
    /// Đọc biến UEFI cần SeSystemEnvironmentPrivilege. Bật một lần khi khởi động ứng dụng.
    /// </summary>
    internal static bool EnableSystemEnvironmentPrivilege()
    {
        try
        {
            var process = System.Diagnostics.Process.GetCurrentProcess().Handle;
            if (!OpenProcessToken(process, TokenAdjustPrivileges | TokenQuery, out var token)) return false;
            if (!LookupPrivilegeValueW(null, "SeSystemEnvironmentPrivilege", out var luid)) return false;

            var tp = new TokenPrivileges
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = SePrivilegeEnabled
            };
            return AdjustTokenPrivileges(token, false, ref tp, 0, nint.Zero, nint.Zero);
        }
        catch { return false; }
    }
}
