using System.IO;
using System.Text.Json;

namespace HardwareInspector.App.Services;

/// <summary>
/// Ghi nhớ số quạt card đồ hoạ do người dùng chọn, theo tên card.
///
/// Cảm biến không đếm đúng mọi card, nên lựa chọn của người dùng được lưu lại
/// để lần mở sau mô hình vẫn đúng. Lưu cạnh máy, trong thư mục dữ liệu cục bộ —
/// không gửi đi đâu. Đọc ghi lỗi thì bỏ qua: đây chỉ là tiện ích hiển thị.
/// </summary>
public sealed class GpuFanOverrideStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HardwareInspector", "gpu-fans.json");

    private Dictionary<string, int>? _cache;

    public int? Get(string? gpuName)
    {
        if (string.IsNullOrWhiteSpace(gpuName)) return null;
        return Load().TryGetValue(gpuName, out var count) ? count : null;
    }

    /// <summary>Đặt số quạt cho card. Truyền 0 hoặc số âm để quay về tự nhận diện.</summary>
    public void Set(string? gpuName, int count)
    {
        if (string.IsNullOrWhiteSpace(gpuName)) return;

        var map = Load();
        if (count > 0) map[gpuName] = count;
        else map.Remove(gpuName);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(map));
        }
        catch { }
    }

    private Dictionary<string, int> Load()
    {
        if (_cache is not null) return _cache;
        try
        {
            _cache = File.Exists(_path)
                ? JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(_path)) ?? new()
                : new();
        }
        catch
        {
            _cache = new();
        }
        return _cache;
    }
}
