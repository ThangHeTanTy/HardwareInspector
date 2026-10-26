using HardwareInspector.Core.Models;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Firmware.Checks;

/// <summary>
/// Rà soát tổng thể danh sách bảng ACPI: thiếu bảng bảo mật cần có,
/// hoặc xuất hiện OEM ID không khớp với hãng sản xuất máy.
/// </summary>
public sealed class AcpiAnomalyCheck : IFirmwareCheck
{
    public string Id => "FW-ACPI";
    public string Title => S("Bất thường trong bảng ACPI", "ACPI table anomalies");
    public bool RequiresAdmin => false;

    public IEnumerable<Finding> Run(FirmwareContext ctx)
    {
        if (ctx.Acpi.Tables.Count == 0)
        {
            yield return new Finding
            {
                Code = "FW-ACPI-000",
                Component = ComponentKind.Firmware,
                Severity = Severity.Notice,
                Title = S("Không liệt kê được bảng ACPI",
"Could not list the ACPI tables"),
                Source = EvidenceSource.AcpiTable,
                Confidence = 1.0
            };
            yield break;
        }

        if (!ctx.Acpi.Has("WSMT"))
            yield return new Finding
            {
                Code = "FW-ACPI-001",
                Component = ComponentKind.Firmware,
                Severity = Severity.Notice,
                Title = S("Thiếu bảng WSMT (bảo vệ vùng SMM)",
"Missing WSMT table (SMM protection)"),
                Detail = S("WSMT cho hệ điều hành biết firmware đã áp dụng các biện pháp giảm thiểu tấn công vào SMM. " +
                         "Máy sản xuất từ khoảng 2018 trở đi thường có bảng này.",
"WSMT tells the OS that the firmware applies mitigations against SMM attacks. " +
                         "Machines made from about 2018 onwards usually have it."),
                Recommendation = S("Cập nhật BIOS lên bản mới nhất.",
"Update the BIOS to the latest version."),
                Source = EvidenceSource.AcpiTable,
                ScorePenalty = 4,
                Confidence = 0.6
            };

        if (ctx.Tpm.IsPresent && !ctx.Acpi.Has("TPM2") && !ctx.Acpi.Has("TCPA"))
            yield return new Finding
            {
                Code = "FW-ACPI-002",
                Component = ComponentKind.Firmware,
                Severity = Severity.Warning,
                Title = S("Windows thấy TPM nhưng firmware không khai báo bảng TPM2/TCPA",
"Windows sees a TPM but the firmware declares no TPM2/TCPA table"),
                Detail = S("Sự lệch pha giữa hai lớp này gặp ở máy ảo hoá, và cũng gặp khi bảng ACPI bị chỉnh sửa.",
"This mismatch between the two layers happens in VMs, and also when ACPI tables have been edited."),
                Recommendation = S("Kiểm tra máy có đang chạy trong máy ảo hay không trước khi kết luận.",
"Check whether the machine is running inside a VM before concluding anything."),
                Source = EvidenceSource.AcpiTable,
                ScorePenalty = 10,
                Confidence = 0.5
            };

        // OEM ID trong bảng ACPI do hãng nạp. Nếu toàn bộ bảng mang OEM ID chung chung
        // trong khi máy là hàng thương hiệu, firmware nhiều khả năng đã được thay bằng bản dựng lại.
        var oemIds = ctx.Acpi.Tables
            .Where(t => !string.IsNullOrWhiteSpace(t.OemId))
            .Select(t => t.OemId!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var suspiciousOem = new[] { "OEMID", "ALASKA", "_ASUS_", "INTEL", "AMD" };
        var manufacturer = ctx.Snapshot.Baseboard.SystemManufacturer;

        if (oemIds.Count == 1 &&
            suspiciousOem.Contains(oemIds[0], StringComparer.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(manufacturer) &&
            !manufacturer.Contains(oemIds[0], StringComparison.OrdinalIgnoreCase) &&
            ctx.Snapshot.IsLaptop)
        {
            yield return new Finding
            {
                Code = "FW-ACPI-003",
                Component = ComponentKind.Firmware,
                Severity = Severity.Notice,
                Title = S($"OEM ID trong bảng ACPI (\"{oemIds[0]}\") không khớp hãng máy ({manufacturer})",
$"The ACPI OEM ID (\"{oemIds[0]}\") does not match the manufacturer ({manufacturer})"),
                Detail = S("Trên laptop chính hãng, OEM ID thường mang mã riêng của hãng. " +
                         "Giá trị chung chung của nhà cung cấp BIOS gặp nhiều ở bo mạch thay thế hoặc firmware dựng lại.",
"On genuine branded laptops the OEM ID usually carries the manufacturer's own code. " +
                         "A generic BIOS-vendor value is common on replacement boards or rebuilt firmware."),
                Recommendation = S("Đối chiếu với một máy cùng model đã biết là nguyên bản.",
"Compare with a known-original machine of the same model."),
                Source = EvidenceSource.AcpiTable,
                ScorePenalty = 8,
                Confidence = 0.45
            };
        }

        var interesting = ctx.Acpi.Tables.Where(t => t.IsHighInterest).Select(t => t.Signature).ToList();
        if (interesting.Count > 0)
            yield return Finding.Info("FW-ACPI-004", ComponentKind.Firmware,
                S("Bảng ACPI đáng chú ý có mặt: ", "Noteworthy ACPI tables present: ") + string.Join(", ", interesting));
    }
}
