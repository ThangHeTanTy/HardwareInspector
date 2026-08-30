using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Firmware;
using HardwareInspector.Firmware.Acpi;
using HardwareInspector.Firmware.Smbios;

namespace HardwareInspector.Firmware.Checks;

/// <summary>
/// Gói toàn bộ dữ liệu firmware thô để các phép kiểm tra dùng chung,
/// tránh đọc lại bảng SMBIOS/ACPI nhiều lần.
/// </summary>
public sealed class FirmwareContext
{
    public required SystemSnapshot Snapshot { get; init; }
    public required SmbiosReader Smbios { get; init; }
    public required AcpiTableScanner Acpi { get; init; }
    public required SecureBootState SecureBoot { get; init; }
    public required TpmState Tpm { get; init; }
    public required DeviceGuardState DeviceGuard { get; init; }
    public required BootConfigurationState BootConfig { get; init; }
    public required IReadOnlyList<EspBinaryEntry> EspBinaries { get; init; }
    public required bool EspReadable { get; init; }
    public required bool IsElevated { get; init; }
}

/// <summary>Một phép kiểm tra firmware. Mỗi lớp con trả lời đúng một câu hỏi.</summary>
public interface IFirmwareCheck
{
    string Id { get; }
    string Title { get; }
    bool RequiresAdmin { get; }
    IEnumerable<Finding> Run(FirmwareContext ctx);
}
