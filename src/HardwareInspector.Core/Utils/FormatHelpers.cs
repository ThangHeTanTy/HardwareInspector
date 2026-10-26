using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Core.Utils;

public static class FormatHelpers
{
    private static readonly string[] SizeUnits = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string ToSize(this long bytes, int decimals = 1)
    {
        if (bytes <= 0) return "0 B";
        var order = (int)Math.Floor(Math.Log(bytes, 1024));
        order = Math.Min(order, SizeUnits.Length - 1);
        var value = bytes / Math.Pow(1024, order);
        return $"{Math.Round(value, decimals)} {SizeUnits[order]}";
    }

    public static string ToHumanDuration(this TimeSpan span)
    {
        if (span.TotalDays >= 365) return S($"{span.TotalDays / 365:0.#} năm ({span.TotalHours:N0} giờ)", $"{span.TotalDays / 365:0.#} years ({span.TotalHours:N0} hours)");
        if (span.TotalDays >= 1) return S($"{span.TotalDays:0.#} ngày ({span.TotalHours:N0} giờ)", $"{span.TotalDays:0.#} days ({span.TotalHours:N0} hours)");
        if (span.TotalHours >= 1) return S($"{span.TotalHours:0.#} giờ", $"{span.TotalHours:0.#} hours");
        return S($"{span.TotalMinutes:0} phút", $"{span.TotalMinutes:0} minutes");
    }

    /// <summary>Số tiền dạng ngắn: "850 nghìn ₫", "1,2 triệu ₫" / "850K ₫", "1.2M ₫".</summary>
    public static string ToVnd(this long amount)
    {
        if (amount >= 1_000_000)
        {
            var m = Math.Round(amount / 1_000_000d, 1);
            return S($"{m.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("vi-VN"))} triệu ₫",
                     $"{m.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}M ₫");
        }
        var k = Math.Round(amount / 1_000d);
        return S($"{k:0} nghìn ₫", $"{k:0}K ₫");
    }

    /// <summary>Khoảng tiền: "0,8–1,5 triệu ₫". Hai đầu bằng nhau thì chỉ in một số.</summary>
    public static string VndRange(long min, long max) =>
        min == max ? min.ToVnd() : $"{min.ToVnd().Replace(" ₫", string.Empty)} – {max.ToVnd()}";

    public static string HoursToHuman(long hours) => TimeSpan.FromHours(hours).ToHumanDuration();

    /// <summary>Chuỗi placeholder mà OEM/thợ sửa hay để lại khi ghi lại SMBIOS.</summary>
    private static readonly string[] Placeholders =
    {
        "to be filled by o.e.m.", "default string", "system serial number",
        "chassis serial number", "base board serial number", "not specified",
        "none", "n/a", "0123456789", "unknown", "oem", "xxxxxxx", "123456789",
        "to be filled by oem", "system manufacturer", "system product name"
    };

    public static bool IsPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var v = value.Trim().ToLowerInvariant();
        if (Placeholders.Contains(v)) return true;
        if (v.All(c => c == '0' || c == '.' || c == '-' || c == ' ')) return true;
        if (v.Length <= 2) return true;
        return false;
    }

    public static string Mask(string? value, int keep = 4)
    {
        if (string.IsNullOrWhiteSpace(value)) return "—";
        if (value.Length <= keep) return new string('•', value.Length);
        return string.Concat(value.AsSpan(0, keep), new string('•', Math.Min(8, value.Length - keep)));
    }

    public static string OrDash(this string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
}
