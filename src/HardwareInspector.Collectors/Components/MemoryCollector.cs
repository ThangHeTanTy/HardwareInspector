using HardwareInspector.Collectors.Wmi;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Components;

namespace HardwareInspector.Collectors.Components;

public sealed class MemoryCollector : IInfoCollector
{
    public string Name => "Bộ nhớ RAM";
    public int Order => 30;

    public Task CollectAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        var mem = snapshot.Memory;

        foreach (var mo in WmiQuery.Query(
            "SELECT BankLabel, DeviceLocator, Manufacturer, PartNumber, SerialNumber, Capacity, " +
            "Speed, ConfiguredClockSpeed, FormFactor, SMBIOSMemoryType FROM Win32_PhysicalMemory"))
        {
            ct.ThrowIfCancellationRequested();
            mem.Modules.Add(new MemoryModuleInfo
            {
                BankLabel = mo.Str("BankLabel"),
                DeviceLocator = mo.Str("DeviceLocator"),
                Manufacturer = mo.Str("Manufacturer"),
                PartNumber = mo.Str("PartNumber"),
                SerialNumber = mo.Str("SerialNumber"),
                CapacityBytes = mo.Long("Capacity"),
                SpeedMtps = mo.Int("Speed"),
                ConfiguredSpeedMtps = mo.Int("ConfiguredClockSpeed"),
                FormFactor = TranslateFormFactor(mo.Int("FormFactor", -1)),
                MemoryType = TranslateMemoryType(mo.Int("SMBIOSMemoryType", -1))
            });
        }

        var array = WmiQuery.QuerySingle("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray");
        mem.SlotsTotal = array?.Int("MemoryDevices") ?? mem.Modules.Count;

        var cs = WmiQuery.QuerySingle("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
        mem.TotalPhysicalBytes = cs?.Long("TotalPhysicalMemory")
                                 ?? mem.Modules.Sum(m => m.CapacityBytes);

        var os = WmiQuery.QuerySingle("SELECT FreePhysicalMemory FROM Win32_OperatingSystem");
        mem.AvailablePhysicalBytes = (os?.Long("FreePhysicalMemory") ?? 0) * 1024;

        mem.IsEccEnabled = WmiQuery
            .Query("SELECT MemoryErrorCorrection FROM Win32_PhysicalMemoryArray")
            .Any(a => a.Int("MemoryErrorCorrection") is 4 or 5 or 6);

        mem.ChannelConfiguration = DeduceChannels(mem);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Suy ra cấu hình kênh từ nhãn khe.
    ///
    /// Chỗ dễ sai: nền tảng Intel đời 12 trở lên đặt tên khe kiểu
    /// "Controller0-ChannelA-DIMM0" và "Controller1-ChannelA-DIMM0". Hai khe này
    /// thuộc HAI bộ điều khiển khác nhau nên đang chạy hai kênh, dù phần "ChannelA"
    /// giống hệt nhau. Chỉ dò chữ "ChannelA/ChannelB" sẽ kết luận ngược hoàn toàn.
    /// Vì vậy khoá phân biệt phải ghép cả số hiệu bộ điều khiển lẫn tên kênh.
    /// </summary>
    private static string DeduceChannels(MemorySubsystemInfo mem)
    {
        if (mem.Modules.Count == 0) return "Không đọc được";
        if (mem.Modules.Count == 1) return "Single channel (1 thanh)";

        var tokens = mem.Modules
            .Select(m => ExtractChannelToken(m.DeviceLocator, m.BankLabel))
            .ToList();

        // Không nhãn nào cho biết kênh: nói thẳng là không xác định được,
        // thay vì đoán rồi cảnh báo nhầm.
        if (tokens.All(string.IsNullOrEmpty))
            return $"Không xác định được từ nhãn khe ({mem.Modules.Count} thanh)";

        var distinct = tokens.Where(t => !string.IsNullOrEmpty(t))
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .Count();

        return distinct >= 2
            ? $"Dual/Multi channel ({mem.Modules.Count} thanh, {distinct} kênh)"
            : $"Cùng một kênh ({mem.Modules.Count} thanh) — kiểm tra lại vị trí cắm";
    }

    /// <summary>
    /// Dựng khoá phân biệt kênh từ nhãn khe. Trả về chuỗi rỗng khi nhãn
    /// không chứa thông tin nào đủ tin cậy để kết luận.
    /// </summary>
    private static string ExtractChannelToken(string locator, string bank)
    {
        var raw = $"{locator} {bank}".ToUpperInvariant();
        var s = raw.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);

        var controller = MatchGroup(s, @"CONTROLLER(\d+)");
        var channel = MatchGroup(s, @"CHANNEL([A-Z0-9])");

        if (controller is not null && channel is not null) return $"C{controller}{channel}";
        if (channel is not null) return channel;
        if (controller is not null) return $"C{controller}";

        // Kiểu nhãn cũ: DIMM_A1 / DIMM_B1, hoặc DIMMA / DIMMB
        var dimmLetter = MatchGroup(s, @"DIMM([A-Z])");
        if (dimmLetter is not null) return dimmLetter;

        // Kiểu BANK 0 / BANK 1 — mỗi bank là một kênh trên phần lớn bo mạch.
        var bankIndex = MatchGroup(s, @"BANK(\d+)");
        if (bankIndex is not null) return $"B{bankIndex}";

        return string.Empty;
    }

    private static string? MatchGroup(string input, string pattern)
    {
        var m = System.Text.RegularExpressions.Regex.Match(input, pattern);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string TranslateFormFactor(int code) => code switch
    {
        8 => "DIMM",
        12 => "SODIMM",
        13 => "SRIMM",
        _ => "Khác"
    };

    private static string TranslateMemoryType(int code) => code switch
    {
        20 => "DDR",
        21 => "DDR2",
        24 => "DDR3",
        26 => "DDR4",
        34 => "DDR5",
        35 => "LPDDR5",
        _ => "Không xác định"
    };
}
