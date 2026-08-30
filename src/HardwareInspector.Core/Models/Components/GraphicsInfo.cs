namespace HardwareInspector.Core.Models.Components;

public sealed class GraphicsInfo
{
    public string Name { get; set; } = "Không xác định";
    public string Vendor { get; set; } = string.Empty;
    public string? PnpDeviceId { get; set; }
    public long VideoMemoryBytes { get; set; }
    public string DriverVersion { get; set; } = string.Empty;
    public DateTime? DriverDate { get; set; }

    /// <summary>Phiên bản vBIOS. Lệch so với vBIOS gốc là dấu hiệu card đã bị flash.</summary>
    public string? VideoBiosVersion { get; set; }

    public bool IsDiscrete { get; set; }
    public string? CurrentResolution { get; set; }
    public int? CurrentRefreshHz { get; set; }

    /// <summary>Số làn PCIe hiện hành và tối đa — sụt làn là dấu hiệu khe/chân tiếp xúc lỗi.</summary>
    public int? PcieLinkWidthCurrent { get; set; }
    public int? PcieLinkWidthMax { get; set; }
    public string? PcieLinkSpeed { get; set; }
}
