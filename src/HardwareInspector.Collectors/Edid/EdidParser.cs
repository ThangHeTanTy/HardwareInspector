using System.Text;
using HardwareInspector.Core.Models.Components;

namespace HardwareInspector.Collectors.Edid;

/// <summary>
/// Bóc tách khối EDID 128 byte. Đây là nguồn duy nhất cho biết
/// mã panel thật và tuần/năm sản xuất — dữ liệu then chốt để phát hiện màn hình đã bị thay.
/// </summary>
public static class EdidParser
{
    public static bool TryParse(byte[] edid, DisplayInfo target)
    {
        if (edid.Length < 128) return false;
        // Chữ ký cố định 00 FF FF FF FF FF FF 00
        if (edid[0] != 0x00 || edid[1] != 0xFF || edid[7] != 0x00) return false;

        target.ManufacturerCode = DecodeManufacturer(edid[8], edid[9]);
        target.ManufacturerName = MonitorVendors.Resolve(target.ManufacturerCode);
        target.ProductCode = $"{edid[11]:X2}{edid[10]:X2}";

        var week = edid[16];
        var year = edid[17] + 1990;
        if (week is > 0 and <= 54) target.ManufactureWeek = week;
        if (year is > 1990 and < 2100) target.ManufactureYear = year;

        var hCm = edid[21];
        var vCm = edid[22];
        if (hCm > 0 && vCm > 0)
            target.DiagonalInches = (int)Math.Round(Math.Sqrt(hCm * hCm + vCm * vCm) / 2.54);

        var bitDepthCode = (edid[20] >> 4) & 0x07;
        target.BitsPerColor = bitDepthCode switch
        {
            1 => 6, 2 => 8, 3 => 10, 4 => 12, 5 => 14, 6 => 16, _ => null
        };

        ParseDescriptors(edid, target);
        ParsePreferredTiming(edid, target);
        return true;
    }

    private static void ParseDescriptors(byte[] edid, DisplayInfo target)
    {
        // 4 khối mô tả 18 byte bắt đầu tại offset 54
        for (var i = 54; i <= 108; i += 18)
        {
            if (edid[i] != 0 || edid[i + 1] != 0 || edid[i + 2] != 0) continue;
            var tag = edid[i + 3];
            var text = ReadDescriptorText(edid, i + 5);

            switch (tag)
            {
                case 0xFC: target.PanelModel = text; break;   // Monitor name
                case 0xFF: target.SerialNumber = text; break; // Serial ASCII
            }
        }

        if (string.IsNullOrWhiteSpace(target.SerialNumber))
        {
            var numericSerial = BitConverter.ToUInt32(edid, 12);
            if (numericSerial is > 0 and < uint.MaxValue)
                target.SerialNumber = numericSerial.ToString();
        }
    }

    private static void ParsePreferredTiming(byte[] edid, DisplayInfo target)
    {
        const int dtd = 54;
        var pixelClock = (edid[dtd] | (edid[dtd + 1] << 8)) * 10_000; // Hz
        if (pixelClock == 0) return;

        var hActive = edid[dtd + 2] | ((edid[dtd + 4] & 0xF0) << 4);
        var hBlank = edid[dtd + 3] | ((edid[dtd + 4] & 0x0F) << 8);
        var vActive = edid[dtd + 5] | ((edid[dtd + 7] & 0xF0) << 4);
        var vBlank = edid[dtd + 6] | ((edid[dtd + 7] & 0x0F) << 8);

        if (hActive > 0 && vActive > 0)
        {
            // Đây là độ phân giải gốc của tấm nền, khác với độ phân giải đang đặt
            // trong Windows. Giữ riêng hai giá trị để phát hiện màn đang chạy sai chuẩn.
            target.NativeWidthPx = hActive;
            target.NativeHeightPx = vActive;

            var total = (long)(hActive + hBlank) * (vActive + vBlank);
            if (total > 0) target.MaxRefreshHz = (int)Math.Round(pixelClock / (double)total);
        }
    }

    private static string ReadDescriptorText(byte[] edid, int start)
    {
        var sb = new StringBuilder();
        for (var i = start; i < start + 13 && i < edid.Length; i++)
        {
            if (edid[i] == 0x0A) break;
            sb.Append((char)edid[i]);
        }
        return sb.ToString().Trim();
    }

    private static string DecodeManufacturer(byte b1, byte b2)
    {
        var value = (b1 << 8) | b2;
        return string.Concat(
            (char)('A' + ((value >> 10) & 0x1F) - 1),
            (char)('A' + ((value >> 5) & 0x1F) - 1),
            (char)('A' + (value & 0x1F) - 1));
    }
}

internal static class MonitorVendors
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AUO"] = "AU Optronics", ["LGD"] = "LG Display", ["BOE"] = "BOE Technology",
        ["SDC"] = "Samsung Display", ["CMN"] = "Chi Mei / Innolux", ["SHP"] = "Sharp",
        ["IVO"] = "InfoVision", ["PNP"] = "Panasonic", ["APP"] = "Apple",
        ["DEL"] = "Dell", ["LEN"] = "Lenovo", ["HWP"] = "HP", ["ACR"] = "Acer",
        ["AUS"] = "ASUS", ["MSI"] = "MSI", ["GSM"] = "LG Electronics", ["SAM"] = "Samsung",
        ["VSC"] = "ViewSonic", ["BNQ"] = "BenQ", ["AOC"] = "AOC", ["CSO"] = "CSOT"
    };

    public static string Resolve(string code) =>
        Map.TryGetValue(code, out var name) ? name : code;
}
