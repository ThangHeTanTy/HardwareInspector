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

public sealed class StorageDeviceInfo
{
    public string Model { get; set; } = "Không xác định";
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
    public string? SmartFailureReason { get; set; }

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
            if ((w & 0x01) != 0) yield return "Vùng dự phòng đã xuống dưới ngưỡng an toàn";
            if ((w & 0x02) != 0) yield return "Nhiệt độ vượt ngưỡng tới hạn";
            if ((w & 0x04) != 0) yield return "Độ tin cậy của ổ đã suy giảm";
            if ((w & 0x08) != 0) yield return "Ổ đã chuyển sang chế độ chỉ đọc";
            if ((w & 0x10) != 0) yield return "Bộ nhớ đệm có pin dự phòng bị lỗi";
            if ((w & 0x20) != 0) yield return "Vùng nhớ bền vững chỉ còn đọc được";
        }
    }
}
