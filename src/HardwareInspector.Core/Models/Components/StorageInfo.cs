using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Core.Models.Components;

public sealed record SmartAttribute(
    byte Id,
    string Name,
    byte Current,
    byte Worst,
    byte Threshold,
    ulong RawValue,
    bool IsPreFailure)
{
    public bool IsFailing => Threshold > 0 && Current > 0 && Current <= Threshold;
}

/// <summary>Lý do không đọc được S.M.A.R.T., để hiển thị hướng khắc phục đúng ngôn ngữ.</summary>
public enum SmartFailure { None, UsbBridge, NotElevated, ControllerRejected }

public sealed class StorageDeviceInfo
{
    public string Model { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string FirmwareRevision { get; set; } = string.Empty;
    public string BusType { get; set; } = string.Empty;   // NVMe / SATA / USB
    public string MediaType { get; set; } = string.Empty; // SSD / HDD
    public long CapacityBytes { get; set; }
    public int DeviceIndex { get; set; }
    public int? SpindleSpeedRpm { get; set; }

    /// <summary>Nguồn đọc được S.M.A.R.T. — hiển thị để người dùng biết dữ liệu đến từ đâu.</summary>
    public string? SmartSource { get; set; }
    public bool SmartAvailable { get; set; }
    public SmartFailure SmartFailureReason { get; set; }

    public string? SmartFailureReasonText => SmartFailureReason switch
    {
        SmartFailure.UsbBridge => S(
            "Ổ gắn qua cầu USB. Phần lớn cầu USB không chuyển tiếp lệnh S.M.A.R.T. " +
            "Hãy tháo ổ ra và cắm trực tiếp vào cổng SATA hoặc khe M.2 để đọc được.",
            "The drive is behind a USB bridge. Most USB bridges do not pass S.M.A.R.T. commands through. " +
            "Remove the drive and connect it directly to a SATA port or M.2 slot to read it."),
        SmartFailure.NotElevated => S(
            "Phiên chạy không có quyền Administrator nên không mở được thiết bị ở mức khối. " +
            "Hãy chạy lại ứng dụng bằng Run as administrator.",
            "The session has no Administrator rights, so the block device could not be opened. " +
            "Run the app again with Run as administrator."),
        SmartFailure.ControllerRejected => S(
            "Driver của controller không chấp nhận lệnh S.M.A.R.T. " +
            "Thường gặp khi ổ nằm sau RAID (Intel RST / AMD RAID). " +
            "Chuyển chế độ SATA trong BIOS sang AHCI sẽ đọc được.",
            "The controller driver rejected the S.M.A.R.T. command. " +
            "Common when the drive sits behind RAID (Intel RST / AMD RAID). " +
            "Switching the BIOS SATA mode to AHCI makes it readable."),
        _ => null
    };

    // --- Tuổi thọ ---
    public long? PowerOnHours { get; set; }
    public long? PowerCycleCount { get; set; }
    public int? PercentageUsed { get; set; }
    public int? RemainingLifePercent { get; set; }
    public int? AvailableSparePercent { get; set; }
    public int? AvailableSpareThresholdPercent { get; set; }
    public long? TotalHostWritesBytes { get; set; }
    public long? TotalHostReadsBytes { get; set; }
    public long? UnsafeShutdowns { get; set; }

    // --- Hỏng hóc ---
    public long? ReallocatedSectors { get; set; }
    public long? ReallocationEvents { get; set; }
    public long? PendingSectors { get; set; }
    public long? UncorrectableSectors { get; set; }
    public long? ReportedUncorrectable { get; set; }
    public long? CrcErrors { get; set; }
    public long? MediaErrors { get; set; }
    public long? ErrorLogEntries { get; set; }
    public long? ShockErrors { get; set; }
    public byte? NvmeCriticalWarning { get; set; }
    public bool SmartPredictFailure { get; set; }

    public double? TemperatureC { get; set; }
    public List<SmartAttribute> Attributes { get; } = new();

    public bool IsSsd => MediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
                         BusType.Contains("NVMe", StringComparison.OrdinalIgnoreCase);

    public double? TeraBytesWritten =>
        TotalHostWritesBytes.HasValue ? TotalHostWritesBytes.Value / 1_099_511_627_776d : null;

    public double? TeraBytesRead =>
        TotalHostReadsBytes.HasValue ? TotalHostReadsBytes.Value / 1_099_511_627_776d : null;

    public double? PowerOnYears => PowerOnHours.HasValue ? PowerOnHours.Value / 8760d : null;

    /// <summary>Giải mã cờ cảnh báo tới hạn của NVMe thành mô tả đọc được.</summary>
    public IEnumerable<string> NvmeWarnings
    {
        get
        {
            if (NvmeCriticalWarning is not { } w || w == 0) yield break;
            if ((w & 0x01) != 0) yield return S("Vùng dự phòng đã xuống dưới ngưỡng an toàn", "Available spare has fallen below the threshold");
            if ((w & 0x02) != 0) yield return S("Nhiệt độ vượt ngưỡng tới hạn", "Temperature exceeded the critical threshold");
            if ((w & 0x04) != 0) yield return S("Độ tin cậy của ổ đã suy giảm", "Drive reliability has degraded");
            if ((w & 0x08) != 0) yield return S("Ổ đã chuyển sang chế độ chỉ đọc", "The drive has switched to read-only mode");
            if ((w & 0x10) != 0) yield return S("Bộ nhớ đệm có pin dự phòng bị lỗi", "The volatile memory backup has failed");
            if ((w & 0x20) != 0) yield return S("Vùng nhớ bền vững chỉ còn đọc được", "The persistent memory region is read-only");
        }
    }
}
