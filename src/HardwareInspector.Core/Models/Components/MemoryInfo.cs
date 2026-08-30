namespace HardwareInspector.Core.Models.Components;

public sealed class MemoryModuleInfo
{
    public string BankLabel { get; set; } = string.Empty;
    public string DeviceLocator { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string PartNumber { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public long CapacityBytes { get; set; }
    public int SpeedMtps { get; set; }              // tốc độ danh định của thanh RAM
    public int ConfiguredSpeedMtps { get; set; }    // tốc độ đang chạy thực tế
    public string FormFactor { get; set; } = string.Empty;
    public string MemoryType { get; set; } = string.Empty;
    public int? ManufactureYear { get; set; }
    public int? ManufactureWeek { get; set; }
}

public sealed class MemorySubsystemInfo
{
    public List<MemoryModuleInfo> Modules { get; } = new();
    public long TotalPhysicalBytes { get; set; }
    public long AvailablePhysicalBytes { get; set; }
    public int SlotsTotal { get; set; }
    public int SlotsUsed => Modules.Count;
    public bool IsEccEnabled { get; set; }

    /// <summary>Chạy đa kênh hay không — suy ra từ số thanh và vị trí khe.</summary>
    public string ChannelConfiguration { get; set; } = "Không xác định";
}
