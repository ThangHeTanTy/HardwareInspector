using HardwareInspector.Collectors.Wmi;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Components;
using Microsoft.Win32;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Collectors.Components;

/// <summary>
/// Tìm dấu vết máy đang thuộc quyền quản lý của một tổ chức.
///
/// Mỗi nguồn đọc độc lập và đều bọc try: máy cá nhân thiếu hẳn các khoá registry này,
/// và đó chính là kết quả mong muốn chứ không phải lỗi.
/// </summary>
public sealed class OwnershipCollector : IInfoCollector
{
    public string Name => S("Khoá doanh nghiệp và quyền sở hữu", "Enterprise locks and ownership");
    public int Order => 55;

    public Task CollectAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        var o = snapshot.Ownership;
        o.Elevated = BaseboardCollector.IsElevated();

        Try(() => ReadAutopilot(o));
        Try(() => ReadMdm(o));
        Try(() => ReadAzureAd(o));
        Try(() => ReadDomain(o));
        Try(() => ReadAmt(o));
        Try(() => ReadAbsolute(o));
        Try(() => ReadBitLocker(o));
        Try(() => ReadBiosPassword(o, snapshot.Baseboard.SystemManufacturer));

        o.Collected = true;
        return Task.CompletedTask;
    }

    private static void Try(Action action)
    {
        try { action(); } catch { }
    }

    /// <summary>
    /// Hồ sơ Autopilot được Windows tải về và lưu lại ngay khi máy kết nối mạng lần đầu.
    /// Có tên miền tổ chức ở đây nghĩa là sau khi cài lại, màn hình cài đặt sẽ đòi tài khoản của tổ chức đó.
    /// </summary>
    private static void ReadAutopilot(OwnershipInfo o)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Provisioning\Diagnostics\Autopilot");
        if (key is null) return;
        o.AutopilotTenantDomain = Clean(key.GetValue("CloudAssignedTenantDomain"));
        o.AutopilotTenantId = Clean(key.GetValue("CloudAssignedTenantId"));
    }

    private static void ReadMdm(OwnershipInfo o)
    {
        using var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Enrollments");
        if (root is null) return;

        foreach (var name in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(name);
            var provider = Clean(key?.GetValue("ProviderID"));
            if (provider is null) continue;

            // EnrollmentState 1 = đang đăng ký. Mục cũ đã huỷ còn sót lại thì bỏ qua.
            if (key!.GetValue("EnrollmentState") is int state && state != 1) continue;

            o.MdmEnrollments.Add(new MdmEnrollment(provider, Clean(key.GetValue("UPN"))));
        }
    }

    private static void ReadAzureAd(OwnershipInfo o)
    {
        using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo");
        if (root is null) return;

        foreach (var name in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(name);
            if (key is null) continue;
            o.AzureAdJoined = true;
            o.AzureAdTenantId ??= Clean(key.GetValue("TenantId"));
            o.AzureAdUser ??= Clean(key.GetValue("UserEmail"));
        }
    }

    private static void ReadDomain(OwnershipInfo o)
    {
        var cs = WmiQuery.QuerySingle("SELECT PartOfDomain, Domain FROM Win32_ComputerSystem");
        if (cs is null) return;
        o.DomainJoined = cs.Bool("PartOfDomain");
        if (o.DomainJoined) o.DomainName = Clean(cs.Str("Domain"));
    }

    /// <summary>Thiết bị "Active Management Technology - SOL" chỉ xuất hiện khi AMT đã được bật trong firmware.</summary>
    private static void ReadAmt(OwnershipInfo o)
    {
        o.IntelAmtActive = WmiQuery.Query(
                "SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%Active Management Technology%'")
            .Any();
    }

    /// <summary>
    /// Absolute (Computrace) cài agent rpcnet. Agent này được firmware tự cài lại sau mỗi lần cài
    /// Windows, nên máy đã báo mất qua Absolute có thể bị khoá dù chủ mới đã cài sạch.
    /// </summary>
    private static void ReadAbsolute(OwnershipInfo o)
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var systemX86 = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);

        o.AbsoluteAgentFound =
            File.Exists(Path.Combine(system, "rpcnet.exe")) ||
            (!string.IsNullOrEmpty(systemX86) && File.Exists(Path.Combine(systemX86, "rpcnet.exe"))) ||
            WmiQuery.Query("SELECT Name FROM Win32_Service WHERE Name = 'rpcnet'").Any();
    }

    private static void ReadBitLocker(OwnershipInfo o)
    {
        var volumes = WmiQuery.Query(
                "SELECT DriveLetter, ProtectionStatus FROM Win32_EncryptableVolume",
                @"root\CIMV2\Security\MicrosoftVolumeEncryption")
            .ToList();
        if (volumes.Count == 0 && !o.Elevated) return;   // thiếu quyền: để null = không rõ

        o.BitLockerProtectedVolumes = volumes
            .Where(v => v.Int("ProtectionStatus") == 1)
            .Select(v => v.Str("DriveLetter"))
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .ToList();
    }

    /// <summary>
    /// Ba hãng laptop doanh nghiệp lớn đều cho đọc trạng thái mật khẩu BIOS qua WMI.
    /// Máy có mật khẩu Setup mà người bán không đưa thì không vào được BIOS để bật lại
    /// Secure Boot, đổi thứ tự khởi động hay tắt AMT.
    /// </summary>
    private static void ReadBiosPassword(OwnershipInfo o, string manufacturer)
    {
        var m = manufacturer.ToLowerInvariant();

        if (m.Contains("lenovo"))
        {
            var s = WmiQuery.QuerySingle("SELECT PasswordState FROM Lenovo_BiosPasswordSettings", WmiQuery.WmiNamespace);
            if (s is not null) o.BiosPasswordSet = s.Int("PasswordState") != 0;
        }
        else if (m.Contains("hp") || m.Contains("hewlett"))
        {
            var s = WmiQuery.QuerySingle(
                "SELECT IsSet FROM HP_BIOSPassword WHERE Name = 'Setup Password'", @"root\HP\InstrumentedBIOS");
            if (s is not null) o.BiosPasswordSet = s.Int("IsSet") != 0;
        }
        else if (m.Contains("dell"))
        {
            var s = WmiQuery.QuerySingle(
                "SELECT IsPasswordSet FROM PasswordObject WHERE NameId = 'Admin'", @"root\dcim\sysman\wmisecurity");
            if (s is not null) o.BiosPasswordSet = s.Int("IsPasswordSet") != 0;
        }
    }

    private static string? Clean(object? value)
    {
        var s = value?.ToString()?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }
}
