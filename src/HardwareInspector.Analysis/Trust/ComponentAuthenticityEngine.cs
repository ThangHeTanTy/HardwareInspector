using HardwareInspector.Core.Cpu;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Components;
using HardwareInspector.Core.Utils;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Analysis.Trust;

/// <summary>
/// Phát hiện linh kiện bị khai man.
///
/// Chiêu phổ biến nhất ở thị trường máy cũ là làm cho một con CPU đời thấp
/// hiện ra như đời cao bằng cách sửa tên hiển thị. Tên thì sửa được, nhưng Family/Model
/// trong CPUID leaf 1 nằm trong chính silicon.
///
/// Cách chống lại: đối chiếu thế hệ mà cái tên tự nhận với thế hệ thật suy ra từ
/// Family/Model, và đối chiếu tên trong registry với chuỗi CPUID. Một con i3 thế hệ 5
/// báo Family 6 Model 3Dh; dù chuỗi tên có ghi i7-9700K thì Model vẫn là 3Dh.
/// Không dùng tên CPU trong BIOS: bo mạch bán lẻ hay ghi chuỗi chung chung ở đó,
/// dễ báo nhầm.
/// </summary>
public sealed class ComponentAuthenticityEngine
{
    public IEnumerable<Finding> Inspect(SystemSnapshot snapshot)
    {
        foreach (var f in InspectProcessor(snapshot)) yield return f;
        foreach (var f in InspectMemory(snapshot)) yield return f;
        foreach (var f in InspectStorage(snapshot)) yield return f;
        foreach (var f in InspectGraphics(snapshot)) yield return f;
    }

    // ==================================================================
    private IEnumerable<Finding> InspectProcessor(SystemSnapshot snapshot)
    {
        if (snapshot.PrimaryCpu is not { } cpu) yield break;
        var sig = cpu.CpuId;

        if (!sig.IsValid)
        {
            yield return new Finding
            {
                Code = "AUTH-CPU-000",
                Component = ComponentKind.Cpu,
                Severity = Severity.Notice,
                Title = S("Không đọc được chữ ký CPUID", "Could not read the CPUID signature"),
                Detail = S("Thiếu chữ ký này thì không thể kiểm chứng CPU có đúng như tên nó khai hay không. " +
                           "Thường gặp trên nền tảng ARM hoặc khi chạy trong máy ảo có chặn CPUID.",
                           "Without this signature there is no way to verify the CPU is what its name claims. " +
                           "Common on ARM platforms or inside VMs that block CPUID."),
                Source = EvidenceSource.Wmi,
                Confidence = 1.0
            };
            yield break;
        }

        // --- Đối chiếu 1: thế hệ tự nhận với thế hệ thật ---
        if (cpu.ClaimedGeneration is { } claimed && cpu.ActualGeneration is { } actual && claimed != actual)
        {
            var gap = Math.Abs(claimed - actual);
            yield return new Finding
            {
                Code = "AUTH-CPU-001",
                Component = ComponentKind.Cpu,
                Severity = gap >= 2 ? Severity.Critical : Severity.Warning,
                Title = S($"CPU tự nhận thế hệ {claimed} nhưng silicon là thế hệ {actual}", $"CPU claims generation {claimed} but the silicon is generation {actual}"),
                Detail = S(
                    $"Tên đang hiển thị: \"{cpu.Name}\" — tức là {cpu.ClaimedModelNumber ?? "không rõ model"}, thế hệ {claimed}.\n" +
                    $"Chữ ký CPUID đọc trực tiếp từ chip: {sig.SignatureText}, tương ứng vi kiến trúc " +
                    $"{cpu.ActualMicroarchitecture ?? "không rõ"} — thế hệ {actual}.\n\n" +
                    "Family và Model trong CPUID nằm trong chính silicon nên không sửa được bằng phần mềm. " +
                    "Khi cái tên và chữ ký mâu thuẫn, cái tên là thứ đã bị thay đổi.",
                    $"Displayed name: \"{cpu.Name}\" — that is {cpu.ClaimedModelNumber ?? "unknown model"}, generation {claimed}.\n" +
                    $"CPUID signature read directly from the chip: {sig.SignatureText}, matching the " +
                    $"{cpu.ActualMicroarchitecture ?? "unknown"} microarchitecture — generation {actual}.\n\n" +
                    "Family and Model in CPUID live in the silicon and cannot be changed by software. " +
                    "When the name and the signature disagree, it is the name that was changed."),
                Recommendation = S(
                    "Đối chiếu bằng công cụ độc lập: CPU-Z hiển thị cả tên lẫn Family/Model/Stepping ở cùng một chỗ. " +
                    "Nếu xác nhận lệch thế hệ, đây là hàng khai man — dừng giao dịch.",
                    "Cross-check with an independent tool: CPU-Z shows the name and Family/Model/Stepping side by side. " +
                    "If the generation mismatch is confirmed, the part is misrepresented — stop the deal."),
                Source = EvidenceSource.Wmi,
                ScorePenalty = gap >= 2 ? 55 : 30,
                Confidence = gap >= 2 ? 0.9 : 0.65,
                Evidence = new Dictionary<string, string>
                {
                    ["DisplayedName"] = cpu.Name,
                    ["CpuIdBrandString"] = sig.BrandString,
                    ["CpuIdSignature"] = sig.SignatureText,
                    ["Microarchitecture"] = cpu.ActualMicroarchitecture ?? "—",
                    ["ClaimedGeneration"] = claimed.ToString(),
                    ["ActualGeneration"] = actual.ToString()
                }
            };
        }

        var brand = sig.BrandString;

        // --- Đối chiếu 2: tên trong registry với chuỗi CPUID ---
        if (!string.IsNullOrWhiteSpace(brand) &&
            !string.IsNullOrWhiteSpace(cpu.RegistryProcessorName) &&
            !CpuNameParser.NamesMatch(brand, cpu.RegistryProcessorName!))
        {
            yield return new Finding
            {
                Code = "AUTH-CPU-003",
                Component = ComponentKind.Cpu,
                Severity = Severity.Warning,
                Title = S("Tên CPU trong registry khác chuỗi CPUID hiện tại", "The CPU name in the registry differs from the current CPUID string"),
                Detail = $"Registry: \"{cpu.RegistryProcessorName}\"\nCPUID: \"{brand}\"\n\n" + S(
                    "Windows ghi giá trị registry một lần lúc khởi động. Hai chuỗi lệch nhau " +
                    "hoặc do CPU vừa được thay mà chưa khởi động lại, hoặc do registry đã bị sửa tay " +
                    "để đánh lừa các công cụ đọc từ đó.",
                    "Windows writes the registry value once at boot. A mismatch means " +
                    "either the CPU was just swapped without a reboot, or the registry was edited by hand " +
                    "to fool tools that read from it."),
                Recommendation = S("Khởi động lại máy rồi chạy lại. Nếu vẫn lệch, giá trị registry đã bị can thiệp.",
                                   "Restart the machine and run again. If it still differs, the registry value was tampered with."),
                Source = EvidenceSource.Registry,
                ScorePenalty = 25,
                Confidence = 0.7
            };
        }

        // --- Đối chiếu 3: số luồng thật với số luồng của model được khai ---
        if (sig.LogicalProcessorsFromCpuId > 0 && cpu.LogicalCores > 0 &&
            Math.Abs(sig.LogicalProcessorsFromCpuId - cpu.LogicalCores) > cpu.LogicalCores * 0.5)
        {
            yield return new Finding
            {
                Code = "AUTH-CPU-004",
                Component = ComponentKind.Cpu,
                Severity = Severity.Notice,
                Title = S("Số luồng theo CPUID lệch nhiều so với số Windows báo", "CPUID thread count differs a lot from what Windows reports"),
                Detail = S($"CPUID leaf 1: {sig.LogicalProcessorsFromCpuId} luồng. Windows: {cpu.LogicalCores} luồng. " +
                           "Chênh lệch nhỏ là bình thường vì leaf 1 báo số luồng tối đa của gói chứ không phải số đang bật, " +
                           "nhưng chênh lệch lớn cần xem lại.",
                           $"CPUID leaf 1: {sig.LogicalProcessorsFromCpuId} threads. Windows: {cpu.LogicalCores} threads. " +
                           "A small difference is normal because leaf 1 reports the package maximum rather than what is enabled, " +
                           "but a large one deserves a look."),
                Recommendation = S("Kiểm tra xem có nhân nào bị tắt trong BIOS không.", "Check whether any cores are disabled in the BIOS."),
                Source = EvidenceSource.Wmi,
                ScorePenalty = 6,
                Confidence = 0.4
            };
        }

        // --- Đối chiếu 4: máy ảo ---
        if (sig.IsHypervisorPresent)
        {
            var isVm = !string.IsNullOrWhiteSpace(sig.HypervisorVendor) &&
                       !sig.HypervisorVendor.Contains("Microsoft Hv", StringComparison.OrdinalIgnoreCase);

            yield return new Finding
            {
                Code = "AUTH-CPU-005",
                Component = ComponentKind.Cpu,
                Severity = isVm ? Severity.Warning : Severity.Info,
                Title = isVm
                    ? S("Hệ thống đang chạy trong máy ảo", "The system is running inside a virtual machine")
                    : S("Có lớp ảo hoá của Windows đang hoạt động (Hyper-V/VBS)", "A Windows virtualization layer is active (Hyper-V/VBS)"),
                Detail = $"Hypervisor vendor: \"{sig.HypervisorVendor}\". " +
                         (isVm
                             ? S("Trong máy ảo, gần như toàn bộ thông tin phần cứng đều do lớp ảo hoá dựng ra. " +
                                 "Kết quả kiểm tra không phản ánh phần cứng thật.",
                                 "Inside a VM almost all hardware information is synthesized by the hypervisor. " +
                                 "The results do not reflect real hardware.")
                             : S("Đây là hành vi bình thường khi bật bảo mật dựa trên ảo hoá.",
                                 "This is normal when virtualization-based security is enabled.")),
                Recommendation = isVm ? S("Chạy ứng dụng trực tiếp trên máy thật để có kết quả có ý nghĩa.", "Run the app directly on the real machine for meaningful results.") : null,
                Source = EvidenceSource.Wmi,
                ScorePenalty = isVm ? 20 : 0,
                Confidence = 0.9
            };
        }

        // --- Đối chiếu 5: xung nhịp tối đa với thông số của model ---
        if (sig.MaxFrequencyMhz > 0 && cpu.MaxClockMhz > 0)
        {
            var ratio = cpu.MaxClockMhz / (double)sig.MaxFrequencyMhz;
            if (ratio is > 1.35 or < 0.65)
                yield return new Finding
                {
                    Code = "AUTH-CPU-006",
                    Component = ComponentKind.Cpu,
                    Severity = Severity.Notice,
                    Title = S("Xung nhịp tối đa Windows báo lệch nhiều so với CPUID", "The max clock Windows reports differs a lot from CPUID"),
                    Detail = S($"Windows (từ SMBIOS): {cpu.MaxClockMhz} MHz. CPUID leaf 0x16: {sig.MaxFrequencyMhz} MHz. " +
                               "Giá trị SMBIOS do BIOS ghi và hay bị điền sai; giá trị CPUID đến từ chip.",
                               $"Windows (from SMBIOS): {cpu.MaxClockMhz} MHz. CPUID leaf 0x16: {sig.MaxFrequencyMhz} MHz. " +
                               "The SMBIOS value is written by the BIOS and often wrong; the CPUID value comes from the chip."),
                    Source = EvidenceSource.Smbios,
                    ScorePenalty = 5,
                    Confidence = 0.5
                };
        }
    }

    // ==================================================================
    private IEnumerable<Finding> InspectMemory(SystemSnapshot snapshot)
    {
        var mem = snapshot.Memory;
        if (mem.Modules.Count == 0) yield break;

        // Tổng dung lượng các thanh phải khớp tổng bộ nhớ hệ thống báo về.
        var declared = mem.Modules.Sum(m => m.CapacityBytes);
        if (declared > 0 && mem.TotalPhysicalBytes > 0)
        {
            var ratio = declared / (double)mem.TotalPhysicalBytes;
            if (ratio is > 1.15 or < 0.85)
                yield return new Finding
                {
                    Code = "AUTH-RAM-001",
                    Component = ComponentKind.Memory,
                    Severity = Severity.Warning,
                    Title = S("Tổng dung lượng các thanh RAM không khớp bộ nhớ hệ thống", "The modules' total capacity does not match system memory"),
                    Detail = S($"Cộng dung lượng từng thanh trong SMBIOS: {declared / 1024 / 1024 / 1024.0:0.#} GB. " +
                               $"Bộ nhớ hệ thống thực nhận: {mem.TotalPhysicalBytes / 1024 / 1024 / 1024.0:0.#} GB.\n\n" +
                               "Thông tin từng thanh nằm trong SMBIOS Type 17 do BIOS cung cấp nên sửa được; " +
                               "tổng bộ nhớ hệ thống thì đến từ bộ điều khiển bộ nhớ. Lệch nhau nghĩa là " +
                               "bảng mô tả RAM đã bị ghi sai, hoặc có thanh RAM lỗi không được nhận đủ.",
                               $"Sum of module capacities in SMBIOS: {declared / 1024 / 1024 / 1024.0:0.#} GB. " +
                               $"System memory actually recognised: {mem.TotalPhysicalBytes / 1024 / 1024 / 1024.0:0.#} GB.\n\n" +
                               "Per-module data lives in SMBIOS Type 17, supplied by the BIOS and editable; " +
                               "total system memory comes from the memory controller. A mismatch means " +
                               "the memory table was written incorrectly, or a faulty module is not fully recognised."),
                    Recommendation = S("Kiểm tra lại bằng Task Manager và CPU-Z tab SPD. " +
                                       "Nếu thiếu dung lượng thật thì có thanh RAM đang lỗi.",
                                       "Check again with Task Manager and CPU-Z's SPD tab. " +
                                       "If real capacity is missing, a module is faulty."),
                    Source = EvidenceSource.Smbios,
                    ScorePenalty = 25,
                    Confidence = 0.75
                };
        }

        // Thanh RAM không có mã hãng và part number là dấu hiệu hàng không rõ nguồn gốc,
        // hoặc SMBIOS đã bị ghi lại.
        var anonymous = mem.Modules.Count(m =>
            FormatHelpers.IsPlaceholder(m.Manufacturer) &&
            FormatHelpers.IsPlaceholder(m.PartNumber));

        if (anonymous > 0)
            yield return new Finding
            {
                Code = "AUTH-RAM-002",
                Component = ComponentKind.Memory,
                Severity = Severity.Notice,
                Title = S($"{anonymous}/{mem.Modules.Count} thanh RAM không có thông tin hãng và mã sản phẩm",
                          $"{anonymous}/{mem.Modules.Count} memory module(s) have no vendor or part number"),
                Detail = S("Thanh RAM chính hãng luôn ghi mã nhà sản xuất trong chip SPD. " +
                           "Trống hoàn toàn thường gặp ở RAM tháo máy không rõ nguồn hoặc RAM đã bị ghi lại SPD.",
                           "Genuine modules always store the manufacturer code in the SPD chip. " +
                           "A blank SPD is typical of pulled modules of unknown origin or modules with rewritten SPD."),
                Recommendation = S("Mở CPU-Z tab SPD để xem trực tiếp nội dung chip SPD của từng khe.",
                                   "Open CPU-Z's SPD tab to read each slot's SPD chip directly."),
                Source = EvidenceSource.Smbios,
                ScorePenalty = 8,
                Confidence = 0.6
            };
    }

    // ==================================================================
    private IEnumerable<Finding> InspectStorage(SystemSnapshot snapshot)
    {
        foreach (var d in snapshot.StorageDevices)
        {
            // Dung lượng khai báo lệch nhiều so với dung lượng thật là chiêu của ổ giả:
            // firmware bị sửa để báo dung lượng lớn hơn thực tế.
            if (d.CapacityBytes > 0 && d.IsSsd)
            {
                var tb = d.CapacityBytes / 1_000_000_000_000d;
                if (tb >= 4 && string.IsNullOrWhiteSpace(d.SmartSource))
                    yield return new Finding
                    {
                        Code = "AUTH-SSD-001",
                        Component = ComponentKind.Storage,
                        Severity = Severity.Warning,
                        Title = S($"Ổ khai {tb:0.#} TB nhưng không phản hồi lệnh S.M.A.R.T.", $"Drive claims {tb:0.#} TB but does not answer S.M.A.R.T. commands"),
                        Detail = S("Ổ dung lượng lớn mà controller không hỗ trợ tập lệnh chuẩn là tổ hợp " +
                                   "hay gặp ở ổ giả dung lượng — loại dùng firmware sửa để báo dung lượng " +
                                   "gấp nhiều lần dung lượng NAND thật.",
                                   "A large drive whose controller does not support the standard command set is a typical " +
                                   "sign of a fake-capacity drive — one with modified firmware reporting " +
                                   "many times its real NAND capacity."),
                        Recommendation = S("Kiểm tra bằng H2testw hoặc F3: ghi đầy ổ bằng dữ liệu kiểm chứng rồi đọc lại. " +
                                           "Đây là cách duy nhất chắc chắn phát hiện ổ giả dung lượng.",
                                           "Test with H2testw or F3: fill the drive with verifiable data and read it back. " +
                                           "This is the only sure way to catch a fake-capacity drive."),
                        Source = EvidenceSource.SmartAta,
                        ScorePenalty = 20,
                        Confidence = 0.5
                    };
            }

            // Model ghi là SSD nhưng có tốc độ vòng quay là mâu thuẫn vật lý.
            if (d.IsSsd && d.SpindleSpeedRpm is > 0)
                yield return new Finding
                {
                    Code = "AUTH-SSD-002",
                    Component = ComponentKind.Storage,
                    Severity = Severity.Notice,
                    Title = S("Ổ được khai là SSD nhưng báo có tốc độ vòng quay", "Drive is declared as an SSD but reports a spindle speed"),
                    Detail = S($"Model \"{d.Model}\" báo {d.SpindleSpeedRpm} vòng/phút. " +
                               "SSD không có bộ phận quay. Thường là ổ cơ được đổi tên model, " +
                               "hoặc ổ lai SSHD bị mô tả không chính xác.",
                               $"Model \"{d.Model}\" reports {d.SpindleSpeedRpm} rpm. " +
                               "SSDs have no spinning parts. Usually a hard disk with a renamed model, " +
                               "or a hybrid SSHD described inaccurately."),
                    Recommendation = S("Chạy bài đo tốc độ ngẫu nhiên: ổ cơ có độ trễ truy cập cao hơn SSD hàng chục lần.",
                                       "Run a random-access benchmark: hard disks have tens of times the access latency of SSDs."),
                    Source = EvidenceSource.Wmi,
                    ScorePenalty = 15,
                    Confidence = 0.6
                };
        }
    }

    // ==================================================================
    private IEnumerable<Finding> InspectGraphics(SystemSnapshot snapshot)
    {
        foreach (var gpu in snapshot.GraphicsAdapters.Where(g => g.IsDiscrete))
        {
            if (string.IsNullOrWhiteSpace(gpu.PnpDeviceId)) continue;

            // PnPDeviceID chứa VEN_xxxx&DEV_xxxx đọc từ thanh ghi cấu hình PCI của card.
            // Tên hiển thị lại do driver quyết định dựa trên bảng tra của hãng — mà bảng đó
            // có thể bị sửa, hoặc device ID bị đổi bằng cách flash lại vBIOS.
            var deviceId = ExtractPciId(gpu.PnpDeviceId, "DEV_");
            var vendorId = ExtractPciId(gpu.PnpDeviceId, "VEN_");
            if (deviceId is null || vendorId is null) continue;

            var expectedVendor = vendorId.ToUpperInvariant() switch
            {
                "10DE" => "NVIDIA",
                "1002" or "1022" => "AMD",
                "8086" => "INTEL",
                _ => null
            };

            if (expectedVendor is not null &&
                !gpu.Vendor.Contains(expectedVendor, StringComparison.OrdinalIgnoreCase) &&
                !gpu.Name.Contains(expectedVendor, StringComparison.OrdinalIgnoreCase))
            {
                yield return new Finding
                {
                    Code = "AUTH-GPU-001",
                    Component = ComponentKind.Gpu,
                    Severity = Severity.Warning,
                    Title = S("Tên card không khớp mã nhà sản xuất trên bus PCI", "The card name does not match the vendor code on the PCI bus"),
                    Detail = S($"PCI vendor ID {vendorId} tương ứng {expectedVendor}, " +
                               $"nhưng tên hiển thị là \"{gpu.Name}\" của {gpu.Vendor}.",
                               $"PCI vendor ID {vendorId} belongs to {expectedVendor}, " +
                               $"but the displayed name is \"{gpu.Name}\" from {gpu.Vendor}."),
                    Recommendation = S("Đối chiếu mã VEN/DEV trên trang tra cứu PCI ID để biết card thật là gì.",
                                       "Look up the VEN/DEV codes on a PCI ID site to find out what the card really is."),
                    Source = EvidenceSource.Wmi,
                    ScorePenalty = 25,
                    Confidence = 0.7,
                    Evidence = new Dictionary<string, string>
                    {
                        ["PciVendorId"] = vendorId,
                        ["PciDeviceId"] = deviceId,
                        ["ReportedName"] = gpu.Name
                    }
                };
            }

            yield return new Finding
            {
                Code = "AUTH-GPU-002",
                Component = ComponentKind.Gpu,
                Severity = Severity.Info,
                Title = S($"Mã phần cứng card: PCI\\VEN_{vendorId}&DEV_{deviceId}", $"Card hardware ID: PCI\\VEN_{vendorId}&DEV_{deviceId}"),
                Detail = S("Mã này đọc từ thanh ghi cấu hình PCI. Tra nó trên trang PCI ID để biết " +
                           "model thật của card, độc lập với tên mà driver hiển thị. " +
                           "Card bị flash vBIOS của model cao hơn vẫn giữ nguyên phần lớn đặc điểm phần cứng, " +
                           "nên hãy so thêm dung lượng VRAM và độ rộng bus nhớ với thông số chính thức.",
                           "This ID is read from the PCI configuration registers. Look it up on a PCI ID site to find " +
                           "the card's real model, independent of the name the driver shows. " +
                           "A card flashed with a higher model's vBIOS keeps most of its hardware traits, " +
                           "so also compare VRAM size and memory bus width with the official specs."),
                Source = EvidenceSource.Wmi,
                Confidence = 1.0
            };
        }
    }

    private static string? ExtractPciId(string pnpDeviceId, string prefix)
    {
        var index = pnpDeviceId.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;
        var start = index + prefix.Length;
        if (start + 4 > pnpDeviceId.Length) return null;
        var value = pnpDeviceId.Substring(start, 4);
        return value.All(Uri.IsHexDigit) ? value.ToUpperInvariant() : null;
    }
}
