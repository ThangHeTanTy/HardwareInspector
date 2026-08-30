using HardwareInspector.Analysis.Scoring;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Assessment;
using HardwareInspector.Core.Models.Sensors;
using HardwareInspector.Core.Utils;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Analysis.Analyzers;

public sealed class MotherboardAnalyzer : IComponentAnalyzer
{
    public ComponentKind Kind => ComponentKind.Motherboard;

    public bool CanAnalyze(SystemSnapshot snapshot) => true;

    public ComponentAssessment Analyze(SystemSnapshot snapshot)
    {
        var b = snapshot.Baseboard;
        var os = snapshot.OperatingSystem;
        var card = new ScoreCard(Kind, $"{b.SystemManufacturer.OrDash()} {b.SystemProductName.OrDash()}");

        card.Metric("Nhà sản xuất", b.SystemManufacturer.OrDash())
            .Metric("Model", b.SystemProductName.OrDash())
            .Metric("Serial máy", FormatHelpers.Mask(b.SystemSerialNumber))
            .Metric("Bo mạch chủ", $"{b.BoardManufacturer.OrDash()} {b.BoardProduct.OrDash()}")
            .Metric("Serial bo mạch", FormatHelpers.Mask(b.BoardSerialNumber))
            .Metric("Kiểu vỏ máy", b.ChassisType.OrDash())
            .Metric("Trạng thái vỏ máy", b.ChassisSecurityStatus)
            .Metric("Hệ điều hành", $"{os.Caption} build {os.BuildNumber}")
            .Metric("Ngày cài Windows", os.InstallDate?.ToString("dd/MM/yyyy") ?? "—")
            .Metric("Kênh cấp phép", os.LicenseChannel)
            .Metric("Kích hoạt", os.IsActivated ? "Đã kích hoạt" : "Chưa kích hoạt", null,
                os.IsActivated ? Severity.Info : Severity.Notice)
            .Metric("Thời gian bật máy hiện tại", os.Uptime.ToHumanDuration());

        // Cụm định danh dùng để tra thông số chính thức. Gom lại một chỗ và cho copy được
        // vì đây là thứ người dùng phải dán vào ô tìm kiếm của hãng.
        var sku = FormatHelpers.IsPlaceholder(b.SystemSku) ? null : b.SystemSku;
        card.Metric("Chuỗi tra cứu model",
            string.Join(" ", new[] { b.SystemManufacturer, b.SystemProductName, sku }
                .Where(x => !string.IsNullOrWhiteSpace(x))).Trim(),
            "Dán chuỗi này vào trang hỗ trợ của hãng để lấy thông số nâng cấp chính thức");

        card.MetricIf(sku is not null, "Mã SKU", sku.OrDash());
        card.MetricIf(!FormatHelpers.IsPlaceholder(b.SystemFamily), "Dòng sản phẩm", b.SystemFamily.OrDash());

        var up = snapshot.Upgrade;
        if (up.DataAvailable)
        {
            card.MetricIf(up.MaxMemoryBytes > 0, "RAM tối đa hỗ trợ", up.MaxMemoryBytes.ToSize(0));
            card.MetricIf(up.MemorySlotsTotal > 0, "Khe RAM",
                $"{up.MemorySlotsOccupied}/{up.MemorySlotsTotal} đã dùng");
            card.MetricIf(up.ExpansionSlots.Count > 0, "Khe mở rộng",
                $"{up.ExpansionSlots.Count} khe, {up.ExpansionSlots.Count(x => x.IsAvailable)} còn trống");

            // Phải tách ra biến rồi kiểm tra bằng if thường.
            // MetricIf là method bình thường nên mọi đối số đều được tính trước khi gọi:
            // điều kiện không ngăn được chuỗi nội suy chạy, và máy không có khe M.2 nào
            // sẽ khiến biểu thức bên trong ném NullReferenceException.
            var fastestSlot = up.FastestSsdSlot;
            if (fastestSlot?.TheoreticalMBps is { } ceiling)
                card.Metric("Trần tốc độ ổ SSD", $"~{ceiling:N0} MB/s qua {fastestSlot.TypeText}");
        }

        var fans = snapshot.SensorSnapshot.Where(s => s.Kind == SensorKind.Fan).ToList();
        foreach (var fan in fans.Take(4))
            card.Metric($"Quạt · {fan.SensorName}", $"{fan.Min:0} – {fan.Max:0} RPM (TB {fan.Average:0})");

        if (fans.Count > 0 && fans.All(f => f.Max < 50))
            card.Add("MB-FAN-001", Kind, Severity.Warning,
                "Không cảm biến quạt nào ghi nhận vòng quay",
                "Quạt có thể đã hỏng, dây bị tuột, hoặc máy đang ở chế độ quạt dừng khi tải thấp.",
                "Chạy bài kiểm tra tải CPU và lắng nghe tiếng quạt.",
                penalty: 12, confidence: 0.5, source: EvidenceSource.Sensor);

        if (os.InstallDate is { } installed)
        {
            var daysSince = (DateTime.Now - installed).TotalDays;
            if (daysSince < 7)
                card.Add("MB-OS-001", Kind, Severity.Notice,
                    $"Windows vừa được cài lại cách đây {daysSince:0} ngày",
                    "Cài lại sạch trước khi bán là chuyện bình thường, nhưng nó cũng xoá sạch " +
                    "nhật ký sự cố và lịch sử driver — những thứ giúp bạn biết máy từng gặp vấn đề gì. " +
                    "Hãy dựa nhiều hơn vào số liệu phần cứng (S.M.A.R.T., chu kỳ pin) vốn không bị xoá.",
                    "Đừng vội tin vào Event Log sạch sẽ trên một máy vừa cài lại.",
                    penalty: 0, confidence: 0.9);
        }

        if (b.ChassisSecurityStatus.Contains("đã bị mở", StringComparison.OrdinalIgnoreCase))
            card.Add("MB-CHASSIS-001", Kind, Severity.Notice,
                "Cảm biến vỏ máy ghi nhận đã từng mở",
                penalty: 5, confidence: 0.7, source: EvidenceSource.Smbios);

        foreach (var f in snapshot.ReliabilityFindings.Where(f => f.Component == ComponentKind.Motherboard))
            card.Add(f);

        return card.Build();
    }
}
