using HardwareInspector.Collectors.Native;
using HardwareInspector.Core.Models.Components;
using Microsoft.Win32.SafeHandles;

namespace HardwareInspector.Collectors.Smart;

/// <summary>
/// Đọc S.M.A.R.T. của ổ SATA/ATA bằng SMART_RCV_DRIVE_DATA gửi thẳng xuống
/// \\.\PhysicalDriveN. Nếu driver từ chối, thử lại bằng IOCTL_ATA_PASS_THROUGH —
/// một số controller (đặc biệt là Intel RST và cầu USB) chỉ chấp nhận đường thứ hai.
/// </summary>
internal static class AtaSmartReader
{
    private const byte SmartCmd = 0xB0;
    private const byte SmartReadAttributes = 0xD0;
    private const byte SmartReadThresholds = 0xD1;

    private const int SendCmdInParamsSize = 32;
    private const int SendCmdOutParamsHeaderSize = 16;
    private const int AttributeBufferSize = 512;

    public static bool TryPopulate(StorageDeviceInfo device)
    {
        using var handle = StorageNativeMethods.OpenPhysicalDrive(device.DeviceIndex);
        if (handle is null) return false;

        // SMART_GET_VERSION cho biết cổng có hỗ trợ tập lệnh SMART hay không.
        // Bỏ qua kết quả: một số driver trả lỗi ở đây nhưng vẫn phục vụ lệnh đọc.
        var versionBuffer = new byte[24];
        try
        {
            StorageNativeMethods.DeviceIoControl(handle, StorageNativeMethods.SmartGetVersion,
                null, 0, versionBuffer, (uint)versionBuffer.Length, out _, nint.Zero);
        }
        catch { }

        var data = ReadSmartBuffer(handle, device.DeviceIndex, SmartReadAttributes)
                   ?? ReadViaAtaPassThrough(handle, SmartReadAttributes);
        if (data is null) return false;

        var thresholds = ReadSmartBuffer(handle, device.DeviceIndex, SmartReadThresholds)
                         ?? ReadViaAtaPassThrough(handle, SmartReadThresholds);

        ParseAttributes(data, thresholds, device);
        if (device.Attributes.Count == 0) return false;

        device.SmartAvailable = true;
        device.SmartSource = "ATA SMART READ DATA";
        return true;
    }

    /// <summary>Đường thứ nhất: SENDCMDINPARAMS / SENDCMDOUTPARAMS.</summary>
    private static byte[]? ReadSmartBuffer(SafeFileHandle handle, int driveIndex, byte feature)
    {
        var input = new byte[SendCmdInParamsSize];
        BitConverter.GetBytes(AttributeBufferSize).CopyTo(input, 0); // cBufferSize

        // IDEREGS bắt đầu ở offset 4
        input[4] = feature;      // bFeaturesReg
        input[5] = 1;            // bSectorCountReg
        input[6] = 1;            // bSectorNumberReg
        input[7] = 0x4F;         // bCylLowReg   — chữ ký bắt buộc của lệnh SMART
        input[8] = 0xC2;         // bCylHighReg
        input[9] = 0xA0;         // bDriveHeadReg
        input[10] = SmartCmd;    // bCommandReg
        input[11] = 0;           // bReserved
        input[12] = (byte)driveIndex; // bDriveNumber

        var output = new byte[SendCmdOutParamsHeaderSize + AttributeBufferSize];

        try
        {
            var ok = StorageNativeMethods.DeviceIoControl(
                handle, StorageNativeMethods.SmartRcvDriveData,
                input, (uint)input.Length,
                output, (uint)output.Length,
                out var returned, nint.Zero);

            if (!ok || returned < SendCmdOutParamsHeaderSize + 4) return null;
        }
        catch { return null; }

        var buffer = new byte[AttributeBufferSize];
        Array.Copy(output, SendCmdOutParamsHeaderSize, buffer, 0, AttributeBufferSize);
        return buffer.All(b => b == 0) ? null : buffer;
    }

    /// <summary>Đường thứ hai: ATA_PASS_THROUGH_EX với vùng đệm nối liền.</summary>
    private static byte[]? ReadViaAtaPassThrough(SafeFileHandle handle, byte feature)
    {
        // ATA_PASS_THROUGH_EX là 40 byte trên x64 (có padding), tiếp theo là vùng dữ liệu.
        const int headerSize = 40;
        var buffer = new byte[headerSize + AttributeBufferSize];

        BitConverter.GetBytes((ushort)headerSize).CopyTo(buffer, 0);   // Length
        BitConverter.GetBytes((ushort)0x02).CopyTo(buffer, 2);         // AtaFlags = DATA_IN
        buffer[4] = 0;                                                 // PathId
        buffer[5] = 0;                                                 // TargetId
        buffer[6] = 0;                                                 // Lun
        buffer[7] = 0;                                                 // ReservedAsUchar
        BitConverter.GetBytes(AttributeBufferSize).CopyTo(buffer, 8);  // DataTransferLength
        BitConverter.GetBytes(10).CopyTo(buffer, 12);                  // TimeOutValue (giây)
        BitConverter.GetBytes((long)headerSize).CopyTo(buffer, 24);    // DataBufferOffset

        // CurrentTaskFile[8] tại offset 32
        buffer[32] = feature;   // Features
        buffer[33] = 1;         // SectorCount
        buffer[34] = 1;         // LbaLow
        buffer[35] = 0x4F;      // LbaMid
        buffer[36] = 0xC2;      // LbaHigh
        buffer[37] = 0xA0;      // Device
        buffer[38] = SmartCmd;  // Command

        try
        {
            var ok = StorageNativeMethods.DeviceIoControl(
                handle, StorageNativeMethods.IoctlAtaPassThrough,
                buffer, (uint)buffer.Length,
                buffer, (uint)buffer.Length,
                out var returned, nint.Zero);

            if (!ok || returned < headerSize + 4) return null;
        }
        catch { return null; }

        var data = new byte[AttributeBufferSize];
        Array.Copy(buffer, headerSize, data, 0, AttributeBufferSize);
        return data.All(b => b == 0) ? null : data;
    }

    private static void ParseAttributes(byte[] data, byte[]? thresholds, StorageDeviceInfo device)
    {
        // 2 byte revision, sau đó tối đa 30 bản ghi 12 byte.
        for (var offset = 2; offset + 12 <= data.Length && offset < 2 + 30 * 12; offset += 12)
        {
            var id = data[offset];
            if (id == 0) continue;

            var flags = BitConverter.ToUInt16(data, offset + 1);
            var current = data[offset + 3];
            var worst = data[offset + 4];

            ulong raw = 0;
            for (var i = 0; i < 6; i++)
                raw |= (ulong)data[offset + 5 + i] << (8 * i);

            var def = SmartAttributeCatalog.Lookup(id);
            var threshold = thresholds is not null ? FindThreshold(thresholds, id) : (byte)0;

            device.Attributes.Add(new SmartAttribute(
                id, def.Name, current, worst, threshold, raw,
                def.PreFailure || (flags & 0x0001) != 0));

            MapToTypedFields(device, id, raw, current);
        }

        // Ổ tự báo sắp hỏng khi bất kỳ thuộc tính pre-failure nào rơi xuống dưới ngưỡng.
        device.SmartPredictFailure = device.Attributes.Any(a => a.IsPreFailure && a.IsFailing);
    }

    private static byte FindThreshold(byte[] thresholds, byte id)
    {
        for (var offset = 2; offset + 12 <= thresholds.Length; offset += 12)
            if (thresholds[offset] == id) return thresholds[offset + 1];
        return 0;
    }

    private static void MapToTypedFields(StorageDeviceInfo d, byte id, ulong raw, byte current)
    {
        // Giá trị raw của nhiều thuộc tính chỉ dùng 32 bit thấp; phần cao là dữ liệu phụ
        // do hãng tự định nghĩa (ví dụ nhiệt độ min/max nhét chung vào attribute 194).
        var low32 = (long)(raw & 0xFFFFFFFF);

        switch (id)
        {
            case 0x09: d.PowerOnHours ??= low32; break;
            case 0x0C: d.PowerCycleCount ??= low32; break;
            case 0x05: d.ReallocatedSectors = low32; break;
            case 0xC4: d.ReallocationEvents = low32; break;
            case 0xC5: d.PendingSectors = low32; break;
            case 0xC6: d.UncorrectableSectors = low32; break;
            case 0xC7: d.CrcErrors = low32; break;
            case 0xC0: d.UnsafeShutdowns ??= low32; break;
            case 0xBF: d.ShockErrors = low32; break;
            case 0xBB: d.ReportedUncorrectable = low32; break;
            case 0xC2 or 0xBE:
                var temp = (long)(raw & 0xFF);
                if (temp is > 0 and < 120) d.TemperatureC ??= temp;
                break;
            case 0xA9 or 0xE7 or 0xE9 or 0xE8:
                // Các hãng dùng ID khác nhau cho tuổi thọ còn lại, nhưng đều là
                // giá trị chuẩn hoá 100 → 0 chứ không phải raw.
                d.RemainingLifePercent ??= current;
                break;
            case 0xAA: d.AvailableSparePercent ??= current; break;
            case 0xF1 or 0xF2 when raw > 0:
                var bytes = (long)(raw * 512);
                if (id == 0xF1) d.TotalHostWritesBytes ??= bytes;
                else d.TotalHostReadsBytes ??= bytes;
                break;
            case 0xF9 when raw > 0:  // Total NAND writes theo đơn vị GiB ở một số hãng
                d.TotalHostWritesBytes ??= (long)(raw * 1024L * 1024 * 1024);
                break;
        }
    }
}
