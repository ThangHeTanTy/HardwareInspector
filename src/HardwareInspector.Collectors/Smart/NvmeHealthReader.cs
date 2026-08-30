using HardwareInspector.Collectors.Native;
using HardwareInspector.Core.Models.Components;

namespace HardwareInspector.Collectors.Smart;

/// <summary>
/// Đọc log page 0x02 (SMART / Health Information) của ổ NVMe qua
/// IOCTL_STORAGE_QUERY_PROPERTY với StorageDeviceProtocolSpecificProperty.
/// Đây là đường chính thức mà Windows mở cho phần mềm người dùng và hoạt động
/// trên mọi ổ NVMe dùng driver stornvme chuẩn.
/// </summary>
internal static class NvmeHealthReader
{
    // STORAGE_PROPERTY_QUERY.PropertyId
    private const uint StorageDeviceProtocolSpecificProperty = 50;
    private const uint PropertyStandardQuery = 0;

    // STORAGE_PROTOCOL_TYPE
    private const uint ProtocolTypeNvme = 3;
    // STORAGE_PROTOCOL_NVME_DATA_TYPE
    private const uint NvmeDataTypeLogPage = 2;
    private const uint NvmeLogPageHealthInfo = 0x02;

    private const int PropertyQueryHeaderSize = 8;      // PropertyId + QueryType
    private const int ProtocolSpecificDataSize = 40;    // STORAGE_PROTOCOL_SPECIFIC_DATA
    private const int LogPageSize = 512;
    private const int DataOffsetInBuffer = PropertyQueryHeaderSize + ProtocolSpecificDataSize;

    public static bool TryPopulate(StorageDeviceInfo device)
    {
        using var handle = StorageNativeMethods.OpenPhysicalDrive(device.DeviceIndex);
        if (handle is null) return false;

        var buffer = new byte[DataOffsetInBuffer + LogPageSize];

        // STORAGE_PROPERTY_QUERY
        BitConverter.GetBytes(StorageDeviceProtocolSpecificProperty).CopyTo(buffer, 0);
        BitConverter.GetBytes(PropertyStandardQuery).CopyTo(buffer, 4);

        // STORAGE_PROTOCOL_SPECIFIC_DATA bắt đầu ngay sau header
        var p = PropertyQueryHeaderSize;
        BitConverter.GetBytes(ProtocolTypeNvme).CopyTo(buffer, p + 0);
        BitConverter.GetBytes(NvmeDataTypeLogPage).CopyTo(buffer, p + 4);
        BitConverter.GetBytes(NvmeLogPageHealthInfo).CopyTo(buffer, p + 8);   // RequestValue
        BitConverter.GetBytes(0u).CopyTo(buffer, p + 12);                     // RequestSubValue
        BitConverter.GetBytes((uint)ProtocolSpecificDataSize).CopyTo(buffer, p + 16); // DataOffset
        BitConverter.GetBytes((uint)LogPageSize).CopyTo(buffer, p + 20);      // DataLength

        bool ok;
        try
        {
            ok = StorageNativeMethods.DeviceIoControl(
                handle, StorageNativeMethods.IoctlStorageQueryProperty,
                buffer, (uint)buffer.Length,
                buffer, (uint)buffer.Length,
                out var returned, nint.Zero) && returned >= DataOffsetInBuffer;
        }
        catch { return false; }

        if (!ok) return false;

        var log = new byte[LogPageSize];
        Array.Copy(buffer, DataOffsetInBuffer, log, 0, LogPageSize);

        // Ổ chưa từng ghi gì sẽ trả về toàn số 0 — coi như đọc thất bại.
        if (log.All(b => b == 0)) return false;

        Parse(log, device);
        device.SmartAvailable = true;
        device.SmartSource = "NVMe log page 0x02";
        return true;
    }

    /// <summary>
    /// Bố cục log page 0x02 theo đặc tả NVM Express. Các bộ đếm đều là số 128 bit
    /// little-endian; thực tế 64 bit thấp là quá đủ nên chỉ đọc phần đó.
    /// </summary>
    private static void Parse(byte[] log, StorageDeviceInfo d)
    {
        d.NvmeCriticalWarning = log[0];

        var kelvin = BitConverter.ToUInt16(log, 1);
        if (kelvin > 0) d.TemperatureC = kelvin - 273.15;

        d.AvailableSparePercent = log[3];
        d.AvailableSpareThresholdPercent = log[4];

        var used = log[5];
        d.PercentageUsed = used;
        d.RemainingLifePercent = Math.Max(0, 100 - used);

        // Data Units: mỗi đơn vị = 1000 × 512 byte
        d.TotalHostReadsBytes = (long)(Read64(log, 32) * 1000 * 512);
        d.TotalHostWritesBytes = (long)(Read64(log, 48) * 1000 * 512);

        d.PowerCycleCount = (long)Read64(log, 112);
        d.PowerOnHours = (long)Read64(log, 128);
        d.UnsafeShutdowns = (long)Read64(log, 144);
        d.MediaErrors = (long)Read64(log, 160);
        d.ErrorLogEntries = (long)Read64(log, 176);
    }

    private static ulong Read64(byte[] buffer, int offset) => BitConverter.ToUInt64(buffer, offset);
}
