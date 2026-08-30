using HardwareInspector.Core.Models.Components;

namespace HardwareInspector.Firmware.Smbios;

/// <summary>
/// Bảng tra mã loại khe của SMBIOS Type 9 sang nhóm có nghĩa khi bàn nâng cấp.
///
/// Điểm quan trọng: từ SMBIOS 3.x, mã loại khe đã mã hoá sẵn cả thế hệ PCIe
/// lẫn số làn. Nhờ đó có thể suy ra băng thông trần của khe M.2 mà không cần
/// đọc thanh ghi cấu hình PCI — thứ đòi hỏi driver kernel.
/// </summary>
public static class SmbiosSlotCatalog
{
    public sealed record SlotType(string Text, ExpansionSlotKind Kind, int? Generation, int? Lanes);

    private static readonly Dictionary<byte, SlotType> Map = new()
    {
        [0x03] = new("ISA", ExpansionSlotKind.Legacy, null, null),
        [0x06] = new("PCI", ExpansionSlotKind.Legacy, null, null),
        [0x0F] = new("AGP", ExpansionSlotKind.Legacy, null, null),
        [0x12] = new("PCI-X", ExpansionSlotKind.Legacy, null, null),

        // M.2 — chân cắm quyết định khe dùng được cho việc gì
        [0x14] = new("M.2 Socket 1-DP (key A)", ExpansionSlotKind.M2KeyAE, null, null),
        [0x15] = new("M.2 Socket 1-SD (key E)", ExpansionSlotKind.M2KeyAE, null, null),
        [0x16] = new("M.2 Socket 2 (key B)", ExpansionSlotKind.M2KeyB, null, null),
        [0x17] = new("M.2 Socket 3 (key M)", ExpansionSlotKind.M2KeyM, null, null),

        // U.2 — mã đã bao gồm thế hệ PCIe
        [0x1F] = new("U.2 PCIe Gen2", ExpansionSlotKind.U2, 2, 4),
        [0x20] = new("U.2 PCIe Gen3", ExpansionSlotKind.U2, 3, 4),
        [0x24] = new("U.2 PCIe Gen4", ExpansionSlotKind.U2, 4, 4),
        [0x25] = new("U.2 PCIe Gen5", ExpansionSlotKind.U2, 5, 4),

        [0x21] = new("PCIe Mini 52 chân", ExpansionSlotKind.Mini, null, null),
        [0x22] = new("PCIe Mini 52 chân", ExpansionSlotKind.Mini, null, null),
        [0x23] = new("PCIe Mini 76 chân", ExpansionSlotKind.Mini, null, null),

        // PCI Express không nêu thế hệ
        [0xA5] = new("PCI Express", ExpansionSlotKind.PciExpress, null, null),
        [0xA6] = new("PCI Express x1", ExpansionSlotKind.PciExpress, null, 1),
        [0xA7] = new("PCI Express x2", ExpansionSlotKind.PciExpress, null, 2),
        [0xA8] = new("PCI Express x4", ExpansionSlotKind.PciExpress, null, 4),
        [0xA9] = new("PCI Express x8", ExpansionSlotKind.PciExpress, null, 8),
        [0xAA] = new("PCI Express x16", ExpansionSlotKind.PciExpress, null, 16),

        [0xAB] = new("PCIe Gen2", ExpansionSlotKind.PciExpress, 2, null),
        [0xAC] = new("PCIe Gen2 x1", ExpansionSlotKind.PciExpress, 2, 1),
        [0xAD] = new("PCIe Gen2 x2", ExpansionSlotKind.PciExpress, 2, 2),
        [0xAE] = new("PCIe Gen2 x4", ExpansionSlotKind.PciExpress, 2, 4),
        [0xAF] = new("PCIe Gen2 x8", ExpansionSlotKind.PciExpress, 2, 8),
        [0xB0] = new("PCIe Gen2 x16", ExpansionSlotKind.PciExpress, 2, 16),

        [0xB1] = new("PCIe Gen3", ExpansionSlotKind.PciExpress, 3, null),
        [0xB2] = new("PCIe Gen3 x1", ExpansionSlotKind.PciExpress, 3, 1),
        [0xB3] = new("PCIe Gen3 x2", ExpansionSlotKind.PciExpress, 3, 2),
        [0xB4] = new("PCIe Gen3 x4", ExpansionSlotKind.PciExpress, 3, 4),
        [0xB5] = new("PCIe Gen3 x8", ExpansionSlotKind.PciExpress, 3, 8),
        [0xB6] = new("PCIe Gen3 x16", ExpansionSlotKind.PciExpress, 3, 16),

        [0xB8] = new("PCIe Gen4", ExpansionSlotKind.PciExpress, 4, null),
        [0xB9] = new("PCIe Gen4 x1", ExpansionSlotKind.PciExpress, 4, 1),
        [0xBA] = new("PCIe Gen4 x2", ExpansionSlotKind.PciExpress, 4, 2),
        [0xBB] = new("PCIe Gen4 x4", ExpansionSlotKind.PciExpress, 4, 4),
        [0xBC] = new("PCIe Gen4 x8", ExpansionSlotKind.PciExpress, 4, 8),
        [0xBD] = new("PCIe Gen4 x16", ExpansionSlotKind.PciExpress, 4, 16),

        [0xBE] = new("PCIe Gen5", ExpansionSlotKind.PciExpress, 5, null),
        [0xBF] = new("PCIe Gen5 x1", ExpansionSlotKind.PciExpress, 5, 1),
        [0xC0] = new("PCIe Gen5 x2", ExpansionSlotKind.PciExpress, 5, 2),
        [0xC1] = new("PCIe Gen5 x4", ExpansionSlotKind.PciExpress, 5, 4),
        [0xC2] = new("PCIe Gen5 x8", ExpansionSlotKind.PciExpress, 5, 8),
        [0xC3] = new("PCIe Gen5 x16", ExpansionSlotKind.PciExpress, 5, 16),
        [0xC4] = new("PCIe Gen6 trở lên", ExpansionSlotKind.PciExpress, 6, null)
    };

    public static SlotType Lookup(byte code) =>
        Map.TryGetValue(code, out var t) ? t : new SlotType($"Khe loại 0x{code:X2}", ExpansionSlotKind.Unknown, null, null);

    /// <summary>Số làn lấy từ trường Data Bus Width (offset 06h) khi mã loại khe không nói rõ.</summary>
    public static int? LanesFromBusWidth(byte code) => code switch
    {
        0x08 => 1,
        0x09 => 2,
        0x0A => 4,
        0x0B => 8,
        0x0C => 12,
        0x0D => 16,
        0x0E => 32,
        _ => null
    };

    /// <summary>Current Usage tại offset 07h.</summary>
    public static (bool Available, bool Unavailable) TranslateUsage(byte code) => code switch
    {
        3 => (true, false),    // Available
        4 => (false, false),   // In use
        5 => (false, true),    // Unavailable
        _ => (false, false)
    };
}
