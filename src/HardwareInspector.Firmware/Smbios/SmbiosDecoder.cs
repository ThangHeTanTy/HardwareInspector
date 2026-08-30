using HardwareInspector.Core.Models.Firmware;

namespace HardwareInspector.Firmware.Smbios;

/// <summary>Bóc các trường quan trọng của những Type hay bị chỉnh sửa.</summary>
public static class SmbiosDecoder
{
    public sealed record BiosBlock(string Vendor, string Version, string ReleaseDate, int RomSizeKb, bool UefiCapable);
    public sealed record SystemBlock(string Manufacturer, string Product, string Version, string Serial, string Sku, string Family, Guid Uuid);
    public sealed record BoardBlock(string Manufacturer, string Product, string Version, string Serial, string AssetTag);
    public sealed record ChassisBlock(string Manufacturer, byte TypeCode, string Version, string Serial, string AssetTag, byte SecurityStatus);
    public sealed record ProcessorBlock(string SocketDesignation, string Manufacturer, string Version, string SerialNumber, string PartNumber, int MaxSpeedMhz, int CoreCount, int ThreadCount);

    public static BiosBlock? DecodeBios(SmbiosStructure? s)
    {
        if (s is null || s.Length < 0x12) return null;
        var d = s.Data;
        var romSizeKb = (d[0x09] + 1) * 64;
        var characteristicsExt2 = s.Length > 0x13 ? d[0x13] : (byte)0;
        return new BiosBlock(
            s.GetString(d[0x04]),
            s.GetString(d[0x05]),
            s.GetString(d[0x08]),
            romSizeKb,
            (characteristicsExt2 & 0x08) != 0);
    }

    public static SystemBlock? DecodeSystem(SmbiosStructure? s)
    {
        if (s is null || s.Length < 0x19) return null;
        var d = s.Data;
        var uuidBytes = new byte[16];
        Array.Copy(d, 0x08, uuidBytes, 0, 16);
        return new SystemBlock(
            s.GetString(d[0x04]),
            s.GetString(d[0x05]),
            s.GetString(d[0x06]),
            s.GetString(d[0x07]),
            s.Length > 0x19 ? s.GetString(d[0x19]) : string.Empty,
            s.Length > 0x1A ? s.GetString(d[0x1A]) : string.Empty,
            new Guid(uuidBytes));
    }

    public static BoardBlock? DecodeBoard(SmbiosStructure? s)
    {
        if (s is null || s.Length < 0x09) return null;
        var d = s.Data;
        return new BoardBlock(
            s.GetString(d[0x04]),
            s.GetString(d[0x05]),
            s.GetString(d[0x06]),
            s.GetString(d[0x07]),
            s.GetString(d[0x08]));
    }

    public static ChassisBlock? DecodeChassis(SmbiosStructure? s)
    {
        if (s is null || s.Length < 0x0A) return null;
        var d = s.Data;
        return new ChassisBlock(
            s.GetString(d[0x04]),
            (byte)(d[0x05] & 0x7F),
            s.GetString(d[0x06]),
            s.GetString(d[0x07]),
            s.GetString(d[0x08]),
            s.Length > 0x0C ? d[0x0C] : (byte)0);
    }

    /// <summary>
    /// Type 4 — Processor Information. Trường Version ở offset 0x10 chính là chuỗi tên CPU
    /// mà BIOS công bố. Đây là trường bị sửa khi ai đó muốn biến CPU đời thấp thành đời cao,
    /// nên nó phải được đối chiếu với chuỗi brand lấy từ CPUID.
    /// </summary>
    public static ProcessorBlock? DecodeProcessor(SmbiosStructure? s)
    {
        if (s is null || s.Length < 0x1A) return null;
        var d = s.Data;

        return new ProcessorBlock(
            s.GetString(d[0x04]),
            s.GetString(d[0x07]),
            s.GetString(d[0x10]),
            s.Length > 0x20 ? s.GetString(d[0x20]) : string.Empty,
            s.Length > 0x22 ? s.GetString(d[0x22]) : string.Empty,
            BitConverter.ToUInt16(d, 0x14),
            s.Length > 0x23 ? d[0x23] : 0,
            s.Length > 0x25 ? d[0x25] : 0);
    }

    /// <summary>Bit 7 của Type 3 offset 0x05 = vỏ máy đã từng bị khoá/mở.</summary>
    public static bool ChassisLockPresent(SmbiosStructure? s) =>
        s is not null && s.Length > 0x05 && (s.Data[0x05] & 0x80) != 0;
}
