using HardwareInspector.Analysis.Scoring;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Assessment;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Analysis.Analyzers;

/// <summary>
/// Máy có thật sự thuộc về người bán không. Laptop thanh lý doanh nghiệp thường rẻ và bền,
/// nhưng nếu tổ chức cũ chưa gỡ máy khỏi hệ thống quản lý thì chủ mới có thể bị khoá máy
/// sau lần cài lại Windows đầu tiên, hoặc bị xoá dữ liệu từ xa.
/// </summary>
public sealed class OwnershipAnalyzer : IComponentAnalyzer
{
    public ComponentKind Kind => ComponentKind.Ownership;

    public bool CanAnalyze(SystemSnapshot snapshot) => snapshot.Ownership.Collected;

    public ComponentAssessment Analyze(SystemSnapshot snapshot)
    {
        var o = snapshot.Ownership;
        var card = new ScoreCard(Kind, S("Khoá & quyền sở hữu", "Ownership & locks"));
        string Yes(string? detail = null) => detail is null ? S("Có", "Yes") : S($"Có — {detail}", $"Yes — {detail}");
        var no = S("Không", "No");
        var unknown = S("Không đọc được", "Unreadable");

        card.Metric("Windows Autopilot", o.AutopilotProfilePresent ? Yes(o.AutopilotTenantDomain ?? o.AutopilotTenantId) : no, null,
                o.AutopilotProfilePresent ? Severity.Critical : Severity.Info)
            .Metric(S("Quản lý thiết bị (MDM)", "Device management (MDM)"),
                o.MdmEnrollments.Count > 0 ? Yes(string.Join(", ", o.MdmEnrollments.Select(e => e.Upn ?? e.Provider))) : no, null,
                o.MdmEnrollments.Count > 0 ? Severity.Warning : Severity.Info)
            .Metric(S("Tài khoản tổ chức (Entra ID / Azure AD)", "Organisation account (Entra ID / Azure AD)"),
                o.AzureAdJoined ? Yes(o.AzureAdUser ?? o.AzureAdTenantId) : no)
            .Metric(S("Tham gia domain", "Domain joined"), o.DomainJoined ? Yes(o.DomainName) : no)
            .Metric(S("Quản lý từ xa Intel AMT (vPro)", "Intel AMT remote management (vPro)"), o.IntelAmtActive ? Yes() : no)
            .Metric(S("Chống trộm Absolute / Computrace", "Absolute / Computrace anti-theft"), o.AbsoluteAgentFound ? Yes() : no)
            .Metric(S("Ổ mã hoá BitLocker", "BitLocker-encrypted drives"),
                o.BitLockerProtectedVolumes is null ? unknown
                : o.BitLockerProtectedVolumes.Count == 0 ? no : string.Join(", ", o.BitLockerProtectedVolumes))
            .Metric(S("Mật khẩu BIOS", "BIOS password"),
                o.BiosPasswordSet is null ? unknown : o.BiosPasswordSet.Value ? S("Đã đặt", "Set") : S("Chưa đặt", "Not set"));

        if (o.AutopilotProfilePresent)
            card.Add("OWN-AP-001", Kind, Severity.Critical,
                S($"Máy đã đăng ký Windows Autopilot của tổ chức {o.AutopilotTenantDomain ?? o.AutopilotTenantId}",
                  $"The machine is registered to Windows Autopilot for {o.AutopilotTenantDomain ?? o.AutopilotTenantId}"),
                S("Máy vẫn chạy bình thường lúc này, nhưng chỉ cần cài lại hoặc reset Windows là màn hình cài đặt sẽ bắt " +
                  "đăng nhập tài khoản của tổ chức trên. Không có tài khoản đó thì không dùng được máy. Đây là lý do phổ biến " +
                  "khiến laptop thanh lý doanh nghiệp bị \"khoá\" sau khi mua.",
                  "The machine works for now, but after any Windows reinstall or reset the setup screen will demand " +
                  "an account from the organisation above. Without it the machine is unusable. This is the most common reason " +
                  "ex-corporate laptops get \"locked\" after purchase."),
                S("Chỉ bộ phận IT của tổ chức đó gỡ được máy khỏi Autopilot. Yêu cầu người bán làm việc này rồi chạy lại ứng dụng " +
                  "để xác nhận trước khi trả tiền. Không gỡ được thì không mua.",
                  "Only that organisation's IT department can remove the machine from Autopilot. Ask the seller to arrange it, then " +
                  "rerun this app to confirm before paying. If it cannot be removed, do not buy."),
                penalty: 60, confidence: 0.9, source: EvidenceSource.Registry);

        if (o.MdmEnrollments.Count > 0)
            card.Add("OWN-MDM-001", Kind, o.AutopilotProfilePresent ? Severity.Critical : Severity.Warning,
                S("Máy đang chịu sự quản lý từ xa của một tổ chức (MDM)", "The machine is remotely managed by an organisation (MDM)"),
                S($"Đăng ký: {string.Join(", ", o.MdmEnrollments.Select(e => e.Upn is null ? e.Provider : $"{e.Provider} ({e.Upn})"))}. " +
                  "Quản trị viên của tổ chức có thể khoá, xoá sạch hoặc cài phần mềm lên máy từ xa bất cứ lúc nào.",
                  $"Enrolment: {string.Join(", ", o.MdmEnrollments.Select(e => e.Upn is null ? e.Provider : $"{e.Provider} ({e.Upn})"))}. " +
                  "The organisation's admins can lock, wipe or push software to the machine remotely at any time."),
                S("Yêu cầu người bán gỡ máy khỏi hệ thống quản lý của tổ chức (Cài đặt → Tài khoản → Truy cập cơ quan hoặc trường học → Ngắt kết nối) " +
                  "và xác nhận với IT rằng máy đã bị xoá khỏi danh sách thiết bị.",
                  "Ask the seller to remove the machine from the organisation's management (Settings → Accounts → Access work or school → Disconnect) " +
                  "and confirm with IT that it was deleted from their device list."),
                penalty: 35, confidence: 0.85, source: EvidenceSource.Registry);

        if (o.AzureAdJoined && o.MdmEnrollments.Count == 0 && !o.AutopilotProfilePresent)
            card.Add("OWN-AAD-001", Kind, Severity.Warning,
                S("Windows đang đăng nhập bằng tài khoản của một tổ chức", "Windows is joined to an organisation account"),
                S($"Máy tham gia Entra ID (Azure AD){(o.AzureAdUser is null ? string.Empty : $" với tài khoản {o.AzureAdUser}")}. " +
                  "Máy cá nhân thường không ở trạng thái này; đây thường là máy công ty.",
                  $"The machine is joined to Entra ID (Azure AD){(o.AzureAdUser is null ? string.Empty : $" as {o.AzureAdUser}")}. " +
                  "Personal machines are not normally in this state; it is usually a company machine."),
                S("Hỏi rõ nguồn gốc máy. Sau khi mua, cài lại Windows sạch và đăng nhập bằng tài khoản cá nhân.",
                  "Ask where the machine came from. After buying, clean-install Windows and sign in with a personal account."),
                penalty: 20, confidence: 0.8, source: EvidenceSource.Registry);

        if (o.DomainJoined)
            card.Add("OWN-DOM-001", Kind, Severity.Notice,
                S($"Máy đang tham gia domain {o.DomainName}", $"The machine is joined to the {o.DomainName} domain"),
                S("Đây là máy công ty. Domain tự mất khi cài lại Windows, nhưng hãy hỏi rõ máy thanh lý hợp pháp hay không.",
                  "This is a company machine. Domain membership disappears on reinstall, but ask whether it was legitimately disposed of."),
                penalty: 8, confidence: 0.9, source: EvidenceSource.Wmi);

        if (o.IntelAmtActive)
            card.Add("OWN-AMT-001", Kind, Severity.Warning,
                S("Intel AMT (vPro) đang bật — máy điều khiển từ xa được ở mức phần cứng", "Intel AMT (vPro) is on — the machine can be controlled remotely at hardware level"),
                S("AMT cho phép bật, tắt, xem màn hình và cài lại máy qua mạng kể cả khi Windows đã bị xoá. " +
                  "Nếu tổ chức cũ chưa tắt, họ (hoặc kẻ biết mật khẩu MEBx) vẫn có quyền này.",
                  "AMT allows powering on/off, viewing the screen and reinstalling over the network even with Windows wiped. " +
                  "If the previous organisation did not turn it off, they (or anyone with the MEBx password) still can."),
                S("Vào BIOS hoặc màn hình MEBx (thường bấm Ctrl+P lúc khởi động) để tắt AMT. Cần mật khẩu MEBx — yêu cầu người bán cung cấp.",
                  "Turn AMT off in the BIOS or the MEBx screen (usually Ctrl+P at boot). It needs the MEBx password — ask the seller for it."),
                penalty: 12, confidence: 0.75, source: EvidenceSource.Wmi);

        if (o.AbsoluteAgentFound)
            card.Add("OWN-ABS-001", Kind, Severity.Warning,
                S("Có agent chống trộm Absolute / Computrace", "Absolute / Computrace anti-theft agent present"),
                S("Agent này được firmware tự cài lại sau mỗi lần cài Windows. Nếu máy từng được báo mất trên hệ thống Absolute, " +
                  "chủ cũ hoặc tổ chức có thể khoá máy từ xa dù bạn đã cài lại sạch.",
                  "The firmware reinstalls this agent after every Windows install. If the machine was ever reported stolen in Absolute, " +
                  "the previous owner or organisation can lock it remotely even after a clean install."),
                S("Yêu cầu người bán xác nhận máy đã được gỡ khỏi tài khoản Absolute của họ, và kiểm tra trong BIOS mục Computrace/Absolute đang ở trạng thái Disabled.",
                  "Ask the seller to confirm the machine was removed from their Absolute account, and check in the BIOS that Computrace/Absolute is Disabled."),
                penalty: 25, confidence: 0.7, source: EvidenceSource.FileSystem);

        if (o.BitLockerProtectedVolumes is { Count: > 0 } locked)
            card.Add("OWN-BL-001", Kind, Severity.Notice,
                S($"Ổ {string.Join(", ", locked)} đang được mã hoá BitLocker", $"Drive(s) {string.Join(", ", locked)} are BitLocker-encrypted"),
                S("Không có khoá khôi phục thì dữ liệu trên ổ sẽ mất nếu bo mạch, TPM hoặc BIOS thay đổi. " +
                  "Với máy công ty, khoá này thường nằm trong tài khoản tổ chức.",
                  "Without the recovery key, data on the drive is lost if the board, TPM or BIOS changes. " +
                  "On company machines the key usually lives in the organisation's account."),
                S("Nếu không cần dữ liệu cũ, cài lại Windows sạch sau khi mua. Nếu cần giữ, yêu cầu người bán đưa khoá khôi phục.",
                  "If you do not need the old data, clean-install Windows after buying. If you do, ask the seller for the recovery key."),
                penalty: 4, confidence: 0.9, source: EvidenceSource.Wmi);

        if (o.BiosPasswordSet == true)
            card.Add("OWN-PWD-001", Kind, Severity.Warning,
                S("BIOS đang đặt mật khẩu", "A BIOS password is set"),
                S("Không có mật khẩu thì không vào được BIOS để bật Secure Boot, đổi thứ tự khởi động hay cài lại máy từ USB. " +
                  "Trên laptop doanh nghiệp, quên mật khẩu supervisor thường phải thay chip hoặc bo mạch.",
                  "Without the password you cannot enter the BIOS to enable Secure Boot, change the boot order or reinstall from USB. " +
                  "On business laptops a lost supervisor password often means replacing a chip or the board."),
                S("Yêu cầu người bán gỡ mật khẩu ngay tại chỗ, rồi tự vào BIOS thử để chắc chắn.",
                  "Ask the seller to remove the password on the spot, then enter the BIOS yourself to make sure."),
                penalty: 20, confidence: 0.85, source: EvidenceSource.Wmi);

        if (!o.Elevated)
            card.Add("OWN-PRIV-001", Kind, Severity.Notice,
                S("Thiếu quyền Administrator nên chưa kiểm tra đủ", "Not all checks ran without Administrator rights"),
                S("BitLocker và mật khẩu BIOS chỉ đọc được khi chạy với quyền Administrator.",
                  "BitLocker and the BIOS password can only be read with Administrator rights."),
                penalty: 3, confidence: 1.0);

        var clean = !o.AutopilotProfilePresent && o.MdmEnrollments.Count == 0 && !o.AzureAdJoined &&
                    !o.IntelAmtActive && !o.AbsoluteAgentFound && o.BiosPasswordSet != true;
        card.Headline(clean
            ? S("Không thấy dấu hiệu máy thuộc tổ chức khác hay bị khoá", "No sign the machine belongs to an organisation or is locked")
            : S("Máy có dấu hiệu thuộc quyền quản lý của tổ chức khác — xem phát hiện bên dưới",
                "The machine shows signs of being managed by an organisation — see the findings below"));

        return card.Build();
    }
}
