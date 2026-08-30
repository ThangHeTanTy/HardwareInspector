using HardwareInspector.Collectors.Wmi;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Components;
using Microsoft.Win32;

namespace HardwareInspector.Collectors.Components;

public sealed class GraphicsCollector : IInfoCollector
{
    public string Name => "Card đồ họa";
    public int Order => 20;

    public Task CollectAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        foreach (var mo in WmiQuery.Query(
            "SELECT Name, AdapterCompatibility, AdapterRAM, DriverVersion, DriverDate, PNPDeviceID, " +
            "VideoModeDescription, CurrentRefreshRate, VideoProcessor FROM Win32_VideoController"))
        {
            ct.ThrowIfCancellationRequested();
            var gpu = new GraphicsInfo
            {
                Name = mo.Str("Name"),
                Vendor = mo.Str("AdapterCompatibility"),
                DriverVersion = mo.Str("DriverVersion"),
                DriverDate = mo.Date("DriverDate"),
                PnpDeviceId = mo.Str("PNPDeviceID"),
                CurrentResolution = mo.Str("VideoModeDescription"),
                CurrentRefreshHz = mo.Int("CurrentRefreshRate")
            };

            // Win32_VideoController.AdapterRAM là uint32 nên tràn ở mốc 4 GB.
            // Nguồn chính xác hơn nằm trong registry của driver.
            gpu.VideoMemoryBytes = ReadDedicatedVideoMemory(gpu.PnpDeviceId) is { } v && v > 0
                ? v
                : mo.Long("AdapterRAM");

            gpu.IsDiscrete = IsDiscrete(gpu.Vendor, gpu.Name);
            gpu.VideoBiosVersion = ReadVideoBios(gpu.PnpDeviceId);

            snapshot.GraphicsAdapters.Add(gpu);
        }

        return Task.CompletedTask;
    }

    private static bool IsDiscrete(string vendor, string name)
    {
        var v = (vendor + " " + name).ToUpperInvariant();
        if (v.Contains("NVIDIA") || v.Contains("GEFORCE") || v.Contains("QUADRO") || v.Contains("RTX") || v.Contains("GTX"))
            return true;
        if (v.Contains("RADEON") && !v.Contains("VEGA GRAPHICS") && !v.Contains("GRAPHICS PROCESSOR"))
            return true;
        if (v.Contains("ARC A")) return true;
        return false;
    }

    /// <summary>
    /// Đọc HKLM\SYSTEM\...\Class\{4d36e968...}\000X\HardwareInformation.qwMemorySize.
    /// Chính xác hơn AdapterRAM cho card trên 4 GB.
    /// </summary>
    private static long? ReadDedicatedVideoMemory(string? pnpDeviceId)
    {
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (classKey is null) return null;

            foreach (var sub in classKey.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
            {
                using var k = classKey.OpenSubKey(sub);
                if (k is null) continue;
                var matchId = k.GetValue("MatchingDeviceId")?.ToString();
                if (pnpDeviceId is not null && matchId is not null &&
                    !pnpDeviceId.Replace("\\", "").Contains(matchId.Replace("\\", "").Split('&')[0],
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                var qw = k.GetValue("HardwareInformation.qwMemorySize");
                if (qw is long l && l > 0) return l;
                if (k.GetValue("HardwareInformation.MemorySize") is byte[] b && b.Length >= 4)
                    return BitConverter.ToUInt32(b, 0);
            }
        }
        catch { }
        return null;
    }

    private static string? ReadVideoBios(string? pnpDeviceId)
    {
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (classKey is null) return null;

            foreach (var sub in classKey.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
            {
                using var k = classKey.OpenSubKey(sub);
                var bios = k?.GetValue("HardwareInformation.BiosString")?.ToString();
                if (!string.IsNullOrWhiteSpace(bios)) return bios;
            }
        }
        catch { }
        return null;
    }
}
