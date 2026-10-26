using HardwareInspector.Collectors.Wmi;
using Microsoft.Win32;

namespace HardwareInspector.Collectors.Components;

/// <summary>Một thiết bị USB đang cắm, kèm vị trí cổng Windows ghi nhận (ví dụ "Port_#0002.Hub_#0003").</summary>
public sealed record UsbDevice(string InstanceId, string Name, string? Location);

/// <summary>
/// Liệt kê thiết bị USB cấp cổng (không lấy các giao diện con "&amp;MI_" hay hub) để bài thử cổng
/// so danh sách trước và sau khi người dùng cắm USB vào từng cổng.
/// </summary>
public static class UsbProbe
{
    public static IReadOnlyList<UsbDevice> List()
    {
        var result = new List<UsbDevice>();
        foreach (var mo in WmiQuery.Query("SELECT PNPDeviceID, Name FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'USB\\\\VID%'"))
        {
            var id = mo.Str("PNPDeviceID");
            var name = mo.Str("Name");
            if (string.IsNullOrEmpty(id) || id.Contains("&MI_", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Contains("Hub", StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(new UsbDevice(id, string.IsNullOrWhiteSpace(name) ? id : name, ReadLocation(id)));
        }
        return result;
    }

    private static string? ReadLocation(string instanceId)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\{instanceId}");
            var value = key?.GetValue("LocationInformation")?.ToString()?.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch { return null; }
    }
}
