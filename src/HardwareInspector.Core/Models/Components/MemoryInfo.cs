using static HardwareInspector.Core.Localization.Loc;

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
    /// <summary>
    /// Số kênh khác nhau suy ra từ nhãn khe; 0 khi nhãn không cho biết kênh.
    /// Lưu con số thay cho câu mô tả để câu chữ đổi theo ngôn ngữ đang chọn.
    /// </summary>
    public int ChannelGroups { get; set; }

    public string ChannelConfiguration => Modules.Count switch
    {
        0 => S("Không đọc được", "Unreadable"),
        1 => S("Single channel (1 thanh)", "Single channel (1 module)"),
        _ when ChannelGroups == 0 => S($"Không xác định được từ nhãn khe ({Modules.Count} thanh)",
                                       $"Cannot be determined from slot labels ({Modules.Count} modules)"),
        _ when ChannelGroups >= 2 => S($"Dual/Multi channel ({Modules.Count} thanh, {ChannelGroups} kênh)",
                                       $"Dual/Multi channel ({Modules.Count} modules, {ChannelGroups} channels)"),
        _ => S($"Cùng một kênh ({Modules.Count} thanh) — kiểm tra lại vị trí cắm",
               $"Same channel ({Modules.Count} modules) — check which slots are used")
    };
}
