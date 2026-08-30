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
    public string Title => "Chuỗi khởi động an toàn (Secure Boot)";
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
                Title = "Máy đang khởi động ở chế độ Legacy/CSM",
                Detail = "Ở chế độ này không có bất kỳ lớp xác thực chữ ký nào cho bootloader. " +
                         "Toàn bộ các phép kiểm tra Secure Boot bên dưới không áp dụng được.",
                Recommendation = "Nếu máy đủ mới để chạy UEFI, hãy hỏi vì sao chủ cũ chuyển sang Legacy. " +
                                 "Đây là cấu hình thường thấy khi cần khởi động công cụ không có chữ ký.",
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
                Title = "Firmware đang ở Setup Mode — khoá nền tảng đã bị xoá",
                Detail = "Setup Mode nghĩa là biến PK (Platform Key) rỗng. Khi đó firmware chấp nhận " +
                         "mọi khoá do người dùng nạp vào, và mọi bootloader ký bằng khoá đó đều chạy được. " +
                         "Đây là bước bắt buộc để cài bootkit trên máy UEFI.",
                Recommendation = "Vào BIOS, chọn Restore Factory Keys rồi bật lại Secure Boot. " +
                                 "Nếu máy không cho khôi phục khoá gốc, coi như firmware đã bị thay và không nên nhận máy.",
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
                Title = "Secure Boot đang tắt",
                Detail = "Bản thân việc tắt Secure Boot chưa chứng minh máy bị can thiệp — " +
                         "người dùng Linux hoặc game thủ cài driver cũ vẫn hay tắt. " +
                         "Nhưng nó gỡ bỏ lớp phòng thủ đầu tiên chống bootkit.",
                Recommendation = "Bật lại Secure Boot trong BIOS và kiểm tra máy còn khởi động bình thường không. " +
                                 "Nếu bật xong máy không boot được, có thứ gì đó trong chuỗi khởi động không có chữ ký hợp lệ.",
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
                Title = "Không đọc được khoá nền tảng (PK)",
                Detail = "Biến PK rỗng hoặc không truy cập được. Có thể do thiếu quyền, cũng có thể do khoá đã bị xoá.",
                Recommendation = "Chạy lại ứng dụng với quyền Administrator để loại trừ khả năng thiếu quyền.",
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
                Title = "Danh sách thu hồi chữ ký (dbx) có vẻ cũ",
                Detail = $"Ước tính khoảng {sb.DbxUpdateCount} mục thu hồi. Máy được cập nhật đầy đủ thường có " +
                         "hàng trăm mục sau các đợt vá BlackLotus và bootloader GRUB bị lộ.",
                Recommendation = "Cập nhật Windows đầy đủ và nâng BIOS lên bản mới nhất từ trang chủ hãng.",
                Source = EvidenceSource.UefiVariable,
                ScorePenalty = 5,
                Confidence = 0.4
            };
        }
    }
}
