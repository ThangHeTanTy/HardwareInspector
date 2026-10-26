namespace HardwareInspector.Core.Models.Assessment;

public sealed record DriveSummary(string Model, long Bytes, string Type);
public sealed record DisplaySummary(int? Inches, int? RefreshHz);

/// <summary>
/// Bản tóm tắt một máy đã đi xem, lưu lại để so với các máy khác.
/// Chỉ giữ số liệu và mã (không giữ câu chữ), để danh sách hiển thị đúng ngôn ngữ đang chọn.
/// </summary>
public sealed class MachineRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime SavedAt { get; set; } = DateTime.Now;

    /// <summary>Tên người dùng đặt để nhớ, ví dụ "ThinkPad anh Nam Cầu Giấy".</summary>
    public string Nickname { get; set; } = string.Empty;

    /// <summary>Giá người bán đưa ra, đồng. Null khi chưa nhập.</summary>
    public long? AskedPriceVnd { get; set; }

    public string MachineTitle { get; set; } = string.Empty;
    public bool IsLaptop { get; set; }
    public string CpuName { get; set; } = string.Empty;
    public int Cores { get; set; }
    public int Threads { get; set; }
    public long RamBytes { get; set; }
    public string RamType { get; set; } = string.Empty;
    public List<DriveSummary> Drives { get; set; } = new();
    public List<string> Gpus { get; set; } = new();
    public List<DisplaySummary> Displays { get; set; } = new();
    public double? BatteryHealthPercent { get; set; }

    public int OverallScore { get; set; }
    public HealthRating OverallRating { get; set; }
    public PurchaseDecision Decision { get; set; }
    public TrustLevel TrustLevel { get; set; }
    public long RepairMinVnd { get; set; }
    public long RepairMaxVnd { get; set; }
    public int BlockerCount { get; set; }

    /// <summary>Tiền thật phải bỏ ra: giá mua cộng trung bình chi phí khắc phục.</summary>
    public long? EffectiveCostVnd => AskedPriceVnd is { } p ? p + (RepairMinVnd + RepairMaxVnd) / 2 : null;
}
