using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HardwareInspector.Collectors.Native;

/// <summary>
/// P/Invoke để nói chuyện trực tiếp với ổ đĩa qua \\.\PhysicalDriveN.
///
/// Vì sao không dùng WMI: lớp MSStorageDriver_FailurePredictData chỉ hoạt động với
/// driver ATA đời cũ. Trên Windows 10/11 với ổ NVMe hoặc AHCI hiện đại, nó trả về rỗng —
/// đó chính là lý do phần sức khoẻ ổ cứng không đọc được. Đường đi đúng là
/// gửi thẳng lệnh xuống thiết bị bằng DeviceIoControl.
/// </summary>
internal static class StorageNativeMethods
{
    internal const uint GenericRead = 0x80000000;
    internal const uint GenericWrite = 0x40000000;
    internal const uint FileShareRead = 0x00000001;
    internal const uint FileShareWrite = 0x00000002;
    internal const uint OpenExisting = 3;

    internal const uint IoctlStorageQueryProperty = 0x002D1400;
    internal const uint IoctlAtaPassThrough = 0x0004D02C;
    internal const uint SmartGetVersion = 0x00074080;
    internal const uint SmartRcvDriveData = 0x0007C088;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        nint lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, nint hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        byte[]? lpInBuffer, uint nInBufferSize,
        byte[]? lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, nint lpOverlapped);

    /// <summary>
    /// Mở ổ ở chế độ chỉ đọc thuộc tính. Không yêu cầu GENERIC_WRITE cho phần lớn
    /// truy vấn, nhưng lệnh ATA pass-through thì cần, nên thử lần lượt hai mức quyền.
    /// </summary>
    internal static SafeFileHandle? OpenPhysicalDrive(int index)
    {
        var path = $@"\\.\PhysicalDrive{index}";

        var handle = CreateFileW(path, GenericRead | GenericWrite,
            FileShareRead | FileShareWrite, nint.Zero, OpenExisting, 0, nint.Zero);
        if (!handle.IsInvalid) return handle;
        handle.Dispose();

        handle = CreateFileW(path, GenericRead,
            FileShareRead | FileShareWrite, nint.Zero, OpenExisting, 0, nint.Zero);
        if (!handle.IsInvalid) return handle;
        handle.Dispose();

        // Không có quyền nào mở được: hoặc thiếu Administrator, hoặc ổ nằm sau RAID.
        handle = CreateFileW(path, 0,
            FileShareRead | FileShareWrite, nint.Zero, OpenExisting, 0, nint.Zero);
        if (!handle.IsInvalid) return handle;
        handle.Dispose();

        return null;
    }
}
