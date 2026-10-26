using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Core.Models.Components;

public sealed class BaseboardInfo
{
    public string SystemManufacturer { get; set; } = string.Empty;
    public string SystemProductName { get; set; } = string.Empty;
    public string SystemSerialNumber { get; set; } = string.Empty;
    public string SystemSku { get; set; } = string.Empty;
    public string SystemFamily { get; set; } = string.Empty;
    public string SystemUuid { get; set; } = string.Empty;

    public string BoardManufacturer { get; set; } = string.Empty;
    public string BoardProduct { get; set; } = string.Empty;
    public string BoardSerialNumber { get; set; } = string.Empty;
    public string BoardVersion { get; set; } = string.Empty;

    public string ChassisType { get; set; } = string.Empty;
    public string ChassisSerialNumber { get; set; } = string.Empty;
    public string ChassisAssetTag { get; set; } = string.Empty;

    /// <summary>Trạng thái cảm biến mở vỏ máy (SMBIOS Type 3). "Safe" nếu chưa từng bị mở.</summary>
    /// <summary>Mã SecurityStatus của Win32_SystemEnclosure; -1 khi không đọc được.</summary>
    public int ChassisSecurityCode { get; set; } = -1;

    public string ChassisSecurityStatus => ChassisSecurityCode switch
    {
        1 => S("Khác", "Other"),
        3 => S("An toàn (chưa ghi nhận mở vỏ)", "Safe (no intrusion recorded)"),
        4 => S("Có khoá ngoài", "External interface locked out"),
        5 => S("Đã bị mở vỏ / khoá bị vô hiệu", "Opened / lock disabled"),
        _ => S("Không xác định", "Unknown")
    };
}

public sealed class BiosInfo
{
    public string Vendor { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? EcFirmwareVersion { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string SmbiosVersion { get; set; } = string.Empty;
    public bool IsUefi { get; set; }
    public long RomSizeBytes { get; set; }
    public List<string> Characteristics { get; } = new();
}

public sealed class OperatingSystemInfo
{
    public string Caption { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string BuildNumber { get; set; } = string.Empty;
    public DateTime? InstallDate { get; set; }
    public DateTime? LastBootUpTime { get; set; }
    public string Architecture { get; set; } = string.Empty;
    public string LicenseChannel { get; set; } = string.Empty; // OEM_DM / Retail / Volume
    public bool IsActivated { get; set; }
    public bool IsAdminSession { get; set; }
    public TimeSpan Uptime => LastBootUpTime.HasValue ? DateTime.Now - LastBootUpTime.Value : TimeSpan.Zero;
}
