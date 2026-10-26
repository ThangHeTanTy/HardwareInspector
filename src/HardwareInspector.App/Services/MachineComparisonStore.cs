using System.IO;
using System.Text.Json;
using HardwareInspector.Core.Models.Assessment;

namespace HardwareInspector.App.Services;

/// <summary>
/// Danh sách các máy đã đi xem, lưu ra file để so sánh qua nhiều ngày.
/// Chạy từ USB thì file nằm trên USB, nên đi xem máy ở nhiều nơi vẫn dồn về một danh sách.
/// </summary>
public sealed class MachineComparisonStore
{
    private readonly string _path = AppPaths.DataFile("compare.json");
    private List<MachineRecord>? _cache;

    public IReadOnlyList<MachineRecord> All => Load();

    public void Add(MachineRecord record)
    {
        Load().Add(record);
        Save();
    }

    public void Remove(string id)
    {
        Load().RemoveAll(r => r.Id == id);
        Save();
    }

    private List<MachineRecord> Load()
    {
        if (_cache is not null) return _cache;
        try
        {
            _cache = File.Exists(_path)
                ? JsonSerializer.Deserialize<List<MachineRecord>>(File.ReadAllText(_path)) ?? new()
                : new();
        }
        catch
        {
            _cache = new();
        }
        return _cache;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(Load(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
