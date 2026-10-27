using HardwareInspector.Analysis.Scoring;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Assessment;
using HardwareInspector.Core.Models.Components;
using HardwareInspector.Core.Models.Sensors;
using HardwareInspector.Core.Utils;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Analysis.Analyzers;

/// <summary>
/// Mainboard được đánh giá qua ba thứ: bo mạch là model gì, các khe cắm đang dùng ra sao,
/// và còn nâng cấp được tới đâu.
///
/// BIOS cố ý không nằm trong tiêu chí. BIOS đi kèm bo mạch do hãng cung cấp và cập nhật;
/// những trường nó khai (serial, tên hệ thống, ngày phát hành, cảm biến mở vỏ, Secure Boot)
/// trên bo bán lẻ thường để mặc định hoặc trống ngay từ khi mới mua, nên dùng chúng để
/// chấm điểm chỉ sinh ra cảnh báo sai.
/// </summary>
public sealed class MotherboardAnalyzer : IComponentAnalyzer
{
    public ComponentKind Kind => ComponentKind.Motherboard;

    public bool CanAnalyze(SystemSnapshot snapshot) => true;

    public ComponentAssessment Analyze(SystemSnapshot snapshot)
    {
        var b = snapshot.Baseboard;
        var os = snapshot.OperatingSystem;

        // Máy tự ráp thường để trống tên hệ thống ("To Be Filled By O.E.M."),
        // khi đó tên bo mạch mới là thứ người dùng cần thấy.
        var systemKnown = !FormatHelpers.IsPlaceholder(b.SystemManufacturer) && !FormatHelpers.IsPlaceholder(b.SystemProductName);
        var boardName = $"{b.BoardManufacturer.OrDash()} {b.BoardProduct.OrDash()}";
        var card = new ScoreCard(Kind, systemKnown ? $"{b.SystemManufacturer} {b.SystemProductName}" : boardName);

        card.Metric(S("Bo mạch chủ", "Motherboard"), boardName)
            .MetricIf(!string.IsNullOrWhiteSpace(b.BoardVersion) && !FormatHelpers.IsPlaceholder(b.BoardVersion), S("Phiên bản bo", "Board revision"), b.BoardVersion)
            .MetricIf(systemKnown, S("Hãng máy", "System manufacturer"), b.SystemManufacturer)
            .MetricIf(systemKnown, S("Model máy", "System model"), b.SystemProductName)
            .Metric(S("Serial bo mạch", "Board serial"), FormatHelpers.IsPlaceholder(b.BoardSerialNumber)
                ? S("Không khai báo", "Not declared")
                : FormatHelpers.Mask(b.BoardSerialNumber),
                FormatHelpers.IsPlaceholder(b.BoardSerialNumber)
                    ? S("Bo bán lẻ thường để trống trường này, không phải dấu hiệu bất thường.",
                        "Retail boards often leave this blank; it is not a red flag.")
                    : null)
            .MetricIf(systemKnown, S("Serial máy", "System serial"), FormatHelpers.Mask(b.SystemSerialNumber))
            .Metric(S("Kiểu vỏ máy", "Chassis type"), b.ChassisType.OrDash())
            .Metric(S("Hệ điều hành", "Operating system"), $"{os.Caption} build {os.BuildNumber}")
            .Metric(S("Ngày cài Windows", "Windows install date"), os.InstallDate?.ToString("dd/MM/yyyy") ?? "—")
            .Metric(S("Kênh cấp phép", "License channel"), os.LicenseChannel.OrDash())
            .Metric(S("Kích hoạt", "Activation"), os.IsActivated ? S("Đã kích hoạt", "Activated") : S("Chưa kích hoạt", "Not activated"), null,
                os.IsActivated ? Severity.Info : Severity.Notice)
            .Metric(S("Thời gian bật máy hiện tại", "Current uptime"), os.Uptime.ToHumanDuration());

        // Cụm định danh dùng để tra thông số chính thức. Gom lại một chỗ và cho copy được
        // vì đây là thứ người dùng phải dán vào ô tìm kiếm của hãng.
        var sku = FormatHelpers.IsPlaceholder(b.SystemSku) ? null : b.SystemSku;
        var lookup = systemKnown
            ? string.Join(" ", new[] { b.SystemManufacturer, b.SystemProductName, sku }.Where(x => !string.IsNullOrWhiteSpace(x)))
            : $"{b.BoardManufacturer} {b.BoardProduct}";
        card.Metric(S("Chuỗi tra cứu model", "Model lookup string"), lookup.Trim(),
            S("Dán chuỗi này vào trang hỗ trợ của hãng để lấy thông số nâng cấp chính thức",
              "Paste this into the manufacturer's support site to get the official upgrade specs"));

        card.MetricIf(sku is not null, S("Mã SKU", "SKU"), sku.OrDash());
        card.MetricIf(systemKnown && !FormatHelpers.IsPlaceholder(b.SystemFamily), S("Dòng sản phẩm", "Product family"), b.SystemFamily.OrDash());

        AnalyzeExpansion(card, snapshot);

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

        foreach (var f in snapshot.ReliabilityFindings.Where(f => f.Component == ComponentKind.Motherboard))
            card.Add(f);

        return card.Build();
    }

    /// <summary>
    /// Liệt kê từng khe RAM, khe mở rộng và cổng kết nối mà bo mạch tự khai báo,
    /// rồi tóm lại còn nâng cấp được gì. Chỉ là thông tin, không trừ điểm: bo ít khe
    /// không phải là bo hỏng.
    /// </summary>
    private void AnalyzeExpansion(ScoreCard card, SystemSnapshot snapshot)
    {
        var up = snapshot.Upgrade;
        if (!up.DataAvailable)
        {
            card.Metric(S("Khe cắm & mở rộng", "Slots & expansion"), S("Không đọc được", "Unreadable"),
                S("Bo mạch không khai bảng SMBIOS về khe cắm. Hãy tra số khe theo chuỗi tra cứu model.",
                  "The board does not publish SMBIOS slot tables. Look the slot count up using the model lookup string."));
            return;
        }

        // --- RAM ---
        card.MetricIf(up.MaxMemoryBytes > 0, S("RAM tối đa hỗ trợ", "Maximum supported RAM"), up.MaxMemoryBytes.ToSize(0));
        card.MetricIf(up.MemorySlotsTotal > 0, S("Khe RAM", "Memory slots"),
            S($"{up.MemorySlotsOccupied}/{up.MemorySlotsTotal} đã dùng, {up.MemorySlotsFree} còn trống",
              $"{up.MemorySlotsOccupied}/{up.MemorySlotsTotal} used, {up.MemorySlotsFree} free"));
        foreach (var slot in up.MemorySlots)
        {
            var name = FirstNonEmpty(slot.Designation, slot.BankLabel, "DIMM");
            var value = slot.IsOccupied
                ? string.Join(" ", new[]
                  {
                      slot.CapacityBytes > 0 ? slot.CapacityBytes.ToSize(0) : null,
                      slot.MemoryType,
                      slot.SpeedMtps > 0 ? $"{slot.SpeedMtps} MT/s" : null
                  }.Where(x => !string.IsNullOrWhiteSpace(x)))
                : S("Trống", "Empty");
            card.Metric($"  {S("Khe RAM", "RAM slot")} · {name}", value, slot.IsOccupied && !string.IsNullOrWhiteSpace(slot.PartNumber) ? slot.PartNumber.Trim() : null);
        }
        card.MetricIf(up.MemoryIsSoldered, S("Kiểu RAM", "Memory type"), S("Hàn chết trên bo — không nâng cấp được", "Soldered to the board — not upgradeable"),
            null, Severity.Notice);

        // --- Khe mở rộng: PCIe, M.2... ---
        if (up.ExpansionSlots.Count > 0)
        {
            card.Metric(S("Khe mở rộng", "Expansion slots"),
                S($"{up.ExpansionSlots.Count} khe, {up.ExpansionSlots.Count(x => x.IsAvailable)} còn trống",
                  $"{up.ExpansionSlots.Count} slots, {up.ExpansionSlots.Count(x => x.IsAvailable)} free"));
            foreach (var slot in up.ExpansionSlots)
            {
                var type = SlotTypeText(slot);
                var state = slot.IsAvailable ? S("Trống", "Free")
                    : slot.IsUnavailable ? S("Không dùng được", "Unavailable")
                    : S("Đang dùng", "In use");
                card.Metric($"  {S("Khe", "Slot")} · {FirstNonEmpty(slot.Designation, type)}", $"{type} — {state}",
                    slot.IsUnavailable
                        ? S("Thường do khe này dùng chung làn với khe khác, hoặc CPU đang lắp không hỗ trợ.",
                            "Usually the slot shares lanes with another one, or the installed CPU does not support it.")
                        : null);
            }
        }

        var fastestSlot = up.FastestSsdSlot;
        if (fastestSlot?.TheoreticalMBps is { } ceiling)
            card.Metric(S("Trần tốc độ ổ SSD", "SSD speed ceiling"), S($"~{ceiling:N0} MB/s qua {fastestSlot.TypeText}", $"~{ceiling:N0} MB/s via {fastestSlot.TypeText}"));

        // --- Cổng kết nối ---
        var external = up.Ports.Where(p => p.IsExternal && p.Kind != PortKind.Other).ToList();
        if (external.Count > 0)
            card.Metric(S("Cổng ngoài", "External ports"), GroupPorts(external),
                S("Theo khai báo của bo mạch, có thể thiếu cổng do chip phụ cung cấp.",
                  "As declared by the board; ports driven by add-on chips may be missing."));
        var headers = up.Ports.Where(p => !p.IsExternal && p.Kind != PortKind.Other).ToList();
        if (headers.Count > 0)
            card.Metric(S("Đầu cắm trên bo", "Onboard headers"), GroupPorts(headers));

        // --- Tóm tắt khả năng mở rộng ---
        var options = new List<string>();
        if (up.MemorySlotsFree > 0 && !up.MemoryIsSoldered)
            options.Add(S($"thêm {up.MemorySlotsFree} thanh RAM", $"add {up.MemorySlotsFree} RAM stick(s)"));
        else if (up.MemoryHeadroomBytes > 0 && !up.MemoryIsSoldered)
            options.Add(S($"thay RAM lớn hơn (tối đa {up.MaxMemoryBytes.ToSize(0)})", $"swap to larger RAM (up to {up.MaxMemoryBytes.ToSize(0)})"));
        var freeSsd = up.FreeSsdSlots.Count();
        if (freeSsd > 0) options.Add(S($"lắp thêm {freeSsd} ổ M.2", $"add {freeSsd} M.2 drive(s)"));
        var freePcie = up.ExpansionSlots.Count(x => x.IsAvailable && x.Kind == ExpansionSlotKind.PciExpress);
        if (freePcie > 0) options.Add(S($"cắm thêm {freePcie} card PCIe", $"add {freePcie} PCIe card(s)"));

        card.Metric(S("Khả năng mở rộng", "Expansion headroom"),
            options.Count > 0
                ? S("Còn có thể ", "Can still ") + string.Join(", ", options)
                : S("Đã dùng hết khe theo khai báo của bo mạch", "All declared slots are in use"));
    }

    private static string SlotTypeText(ExpansionSlotState slot)
    {
        var text = string.IsNullOrWhiteSpace(slot.TypeText) ? slot.Kind.ToString() : slot.TypeText;
        if (slot.PcieLanes is { } lanes && !text.Contains($"x{lanes}", StringComparison.OrdinalIgnoreCase))
            text += $" x{lanes}";
        return text;
    }

    private static string GroupPorts(IEnumerable<PortConnectorState> ports) =>
        string.Join(", ", ports.GroupBy(p => p.Kind)
            .OrderBy(g => g.Key)
            .Select(g => g.Count() > 1 ? $"{PortName(g.Key)} ×{g.Count()}" : PortName(g.Key)));

    private static string PortName(PortKind kind) => kind switch
    {
        PortKind.UsbA => "USB-A",
        PortKind.UsbC => "USB-C",
        PortKind.Lan => "LAN",
        PortKind.Hdmi => "HDMI",
        PortKind.DisplayPort => "DisplayPort",
        PortKind.Vga => "VGA",
        PortKind.Dvi => "DVI",
        PortKind.Audio => S("Âm thanh", "Audio"),
        PortKind.Ps2 => "PS/2",
        PortKind.Serial => "COM",
        PortKind.Sata => "SATA",
        PortKind.FanHeader => S("Quạt", "Fan"),
        PortKind.UsbHeader => S("Header USB", "USB header"),
        _ => S("Khác", "Other")
    };

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
}
