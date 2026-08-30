using HardwareInspector.Analysis.Scoring;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Assessment;
using HardwareInspector.Core.Utils;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Analysis.Analyzers;

public sealed class FirmwareAnalyzer : IComponentAnalyzer
{
    public ComponentKind Kind => ComponentKind.Firmware;

    public bool CanAnalyze(SystemSnapshot snapshot) => true;

    public ComponentAssessment Analyze(SystemSnapshot snapshot)
    {
        var fw = snapshot.Firmware;
        var bios = snapshot.Bios;
        var card = new ScoreCard(Kind, $"{bios.Vendor.OrDash()} {bios.Version.OrDash()}");

        card.Metric(S("Trạng thái toàn vẹn", "Integrity status"), fw.Status.ToText(), null,
                fw.Status switch
                {
                    IntegrityStatus.Compromised => Severity.Critical,
                    IntegrityStatus.Suspicious => Severity.Warning,
                    IntegrityStatus.Unverified => Severity.Notice,
                    _ => Severity.Info
                })
            .Metric(S("Phiên bản BIOS", "BIOS version"), bios.Version.OrDash())
            .Metric(S("Ngày phát hành", "Release date"), bios.ReleaseDate?.ToString("dd/MM/yyyy") ?? "—")
            .Metric(S("Chuẩn SMBIOS", "SMBIOS version"), bios.SmbiosVersion)
            .MetricIf(bios.EcFirmwareVersion is not null, S("Firmware EC", "EC firmware"), bios.EcFirmwareVersion.OrDash())
            .Metric(S("Chế độ khởi động", "Boot mode"), fw.SecureBoot.BootFromUefi ? "UEFI" : "Legacy/CSM")
            .Metric(S("Secure Boot", "Secure Boot"), Tri(fw.SecureBoot.Enabled))
            .Metric(S("Setup Mode", "Setup Mode"), Tri(fw.SecureBoot.SetupMode),
                fw.SecureBoot.SetupMode == true ? S("Khoá nền tảng đã bị xoá", "The platform key has been cleared") : null,
                fw.SecureBoot.SetupMode == true ? Severity.Critical : Severity.Info)
            .Metric(S("TPM", "TPM"), fw.Tpm.IsPresent ? S($"Có, phiên bản {fw.Tpm.SpecVersion.OrDash()}", $"Present, version {fw.Tpm.SpecVersion.OrDash()}") : S("Không có", "Absent"))
            .Metric(S("VBS / HVCI", "VBS / HVCI"),
                $"{(fw.DeviceGuard.VirtualizationBasedSecurityRunning ? S("Đang chạy", "Running") : S("Tắt", "Off"))} / " +
                $"{(fw.DeviceGuard.HypervisorEnforcedCodeIntegrity ? S("Đang chạy", "Running") : S("Tắt", "Off"))}")
            .Metric(S("Số bảng ACPI", "ACPI table count"), fw.AcpiTables.Count.ToString())
            .MetricIf(fw.EspBinaries.Count > 0, S("File .efi trong phân vùng khởi động", ".efi files on the boot partition"),
                S($"{fw.EspBinaries.Count} file ({fw.EspBinaries.Count(b => !b.IsKnownVendorPath)} nằm ngoài đường dẫn chuẩn)", $"{fw.EspBinaries.Count} files ({fw.EspBinaries.Count(b => !b.IsKnownVendorPath)} outside standard paths)"));

        foreach (var finding in fw.Findings)
            card.Add(finding);

        if (!fw.RanWithAdminRights)
            card.Add("FW-PRIV-001", Kind, Severity.Notice,
                S("Một số phép kiểm tra bị bỏ qua do thiếu quyền Administrator", "Some checks were skipped for lack of Administrator rights"),
                S("Không kiểm tra được phân vùng EFI, cấu hình khởi động và một phần biến UEFI. ", "The EFI partition, boot configuration and part of the UEFI variables could not be inspected. ") +
                "Kết luận \"không phát hiện can thiệp\" trong trường hợp này không có nhiều giá trị.",
                S("Đóng ứng dụng và mở lại bằng chuột phải → Run as administrator.", "Close the app and reopen it with right-click → Run as administrator."),
                penalty: 0, confidence: 1.0);

        // Không cho phép một máy có dấu hiệu can thiệp firmware nhận điểm cao.
        card.Headline(fw.Status switch
        {
            IntegrityStatus.Compromised => S("Phát hiện dấu hiệu firmware đã bị can thiệp", "Signs of firmware tampering found"),
            IntegrityStatus.Suspicious => S("Có bất thường ở tầng firmware, cần kiểm tra thêm", "Firmware anomalies present, further checks needed"),
            IntegrityStatus.Unverified => S("Chưa đủ quyền để kết luận về firmware", "Not enough privilege to judge the firmware"),
            IntegrityStatus.Clean => S("Không phát hiện dấu hiệu can thiệp firmware", "No sign of firmware tampering"),
            _ => S("Chưa kiểm tra firmware", "Firmware not checked")
        });

        return fw.Status switch
        {
            IntegrityStatus.Compromised => card.Cap(30).Build(),
            IntegrityStatus.Suspicious => card.Cap(64).Build(),
            IntegrityStatus.Unverified => card.Cap(79).Build(),
            _ => card.Build()
        };
    }

    private static string Tri(bool? value) => value switch
    {
        true => S("Bật", "On"),
        false => S("Tắt", "Off"),
        _ => S("Không đọc được", "Could not be read")
    };
}
