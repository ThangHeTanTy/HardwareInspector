namespace HardwareInspector.Core.Models.Diagnostics;

/// <summary>
/// Kết quả bài kiểm tra GPU chạy ngay trong ứng dụng.
///
/// Khác với các con số benchmark khác, phần lớn trường ở đây là bằng chứng cứng:
/// VRAM trả về sai dữ liệu hay phép tính cho hai kết quả khác nhau trên cùng đầu vào
/// thì không còn gì để suy diễn — card có lỗi. Vì vậy kết quả được đưa vào snapshot
/// để analyzer chấm điểm, chứ không chỉ hiển thị trong tab benchmark.
/// </summary>
public sealed class GpuTestResult
{
    public string AdapterName { get; set; } = string.Empty;
    public long DedicatedVideoMemoryBytes { get; set; }

    /// <summary>Bài kiểm tra chạy hết kịch bản (không bị huỷ, không mất thiết bị).</summary>
    public bool Completed { get; set; }

    /// <summary>Lý do không chạy được (không có Direct3D 11, không tạo được thiết bị...).</summary>
    public string? SetupError { get; set; }

    // --- Tải nặng ---

    public TimeSpan StressDuration { get; set; }

    /// <summary>Thông lượng tính toán (tỉ phép/giây) ở phút đầu và phút cuối của bài tải.</summary>
    public double InitialThroughput { get; set; }
    public double FinalThroughput { get; set; }

    /// <summary>Số lần đối chiếu kết quả tính toán với kết quả chuẩn, và số lần lệch.</summary>
    public long ComputeChecks { get; set; }
    public long ComputeMismatches { get; set; }

    /// <summary>
    /// Driver đồ hoạ bị reset giữa chừng (TDR, DEVICE_REMOVED/HUNG).
    /// Trên card khoẻ, bài tải không bao giờ làm driver sập.
    /// </summary>
    public bool DeviceLost { get; set; }
    public string? DeviceLostReason { get; set; }

    // --- VRAM ---

    public bool VramTestSkipped { get; set; }
    public string? VramSkipReason { get; set; }
    public long VramTestedBytes { get; set; }
    public int VramPasses { get; set; }
    public long VramErrors { get; set; }

    /// <summary>Vị trí (byte, tính trong vùng đã kiểm) của ô nhớ lỗi đầu tiên.</summary>
    public long? VramFirstErrorOffset { get; set; }

    /// <summary>Tỉ lệ hiệu năng giữ được từ đầu tới cuối bài tải. Dưới ~0.85 là đang tự hạ xung.</summary>
    public double? ThroughputRetention =>
        InitialThroughput > 0 && FinalThroughput > 0 ? FinalThroughput / InitialThroughput : null;

    public bool HasHardErrors => VramErrors > 0 || ComputeMismatches > 0 || DeviceLost;
}
