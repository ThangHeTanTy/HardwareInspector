using HardwareInspector.Core.Models;
using HardwareInspector.Core.Utils;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Firmware.Checks;

/// <summary>
/// Soi phân vùng EFI. Bootkit phải để lại một binary ở đâu đó trong ESP;
/// mọi file .efi nằm ngoài đường dẫn của Microsoft và các OEM lớn đều đáng hỏi.
/// </summary>
public sealed class EspIntegrityCheck : IFirmwareCheck
{
    public string Id => "FW-ESP";
    public string Title => "Nội dung phân vùng khởi động EFI";
    public bool RequiresAdmin => true;

    public IEnumerable<Finding> Run(FirmwareContext ctx)
    {
        if (!ctx.EspReadable)
        {
            if (ctx.IsElevated)
                yield return new Finding
                {
                    Code = "FW-ESP-000",
                    Component = ComponentKind.Firmware,
                    Severity = Severity.Notice,
                    Title = "Không gắn được phân vùng EFI để kiểm tra",
                    Detail = "Máy có thể đang khởi động Legacy, hoặc phân vùng EFI nằm trên ổ khác.",
                    Source = EvidenceSource.FileSystem,
                    Confidence = 1.0
                };
            yield break;
        }

        var unknown = ctx.EspBinaries.Where(b => !b.IsKnownVendorPath).ToList();
        if (unknown.Count == 0)
        {
            yield return Finding.Info("FW-ESP-001", ComponentKind.Firmware,
                $"Phân vùng EFI sạch: {ctx.EspBinaries.Count} file, tất cả nằm trong đường dẫn chuẩn");
            yield break;
        }

        foreach (var entry in unknown.Take(10))
        {
            yield return new Finding
            {
                Code = "FW-ESP-002",
                Component = ComponentKind.Firmware,
                Severity = Severity.Warning,
                Title = $"File khởi động lạ trong phân vùng EFI: {entry.Path}",
                Detail = $"Kích thước {entry.SizeBytes.ToSize()}, sửa lần cuối {entry.LastWriteUtc.ToLocalTime():dd/MM/yyyy HH:mm}. " +
                         "Đường dẫn này không thuộc Microsoft hay các OEM phổ biến.",
                Recommendation = "Đối chiếu mã băm SHA-256 với VirusTotal trước khi kết luận. " +
                                 "Các bản cài Linux, rEFInd hay công cụ chẩn đoán của hãng cũng tạo file ở đây.",
                Source = EvidenceSource.FileSystem,
                ScorePenalty = 12,
                Confidence = 0.6,
                Evidence = new Dictionary<string, string>
                {
                    ["Path"] = entry.Path,
                    ["SHA256"] = entry.Sha256 ?? "—",
                    ["Size"] = entry.SizeBytes.ToString()
                }
            };
        }

        // Bootloader Windows bị sửa gần đây hơn ngày cài Windows là tín hiệu mạnh.
        var bootmgr = ctx.EspBinaries.FirstOrDefault(b =>
            b.Path.EndsWith(@"\BOOTMGFW.EFI", StringComparison.OrdinalIgnoreCase));
        var installDate = ctx.Snapshot.OperatingSystem.InstallDate;

        if (bootmgr is not null && installDate.HasValue &&
            bootmgr.LastWriteUtc.ToLocalTime() > installDate.Value.AddDays(1))
        {
            yield return new Finding
            {
                Code = "FW-ESP-003",
                Component = ComponentKind.Firmware,
                Severity = Severity.Notice,
                Title = "Bootloader Windows được ghi lại sau ngày cài đặt",
                Detail = $"bootmgfw.efi sửa lần cuối {bootmgr.LastWriteUtc.ToLocalTime():dd/MM/yyyy}, " +
                         $"Windows cài ngày {installDate:dd/MM/yyyy}. " +
                         "Windows Update hợp lệ cũng ghi đè file này, nên đây chỉ là tín hiệu cần đối chiếu thêm.",
                Recommendation = "So mã băm bootmgfw.efi với bản sao trong C:\\Windows\\Boot\\EFI.",
                Source = EvidenceSource.FileSystem,
                ScorePenalty = 6,
                Confidence = 0.4
            };
        }
    }
}
