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
    public string Title => "Bất thường trong bảng ACPI";
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
                Title = "Không liệt kê được bảng ACPI",
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
                Title = "Thiếu bảng WSMT (bảo vệ vùng SMM)",
                Detail = "WSMT cho hệ điều hành biết firmware đã áp dụng các biện pháp giảm thiểu tấn công vào SMM. " +
                         "Máy sản xuất từ khoảng 2018 trở đi thường có bảng này.",
                Recommendation = "Cập nhật BIOS lên bản mới nhất.",
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
                Title = "Windows thấy TPM nhưng firmware không khai báo bảng TPM2/TCPA",
                Detail = "Sự lệch pha giữa hai lớp này gặp ở máy ảo hoá, và cũng gặp khi bảng ACPI bị chỉnh sửa.",
                Recommendation = "Kiểm tra máy có đang chạy trong máy ảo hay không trước khi kết luận.",
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
                Title = $"OEM ID trong bảng ACPI (\"{oemIds[0]}\") không khớp hãng máy ({manufacturer})",
                Detail = "Trên laptop chính hãng, OEM ID thường mang mã riêng của hãng. " +
                         "Giá trị chung chung của nhà cung cấp BIOS gặp nhiều ở bo mạch thay thế hoặc firmware dựng lại.",
                Recommendation = "Đối chiếu với một máy cùng model đã biết là nguyên bản.",
                Source = EvidenceSource.AcpiTable,
                ScorePenalty = 8,
                Confidence = 0.45
            };
        }

        var interesting = ctx.Acpi.Tables.Where(t => t.IsHighInterest).Select(t => t.Signature).ToList();
        if (interesting.Count > 0)
            yield return Finding.Info("FW-ACPI-004", ComponentKind.Firmware,
                "Bảng ACPI đáng chú ý có mặt: " + string.Join(", ", interesting));
    }
}
