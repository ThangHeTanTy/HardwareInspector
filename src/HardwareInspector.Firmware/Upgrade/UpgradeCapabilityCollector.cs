using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Components;
using HardwareInspector.Firmware.Smbios;

namespace HardwareInspector.Firmware.Upgrade;

/// <summary>
/// Đọc giới hạn nâng cấp từ bảng SMBIOS thô.
///
/// Ba bảng cần tới, và không bảng nào phơi bày đầy đủ qua WMI:
///   Type 16 — trần dung lượng RAM và số khe mà bo mạch khai báo
///   Type 17 — từng khe RAM, gồm cả khe đang trống (Win32_PhysicalMemory chỉ trả khe đã cắm)
///   Type 9  — khe mở rộng, gồm loại chân M.2, thế hệ PCIe, số làn và tình trạng trống hay đã dùng
/// </summary>
public sealed class UpgradeCapabilityCollector : IInfoCollector
{
    public string Name => "Khả năng nâng cấp";
    public int Order => 70;

    public Task CollectAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        var smbios = new SmbiosReader();
        if (!smbios.Read()) return Task.CompletedTask;

        var upgrade = snapshot.Upgrade;
        upgrade.DataAvailable = true;

        ReadMemoryArray(smbios, upgrade);
        ReadMemorySlots(smbios, upgrade);
        ReadExpansionSlots(smbios, upgrade);

        DetectSolderedMemory(upgrade);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Chỉ kết luận RAM hàn khi có bằng chứng dương.
    ///
    /// Bằng chứng đó là form factor 0x0F ("row of chips") trên mọi vị trí bộ nhớ,
    /// kèm điều kiện không còn khe nào trống. Suy ra từ việc bảng SMBIOS đọc không ra
    /// là sai về nguyên tắc: thiếu dữ liệu chỉ có nghĩa là thiếu dữ liệu.
    /// </summary>
    private static void DetectSolderedMemory(UpgradeCapability upgrade)
    {
        const byte RowOfChips = 0x0F;

        if (upgrade.MemorySlots.Count == 0)
        {
            upgrade.MemoryIsSoldered = false;
            return;
        }

        var occupied = upgrade.MemorySlots.Where(s => s.IsOccupied).ToList();
        if (occupied.Count == 0)
        {
            upgrade.MemoryIsSoldered = false;
            return;
        }

        upgrade.MemoryIsSoldered =
            occupied.All(s => s.FormFactorCode == RowOfChips) &&
            upgrade.MemorySlots.All(s => s.IsOccupied);
    }

    /// <summary>
    /// Type 16. Trường Maximum Capacity là DWORD tính bằng KB; khi giá trị bằng
    /// 0x80000000 nghĩa là trần vượt quá 2 TB và phải đọc Extended Maximum Capacity.
    /// </summary>
    private static void ReadMemoryArray(SmbiosReader smbios, UpgradeCapability upgrade)
    {
        var s = smbios.First(16);
        if (s is null || s.Length < 0x0F) return;

        var d = s.Data;
        var maxCapacityKb = BitConverter.ToUInt32(d, 0x07);

        if (maxCapacityKb == 0x80000000 && s.Length >= 0x17)
            upgrade.MaxMemoryBytes = (long)BitConverter.ToUInt64(d, 0x0F);
        else if (maxCapacityKb > 0)
            upgrade.MaxMemoryBytes = (long)maxCapacityKb * 1024;

        upgrade.MemorySlotsTotal = BitConverter.ToUInt16(d, 0x0D);
        upgrade.MemoryErrorCorrection = TranslateErrorCorrection(d[0x06]);
    }

    /// <summary>
    /// Type 17. Size bằng 0 nghĩa là khe trống — đây chính là thông tin mà
    /// Win32_PhysicalMemory không có, và là thứ quyết định câu trả lời
    /// "còn cắm thêm được thanh nào không".
    /// </summary>
    private static void ReadMemorySlots(SmbiosReader smbios, UpgradeCapability upgrade)
    {
        foreach (var s in smbios.All(17))
        {
            if (s.Length < 0x15) continue;
            var d = s.Data;

            var slot = new MemorySlotState
            {
                Designation = s.GetString(d[0x10]),
                BankLabel = s.GetString(d[0x11]),
                FormFactorCode = d[0x0E],
                FormFactor = TranslateFormFactor(d[0x0E]),
                MemoryType = TranslateMemoryType(d[0x12])
            };

            var sizeRaw = BitConverter.ToUInt16(d, 0x0C);
            if (sizeRaw == 0)
            {
                slot.IsOccupied = false;
            }
            else if (sizeRaw == 0x7FFF && s.Length >= 0x20)
            {
                // Extended Size tính bằng MB, dùng cho thanh từ 32 GB trở lên.
                slot.IsOccupied = true;
                slot.CapacityBytes = (long)BitConverter.ToUInt32(d, 0x1C) * 1024 * 1024;
            }
            else
            {
                slot.IsOccupied = true;
                // Bit 15 = 0 nghĩa là đơn vị MB, = 1 nghĩa là KB.
                slot.CapacityBytes = (sizeRaw & 0x8000) != 0
                    ? (long)(sizeRaw & 0x7FFF) * 1024
                    : (long)sizeRaw * 1024 * 1024;
            }

            if (s.Length >= 0x17) slot.SpeedMtps = BitConverter.ToUInt16(d, 0x15);
            if (s.Length >= 0x1B) slot.PartNumber = s.GetString(d[0x1A]);

            upgrade.MemorySlots.Add(slot);
        }

        // Một số BIOS khai Type 16 sai; số bản ghi Type 17 mới là con số đáng tin.
        if (upgrade.MemorySlotsTotal < upgrade.MemorySlots.Count)
            upgrade.MemorySlotsTotal = upgrade.MemorySlots.Count;
    }

    private static void ReadExpansionSlots(SmbiosReader smbios, UpgradeCapability upgrade)
    {
        foreach (var s in smbios.All(9))
        {
            if (s.Length < 0x0C) continue;
            var d = s.Data;

            var type = SmbiosSlotCatalog.Lookup(d[0x05]);
            var (available, unavailable) = SmbiosSlotCatalog.TranslateUsage(d[0x07]);

            upgrade.ExpansionSlots.Add(new ExpansionSlotState
            {
                Designation = s.GetString(d[0x04]),
                Kind = type.Kind,
                TypeText = type.Text,
                PcieGeneration = type.Generation,
                PcieLanes = type.Lanes ?? SmbiosSlotCatalog.LanesFromBusWidth(d[0x06]),
                IsAvailable = available,
                IsUnavailable = unavailable
            });
        }
    }

    private static string TranslateErrorCorrection(byte code) => code switch
    {
        3 => "Không có",
        4 => "Parity",
        5 => "ECC một bit",
        6 => "ECC nhiều bit",
        7 => "CRC",
        _ => "Không xác định"
    };

    private static string TranslateFormFactor(byte code) => code switch
    {
        0x08 => "DIMM",
        0x09 => "TSOP",
        0x0B => "RIMM",
        0x0C => "SIMM",
        0x0D => "SODIMM",
        0x0F => "Chip hàn trên bo",
        _ => "Khác"
    };

    private static string TranslateMemoryType(byte code) => code switch
    {
        0x14 => "DDR",
        0x15 => "DDR2",
        0x18 => "DDR3",
        0x1A => "DDR4",
        0x22 => "DDR5",
        0x23 => "LPDDR5",
        0x1E => "LPDDR3",
        0x1F => "LPDDR4",
        _ => string.Empty
    };
}
