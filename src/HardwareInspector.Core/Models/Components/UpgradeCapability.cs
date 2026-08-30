namespace HardwareInspector.Core.Models.Components;

/// <summary>Một khe RAM, kể cả khe đang trống — thứ mà WMI không liệt kê.</summary>
public sealed class MemorySlotState
{
    public string Designation { get; set; } = string.Empty;   // DIMM A1, SODIMM 2...
    public string BankLabel { get; set; } = string.Empty;
    public bool IsOccupied { get; set; }
    public long CapacityBytes { get; set; }
    public string MemoryType { get; set; } = string.Empty;
    public string FormFactor { get; set; } = string.Empty;

    /// <summary>Mã form factor thô của SMBIOS. 0x0D = SODIMM, 0x0F = chip hàn thẳng lên bo.</summary>
    public byte FormFactorCode { get; set; }

    public int SpeedMtps { get; set; }
    public string PartNumber { get; set; } = string.Empty;
}

/// <summary>Loại khe mở rộng, đã quy về nhóm có ý nghĩa khi bàn chuyện nâng cấp.</summary>
public enum ExpansionSlotKind
{
    Unknown,
    M2KeyM,        // khe SSD NVMe phổ biến nhất
    M2KeyB,        // SSD SATA hoặc modem
    M2KeyAE,       // Wi-Fi
    PciExpress,
    U2,
    Mini,
    Legacy
}

public sealed class ExpansionSlotState
{
    public string Designation { get; set; } = string.Empty;
    public ExpansionSlotKind Kind { get; set; }
    public string TypeText { get; set; } = string.Empty;
    public int? PcieGeneration { get; set; }
    public int? PcieLanes { get; set; }
    public bool IsAvailable { get; set; }
    public bool IsUnavailable { get; set; }

    /// <summary>Có phải khe lắp được ổ SSD không — quyết định việc có đưa vào tư vấn ổ cứng.</summary>
    public bool AcceptsSsd => Kind is ExpansionSlotKind.M2KeyM or ExpansionSlotKind.M2KeyB or ExpansionSlotKind.U2;

    /// <summary>
    /// Băng thông lý thuyết của khe, tính theo tốc độ truyền thực sau khi trừ mã hoá đường truyền.
    /// PCIe 3.0 dùng mã 128b/130b nên mỗi làn cho khoảng 985 MB/s; các đời sau nhân đôi dần.
    /// </summary>
    public int? TheoreticalMBps
    {
        get
        {
            if (PcieGeneration is not { } gen || PcieLanes is not { } lanes) return null;
            var perLane = gen switch
            {
                1 => 250,
                2 => 500,
                3 => 985,
                4 => 1969,
                5 => 3938,
                6 => 7877,
                _ => 0
            };
            return perLane == 0 ? null : perLane * lanes;
        }
    }
}

/// <summary>
/// Tổng hợp khả năng nâng cấp đọc từ bảng SMBIOS.
///
/// Nguồn dữ liệu là những gì firmware tự khai báo, nên nó đúng với thứ BIOS
/// chấp nhận chứ chưa chắc đúng với giới hạn thật của chipset. Nhiều hãng laptop
/// khai thấp hơn khả năng thực tế; ngược lại một vài bo mạch khai theo chipset
/// mà không tính tới hạn chế của CPU đang lắp. Vì vậy mọi kết luận ở đây đều
/// kèm mức độ chắc chắn chứ không phát biểu như một sự thật tuyệt đối.
/// </summary>
public sealed class UpgradeCapability
{
    public bool DataAvailable { get; set; }

    // --- Bộ nhớ ---
    public long MaxMemoryBytes { get; set; }
    public int MemorySlotsTotal { get; set; }
    public string MemoryErrorCorrection { get; set; } = string.Empty;
    public List<MemorySlotState> MemorySlots { get; } = new();

    public int MemorySlotsOccupied => MemorySlots.Count(s => s.IsOccupied);
    public int MemorySlotsFree => Math.Max(0, MemorySlotsTotal - MemorySlotsOccupied);
    public long InstalledMemoryBytes => MemorySlots.Where(s => s.IsOccupied).Sum(s => s.CapacityBytes);
    public long MemoryHeadroomBytes => Math.Max(0, MaxMemoryBytes - InstalledMemoryBytes);

    /// <summary>Dung lượng tối đa cho mỗi thanh, suy ra từ trần tổng chia cho số khe.</summary>
    public long MaxPerSlotBytes =>
        MemorySlotsTotal > 0 && MaxMemoryBytes > 0 ? MaxMemoryBytes / MemorySlotsTotal : 0;

    public string InstalledMemoryType =>
        MemorySlots.FirstOrDefault(s => s.IsOccupied && !string.IsNullOrWhiteSpace(s.MemoryType))?.MemoryType
        ?? "Không xác định";

    public string InstalledFormFactor =>
        MemorySlots.FirstOrDefault(s => s.IsOccupied && !string.IsNullOrWhiteSpace(s.FormFactor))?.FormFactor
        ?? "Không xác định";

    public int InstalledMemorySpeedMtps =>
        MemorySlots.Where(s => s.IsOccupied).Select(s => s.SpeedMtps).DefaultIfEmpty(0).Max();

    public bool CanAddMemoryWithoutRemoving => MemorySlotsFree > 0 && MemoryHeadroomBytes > 0;
    public bool IsMemoryMaxedOut => MaxMemoryBytes > 0 && InstalledMemoryBytes >= MaxMemoryBytes;

    /// <summary>
    /// RAM hàn thẳng lên bo mạch.
    ///
    /// Chỉ khẳng định khi có bằng chứng dương: mọi vị trí bộ nhớ đều mang form factor
    /// "row of chips" (0x0F) và không còn khe nào trống. Tuyệt đối không suy ra từ việc
    /// đọc bảng SMBIOS thất bại — thiếu dữ liệu không phải bằng chứng cho điều gì cả.
    /// </summary>
    public bool MemoryIsSoldered { get; set; }

    /// <summary>Đọc được khe RAM từ SMBIOS hay không. Sai nghĩa là phải dựa vào WMI.</summary>
    public bool MemorySlotDataAvailable => MemorySlots.Count > 0;

    // --- Khe mở rộng ---
    public List<ExpansionSlotState> ExpansionSlots { get; } = new();

    public IEnumerable<ExpansionSlotState> SsdSlots =>
        ExpansionSlots.Where(s => s.AcceptsSsd);

    public IEnumerable<ExpansionSlotState> FreeSsdSlots =>
        SsdSlots.Where(s => s.IsAvailable);

    /// <summary>Khe SSD nhanh nhất mà máy có, dùng làm trần tốc độ khi tư vấn.</summary>
    public ExpansionSlotState? FastestSsdSlot =>
        SsdSlots.Where(s => s.TheoreticalMBps.HasValue)
                .OrderByDescending(s => s.TheoreticalMBps)
                .FirstOrDefault();
}
