using System.IO;
using System.Text.Json;
using HardwareInspector.Core.Models.Components;

namespace HardwareInspector.App.Services;

/// <summary>
/// Ghi nhớ cổng thật của màn hình do người dùng chọn, theo đường dẫn thiết bị của màn.
///
/// Đường dẫn này gắn với cả màn lẫn cổng xuất hình đang cắm, nên cắm màn sang cổng khác
/// thì lựa chọn cũ không bị áp nhầm. Lưu cục bộ, không gửi đi đâu; đọc ghi lỗi thì bỏ qua.
/// </summary>
public sealed class DisplayPortOverrideStore
{
    private readonly string _path = AppPaths.DataFile("display-ports.json");

    private Dictionary<string, DisplayConnection>? _cache;

    public static string KeyOf(DisplayInfo display) =>
        !string.IsNullOrWhiteSpace(display.MonitorDeviceId) ? display.MonitorDeviceId : display.AdapterDeviceName;

    public DisplayConnection? Get(DisplayInfo display) =>
        Load().TryGetValue(KeyOf(display), out var port) ? port : null;

    /// <summary>Đặt cổng thật cho màn. Truyền null để quay về tự nhận diện.</summary>
    public void Set(DisplayInfo display, DisplayConnection? port)
    {
        var map = Load();
        if (port is { } p) map[KeyOf(display)] = p;
        else map.Remove(KeyOf(display));

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(map));
        }
        catch { }
    }

    private Dictionary<string, DisplayConnection> Load()
    {
        if (_cache is not null) return _cache;
        try
        {
            _cache = File.Exists(_path)
                ? JsonSerializer.Deserialize<Dictionary<string, DisplayConnection>>(File.ReadAllText(_path)) ?? new()
                : new();
        }
        catch
        {
            _cache = new();
        }
        return _cache;
    }
}
