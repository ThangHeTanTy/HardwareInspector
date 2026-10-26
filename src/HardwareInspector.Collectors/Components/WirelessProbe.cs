using HardwareInspector.Collectors.Wmi;

namespace HardwareInspector.Collectors.Components;

/// <summary>Card mạng không dây Windows thấy và trạng thái kết nối của nó.</summary>
public sealed record WirelessState(string? WifiAdapter, bool WifiEnabled, bool WifiConnected, IReadOnlyList<string> BluetoothDevices);

/// <summary>
/// Kiểm tra máy có card Wi-Fi và Bluetooth hoạt động không. Card Wi-Fi bị tháo, hỏng hay thiếu driver
/// là lỗi hay gặp trên laptop cũ mà nhìn bề ngoài không biết được.
/// </summary>
public static class WirelessProbe
{
    private static readonly string[] WifiMarkers = { "WI-FI", "WIFI", "WIRELESS", "WLAN", "802.11" };

    public static WirelessState Read()
    {
        string? adapter = null;
        bool enabled = false, connected = false;

        foreach (var mo in WmiQuery.Query(
                     "SELECT Name, NetEnabled, NetConnectionStatus FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE"))
        {
            var name = mo.Str("Name");
            if (!WifiMarkers.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase))) continue;
            adapter ??= name;
            enabled |= mo.Bool("NetEnabled");
            connected |= mo.Int("NetConnectionStatus") == 2;   // 2 = Connected
        }

        var bluetooth = WmiQuery.Query("SELECT Name FROM Win32_PnPEntity WHERE PNPClass = 'Bluetooth'")
            .Select(m => m.Str("Name"))
            .Where(n => !string.IsNullOrWhiteSpace(n) &&
                        (n.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) || n.Contains("Radio", StringComparison.OrdinalIgnoreCase)) &&
                        !n.Contains("Enumerator", StringComparison.OrdinalIgnoreCase) &&
                        !n.Contains("RFCOMM", StringComparison.OrdinalIgnoreCase) &&
                        !n.Contains("LE Generic", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .Take(3)
            .ToList();

        return new WirelessState(adapter, enabled, connected, bluetooth);
    }
}
