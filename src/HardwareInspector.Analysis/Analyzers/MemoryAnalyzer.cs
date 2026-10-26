using HardwareInspector.Analysis.Scoring;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Assessment;
using HardwareInspector.Core.Models.Components;
using HardwareInspector.Core.Utils;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Analysis.Analyzers;

public sealed class MemoryAnalyzer : IComponentAnalyzer
{
    public ComponentKind Kind => ComponentKind.Memory;

    public bool CanAnalyze(SystemSnapshot snapshot) => snapshot.Memory.TotalPhysicalBytes > 0;

    public ComponentAssessment Analyze(SystemSnapshot snapshot)
    {
        var mem = snapshot.Memory;
        var card = new ScoreCard(Kind, $"{mem.TotalPhysicalBytes.ToSize(0)} RAM");

        var installed = mem.Modules.Sum(m => m.CapacityBytes);
        var up = snapshot.Upgrade;

        // Số khe lấy từ SMBIOS khi đọc được, vì đó là nguồn duy nhất đếm cả khe trống.
        var slotsUsed = up.MemorySlotDataAvailable ? up.MemorySlotsOccupied : mem.SlotsUsed;
        var slotsTotal = up.MemorySlotDataAvailable ? up.MemorySlotsTotal : mem.SlotsTotal;
        var slotsFree = Math.Max(0, slotsTotal - slotsUsed);

        // Hai con số này luôn lệch nhau và đó là chuyện bình thường: phần chênh
        // là bộ nhớ mà firmware giữ lại cho đồ hoạ tích hợp và các vùng dành riêng.
        // Hiển thị tách bạch để người dùng không tưởng máy bị thiếu RAM.
        card.MetricIf(installed > 0, S("Dung lượng lắp đặt", "Installed capacity"), installed.ToSize(0),
                S("Cộng dung lượng ghi trên từng thanh", "Sum of the capacity printed on each module"))
            .Metric(S("Windows nhận", "Reported by Windows"), mem.TotalPhysicalBytes.ToSize(1),
                installed > mem.TotalPhysicalBytes
                    ? S($"Chênh {(installed - mem.TotalPhysicalBytes).ToSize(0)} do firmware giữ cho đồ hoạ tích hợp và vùng dành riêng", $"{(installed - mem.TotalPhysicalBytes).ToSize(0)} shortfall: firmware reserves it for integrated graphics and other reserved regions")
                    : null)
            .Metric(S("Khe đã dùng", "Slots used"), $"{slotsUsed}/{slotsTotal}",
                slotsFree > 0 ? S($"Còn {slotsFree} khe trống", $"{slotsFree} slot(s) free") : S("Đã cắm đầy", "All slots populated"))
            .Metric(S("Cấu hình kênh", "Channel configuration"), mem.ChannelConfiguration)
            .Metric(S("ECC", "ECC"), mem.IsEccEnabled ? S("Có", "Yes") : S("Không", "No"));

        RenderSlots(card, mem, up);

        // Thanh RAM khác hãng hoặc khác dung lượng vẫn chạy được nhưng là dấu hiệu máy đã bị nâng cấp chắp vá.
        if (mem.Modules.Count > 1)
        {
            var mixedVendor = mem.Modules.Select(m => m.Manufacturer.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
            var mixedSize = mem.Modules.Select(m => m.CapacityBytes).Distinct().Count() > 1;
            var mixedSpeed = mem.Modules.Select(m => m.SpeedMtps).Where(s => s > 0).Distinct().Count() > 1;

            if (mixedVendor || mixedSize || mixedSpeed)
                card.Add("RAM-MIX-001", Kind, Severity.Notice,
                    S("Các thanh RAM không đồng bộ", "Memory modules are not matched"),
                    S($"Khác hãng: {(mixedVendor ? "có" : "không")}, khác dung lượng: {(mixedSize ? "có" : "không")}, "
                     + $"khác tốc độ: {(mixedSpeed ? "có" : "không")}. ",
                      $"Different vendors: {(mixedVendor ? "yes" : "no")}, different capacities: {(mixedSize ? "yes" : "no")}, "
                     + $"different speeds: {(mixedSpeed ? "yes" : "no")}. ") +
                    S("Máy vẫn chạy nhưng toàn bộ hệ thống bị kéo về tốc độ của thanh chậm nhất, "
                      + "và cấu hình chắp vá dễ gây lỗi ngẫu nhiên hơn.",
                      "The machine still runs, but the whole system drops to the speed of the slowest module, "
                      + "and a mismatched set is more prone to random errors."),
                    S("Nếu định giữ máy lâu, cân nhắc thay cả bộ RAM cùng loại.", "If you plan to keep the machine, consider replacing the whole set with matched modules."),
                    penalty: 8, confidence: 0.8);
        }

        if (mem.SlotsUsed == 1 && mem.SlotsTotal > 1)
            card.Add("RAM-CH-001", Kind, Severity.Notice,
                S("Chỉ có một thanh RAM — máy đang chạy single channel", "Only one memory module — the machine is running single channel"),
                S("Băng thông bộ nhớ giảm khoảng một nửa. Ảnh hưởng rõ nhất lên card đồ hoạ tích hợp.", "Memory bandwidth is roughly halved. Integrated graphics suffer the most."),
                S("Bổ sung một thanh cùng dung lượng để chạy dual channel. Chi phí thấp, hiệu quả cao.", "Add a second module of the same capacity to enable dual channel. Cheap, and the gain is large."),
                penalty: 8, confidence: 0.9);

        var configuredSpeeds = mem.Modules.Where(m => m.ConfiguredSpeedMtps > 0 && m.SpeedMtps > 0).ToList();
        if (configuredSpeeds.Any(m => m.ConfiguredSpeedMtps < m.SpeedMtps * 0.9))
            card.Add("RAM-SPD-001", Kind, Severity.Notice,
                S("RAM đang chạy chậm hơn tốc độ danh định", "Memory is running below its rated speed"),
                S("Cấu hình XMP/EXPO chưa được bật, hoặc bo mạch giới hạn tốc độ bộ nhớ.", "The XMP/EXPO profile is not enabled, or the board caps memory speed."),
                S("Bật XMP/EXPO trong BIOS nếu bo mạch hỗ trợ.", "Enable XMP/EXPO in the BIOS if the board supports it."),
                penalty: 5, confidence: 0.85);

        AnalyzeUpgradePath(card, snapshot);

        foreach (var f in snapshot.ReliabilityFindings.Where(f => f.Code.Contains("WHEA")))
            card.Add(f);

        if (mem.TotalPhysicalBytes < 8L * 1024 * 1024 * 1024)
            card.Add("RAM-CAP-001", Kind, Severity.Warning,
                S($"Chỉ có {mem.TotalPhysicalBytes.ToSize(0)} RAM", $"Only {mem.TotalPhysicalBytes.ToSize(0)} of RAM"),
                S("Dưới 8 GB, Windows 11 và trình duyệt hiện đại sẽ phải dùng file trang liên tục, " +
                  "kéo theo hao mòn ổ SSD và cảm giác giật lag.",
                  "Below 8 GB, Windows 11 and modern browsers page constantly, " +
                  "wearing the SSD and making the machine feel sluggish."),
                S("Nâng cấp RAM là khoản đầu tư đáng giá nhất cho máy này.", "More RAM is the single best investment for this machine."),
                penalty: 20, confidence: 1.0);

        card.Add("RAM-TEST-001", Kind, Severity.Info,
            S("Không thể kiểm tra lỗi bit RAM từ trong Windows", "Bit-level memory errors cannot be tested from inside Windows"),
            S("Phần mềm chạy trong hệ điều hành chỉ đọc được thông tin mô tả, không thể quét toàn bộ vùng nhớ. " +
              "RAM lỗi nhẹ có thể chạy nhiều giờ không biểu hiện.",
              "Software running inside the OS can only read descriptive data, not scan all of memory. " +
              "Slightly faulty RAM can run for hours without symptoms."),
            S("Chạy MemTest86 từ USB ít nhất 4 lượt trước khi quyết định mua máy đắt tiền.", "Run at least 4 passes of MemTest86 from a USB stick before committing to an expensive machine."),
            penalty: 0, confidence: 1.0);

        foreach (var f in snapshot.AuthenticityFindings.Where(f => f.Component == ComponentKind.Memory))
            card.Add(f);

        return card.Build();
    }

    /// <summary>
    /// In danh sách khe RAM đúng một lần.
    ///
    /// Ưu tiên SMBIOS vì nó liệt kê cả khe đang trống, còn WMI chỉ trả khe đã cắm.
    /// Nhưng SMBIOS lại không có tên hãng, nên với khe đã cắm thì ghép thêm dữ liệu
    /// WMI khớp theo nhãn khe. Trước đây hai nguồn được in riêng nên mỗi thanh RAM
    /// xuất hiện hai lần trong bảng thông số.
    /// </summary>
    private static void RenderSlots(ScoreCard card, MemorySubsystemInfo mem, UpgradeCapability up)
    {
        if (!up.MemorySlotDataAvailable)
        {
            foreach (var m in mem.Modules)
                card.Metric(S($"Khe {m.DeviceLocator.OrDash()}", $"Slot {m.DeviceLocator.OrDash()}"),
                    $"{m.CapacityBytes.ToSize(0)} {m.MemoryType} {DescribeSpeed(m.SpeedMtps, m.ConfiguredSpeedMtps)}",
                    $"{m.Manufacturer.OrDash()} · {m.PartNumber.OrDash()}");
            return;
        }

        foreach (var slot in up.MemorySlots)
        {
            if (!slot.IsOccupied)
            {
                card.Metric(S($"Khe {slot.Designation.OrDash()}", $"Slot {slot.Designation.OrDash()}"), S("Trống", "Empty"),
                    S("Có thể cắm thêm thanh mới vào đây", "A new module can go in here"));
                continue;
            }

            var wmi = mem.Modules.FirstOrDefault(m =>
                m.DeviceLocator.Equals(slot.Designation, StringComparison.OrdinalIgnoreCase));

            var speed = DescribeSpeed(slot.SpeedMtps, wmi?.ConfiguredSpeedMtps ?? 0);
            var maker = wmi?.Manufacturer.OrDash() ?? "—";
            var part = string.IsNullOrWhiteSpace(slot.PartNumber)
                ? wmi?.PartNumber.OrDash() ?? "—"
                : slot.PartNumber;

            card.Metric(S($"Khe {slot.Designation.OrDash()}", $"Slot {slot.Designation.OrDash()}"),
                $"{slot.CapacityBytes.ToSize(0)} {slot.MemoryType} {speed}",
                $"{maker} · {part}");
        }
    }

    private static string DescribeSpeed(int rated, int configured) =>
        configured > 0 && configured != rated
            ? S($"{configured} MT/s (danh định {rated})", $"{configured} MT/s (rated {rated})")
            : $"{rated} MT/s";

    /// <summary>
    /// Trả lời câu hỏi thực tế nhất khi cầm một cái máy cũ: nâng RAM được tới đâu,
    /// và có phải tháo bỏ thanh đang có hay không.
    ///
    /// Số liệu lấy từ SMBIOS Type 16 và Type 17. Type 17 là chỗ duy nhất liệt kê
    /// cả khe đang trống — thứ mà WMI không trả về, nên trước đây không thể biết
    /// máy còn khe nào hay không.
    /// </summary>
    private void AnalyzeUpgradePath(ScoreCard card, SystemSnapshot snapshot)
    {
        var up = snapshot.Upgrade;
        if (!up.DataAvailable)
        {
            card.Metric(S("Khả năng nâng cấp", "Upgrade headroom"), S("Không đọc được bảng SMBIOS", "SMBIOS tables could not be read"), null, Severity.Notice);
            return;
        }

        if (up.MemoryIsSoldered)
        {
            card.Metric(S("Khả năng nâng cấp", "Upgrade headroom"), S("RAM hàn thẳng lên bo mạch — không nâng cấp được", "Memory is soldered to the board — it cannot be upgraded"));
            card.Add("RAM-UP-001", Kind, Severity.Notice,
                S("Bộ nhớ hàn chết, không thể nâng cấp", "Soldered memory, no upgrade possible"),
                S("Mọi vị trí bộ nhớ đều mang form factor \"chip hàn trên bo\" và không còn khe trống. " +
                  "Dung lượng hiện tại là dung lượng vĩnh viễn của máy này.",
                  "Every memory location reports the \"soldered chip\" form factor and there are no free slots. " +
                  "The current capacity is permanent for this machine."),
                S("Nếu cần nhiều RAM hơn, phải đổi máy chứ không nâng cấp được.", "If you need more RAM, you have to change machine — there is no upgrade path."),
                penalty: 0, confidence: 0.85, source: EvidenceSource.Smbios);
            return;
        }

        // Không đọc được khe từ SMBIOS thì lùi về dữ liệu WMI thay vì im lặng
        // hoặc kết luận bừa. Đây là chỗ trước đây báo nhầm là RAM hàn.
        if (!up.MemorySlotDataAvailable)
        {
            var mem = snapshot.Memory;
            card.Add("RAM-UP-006", Kind, Severity.Info,
                S("Không đọc được chi tiết khe RAM từ firmware", "Memory slot detail could not be read from firmware"),
                S($"Windows báo {mem.SlotsUsed}/{mem.SlotsTotal} khe đang dùng, nhưng bảng SMBIOS " +
                  "không trả về danh sách khe nên không xác định được trần dung lượng tối đa.",
                  $"Windows reports {mem.SlotsUsed}/{mem.SlotsTotal} slots in use, but the SMBIOS table " +
                  "returned no slot list, so the maximum capacity cannot be determined."),
                S("Tra thông số nâng cấp theo đúng model máy trên trang hãng, " +
                  "hoặc dùng CPU-Z tab SPD để xem trực tiếp từng khe.",
                  "Look up the upgrade specs for this exact model on the manufacturer's site, " +
                  "or use CPU-Z's SPD tab to inspect each slot."),
                penalty: 0, confidence: 1.0, source: EvidenceSource.Smbios);
            return;
        }

        if (up.MaxMemoryBytes > 0)
            card.Metric(S("RAM tối đa bo mạch hỗ trợ", "Maximum RAM the board supports"), up.MaxMemoryBytes.ToSize(0),
                S($"Theo khai báo của firmware, chia đều {up.MemorySlotsTotal} khe " +
                  $"thì mỗi khe nhận tối đa {up.MaxPerSlotBytes.ToSize(0)}",
                  $"As declared by the firmware; split across {up.MemorySlotsTotal} slots " +
                  $"that is up to {up.MaxPerSlotBytes.ToSize(0)} per slot"));

        if (up.MemoryHeadroomBytes > 0)
            card.Metric(S("Còn nâng thêm được", "Remaining headroom"), up.MemoryHeadroomBytes.ToSize(0));

        // --- Kết luận và lời khuyên ---
        if (up.IsMemoryMaxedOut)
        {
            card.Add("RAM-UP-002", Kind, Severity.Info,
                S($"RAM đã đạt trần {up.MaxMemoryBytes.ToSize(0)} mà bo mạch hỗ trợ", $"Memory is at the board's {up.MaxMemoryBytes.ToSize(0)} maximum"),
                S("Không còn đường nâng cấp bộ nhớ trên máy này.", "There is no memory upgrade path left on this machine."),
                penalty: 0, confidence: 0.75, source: EvidenceSource.Smbios);
        }
        else if (up.CanAddMemoryWithoutRemoving)
        {
            var suggestion = Math.Min(up.MaxPerSlotBytes, up.MemoryHeadroomBytes);
            card.Add("RAM-UP-003", Kind, Severity.Info,
                S($"Còn {up.MemorySlotsFree} khe trống — nâng cấp được mà không phải bỏ thanh cũ",
                  $"{up.MemorySlotsFree} free slot(s) — you can upgrade without removing the current modules"),
                S($"Cắm thêm tối đa {suggestion.ToSize(0)} mỗi khe, loại {up.InstalledMemoryType} " +
                  $"{up.InstalledFormFactor}, tốc độ {up.InstalledMemorySpeedMtps} MT/s để đồng bộ với thanh đang có.\n\n",
                  $"Add up to {suggestion.ToSize(0)} per slot, {up.InstalledMemoryType} " +
                  $"{up.InstalledFormFactor} at {up.InstalledMemorySpeedMtps} MT/s to match the installed modules.\n\n") +
                (up.MemorySlotsOccupied == 1
                    ? S("Thêm một thanh cùng dung lượng sẽ bật chế độ hai kênh, " +
                        "đây là khoản nâng cấp rẻ nhất mà cho khác biệt rõ nhất trên máy này.",
                        "Adding a module of the same capacity enables dual channel, " +
                        "the cheapest upgrade with the most noticeable gain on this machine.")
                    : S("Ưu tiên mua thanh cùng thông số với thanh đang lắp để tránh lệch tốc độ.",
                        "Prefer modules with the same specs as the installed ones to avoid a speed mismatch.")),
                S("Mang thanh RAM cũ ra cửa hàng đối chiếu, hoặc chụp lại mã part number ở trên.",
                  "Take the old module to the shop to compare, or note the part number above."),
                penalty: 0, confidence: 0.8, source: EvidenceSource.Smbios);
        }
        else if (up.MemoryHeadroomBytes > 0)
        {
            card.Add("RAM-UP-004", Kind, Severity.Info,
                S("Hết khe trống nhưng vẫn còn dư trần dung lượng", "No free slots, but capacity headroom remains"),
                S($"Muốn lên tới {up.MaxMemoryBytes.ToSize(0)} thì phải tháo thanh hiện tại " +
                  $"và thay bằng thanh dung lượng lớn hơn (tối đa {up.MaxPerSlotBytes.ToSize(0)} mỗi khe). " +
                  "Chi phí cao hơn vì thanh cũ thành thừa.",
                  $"Reaching {up.MaxMemoryBytes.ToSize(0)} means removing the current modules " +
                  $"and fitting larger ones (up to {up.MaxPerSlotBytes.ToSize(0)} per slot). " +
                  "It costs more because the old modules become spare."),
                S("Cân nhắc bán lại thanh cũ để bù chi phí.", "Consider selling the old modules to offset the cost."),
                penalty: 0, confidence: 0.75, source: EvidenceSource.Smbios);
        }

        card.Add("RAM-UP-005", Kind, Severity.Info,
            S("Về độ chính xác của trần dung lượng", "About the accuracy of the capacity ceiling"),
            S("Con số trần ở trên là thứ firmware tự khai báo. Nhiều hãng laptop khai thấp hơn " +
              "khả năng thật của chipset, nên máy có thể nhận nhiều hơn mức ghi ở đây. " +
              "Ngược lại, một số bo mạch khai theo chipset mà bỏ qua giới hạn của CPU đang lắp.",
              "The ceiling above is what the firmware declares. Many laptop makers declare less than " +
              "the chipset can really handle, so the machine may accept more than shown here. " +
              "Conversely, some boards declare the chipset limit and ignore the installed CPU's limit."),
            S("Trước khi mua thanh RAM đắt tiền, tra thêm thông số chính thức theo đúng model máy.",
              "Before buying expensive RAM, check the official specs for this exact model."),
            penalty: 0, confidence: 1.0, source: EvidenceSource.Smbios);
    }
}
