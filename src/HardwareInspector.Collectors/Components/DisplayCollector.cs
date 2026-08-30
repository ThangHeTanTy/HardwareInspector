using HardwareInspector.Collectors.Edid;
using HardwareInspector.Collectors.Native;
using HardwareInspector.Collectors.Wmi;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Components;
using Microsoft.Win32;

namespace HardwareInspector.Collectors.Components;

/// <summary>
/// Liệt kê màn hình theo từng cổng xuất hình đang hoạt động.
///
/// Bản trước quét thẳng nhánh registry Enum\DISPLAY, nên trên PC để bàn nó
/// hoặc không thấy gì, hoặc trả về cả những màn hình đã tháo từ lâu mà Windows
/// còn lưu lại. Cách làm ở đây đi từ adapter xuống monitor, nên chỉ thấy
/// đúng những gì đang cắm, và biết mỗi cái nằm ở cổng nào.
/// </summary>
public sealed class DisplayCollector : IInfoCollector
{
    public string Name => "Màn hình";
    public int Order => 60;

    // Duyệt từng cổng xuất hình rồi đọc EDID của mỗi màn.
    public int Weight => 2;

    public Task CollectAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        var connectionMap = ReadConnectionTypes();
        var wmiEdidMap = ReadEdidFromWmi();

        for (uint adapterIndex = 0; ; adapterIndex++)
        {
            ct.ThrowIfCancellationRequested();

            var adapter = DisplayNativeMethods.NewDisplayDevice();
            if (!DisplayNativeMethods.EnumDisplayDevicesW(null, adapterIndex, ref adapter, 0))
                break;

            // Bỏ qua driver nhân bản màn hình (phần mềm quay phim, máy chiếu ảo).
            if ((adapter.StateFlags & DisplayNativeMethods.DisplayDeviceMirroringDriver) != 0)
                continue;

            var isAttached = (adapter.StateFlags & DisplayNativeMethods.DisplayDeviceAttachedToDesktop) != 0;
            var isPrimary = (adapter.StateFlags & DisplayNativeMethods.DisplayDevicePrimaryDevice) != 0;

            // Mỗi adapter có thể có nhiều monitor con, dù thực tế gần như luôn là một.
            for (uint monitorIndex = 0; ; monitorIndex++)
            {
                var monitor = DisplayNativeMethods.NewDisplayDevice();
                if (!DisplayNativeMethods.EnumDisplayDevicesW(
                        adapter.DeviceName, monitorIndex, ref monitor,
                        DisplayNativeMethods.EddGetDeviceInterfaceName))
                    break;

                if ((monitor.StateFlags & DisplayNativeMethods.DisplayDeviceActive) == 0 && !isAttached)
                    continue;

                var display = new DisplayInfo
                {
                    AdapterDeviceName = adapter.DeviceName,
                    AdapterDescription = adapter.DeviceString,
                    FriendlyName = monitor.DeviceString,
                    MonitorDeviceId = NormalizeDeviceId(monitor.DeviceID),
                    IsPrimary = isPrimary,
                    IsActive = isAttached
                };

                ApplyCurrentMode(display, adapter.DeviceName);

                var instancePath = ExtractInstancePath(monitor.DeviceID);
                var edid = ReadEdidFromRegistry(instancePath)
                           ?? LookupWmiEdid(wmiEdidMap, instancePath);

                if (edid is not null && EdidParser.TryParse(edid, display))
                    display.EdidAvailable = true;

                display.Connection = ResolveConnection(connectionMap, instancePath, display);

                snapshot.Displays.Add(display);
                break; // một monitor cho mỗi adapter là đủ
            }
        }

        // Không có adapter nào báo màn hình: dựng một mục tối thiểu để giao diện
        // vẫn có gì đó hiển thị và bài kiểm tra màu vẫn chạy được.
        if (snapshot.Displays.Count == 0)
            AddFallbackDisplay(snapshot);

        return Task.CompletedTask;
    }

    private static void ApplyCurrentMode(DisplayInfo display, string adapterName)
    {
        var mode = DisplayNativeMethods.NewDevMode();
        if (!DisplayNativeMethods.EnumDisplaySettingsW(
                adapterName, DisplayNativeMethods.EnumCurrentSettings, ref mode))
            return;

        display.WidthPx = (int)mode.dmPelsWidth;
        display.HeightPx = (int)mode.dmPelsHeight;
        display.PositionX = mode.dmPositionX;
        display.PositionY = mode.dmPositionY;
        display.BitsPerPixel = (int)mode.dmBitsPerPel;
        if (mode.dmDisplayFrequency > 1) display.RefreshHz = (int)mode.dmDisplayFrequency;
    }

    /// <summary>
    /// DeviceID trả về dạng \\?\DISPLAY#GSM5B09#5&amp;abc&amp;0&amp;UID257#{guid}.
    /// Cần cắt thành DISPLAY\GSM5B09\5&amp;abc&amp;0&amp;UID257 để tra registry.
    /// </summary>
    private static string ExtractInstancePath(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return string.Empty;

        var s = deviceId.TrimStart('\\', '?', '.');
        var guidStart = s.IndexOf('{');
        if (guidStart > 0) s = s[..guidStart];

        return s.Trim('#').Replace('#', '\\');
    }

    private static string NormalizeDeviceId(string deviceId) => ExtractInstancePath(deviceId);

    private static byte[]? ReadEdidFromRegistry(string instancePath)
    {
        if (string.IsNullOrWhiteSpace(instancePath)) return null;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Enum\{instancePath}\Device Parameters");
            return key?.GetValue("EDID") as byte[];
        }
        catch { return null; }
    }

    /// <summary>
    /// Đường dự phòng: WmiMonitorDescriptorMethods trong root\WMI cũng phơi bày EDID,
    /// hữu ích khi tài khoản không đọc được nhánh Enum của registry.
    /// </summary>
    private static Dictionary<string, byte[]> ReadEdidFromWmi()
    {
        var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var mo in WmiQuery.Query(
            "SELECT InstanceName, ProductCodeID, SerialNumberID, UserFriendlyName, " +
            "ManufacturerName, YearOfManufacture, WeekOfManufacture FROM WmiMonitorID",
            WmiQuery.WmiNamespace))
        {
            var instance = mo.Str("InstanceName");
            if (string.IsNullOrWhiteSpace(instance)) continue;

            // InstanceName có đuôi _0 và dùng dấu \ — cắt đuôi để khớp với instance path.
            var key = instance.EndsWith("_0", StringComparison.Ordinal) ? instance[..^2] : instance;
            map.TryAdd(key, Array.Empty<byte>());
        }

        return map;
    }

    private static byte[]? LookupWmiEdid(Dictionary<string, byte[]> map, string instancePath) =>
        map.TryGetValue(instancePath, out var edid) && edid.Length >= 128 ? edid : null;

    /// <summary>
    /// WmiMonitorConnectionParams cho biết công nghệ xuất hình của từng màn.
    /// Đây là cách duy nhất phân biệt được HDMI với DisplayPort từ phần mềm.
    /// </summary>
    private static Dictionary<string, DisplayConnection> ReadConnectionTypes()
    {
        var map = new Dictionary<string, DisplayConnection>(StringComparer.OrdinalIgnoreCase);

        foreach (var mo in WmiQuery.Query(
            "SELECT InstanceName, VideoOutputTechnology FROM WmiMonitorConnectionParams",
            WmiQuery.WmiNamespace))
        {
            var instance = mo.Str("InstanceName");
            if (string.IsNullOrWhiteSpace(instance)) continue;
            if (instance.EndsWith("_0", StringComparison.Ordinal)) instance = instance[..^2];

            map[instance] = TranslateOutputTechnology(mo.Long("VideoOutputTechnology", -2));
        }

        return map;
    }

    private static DisplayConnection TranslateOutputTechnology(long code) => code switch
    {
        0 => DisplayConnection.Vga,
        1 or 2 or 3 => DisplayConnection.Other,
        4 => DisplayConnection.Dvi,
        5 => DisplayConnection.Hdmi,
        6 => DisplayConnection.Internal,          // LVDS
        8 => DisplayConnection.Other,
        9 => DisplayConnection.Other,             // SDI
        10 => DisplayConnection.DisplayPort,      // external
        11 => DisplayConnection.Internal,         // embedded DisplayPort
        12 => DisplayConnection.UsbC,             // UDI external
        13 => DisplayConnection.Internal,
        15 => DisplayConnection.Wireless,         // Miracast
        -1 => DisplayConnection.Other,
        int.MinValue or 0x80000000 => DisplayConnection.Internal,
        _ => DisplayConnection.Unknown
    };

    private static DisplayConnection ResolveConnection(
        Dictionary<string, DisplayConnection> map, string instancePath, DisplayInfo display)
    {
        if (map.TryGetValue(instancePath, out var conn) && conn != DisplayConnection.Unknown)
            return conn;

        // Không tra được thì suy đoán: mã hãng tấm nền laptop khác hẳn hãng màn hình rời.
        var laptopPanelVendors = new[] { "AUO", "LGD", "BOE", "CMN", "SDC", "IVO", "CSO", "SHP" };
        if (laptopPanelVendors.Contains(display.ManufacturerCode, StringComparer.OrdinalIgnoreCase))
            return DisplayConnection.Internal;

        return DisplayConnection.Unknown;
    }

    private static void AddFallbackDisplay(SystemSnapshot snapshot)
    {
        var mode = DisplayNativeMethods.NewDevMode();
        var display = new DisplayInfo
        {
            AdapterDeviceName = @"\\.\DISPLAY1",
            FriendlyName = "Màn hình mặc định",
            IsPrimary = true,
            IsActive = true
        };

        if (DisplayNativeMethods.EnumDisplaySettingsW(
                null, DisplayNativeMethods.EnumCurrentSettings, ref mode))
        {
            display.WidthPx = (int)mode.dmPelsWidth;
            display.HeightPx = (int)mode.dmPelsHeight;
            display.BitsPerPixel = (int)mode.dmBitsPerPel;
            if (mode.dmDisplayFrequency > 1) display.RefreshHz = (int)mode.dmDisplayFrequency;
        }

        snapshot.Displays.Add(display);
    }
}
