using HardwareInspector.Core.Models;
using HardwareInspector.Core.Utils;
using HardwareInspector.Firmware.Smbios;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Firmware.Checks;

/// <summary>
/// SMBIOS là nơi đầu tiên bị viết lại khi ai đó muốn "làm mới" một cái máy:
/// đổi tên model, đổi serial để qua mặt bảo hành, hoặc xoá dấu vết bo mạch thay thế.
/// Công cụ ghi SMBIOS thường để lại chuỗi mặc định hoặc bỏ trống trường mà OEM luôn điền.
/// </summary>
public sealed class SmbiosConsistencyCheck : IFirmwareCheck
{
    public string Id => "FW-SMBIOS";
    public string Title => "Tính nhất quán của bảng SMBIOS";
    public bool RequiresAdmin => false;

    public IEnumerable<Finding> Run(FirmwareContext ctx)
    {
        if (!ctx.Smbios.IsAvailable)
        {
            yield return new Finding
            {
                Code = "FW-SMBIOS-000",
                Component = ComponentKind.Firmware,
                Severity = Severity.Notice,
                Title = "Không đọc được bảng SMBIOS thô",
                Detail = "Ứng dụng chỉ dựa được vào dữ liệu WMI đã qua xử lý, độ tin cậy thấp hơn.",
                Source = EvidenceSource.Smbios,
                ScorePenalty = 0,
                Confidence = 1.0
            };
            yield break;
        }

        var system = SmbiosDecoder.DecodeSystem(ctx.Smbios.First(1));
        var board = SmbiosDecoder.DecodeBoard(ctx.Smbios.First(2));
        var chassis = SmbiosDecoder.DecodeChassis(ctx.Smbios.First(3));
        var bios = SmbiosDecoder.DecodeBios(ctx.Smbios.First(0));

        var placeholders = new List<string>();
        void Check(string label, string? value)
        {
            if (FormatHelpers.IsPlaceholder(value)) placeholders.Add($"{label} = \"{value}\"");
        }

        Check("System Manufacturer", system?.Manufacturer);
        Check("System Product", system?.Product);
        Check("System Serial", system?.Serial);
        Check("Board Manufacturer", board?.Manufacturer);
        Check("Board Serial", board?.Serial);
        Check("Chassis Serial", chassis?.Serial);

        if (placeholders.Count >= 3)
        {
            yield return new Finding
            {
                Code = "FW-SMBIOS-001",
                Component = ComponentKind.Firmware,
                Severity = Severity.Warning,
                Title = $"{placeholders.Count} trường định danh trong SMBIOS để giá trị mặc định",
                Detail = "Máy nguyên bản của hãng lớn luôn có serial và model được nạp tại nhà máy. " +
                         "Nhiều trường bỏ trống cùng lúc thường gặp ở ba tình huống: bo mạch thay thế không phải hàng OEM, " +
                         "BIOS được ghi lại bằng programmer mà không nạp lại vùng định danh, hoặc máy lắp từ linh kiện rời.\n\n" +
                         string.Join("\n", placeholders),
                Recommendation = "Đối chiếu serial in trên tem đáy máy với serial phần mềm đọc được. " +
                                 "Lệch nhau là bằng chứng rõ ràng bo mạch hoặc BIOS đã bị thay.",
                Source = EvidenceSource.Smbios,
                ScorePenalty = 18,
                Confidence = 0.8,
                Evidence = placeholders.Select((p, i) => new { i, p })
                    .ToDictionary(x => $"Field{x.i + 1}", x => x.p)
            };
        }

        // Serial hệ thống và serial vỏ máy do OEM nạp thường trùng nhau trên laptop.
        if (system is not null && chassis is not null &&
            !FormatHelpers.IsPlaceholder(system.Serial) &&
            !FormatHelpers.IsPlaceholder(chassis.Serial) &&
            !system.Serial.Equals(chassis.Serial, StringComparison.OrdinalIgnoreCase) &&
            ctx.Snapshot.IsLaptop)
        {
            yield return new Finding
            {
                Code = "FW-SMBIOS-002",
                Component = ComponentKind.Firmware,
                Severity = Severity.Notice,
                Title = "Serial hệ thống khác serial vỏ máy",
                Detail = $"System: {system.Serial} — Chassis: {chassis.Serial}. " +
                         "Trên laptop nguyên bản hai giá trị này thường giống nhau. " +
                         "Khác nhau có thể do bo mạch được thay bằng bo của máy khác, hoặc do OEM đó vốn ghi khác nhau.",
                Recommendation = "Tra serial trên trang bảo hành của hãng. Nếu tra ra model khác với máy đang cầm, dừng giao dịch.",
                Source = EvidenceSource.Smbios,
                ScorePenalty = 10,
                Confidence = 0.55
            };
        }

        if (bios is not null && SmbiosDecoder.ChassisLockPresent(ctx.Smbios.First(3)) == false &&
            chassis?.SecurityStatus == 5)
        {
            yield return new Finding
            {
                Code = "FW-SMBIOS-003",
                Component = ComponentKind.Motherboard,
                Severity = Severity.Notice,
                Title = "Cảm biến vỏ máy ghi nhận máy đã từng được mở",
                Detail = "SMBIOS Type 3 báo trạng thái an ninh vỏ máy ở mức đã bị can thiệp.",
                Recommendation = "Không nhất thiết xấu — máy đã vệ sinh hoặc nâng cấp RAM cũng kích hoạt cờ này. " +
                                 "Hãy hỏi thẳng người bán máy đã được mở ra làm gì.",
                Source = EvidenceSource.Smbios,
                ScorePenalty = 5,
                Confidence = 0.6
            };
        }
    }
}
