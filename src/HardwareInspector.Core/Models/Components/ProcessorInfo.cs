namespace HardwareInspector.Core.Models.Components;

/// <summary>
/// Chữ ký CPU đọc trực tiếp từ lệnh CPUID của silicon.
/// Đây là nguồn dữ liệu quan trọng nhất khi cần biết con CPU thật là gì,
/// vì nó không đi qua BIOS và không thể sửa bằng công cụ ghi SMBIOS.
/// </summary>
public sealed class CpuIdSignature
{
    public string VendorId { get; set; } = string.Empty;      // GenuineIntel / AuthenticAMD
    public string BrandString { get; set; } = string.Empty;   // từ leaf 0x80000002-04

    public int Family { get; set; }
    public int Model { get; set; }
    public int Stepping { get; set; }
    public int ExtendedFamily { get; set; }
    public int ExtendedModel { get; set; }

    /// <summary>Family và Model đã ghép phần mở rộng — dạng dùng để tra vi kiến trúc.</summary>
    public int DisplayFamily { get; set; }
    public int DisplayModel { get; set; }

    public int LogicalProcessorsFromCpuId { get; set; }
    public int L3CacheKbFromCpuId { get; set; }
    public int BaseFrequencyMhz { get; set; }   // leaf 0x16, chỉ Intel đời mới
    public int MaxFrequencyMhz { get; set; }

    public bool IsHypervisorPresent { get; set; }
    public string? HypervisorVendor { get; set; }

    public bool IsValid => !string.IsNullOrWhiteSpace(VendorId);

    public string SignatureText => $"Family {DisplayFamily:X}h Model {DisplayModel:X2}h Stepping {Stepping}";
}

public sealed class ProcessorInfo
{
    public string Name { get; set; } = "Không xác định";
    public string Manufacturer { get; set; } = string.Empty;
    public string Socket { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public int PhysicalCores { get; set; }
    public int LogicalCores { get; set; }
    public int BaseClockMhz { get; set; }
    public int MaxClockMhz { get; set; }
    public int L2CacheKb { get; set; }
    public int L3CacheKb { get; set; }
    public string ProcessorId { get; set; } = string.Empty;

    /// <summary>Chuỗi tên do CPUID trả về — nguồn đáng tin nhất.</summary>
    public CpuIdSignature CpuId { get; set; } = new();

    /// <summary>
    /// Chuỗi "Processor Version" trong SMBIOS Type 4. Trường này do BIOS cung cấp
    /// nên có thể bị sửa. Lệch so với CpuId.BrandString là bằng chứng bị can thiệp.
    /// </summary>
    public string? SmbiosProcessorVersion { get; set; }

    /// <summary>Tên CPU Windows ghi vào registry lúc khởi động.</summary>
    public string? RegistryProcessorName { get; set; }

    public string? MicrocodeCurrent { get; set; }
    public string? MicrocodePrevious { get; set; }

    public bool IsEngineeringSample { get; set; }
    public bool VirtualizationEnabled { get; set; }
    public string? Stepping { get; set; }
    public string? Family { get; set; }
    public string? Model { get; set; }

    /// <summary>Vi kiến trúc suy ra từ Family/Model thật, ví dụ "Broadwell (thế hệ 5)".</summary>
    public string? ActualMicroarchitecture { get; set; }
    public int? ActualGeneration { get; set; }

    /// <summary>Thế hệ mà chuỗi tên tự nhận, ví dụ i7-9700K tự nhận thế hệ 9.</summary>
    public int? ClaimedGeneration { get; set; }
    public string? ClaimedModelNumber { get; set; }
}
