using HardwareInspector.Collectors.Components;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Firmware;
using HardwareInspector.Firmware.Acpi;
using HardwareInspector.Firmware.Checks;
using HardwareInspector.Firmware.Smbios;
using HardwareInspector.Firmware.Tpm;
using HardwareInspector.Firmware.Uefi;
using Microsoft.Win32;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Firmware;

/// <summary>
/// Điều phối toàn bộ khối kiểm tra firmware: đọc dữ liệu thô một lần,
/// chạy tất cả phép kiểm tra, rồi tổng hợp thành một kết luận duy nhất.
/// </summary>
public sealed class FirmwareIntegrityService : IInfoCollector
{
    public string Name => "Kiểm tra toàn vẹn firmware";
    public int Order => 90;

    // Gắn phân vùng EFI, băm từng file .efi và chạy bcdedit.
    public int Weight => 5;

    private readonly IReadOnlyList<IFirmwareCheck> _checks;

    public FirmwareIntegrityService(IEnumerable<IFirmwareCheck>? checks = null)
    {
        _checks = checks?.ToList() ?? new List<IFirmwareCheck>
        {
            new SecureBootChainCheck(),
            new WpbtCheck(),
            new SmbiosConsistencyCheck(),
            new BiosMetadataCheck(),
            new BootIntegrityCheck(),
            new EspIntegrityCheck(),
            new OemLicenseCheck(),
            new AcpiAnomalyCheck()
        };
    }

    public Task CollectAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        var report = snapshot.Firmware;
        var elevated = BaseboardCollector.IsElevated();
        report.RanWithAdminRights = elevated;

        var smbios = new SmbiosReader();
        smbios.Read();

        var acpi = new AcpiTableScanner();
        acpi.Scan();
        report.AcpiTables.AddRange(acpi.Tables);

        var tpmInspector = new TpmInspector();
        report.Tpm = tpmInspector.Inspect();
        report.DeviceGuard = tpmInspector.InspectDeviceGuard();
        report.SecureBoot = new SecureBootInspector().Inspect();
        report.BootConfig = ReadBootConfiguration();

        var espBinaries = new List<EspBinaryEntry>();
        var espReadable = false;
        if (elevated)
            espBinaries = new EspScanner().Scan(out espReadable);
        report.EspBinaries.AddRange(espBinaries);

        // Nạp chuỗi tên CPU do BIOS công bố vào model, để lớp phân tích đối chiếu
        // được với chuỗi brand lấy từ CPUID.
        var processorBlock = SmbiosDecoder.DecodeProcessor(smbios.First(4));
        if (processorBlock is not null && snapshot.PrimaryCpu is { } primaryCpu)
            primaryCpu.SmbiosProcessorVersion = processorBlock.Version;

        var ctx = new FirmwareContext
        {
            Snapshot = snapshot,
            Smbios = smbios,
            Acpi = acpi,
            SecureBoot = report.SecureBoot,
            Tpm = report.Tpm,
            DeviceGuard = report.DeviceGuard,
            BootConfig = report.BootConfig,
            EspBinaries = espBinaries,
            EspReadable = espReadable,
            IsElevated = elevated
        };

        foreach (var check in _checks)
        {
            ct.ThrowIfCancellationRequested();
            if (check.RequiresAdmin && !elevated) continue;

            try { report.Findings.AddRange(check.Run(ctx)); }
            catch (Exception ex)
            {
                report.Findings.Add(new Finding
                {
                    Code = $"{check.Id}-ERR",
                    Component = ComponentKind.Firmware,
                    Severity = Severity.Info,
                    Title = $"Phép kiểm tra \"{check.Title}\" không hoàn tất",
                    Detail = ex.Message,
                    Confidence = 1.0
                });
            }
        }

        report.Status = Conclude(report, elevated);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Quy tắc kết luận: bằng chứng cứng thắng heuristic.
    /// Khi thiếu quyền Administrator, kết quả tốt nhất chỉ là "chưa đủ dữ liệu"
    /// — không bao giờ khẳng định máy sạch dựa trên một phép kiểm tra bị bỏ qua.
    /// </summary>
    private static IntegrityStatus Conclude(FirmwareIntegrityReport report, bool elevated)
    {
        var actionable = report.Findings.Where(f => f.Severity >= Severity.Notice).ToList();

        var hardEvidence = actionable.Any(f => f.Severity == Severity.Critical && f.Confidence >= 0.8);
        if (hardEvidence) return IntegrityStatus.Compromised;

        var warnings = actionable.Count(f => f.Severity >= Severity.Warning);
        if (warnings >= 2) return IntegrityStatus.Suspicious;
        if (warnings == 1) return IntegrityStatus.Suspicious;

        if (!elevated) return IntegrityStatus.Unverified;

        var notices = actionable.Count(f => f.Severity == Severity.Notice);
        return notices >= 4 ? IntegrityStatus.Suspicious : IntegrityStatus.Clean;
    }

    private static BootConfigurationState ReadBootConfiguration()
    {
        var state = new BootConfigurationState();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control");
            state.SafeBoot = key?.GetValue("SystemStartOptions")?.ToString()
                ?.Contains("SAFEBOOT", StringComparison.OrdinalIgnoreCase) ?? false;

            var options = key?.GetValue("SystemStartOptions")?.ToString() ?? string.Empty;
            state.TestSigningEnabled = options.Contains("TESTSIGNING", StringComparison.OrdinalIgnoreCase);
            state.IntegrityChecksDisabled = options.Contains("NOINTEGRITYCHECKS", StringComparison.OrdinalIgnoreCase)
                                            || options.Contains("DISABLE_INTEGRITY_CHECKS", StringComparison.OrdinalIgnoreCase);
            state.DebuggerEnabled = options.Contains("DEBUG", StringComparison.OrdinalIgnoreCase);
        }
        catch { }

        // Registry không phải lúc nào cũng phản ánh đúng; đối chiếu thêm bằng bcdedit.
        try
        {
            var output = RunProcess("bcdedit", "/enum {current}");
            if (!string.IsNullOrWhiteSpace(output))
            {
                foreach (var line in output.Split('\n'))
                {
                    var l = line.Trim().ToLowerInvariant();
                    if (l.StartsWith("testsigning") && l.Contains("yes")) state.TestSigningEnabled = true;
                    if (l.StartsWith("nointegritychecks") && l.Contains("yes")) state.IntegrityChecksDisabled = true;
                    if (l.StartsWith("debug") && l.Contains("yes")) state.DebuggerEnabled = true;
                }
            }
        }
        catch { }

        return state;
    }

    private static string RunProcess(string file, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(file, args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var p = System.Diagnostics.Process.Start(psi);
        if (p is null) return string.Empty;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(10_000);
        return output;
    }
}
