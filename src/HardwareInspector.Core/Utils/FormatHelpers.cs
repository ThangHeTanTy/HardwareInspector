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
        if (span.TotalDays >= 365) return $"{span.TotalDays / 365:0.#} năm ({span.TotalHours:N0} giờ)";
        if (span.TotalDays >= 1) return $"{span.TotalDays:0.#} ngày ({span.TotalHours:N0} giờ)";
        if (span.TotalHours >= 1) return $"{span.TotalHours:0.#} giờ";
        return $"{span.TotalMinutes:0} phút";
    }

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
