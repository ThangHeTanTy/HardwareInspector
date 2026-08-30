using HardwareInspector.Collectors.Wmi;
using HardwareInspector.Core.Models.Firmware;

namespace HardwareInspector.Firmware.Tpm;

public sealed class TpmInspector
{
    private const string MeasuredBootLogPath = @"C:\Windows\Logs\MeasuredBoot";

    public TpmState Inspect()
    {
        var state = new TpmState();

        var tpm = WmiQuery.QuerySingle("SELECT * FROM Win32_Tpm", WmiQuery.TpmNamespace);
        if (tpm is not null)
        {
            state.IsPresent = true;
            state.IsEnabled = tpm.Bool("IsEnabled_InitialValue");
            state.IsActivated = tpm.Bool("IsActivated_InitialValue");
            state.IsOwned = tpm.Bool("IsOwned_InitialValue");
            state.SpecVersion = tpm.Str("SpecVersion");
            state.ManufacturerId = tpm.Str("ManufacturerIdTxt");
            if (string.IsNullOrWhiteSpace(state.ManufacturerId))
                state.ManufacturerId = tpm.Str("ManufacturerId");
            state.ManufacturerVersion = tpm.Str("ManufacturerVersion");
        }

        try
        {
            state.MeasuredBootLogPresent = Directory.Exists(MeasuredBootLogPath) &&
                                           Directory.EnumerateFiles(MeasuredBootLogPath, "*.log").Any();
        }
        catch { }

        return state;
    }

    public DeviceGuardState InspectDeviceGuard()
    {
        var dg = new DeviceGuardState();
        var mo = WmiQuery.QuerySingle(
            "SELECT * FROM Win32_DeviceGuard", @"root\Microsoft\Windows\DeviceGuard");
        if (mo is null) return dg;

        int[] Read(string prop)
        {
            try { return mo[prop] as int[] ?? Array.Empty<int>(); }
            catch { return Array.Empty<int>(); }
        }

        var running = Read("SecurityServicesRunning");
        var configured = Read("SecurityServicesConfigured");
        var available = Read("AvailableSecurityProperties");

        dg.VirtualizationBasedSecurityRunning = mo.Int("VirtualizationBasedSecurityStatus") == 2;
        dg.HypervisorEnforcedCodeIntegrity = running.Contains(2) || configured.Contains(2);
        dg.SecureLaunchRunning = running.Contains(7);
        dg.SystemGuardCapable = available.Contains(7);
        dg.KernelDmaProtection = available.Contains(6);

        return dg;
    }
}
