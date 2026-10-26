using HardwareInspector.Core.Models;
using HardwareInspector.Core.Utils;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Firmware.Checks;

/// <summary>
/// Đối chiếu siêu dữ liệu BIOS với phần còn lại của máy.
/// Ba tín hiệu đáng giá: chuỗi phiên bản không theo định dạng của hãng,
/// ngày phát hành BIOS mới hơn ngày sản xuất máy một cách bất thường,
/// và BIOS quá cũ so với dòng máy (chưa từng được cập nhật vá lỗi bảo mật).
/// </summary>
public sealed class BiosMetadataCheck : IFirmwareCheck
{
    public string Id => "FW-BIOS";
    public string Title => S("Siêu dữ liệu BIOS", "BIOS metadata");
    public bool RequiresAdmin => false;

    /// <summary>Chuỗi đặc trưng của các bản BIOS đã bị chỉnh sửa hoặc build lại.</summary>
    private static readonly string[] ModdedMarkers =
    {
        "mod", "unlock", "unlocked", "custom", "patched", "hack",
        "test", "debug", "beta", "eng", "dev", "svl", "resigned"
    };

    public IEnumerable<Finding> Run(FirmwareContext ctx)
    {
        var bios = ctx.Snapshot.Bios;
        var os = ctx.Snapshot.OperatingSystem;

        if (!string.IsNullOrWhiteSpace(bios.Version))
        {
            var v = bios.Version.ToLowerInvariant();
            var hit = ModdedMarkers.FirstOrDefault(m =>
                v.Contains(m, StringComparison.OrdinalIgnoreCase) &&
                !v.Contains("modem", StringComparison.OrdinalIgnoreCase));

            if (hit is not null)
            {
                yield return new Finding
                {
                    Code = "FW-BIOS-001",
                    Component = ComponentKind.Firmware,
                    Severity = Severity.Critical,
                    Title = S("Chuỗi phiên bản BIOS chứa dấu hiệu bản chỉnh sửa",
                    "The BIOS version string shows signs of a modified build"),
                    Detail = S($"Phiên bản báo về: \"{bios.Version}\" (khớp từ khoá \"{hit}\"). " +
                             "BIOS chính hãng dùng định dạng số hoặc mã nội bộ cố định, không mang các từ này.",
                             $"Reported version: \"{bios.Version}\" (matched keyword \"{hit}\"). " +
                             "Genuine BIOSes use fixed numeric or internal codes and never carry these words."),
                    Recommendation = S("Tải BIOS gốc từ trang chủ hãng theo đúng serial và nạp lại. " +
                                     "Nếu máy đang chạy BIOS mod để mở khoá tính năng, mọi bảo hành đều mất hiệu lực.",
                                     "Download the original BIOS for this serial from the manufacturer's site and reflash it. " +
                                     "If the machine runs a modded BIOS to unlock features, every warranty is void."),
                    Source = EvidenceSource.Smbios,
                    ScorePenalty = 35,
                    Confidence = 0.7,
                    Evidence = new Dictionary<string, string> { ["BiosVersion"] = bios.Version }
                };
            }
        }

        if (FormatHelpers.IsPlaceholder(bios.Vendor))
        {
            yield return new Finding
            {
                Code = "FW-BIOS-002",
                Component = ComponentKind.Firmware,
                Severity = Severity.Warning,
                Title = S("Không xác định được nhà cung cấp BIOS",
                "The BIOS vendor could not be identified"),
                Detail = S($"Trường vendor đọc được: \"{bios.Vendor.OrDash()}\".",
                $"Vendor field read: \"{bios.Vendor.OrDash()}\"."),
                Recommendation = S("Kiểm tra lại bằng cách vào thẳng màn hình BIOS khi khởi động.",
                "Check by entering the BIOS screen directly at boot."),
                Source = EvidenceSource.Smbios,
                ScorePenalty = 12,
                Confidence = 0.7
            };
        }

        if (bios.ReleaseDate.HasValue)
        {
            var age = DateTime.Now - bios.ReleaseDate.Value;

            if (bios.ReleaseDate.Value > DateTime.Now.AddDays(1))
            {
                yield return new Finding
                {
                    Code = "FW-BIOS-003",
                    Component = ComponentKind.Firmware,
                    Severity = Severity.Warning,
                    Title = S("Ngày phát hành BIOS nằm ở tương lai",
                    "The BIOS release date is in the future"),
                    Detail = S($"BIOS ghi ngày {bios.ReleaseDate:dd/MM/yyyy}. " +
                             "Điều này xảy ra khi bảng SMBIOS bị ghi tay hoặc đồng hồ hệ thống sai.",
                             $"The BIOS says {bios.ReleaseDate:dd/MM/yyyy}. " +
                             "This happens when the SMBIOS table was written by hand or the system clock is wrong."),
                    Recommendation = S("Kiểm tra ngày giờ hệ thống trước. Nếu giờ hệ thống đúng, bảng SMBIOS đã bị sửa.",
                    "Check the system date and time first. If it is correct, the SMBIOS table was edited."),
                    Source = EvidenceSource.Smbios,
                    ScorePenalty = 20,
                    Confidence = 0.85
                };
            }
            else if (age.TotalDays > 365 * 6)
            {
                yield return new Finding
                {
                    Code = "FW-BIOS-004",
                    Component = ComponentKind.Firmware,
                    Severity = Severity.Notice,
                    Title = S($"BIOS chưa được cập nhật trong {age.TotalDays / 365:0.#} năm",
                    $"The BIOS has not been updated in {age.TotalDays / 365:0.#} years"),
                    Detail = S($"Bản BIOS đang chạy phát hành ngày {bios.ReleaseDate:dd/MM/yyyy}. " +
                             "Các lỗ hổng firmware công bố sau mốc này đều chưa được vá.",
                             $"The running BIOS was released on {bios.ReleaseDate:dd/MM/yyyy}. " +
                             "Firmware vulnerabilities disclosed after that date are unpatched."),
                    Recommendation = S("Cập nhật BIOS từ trang chủ hãng, ưu tiên bản có vá microcode CPU.",
                    "Update the BIOS from the manufacturer's site, preferably a version with CPU microcode fixes."),
                    Source = EvidenceSource.Smbios,
                    ScorePenalty = 6,
                    Confidence = 0.9
                };
            }

            // Windows cài trước khi BIOS ra đời là điều bất khả thi trên một máy chưa bị can thiệp.
            if (os.InstallDate.HasValue && os.InstallDate.Value < bios.ReleaseDate.Value.AddDays(-30))
            {
                yield return new Finding
                {
                    Code = "FW-BIOS-005",
                    Component = ComponentKind.Firmware,
                    Severity = Severity.Notice,
                    Title = S("Windows được cài trước ngày phát hành BIOS",
                    "Windows was installed before the BIOS release date"),
                    Detail = S($"Windows cài ngày {os.InstallDate:dd/MM/yyyy}, BIOS phát hành {bios.ReleaseDate:dd/MM/yyyy}. " +
                             "Thường gặp khi BIOS mới được nạp lại gần đây, hoặc khi ổ cứng được bê từ máy khác sang.",
                             $"Windows installed on {os.InstallDate:dd/MM/yyyy}, BIOS released {bios.ReleaseDate:dd/MM/yyyy}. " +
                             "Usually the BIOS was reflashed recently, or the drive was moved from another machine."),
                    Recommendation = S("Hỏi người bán gần đây có cập nhật BIOS hoặc thay bo mạch không.",
                    "Ask the seller whether the BIOS was recently updated or the board replaced."),
                    Source = EvidenceSource.Smbios,
                    ScorePenalty = 5,
                    Confidence = 0.5
                };
            }
        }

        if (ctx.Snapshot.PrimaryCpu is { } cpu &&
            !string.IsNullOrWhiteSpace(cpu.MicrocodeCurrent) &&
            !string.IsNullOrWhiteSpace(cpu.MicrocodePrevious) &&
            !cpu.MicrocodeCurrent.Equals(cpu.MicrocodePrevious, StringComparison.OrdinalIgnoreCase))
        {
            var current = ParseHex(cpu.MicrocodeCurrent);
            var previous = ParseHex(cpu.MicrocodePrevious);

            if (current < previous)
            {
                yield return new Finding
                {
                    Code = "FW-BIOS-006",
                    Component = ComponentKind.Firmware,
                    Severity = Severity.Warning,
                    Title = S("Microcode CPU đang chạy thấp hơn bản BIOS nạp lúc khởi động",
                    "The running CPU microcode is older than what the BIOS loaded at boot"),
                    Detail = S($"BIOS nạp {cpu.MicrocodePrevious}, hệ thống hiện chạy {cpu.MicrocodeCurrent}. " +
                             "Hạ cấp microcode là kỹ thuật quen thuộc để mở lại các lỗ hổng đã bị vá.",
                             $"The BIOS loaded {cpu.MicrocodePrevious}, the system now runs {cpu.MicrocodeCurrent}. " +
                             "Downgrading microcode is a well-known way to reopen patched vulnerabilities."),
                    Recommendation = S("Khởi động lại và kiểm tra lại. Nếu vẫn thấp hơn, xem lại driver hoặc công cụ nạp microcode đang chạy trên máy.",
                    "Restart and check again. If it is still lower, look at the drivers or microcode loaders running on the machine."),
                    Source = EvidenceSource.Registry,
                    ScorePenalty = 18,
                    Confidence = 0.65
                };
            }
        }
    }

    private static long ParseHex(string value)
    {
        var s = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        return long.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
    }
}
