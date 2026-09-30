using HardwareInspector.Core.Models.Components;
using HardwareInspector.Core.Models.Firmware;
using HardwareInspector.Core.Models.Sensors;

namespace HardwareInspector.Core.Models;

/// <summary>
/// Ảnh chụp toàn bộ trạng thái phần cứng tại một thời điểm.
/// Mọi lớp phân tích chỉ đọc từ đây — không gọi WMI trực tiếp.
/// </summary>
public sealed class SystemSnapshot
{
    public DateTime CapturedAtLocal { get; set; } = DateTime.Now;

    public List<ProcessorInfo> Processors { get; } = new();
    public List<GraphicsInfo> GraphicsAdapters { get; } = new();
    public MemorySubsystemInfo Memory { get; set; } = new();
    public List<StorageDeviceInfo> StorageDevices { get; } = new();
    public List<BatteryInfo> Batteries { get; } = new();
    public List<DisplayInfo> Displays { get; } = new();
    public BaseboardInfo Baseboard { get; set; } = new();
    public BiosInfo Bios { get; set; } = new();
    public OperatingSystemInfo OperatingSystem { get; set; } = new();
    public FirmwareIntegrityReport Firmware { get; set; } = new();

    /// <summary>Khả năng nâng cấp RAM và ổ cứng, đọc từ bảng SMBIOS.</summary>
    public UpgradeCapability Upgrade { get; set; } = new();

    /// <summary>
    /// Kết quả bài kiểm tra GPU (tải nặng + VRAM) nếu người dùng đã chạy.
    /// Null nghĩa là chưa chạy — số liệu nhiệt GPU khi đó chỉ phản ánh lúc nghỉ.
    /// </summary>
    public Diagnostics.GpuTestResult? GpuTest { get; set; }

    /// <summary>Kết quả đo cảm biến min/max/avg của phiên chạy.</summary>
    public List<SensorStats> SensorSnapshot { get; } = new();

    /// <summary>Sự cố phần cứng trích từ Event Log (WHEA, disk, bugcheck).</summary>
    public List<Finding> ReliabilityFindings { get; } = new();

    /// <summary>Phát hiện về linh kiện bị khai man hoặc dán nhãn lại.</summary>
    public List<Finding> AuthenticityFindings { get; } = new();

    /// <summary>Đầu mối tra bảo hành cho từng linh kiện có serial.</summary>
    public List<Assessment.WarrantyLookup> WarrantyLookups { get; } = new();

    public ProcessorInfo? PrimaryCpu => Processors.FirstOrDefault();
    public GraphicsInfo? PrimaryGpu =>
        GraphicsAdapters.FirstOrDefault(g => g.IsDiscrete) ?? GraphicsAdapters.FirstOrDefault();
    public BatteryInfo? PrimaryBattery => Batteries.FirstOrDefault(b => b.IsPresent);
    public bool IsLaptop => Batteries.Any(b => b.IsPresent) ||
                            Baseboard.ChassisType.Contains("Notebook", StringComparison.OrdinalIgnoreCase) ||
                            Baseboard.ChassisType.Contains("Laptop", StringComparison.OrdinalIgnoreCase);
}
