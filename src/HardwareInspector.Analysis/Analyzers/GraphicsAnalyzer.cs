using HardwareInspector.Analysis.Scoring;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Assessment;
using HardwareInspector.Core.Models.Sensors;
using HardwareInspector.Core.Utils;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Analysis.Analyzers;

/// <summary>
/// Card đồ hoạ không có đồng hồ đếm giờ như ổ cứng, nên phải đọc gián tiếp:
/// nhiệt độ dưới tải, chênh lệch giữa nhiệt độ GPU và điểm nóng (hotspot),
/// và bộ nhớ VRAM có báo lỗi hay không.
/// </summary>
public sealed class GraphicsAnalyzer : IComponentAnalyzer
{
    public ComponentKind Kind => ComponentKind.Gpu;

    public bool CanAnalyze(SystemSnapshot snapshot) => snapshot.GraphicsAdapters.Count > 0;

    public ComponentAssessment Analyze(SystemSnapshot snapshot)
    {
        var gpu = snapshot.PrimaryGpu!;
        var card = new ScoreCard(Kind, gpu.Name.OrDash());

        card.Metric("Nhà sản xuất", gpu.Vendor.OrDash())
            .Metric("Loại", gpu.IsDiscrete ? "Card rời" : "Đồ hoạ tích hợp")
            .MetricIf(gpu.VideoMemoryBytes > 0, "Bộ nhớ đồ hoạ", gpu.VideoMemoryBytes.ToSize(0))
            .Metric("Driver", $"{gpu.DriverVersion.OrDash()} ({gpu.DriverDate:dd/MM/yyyy})")
            .MetricIf(gpu.VideoBiosVersion is not null, "vBIOS", gpu.VideoBiosVersion.OrDash());

        if (snapshot.GraphicsAdapters.Count > 1)
            card.Metric("Các card khác",
                string.Join(", ", snapshot.GraphicsAdapters.Skip(1).Select(g => g.Name)));

        var temps = snapshot.SensorSnapshot
            .Where(s => s.Kind == SensorKind.Temperature && IsGpuSensor(s))
            .ToList();
        var clocks = snapshot.SensorSnapshot
            .Where(s => s.Kind == SensorKind.Clock && IsGpuSensor(s))
            .ToList();
        var fans = snapshot.SensorSnapshot
            .Where(s => s.Kind == SensorKind.Fan && IsGpuSensor(s))
            .ToList();
        card.AttachSensors(temps.Concat(clocks).Concat(fans));

        var core = temps.FirstOrDefault(t => t.SensorName.Contains("Core", StringComparison.OrdinalIgnoreCase))
                   ?? temps.FirstOrDefault();
        var hotspot = temps.FirstOrDefault(t =>
            t.SensorName.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase) ||
            t.SensorName.Contains("Junction", StringComparison.OrdinalIgnoreCase));
        var memoryTemp = temps.FirstOrDefault(t =>
            t.SensorName.Contains("Memory", StringComparison.OrdinalIgnoreCase));

        if (core is not null)
        {
            card.Metric("Nhiệt độ GPU (min / TB / max)",
                $"{core.Min:0} / {core.Average:0} / {core.Max:0} °C");

            if (core.Max >= 90)
                card.Add("GPU-TEMP-001", Kind, Severity.Warning,
                    $"GPU đạt {core.Max:0} °C",
                    "Nhiệt độ này khiến card tự hạ xung. Nguyên nhân phổ biến là keo tản nhiệt khô hoặc quạt bám bụi.",
                    "Vệ sinh và thay keo tản nhiệt cho card.",
                    penalty: 15, confidence: 0.85, source: EvidenceSource.Sensor);
        }
        else
        {
            card.Metric("Nhiệt độ GPU", "Không đọc được",
                "Cần quyền Administrator, hoặc card không phơi bày cảm biến", Severity.Notice);
        }

        // Chênh lệch lớn giữa nhiệt độ nhân và điểm nóng nhất là dấu hiệu kinh điển
        // của keo/pad tản nhiệt đã xuống cấp — thường gặp ở card từng chạy đào coin.
        if (core is not null && hotspot is not null)
        {
            var delta = hotspot.Max - core.Max;
            card.Metric("Chênh lệch nhân ↔ điểm nóng", $"{delta:0} °C");

            if (delta > 25)
                card.Add("GPU-TEMP-002", Kind, Severity.Critical,
                    $"Chênh lệch điểm nóng lên tới {delta:0} °C",
                    "Trên card khoẻ, chênh lệch này thường dưới 15 °C. " +
                    "Mức cao bất thường cho thấy keo tản nhiệt đã tách lớp hoặc lắp lệch, " +
                    "hay gặp ở card đã chạy tải nặng liên tục nhiều tháng.",
                    "Yêu cầu người bán thay keo và pad trước khi giao, hoặc trừ chi phí bảo dưỡng.",
                    penalty: 28, confidence: 0.85, source: EvidenceSource.Sensor);
            else if (delta > 18)
                card.Add("GPU-TEMP-003", Kind, Severity.Warning,
                    $"Chênh lệch điểm nóng {delta:0} °C",
                    "Cao hơn mức bình thường. Keo tản nhiệt bắt đầu xuống cấp.",
                    penalty: 12, confidence: 0.7, source: EvidenceSource.Sensor);
        }

        if (memoryTemp is not null)
        {
            card.Metric("Nhiệt độ VRAM (max)", $"{memoryTemp.Max:0} °C");
            if (memoryTemp.Max >= 95)
                card.Add("GPU-VRAM-001", Kind, Severity.Warning,
                    $"VRAM nóng {memoryTemp.Max:0} °C",
                    "GDDR6 hoạt động ổn định dưới khoảng 95 °C. Vượt mức này thường do pad tản nhiệt đã chai.",
                    "Thay pad tản nhiệt cho chip nhớ.",
                    penalty: 14, confidence: 0.8, source: EvidenceSource.Sensor);
        }

        if (fans.Count > 0)
        {
            foreach (var fan in fans.Take(3))
                card.Metric($"Quạt {fan.SensorName}", $"{fan.Min:0} – {fan.Max:0} RPM");

            if (fans.All(f => f.Max < 100))
                card.Add("GPU-FAN-001", Kind, Severity.Warning,
                    "Quạt card không quay trong suốt phiên đo",
                    "Có thể card đang ở chế độ quạt dừng khi tải thấp (bình thường), " +
                    "cũng có thể quạt đã hỏng hoặc dây bị đứt.",
                    "Chạy bài kiểm tra tải GPU và quan sát quạt có khởi động không.",
                    penalty: 10, confidence: 0.5, source: EvidenceSource.Sensor);
        }

        var clock = clocks.FirstOrDefault(c => c.SensorName.Contains("Core", StringComparison.OrdinalIgnoreCase));
        if (clock is not null)
            card.Metric("Xung nhân (min / TB / max)", $"{clock.Min:0} / {clock.Average:0} / {clock.Max:0} MHz");

        if (gpu.DriverDate is { } dd && (DateTime.Now - dd).TotalDays > 730)
            card.Add("GPU-DRV-001", Kind, Severity.Notice,
                $"Driver đồ hoạ đã cũ ({dd:MM/yyyy})",
                "Driver quá cũ thường gây lỗi hiển thị và không hỗ trợ tựa game/ứng dụng mới.",
                "Cài driver mới nhất từ trang của hãng card.",
                penalty: 5, confidence: 0.9);

        if (gpu.IsDiscrete)
            card.Add("GPU-CHECK-001", Kind, Severity.Info,
                "Cần kiểm tra thủ công phần hiển thị",
                "Phần mềm không phát hiện được artifact (điểm ảnh lỗi, sọc, chấm màu) khi card yếu. " +
                "Đây là biểu hiện đặc trưng của card từng chạy quá nhiệt hoặc bị flash vBIOS sai.",
                "Chạy bài kiểm tra tải GPU trong ứng dụng ít nhất 15 phút và quan sát màn hình.",
                penalty: 0, confidence: 1.0);

        foreach (var f in snapshot.AuthenticityFindings.Where(f => f.Component == ComponentKind.Gpu))
            card.Add(f);

        return card.Build();
    }

    private static bool IsGpuSensor(SensorStats s) =>
        s.HardwareName.Contains("GPU", StringComparison.OrdinalIgnoreCase) ||
        s.HardwareName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
        s.HardwareName.Contains("AMD Radeon", StringComparison.OrdinalIgnoreCase) ||
        s.HardwareName.Contains("Intel Arc", StringComparison.OrdinalIgnoreCase);
}
