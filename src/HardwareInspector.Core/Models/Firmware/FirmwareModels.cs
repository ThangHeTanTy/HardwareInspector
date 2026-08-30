namespace HardwareInspector.Core.Models.Firmware;

/// <summary>Một cấu trúc SMBIOS thô đã được tách header + chuỗi.</summary>
public sealed class SmbiosStructure
{
    public byte Type { get; init; }
    public byte Length { get; init; }
    public ushort Handle { get; init; }
    public byte[] Data { get; init; } = Array.Empty<byte>();
    public List<string> Strings { get; } = new();

    public string GetString(int index) =>
        index > 0 && index <= Strings.Count ? Strings[index - 1] : string.Empty;
}

public sealed record AcpiTableInfo(string Signature, int Length, string? OemId, string? OemTableId)
{
    /// <summary>Các bảng ACPI cần soi kỹ khi truy vết can thiệp firmware.</summary>
    public bool IsHighInterest => Signature is "WPBT" or "MSDM" or "SLIC" or "TPM2" or "TCPA" or "WSMT" or "DMAR";
}

public sealed class SecureBootState
{
    public bool? Enabled { get; set; }
    public bool? SetupMode { get; set; }          // true = chưa nạp khoá PK -> ai cũng ký được
    public bool? DeployedMode { get; set; }
    public bool? PlatformKeyPresent { get; set; }
    public int? DbxUpdateCount { get; set; }
    public DateTime? DbxLastUpdate { get; set; }
    public string? VendorKeysState { get; set; }
    public bool BootFromUefi { get; set; }
}

public sealed class TpmState
{
    public bool IsPresent { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsActivated { get; set; }
    public bool IsOwned { get; set; }
    public string SpecVersion { get; set; } = string.Empty;
    public string ManufacturerId { get; set; } = string.Empty;
    public string ManufacturerVersion { get; set; } = string.Empty;
    public bool MeasuredBootLogPresent { get; set; }
}

public sealed class DeviceGuardState
{
    public bool VirtualizationBasedSecurityRunning { get; set; }
    public bool HypervisorEnforcedCodeIntegrity { get; set; }
    public bool SecureLaunchRunning { get; set; }     // System Guard Secure Launch (DRTM)
    public bool SystemGuardCapable { get; set; }
    public bool KernelDmaProtection { get; set; }
}

public sealed class BootConfigurationState
{
    public bool TestSigningEnabled { get; set; }
    public bool IntegrityChecksDisabled { get; set; }
    public bool DebuggerEnabled { get; set; }
    public bool SafeBoot { get; set; }
    public string? BootOrder { get; set; }
    public List<string> BootEntries { get; } = new();
}

public sealed class EspBinaryEntry
{
    public string Path { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime LastWriteUtc { get; set; }
    public string? Sha256 { get; set; }
    public bool IsKnownVendorPath { get; set; }
    public string? SignatureSubject { get; set; }
    public bool SignatureValid { get; set; }
}

/// <summary>Kết quả tổng hợp của toàn bộ khối kiểm tra firmware/BIOS.</summary>
public sealed class FirmwareIntegrityReport
{
    public IntegrityStatus Status { get; set; } = IntegrityStatus.Unknown;
    public SecureBootState SecureBoot { get; set; } = new();
    public TpmState Tpm { get; set; } = new();
    public DeviceGuardState DeviceGuard { get; set; } = new();
    public BootConfigurationState BootConfig { get; set; } = new();
    public List<AcpiTableInfo> AcpiTables { get; } = new();
    public List<EspBinaryEntry> EspBinaries { get; } = new();
    public List<Finding> Findings { get; } = new();
    public bool RanWithAdminRights { get; set; }
}
