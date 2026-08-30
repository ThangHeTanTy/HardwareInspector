using HardwareInspector.Collectors.Cpu;
using HardwareInspector.Core.Cpu;
using HardwareInspector.Collectors.Wmi;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Components;
using Microsoft.Win32;

namespace HardwareInspector.Collectors.Components;

public sealed class ProcessorCollector : IInfoCollector
{
    public string Name => "Bộ xử lý";
    public int Order => 10;

    public Task CollectAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        // CPUID chỉ cần đọc một lần cho cả hệ thống.
        var signature = CpuIdReader.Read();

        foreach (var mo in WmiQuery.Query(
            "SELECT Name, Manufacturer, SocketDesignation, NumberOfCores, NumberOfLogicalProcessors, " +
            "MaxClockSpeed, CurrentClockSpeed, L2CacheSize, L3CacheSize, ProcessorId, Architecture, " +
            "VirtualizationFirmwareEnabled, Description FROM Win32_Processor"))
        {
            ct.ThrowIfCancellationRequested();

            var cpu = new ProcessorInfo
            {
                Name = mo.Str("Name"),
                Manufacturer = mo.Str("Manufacturer"),
                Socket = mo.Str("SocketDesignation"),
                PhysicalCores = mo.Int("NumberOfCores"),
                LogicalCores = mo.Int("NumberOfLogicalProcessors"),
                MaxClockMhz = mo.Int("MaxClockSpeed"),
                BaseClockMhz = mo.Int("CurrentClockSpeed"),
                L2CacheKb = mo.Int("L2CacheSize"),
                L3CacheKb = mo.Int("L3CacheSize"),
                ProcessorId = mo.Str("ProcessorId"),
                Architecture = TranslateArchitecture(mo.Int("Architecture", -1)),
                VirtualizationEnabled = mo.Bool("VirtualizationFirmwareEnabled"),
                CpuId = signature
            };

            ParseDescription(mo.Str("Description"), cpu);
            ReadMicrocode(cpu);
            ReadRegistryName(cpu);
            ResolveGenerations(cpu);
            cpu.IsEngineeringSample = DetectEngineeringSample(cpu);

            if (signature.L3CacheKbFromCpuId > 0 && cpu.L3CacheKb == 0)
                cpu.L3CacheKb = signature.L3CacheKbFromCpuId;

            snapshot.Processors.Add(cpu);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Xác định thế hệ thật từ Family/Model, và thế hệ mà cái tên tự nhận.
    /// Chênh lệch giữa hai con số này chính là thứ dùng để phát hiện CPU bị dán nhãn lại.
    /// </summary>
    private static void ResolveGenerations(ProcessorInfo cpu)
    {
        var sig = cpu.CpuId;
        if (!sig.IsValid) return;

        var entry = Cpu.MicroarchitectureCatalog.Lookup(sig.VendorId, sig.DisplayFamily, sig.DisplayModel);
        if (entry is not null)
        {
            cpu.ActualMicroarchitecture = entry.Name;
            cpu.ActualGeneration = entry.Generation
                ?? Cpu.MicroarchitectureCatalog.RefineIntelGeneration(sig.DisplayModel, sig.Stepping);
        }

        // Tên dùng để suy thế hệ tự nhận phải lấy từ chuỗi brand của CPUID nếu có,
        // vì đó là chuỗi khó sửa hơn. Chỉ khi CPUID không trả về gì mới dùng tên của WMI.
        var nameForParsing = !string.IsNullOrWhiteSpace(sig.BrandString) ? sig.BrandString : cpu.Name;
        var parsed = Cpu.CpuNameParser.Parse(nameForParsing);
        cpu.ClaimedModelNumber = parsed.ModelNumber;
        cpu.ClaimedGeneration = parsed.Generation;
    }

    private static string TranslateArchitecture(int code) => code switch
    {
        0 => "x86",
        5 => "ARM",
        9 => "x64",
        12 => "ARM64",
        _ => "Không xác định"
    };

    private static void ParseDescription(string description, ProcessorInfo cpu)
    {
        var parts = description.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "family": cpu.Family = parts[i + 1]; break;
                case "model": cpu.Model = parts[i + 1]; break;
                case "stepping": cpu.Stepping = parts[i + 1]; break;
            }
        }
    }

    private static void ReadMicrocode(ProcessorInfo cpu)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key is null) return;
            cpu.MicrocodeCurrent = FormatRevision(key.GetValue("Update Revision"));
            cpu.MicrocodePrevious = FormatRevision(key.GetValue("Previous Update Revision"));
        }
        catch { }
    }

    /// <summary>
    /// Windows ghi ProcessorNameString vào registry lúc khởi động, lấy từ CPUID.
    /// Đây là nguồn thứ ba để đối chiếu.
    /// </summary>
    private static void ReadRegistryName(ProcessorInfo cpu)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            cpu.RegistryProcessorName = key?.GetValue("ProcessorNameString")?.ToString()?.Trim();
        }
        catch { }
    }

    private static string? FormatRevision(object? value) => value switch
    {
        byte[] b when b.Length >= 8 => "0x" + BitConverter.ToUInt32(b, 4).ToString("X8"),
        byte[] b when b.Length >= 4 => "0x" + BitConverter.ToUInt32(b, 0).ToString("X8"),
        int i => "0x" + i.ToString("X8"),
        _ => null
    };

    /// <summary>
    /// Mẫu kỹ thuật có chuỗi tên đặc trưng, và quan trọng hơn là ProcessorId
    /// thường chứa signature với stepping 0 hoặc chuỗi toàn số 0.
    /// </summary>
    private static bool DetectEngineeringSample(ProcessorInfo cpu)
    {
        var candidates = new[] { cpu.Name, cpu.CpuId.BrandString, cpu.RegistryProcessorName ?? string.Empty };

        foreach (var raw in candidates)
        {
            var n = raw.ToUpperInvariant();
            if (n.Contains("ENGINEERING SAMPLE") || n.Contains("AMD ENG SAMPLE")) return true;
            if (n.Contains("GENUINE INTEL(R) CPU 0000")) return true;
            if (n.Contains("GENUINE INTEL(R) CPU @")) return true;
        }

        return false;
    }
}
