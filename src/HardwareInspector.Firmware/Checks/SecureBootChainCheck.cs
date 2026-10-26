using HardwareInspector.Core.Models;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Firmware.Checks;

/// <summary>
/// Chuỗi khởi động tin cậy: Secure Boot -> Setup Mode -> khoá PK -> dbx.
/// Máy đã bị can thiệp BIOS gần như luôn phải tắt Secure Boot hoặc xoá khoá PK
/// để nạp được module không có chữ ký hợp lệ.
/// </summary>
public sealed class SecureBootChainCheck : IFirmwareCheck
{
    public string Id => "FW-SB";
    public string Title => S("Chuỗi khởi động an toàn (Secure Boot)", "Secure boot chain (Secure Boot)");
    public bool RequiresAdmin => false;

    public IEnumerable<Finding> Run(FirmwareContext ctx)
    {
        var sb = ctx.SecureBoot;

        if (!sb.BootFromUefi)
        {
            yield return new Finding
            {
                Code = "FW-SB-000",
                Component = ComponentKind.Firmware,
                Severity = Severity.Warning,
                Title = S("Máy đang khởi động ở chế độ Legacy/CSM",
                "The machine is booting in Legacy/CSM mode"),
                Detail = S("Ở chế độ này không có bất kỳ lớp xác thực chữ ký nào cho bootloader. " +
                         "Toàn bộ các phép kiểm tra Secure Boot bên dưới không áp dụng được.",
                         "In this mode there is no signature verification for the bootloader at all. " +
                         "None of the Secure Boot checks below apply."),
                Recommendation = S("Nếu máy đủ mới để chạy UEFI, hãy hỏi vì sao chủ cũ chuyển sang Legacy. " +
                                 "Đây là cấu hình thường thấy khi cần khởi động công cụ không có chữ ký.",
                                 "If the machine is new enough for UEFI, ask why the previous owner switched to Legacy. " +
                                 "It is a common setup when booting unsigned tools."),
                Source = EvidenceSource.UefiVariable,
                ScorePenalty = 12,
                Confidence = 0.9
            };
            yield break;
        }

        if (sb.SetupMode == true)
        {
            yield return new Finding
            {
                Code = "FW-SB-001",
                Component = ComponentKind.Firmware,
                Severity = Severity.Critical,
                Title = S("Firmware đang ở Setup Mode — khoá nền tảng đã bị xoá",
                "Firmware is in Setup Mode — the platform key has been cleared"),
                Detail = S("Setup Mode nghĩa là biến PK (Platform Key) rỗng. Khi đó firmware chấp nhận " +
                         "mọi khoá do người dùng nạp vào, và mọi bootloader ký bằng khoá đó đều chạy được. " +
                         "Đây là bước bắt buộc để cài bootkit trên máy UEFI.",
                         "Setup Mode means the PK (Platform Key) variable is empty. The firmware then accepts " +
                         "any key the user enrols, and any bootloader signed with it will run. " +
                         "This is a required step for installing a bootkit on a UEFI machine."),
                Recommendation = S("Vào BIOS, chọn Restore Factory Keys rồi bật lại Secure Boot. " +
                                 "Nếu máy không cho khôi phục khoá gốc, coi như firmware đã bị thay và không nên nhận máy.",
                                 "Enter the BIOS, choose Restore Factory Keys and re-enable Secure Boot. " +
                                 "If the machine cannot restore the factory keys, treat the firmware as replaced and do not accept the machine."),
                Source = EvidenceSource.UefiVariable,
                ScorePenalty = 45,
                Confidence = 0.95
            };
        }

        if (sb.Enabled == false && sb.SetupMode != true)
        {
            yield return new Finding
            {
                Code = "FW-SB-002",
                Component = ComponentKind.Firmware,
                Severity = Severity.Warning,
                Title = S("Secure Boot đang tắt",
                "Secure Boot is off"),
                Detail = S("Bản thân việc tắt Secure Boot chưa chứng minh máy bị can thiệp — " +
                         "người dùng Linux hoặc game thủ cài driver cũ vẫn hay tắt. " +
                         "Nhưng nó gỡ bỏ lớp phòng thủ đầu tiên chống bootkit.",
                         "Turning Secure Boot off does not by itself prove tampering — " +
                         "Linux users and gamers with old drivers often turn it off. " +
                         "But it removes the first line of defence against bootkits."),
                Recommendation = S("Bật lại Secure Boot trong BIOS và kiểm tra máy còn khởi động bình thường không. " +
                                 "Nếu bật xong máy không boot được, có thứ gì đó trong chuỗi khởi động không có chữ ký hợp lệ.",
                                 "Turn Secure Boot back on in the BIOS and check the machine still boots normally. " +
                                 "If it no longer boots, something in the boot chain lacks a valid signature."),
                Source = EvidenceSource.UefiVariable,
                ScorePenalty = 15,
                Confidence = 0.9
            };
        }

        if (sb.PlatformKeyPresent == false && sb.SetupMode != true)
        {
            yield return new Finding
            {
                Code = "FW-SB-003",
                Component = ComponentKind.Firmware,
                Severity = Severity.Warning,
                Title = S("Không đọc được khoá nền tảng (PK)",
                "Could not read the platform key (PK)"),
                Detail = S("Biến PK rỗng hoặc không truy cập được. Có thể do thiếu quyền, cũng có thể do khoá đã bị xoá.",
                "The PK variable is empty or inaccessible. It may be missing rights, or the key may have been cleared."),
                Recommendation = S("Chạy lại ứng dụng với quyền Administrator để loại trừ khả năng thiếu quyền.",
                "Run the app again as Administrator to rule out missing rights."),
                Source = EvidenceSource.UefiVariable,
                ScorePenalty = 8,
                Confidence = 0.5
            };
        }

        if (sb.DbxUpdateCount is > 0 and < 20)
        {
            yield return new Finding
            {
                Code = "FW-SB-004",
                Component = ComponentKind.Firmware,
                Severity = Severity.Notice,
                Title = S("Danh sách thu hồi chữ ký (dbx) có vẻ cũ",
                "The signature revocation list (dbx) looks outdated"),
                Detail = S($"Ước tính khoảng {sb.DbxUpdateCount} mục thu hồi. Máy được cập nhật đầy đủ thường có " +
                         "hàng trăm mục sau các đợt vá BlackLotus và bootloader GRUB bị lộ.",
                         $"About {sb.DbxUpdateCount} revocation entries. A fully updated machine usually has " +
                         "hundreds after the BlackLotus and leaked GRUB bootloader patches."),
                Recommendation = S("Cập nhật Windows đầy đủ và nâng BIOS lên bản mới nhất từ trang chủ hãng.",
                "Fully update Windows and upgrade the BIOS to the latest version from the manufacturer's site."),
                Source = EvidenceSource.UefiVariable,
                ScorePenalty = 5,
                Confidence = 0.4
            };
        }
    }
}
