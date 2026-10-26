namespace HardwareInspector.Core.Models.Components;

/// <summary>Một lần đăng ký vào hệ thống quản lý thiết bị (MDM) như Intune.</summary>
public sealed record MdmEnrollment(string Provider, string? Upn);

/// <summary>
/// Dữ liệu thô về việc máy có thuộc quyền quản lý của một tổ chức hay không.
///
/// Đây là rủi ro lớn nhất với laptop thanh lý doanh nghiệp mà người mua ít kinh nghiệm
/// không biết: máy chạy bình thường lúc xem, nhưng cài lại Windows xong thì tự khoá
/// và đòi đăng nhập tài khoản công ty (Autopilot), hoặc bị quản trị viên xoá/khoá từ xa (MDM).
/// Collector chỉ ghi lại dữ liệu; câu chữ do analyzer sinh ra để đổi được ngôn ngữ.
/// </summary>
public sealed class OwnershipInfo
{
    /// <summary>Đã chạy collector chưa. False thì analyzer bỏ qua hạng mục.</summary>
    public bool Collected { get; set; }

    /// <summary>Có quyền Administrator khi đọc — thiếu quyền thì BitLocker và mật khẩu BIOS không đọc được.</summary>
    public bool Elevated { get; set; }

    // --- Windows Autopilot: máy đã nhận hồ sơ triển khai của một tổ chức ---
    public string? AutopilotTenantDomain { get; set; }
    public string? AutopilotTenantId { get; set; }
    public bool AutopilotProfilePresent => !string.IsNullOrWhiteSpace(AutopilotTenantDomain) || !string.IsNullOrWhiteSpace(AutopilotTenantId);

    // --- Đăng ký quản lý thiết bị ---
    public List<MdmEnrollment> MdmEnrollments { get; } = new();

    // --- Tham gia thư mục của tổ chức ---
    public bool AzureAdJoined { get; set; }
    public string? AzureAdTenantId { get; set; }
    public string? AzureAdUser { get; set; }
    public bool DomainJoined { get; set; }
    public string? DomainName { get; set; }

    // --- Quản lý từ xa ở mức phần cứng và phần mềm chống trộm ---
    /// <summary>Thiết bị Intel AMT (vPro) đang hiện trong Device Manager — AMT đã được bật.</summary>
    public bool IntelAmtActive { get; set; }

    /// <summary>Dấu vết agent Absolute/Computrace (rpcnet) — loại chống trộm sống trong firmware.</summary>
    public bool AbsoluteAgentFound { get; set; }

    // --- Khoá dữ liệu và khoá BIOS ---
    /// <summary>Các ổ đang được BitLocker bảo vệ, ví dụ "C:". Null khi không đọc được.</summary>
    public List<string>? BitLockerProtectedVolumes { get; set; }

    /// <summary>Mật khẩu BIOS/Setup đã đặt hay chưa. Null khi hãng không cho đọc qua WMI.</summary>
    public bool? BiosPasswordSet { get; set; }
}
