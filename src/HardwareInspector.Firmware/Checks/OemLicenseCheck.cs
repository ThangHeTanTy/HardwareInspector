using HardwareInspector.Core.Models;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Firmware.Checks;

/// <summary>
/// Bảng ACPI MSDM chứa khoá Windows OEM được nạp vĩnh viễn vào firmware tại nhà máy.
/// Nó gắn liền với bo mạch. Vắng MSDM trên một máy thương hiệu bán kèm Windows,
/// hoặc kênh cấp phép đang dùng không phải OEM, là chỉ dấu mạnh cho việc bo mạch đã bị thay
/// hoặc firmware đã được ghi lại từ ảnh của máy khác.
/// </summary>
public sealed class OemLicenseCheck : IFirmwareCheck
{
    public string Id => "FW-MSDM";
    public string Title => "Khoá Windows nhúng trong firmware";
    public bool RequiresAdmin => false;

    private static readonly string[] BrandedOems =
    {
        "dell", "hp", "hewlett", "lenovo", "asus", "acer", "msi", "samsung",
        "toshiba", "fujitsu", "sony", "lg ", "microsoft", "apple", "gigabyte", "razer"
    };

    public IEnumerable<Finding> Run(FirmwareContext ctx)
    {
        var hasMsdm = ctx.Acpi.Has("MSDM");
        var hasSlic = ctx.Acpi.Has("SLIC");
        var manufacturer = ctx.Snapshot.Baseboard.SystemManufacturer.ToLowerInvariant();
        var isBranded = BrandedOems.Any(b => manufacturer.Contains(b));
        var channel = ctx.Snapshot.OperatingSystem.LicenseChannel;

        if (isBranded && !hasMsdm && !hasSlic && ctx.Snapshot.IsLaptop)
        {
            yield return new Finding
            {
                Code = "FW-MSDM-001",
                Component = ComponentKind.Firmware,
                Severity = Severity.Warning,
                Title = "Laptop thương hiệu nhưng firmware không có khoá Windows OEM",
                Detail = $"Không tìm thấy bảng MSDM hay SLIC trên máy {ctx.Snapshot.Baseboard.SystemManufacturer}. " +
                         "Laptop bán kèm Windows từ nhà máy luôn có một trong hai bảng này, và nó nằm trong firmware " +
                         "chứ không nằm trên ổ cứng nên không mất khi cài lại Windows.",
                Recommendation = "Hai khả năng: máy vốn bán ra không kèm Windows (FreeDOS), " +
                                 "hoặc bo mạch/BIOS đã được thay hay ghi lại. " +
                                 "Tra cấu hình xuất xưởng theo serial trên trang hãng để phân biệt.",
                Source = EvidenceSource.AcpiTable,
                ScorePenalty = 14,
                Confidence = 0.6
            };
        }

        if (hasMsdm && !string.IsNullOrWhiteSpace(channel) &&
            !channel.Contains("OEM", StringComparison.OrdinalIgnoreCase) &&
            !channel.Contains("Không xác định", StringComparison.OrdinalIgnoreCase))
        {
            yield return new Finding
            {
                Code = "FW-MSDM-002",
                Component = ComponentKind.OperatingSystem,
                Severity = Severity.Notice,
                Title = "Firmware có khoá OEM nhưng Windows đang kích hoạt bằng kênh khác",
                Detail = $"Kênh cấp phép hiện tại: {channel}. Máy có sẵn khoá OEM trong firmware " +
                         "nhưng người cài lại dùng khoá Volume hoặc công cụ kích hoạt ngoài.",
                Recommendation = "Cài lại Windows sạch để máy tự nhận khoá OEM trong firmware. " +
                                 "Nếu máy đang kích hoạt bằng công cụ crack, hãy coi toàn bộ hệ điều hành là không tin cậy.",
                Source = EvidenceSource.AcpiTable,
                ScorePenalty = 8,
                Confidence = 0.7
            };
        }

        if (hasMsdm)
            yield return Finding.Info("FW-MSDM-003", ComponentKind.Firmware,
                "Firmware có khoá Windows OEM nguyên bản (bảng MSDM)");
    }
}
