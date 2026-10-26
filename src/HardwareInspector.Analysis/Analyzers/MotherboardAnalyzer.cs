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

        card.Metric(S("Nhà sản xuất", "Manufacturer"), b.SystemManufacturer.OrDash())
            .Metric("Model", b.SystemProductName.OrDash())
            .Metric(S("Serial máy", "System serial"), FormatHelpers.Mask(b.SystemSerialNumber))
            .Metric(S("Bo mạch chủ", "Motherboard"), $"{b.BoardManufacturer.OrDash()} {b.BoardProduct.OrDash()}")
            .Metric(S("Serial bo mạch", "Board serial"), FormatHelpers.Mask(b.BoardSerialNumber))
            .Metric(S("Kiểu vỏ máy", "Chassis type"), b.ChassisType.OrDash())
            .Metric(S("Trạng thái vỏ máy", "Chassis intrusion status"), b.ChassisSecurityStatus)
            .Metric(S("Hệ điều hành", "Operating system"), $"{os.Caption} build {os.BuildNumber}")
            .Metric(S("Ngày cài Windows", "Windows install date"), os.InstallDate?.ToString("dd/MM/yyyy") ?? "—")
            .Metric(S("Kênh cấp phép", "License channel"), os.LicenseChannel.OrDash())
            .Metric(S("Kích hoạt", "Activation"), os.IsActivated ? S("Đã kích hoạt", "Activated") : S("Chưa kích hoạt", "Not activated"), null,
                os.IsActivated ? Severity.Info : Severity.Notice)
            .Metric(S("Thời gian bật máy hiện tại", "Current uptime"), os.Uptime.ToHumanDuration());

        // Cụm định danh dùng để tra thông số chính thức. Gom lại một chỗ và cho copy được
        // vì đây là thứ người dùng phải dán vào ô tìm kiếm của hãng.
        var sku = FormatHelpers.IsPlaceholder(b.SystemSku) ? null : b.SystemSku;
        card.Metric(S("Chuỗi tra cứu model", "Model lookup string"),
            string.Join(" ", new[] { b.SystemManufacturer, b.SystemProductName, sku }
                .Where(x => !string.IsNullOrWhiteSpace(x))).Trim(),
            S("Dán chuỗi này vào trang hỗ trợ của hãng để lấy thông số nâng cấp chính thức",
              "Paste this into the manufacturer's support site to get the official upgrade specs"));

        card.MetricIf(sku is not null, S("Mã SKU", "SKU"), sku.OrDash());
        card.MetricIf(!FormatHelpers.IsPlaceholder(b.SystemFamily), S("Dòng sản phẩm", "Product family"), b.SystemFamily.OrDash());

        var up = snapshot.Upgrade;
        if (up.DataAvailable)
        {
            card.MetricIf(up.MaxMemoryBytes > 0, S("RAM tối đa hỗ trợ", "Maximum supported RAM"), up.MaxMemoryBytes.ToSize(0));
            card.MetricIf(up.MemorySlotsTotal > 0, S("Khe RAM", "Memory slots"),
                S($"{up.MemorySlotsOccupied}/{up.MemorySlotsTotal} đã dùng", $"{up.MemorySlotsOccupied}/{up.MemorySlotsTotal} used"));
            card.MetricIf(up.ExpansionSlots.Count > 0, S("Khe mở rộng", "Expansion slots"),
                S($"{up.ExpansionSlots.Count} khe, {up.ExpansionSlots.Count(x => x.IsAvailable)} còn trống",
                  $"{up.ExpansionSlots.Count} slots, {up.ExpansionSlots.Count(x => x.IsAvailable)} free"));

            // Phải tách ra biến rồi kiểm tra bằng if thường.
            // MetricIf là method bình thường nên mọi đối số đều được tính trước khi gọi:
            // điều kiện không ngăn được chuỗi nội suy chạy, và máy không có khe M.2 nào
            // sẽ khiến biểu thức bên trong ném NullReferenceException.
            var fastestSlot = up.FastestSsdSlot;
            if (fastestSlot?.TheoreticalMBps is { } ceiling)
                card.Metric(S("Trần tốc độ ổ SSD", "SSD speed ceiling"), S($"~{ceiling:N0} MB/s qua {fastestSlot.TypeText}", $"~{ceiling:N0} MB/s via {fastestSlot.TypeText}"));
        }

        var fans = snapshot.SensorSnapshot.Where(s => s.Kind == SensorKind.Fan).ToList();
        foreach (var fan in fans.Take(4))
            card.Metric(S($"Quạt · {fan.SensorName}", $"Fan · {fan.SensorName}"), S($"{fan.Min:0} – {fan.Max:0} RPM (TB {fan.Average:0})", $"{fan.Min:0} – {fan.Max:0} RPM (avg {fan.Average:0})"));

        if (fans.Count > 0 && fans.All(f => f.Max < 50))
            card.Add("MB-FAN-001", Kind, Severity.Warning,
                S("Không cảm biến quạt nào ghi nhận vòng quay", "No fan sensor reported any rotation"),
                S("Quạt có thể đã hỏng, dây bị tuột, hoặc máy đang ở chế độ quạt dừng khi tải thấp.",
                  "A fan may be dead or unplugged, or the machine stops its fans at low load."),
                S("Chạy bài kiểm tra tải CPU và lắng nghe tiếng quạt.", "Run the CPU stress test and listen for the fans."),
                penalty: 12, confidence: 0.5, source: EvidenceSource.Sensor);

        if (os.InstallDate is { } installed)
        {
            var daysSince = (DateTime.Now - installed).TotalDays;
            if (daysSince < 7)
                card.Add("MB-OS-001", Kind, Severity.Notice,
                    S($"Windows vừa được cài lại cách đây {daysSince:0} ngày", $"Windows was reinstalled {daysSince:0} days ago"),
                    S("Cài lại sạch trước khi bán là chuyện bình thường, nhưng nó cũng xoá sạch " +
                      "nhật ký sự cố và lịch sử driver — những thứ giúp bạn biết máy từng gặp vấn đề gì. " +
                      "Hãy dựa nhiều hơn vào số liệu phần cứng (S.M.A.R.T., chu kỳ pin) vốn không bị xoá.",
                      "A clean reinstall before selling is normal, but it also wipes the " +
                      "crash logs and driver history that would tell you what problems the machine had. " +
                      "Rely more on hardware counters (S.M.A.R.T., battery cycles), which cannot be wiped."),
                    S("Đừng vội tin vào Event Log sạch sẽ trên một máy vừa cài lại.", "Do not read much into a clean Event Log on a freshly reinstalled machine."),
                    penalty: 0, confidence: 0.9);
        }

        if (b.ChassisSecurityCode == 5)
            card.Add("MB-CHASSIS-001", Kind, Severity.Notice,
                S("Cảm biến vỏ máy ghi nhận đã từng mở", "The chassis intrusion sensor recorded an opening"),
                penalty: 5, confidence: 0.7, source: EvidenceSource.Smbios);

        foreach (var f in snapshot.ReliabilityFindings.Where(f => f.Component == ComponentKind.Motherboard))
            card.Add(f);

        return card.Build();
    }
}
