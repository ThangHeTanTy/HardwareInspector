using HardwareInspector.Core.Models;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Firmware.Checks;

/// <summary>
/// WPBT (Windows Platform Binary Table) cho phép firmware nhúng một file thực thi
/// mà Windows sẽ chạy với quyền cao nhất ở mỗi lần khởi động, và nó sống sót
/// qua cả việc format ổ cứng lẫn cài lại Windows.
/// Một số OEM dùng WPBT hợp lệ để cài driver hoặc phần mềm chống trộm.
/// Nhưng đây cũng là cơ chế mà LoJax và các bootkit thương mại lạm dụng.
/// </summary>
public sealed class WpbtCheck : IFirmwareCheck
{
    public string Id => "FW-WPBT";
    public string Title => "Bảng nhị phân nền tảng (WPBT)";
    public bool RequiresAdmin => false;

    public IEnumerable<Finding> Run(FirmwareContext ctx)
    {
        var wpbt = ctx.Acpi.Get("WPBT");
        if (wpbt is null) yield break;

        var vendor = ctx.Snapshot.Baseboard.SystemManufacturer;

        yield return new Finding
        {
            Code = "FW-WPBT-001",
            Component = ComponentKind.Firmware,
            Severity = Severity.Warning,
            Title = "Firmware có nhúng chương trình tự chạy vào Windows (WPBT)",
            Detail = $"Phát hiện bảng ACPI WPBT ({wpbt.Length} byte, OEM: {wpbt.OemId ?? "không rõ"}). " +
                     "Bảng này chứa một file thực thi mà Windows nạp và chạy với quyền hệ thống ở mỗi lần khởi động. " +
                     "Nó tồn tại độc lập với ổ cứng, nên format hay cài lại Windows đều không xoá được.",
            Recommendation = $"Kiểm tra xem {(string.IsNullOrWhiteSpace(vendor) ? "hãng sản xuất" : vendor)} " +
                             "có công bố dùng WPBT cho phần mềm nào không (Lenovo, ASUS, Dell từng dùng cho driver hoặc anti-theft). " +
                             "Nếu hãng không dùng WPBT cho dòng máy này, đây là dấu hiệu firmware đã bị chỉnh sửa. " +
                             "Đối chiếu thêm: quét tiến trình lạ ngay sau khi cài mới Windows offline.",
            Source = EvidenceSource.AcpiTable,
            ScorePenalty = 20,
            Confidence = 0.75,
            Evidence = new Dictionary<string, string>
            {
                ["Signature"] = wpbt.Signature,
                ["Length"] = wpbt.Length.ToString(),
                ["OemId"] = wpbt.OemId ?? "—",
                ["OemTableId"] = wpbt.OemTableId ?? "—"
            }
        };
    }
}
