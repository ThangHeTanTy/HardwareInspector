using System.Text.RegularExpressions;

namespace HardwareInspector.Core.Cpu;

/// <summary>
/// Bóc số hiệu model và thế hệ từ chuỗi tên thương mại của CPU.
/// Kết quả ở đây là cái mà máy *tự nhận*, dùng để đối chiếu với Family/Model thật.
/// </summary>
public static partial class CpuNameParser
{
    public sealed record ParsedName(string? ModelNumber, int? Generation, string? Tier);

    [GeneratedRegex(@"\b(i[3579])[\s-]*(\d{4,5})([A-Z]{0,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex IntelCoreRegex();

    [GeneratedRegex(@"\bUltra\s+([3579])\s+(\d{3})([A-Z]{0,2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex IntelUltraRegex();

    [GeneratedRegex(@"\bRyzen\s+([3579])\s+(\d{4})([A-Z]{0,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex RyzenRegex();

    public static ParsedName Parse(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return new ParsedName(null, null, null);

        var core = IntelCoreRegex().Match(name);
        if (core.Success)
        {
            var tier = core.Groups[1].Value.ToLowerInvariant();
            var digits = core.Groups[2].Value;
            var suffix = core.Groups[3].Value.ToUpperInvariant();

            // 4 chữ số → thế hệ là chữ số đầu (4790K = thế hệ 4).
            // 5 chữ số → thế hệ là hai chữ số đầu (10700K = thế hệ 10).
            var generation = digits.Length == 5
                ? int.Parse(digits[..2])
                : int.Parse(digits[..1]);

            return new ParsedName($"{tier}-{digits}{suffix}", generation, tier);
        }

        var ultra = IntelUltraRegex().Match(name);
        if (ultra.Success)
        {
            // Core Ultra series 1 (Meteor Lake) dùng số hiệu 3 chữ số bắt đầu bằng 1.
            var digits = ultra.Groups[2].Value;
            var series = digits.StartsWith('1') ? 1 : digits.StartsWith('2') ? 2 : (int?)null;
            return new ParsedName($"Ultra {ultra.Groups[1].Value} {digits}{ultra.Groups[3].Value}",
                series is null ? null : 100 + series, $"ultra{ultra.Groups[1].Value}");
        }

        var ryzen = RyzenRegex().Match(name);
        if (ryzen.Success)
        {
            var digits = ryzen.Groups[2].Value;
            return new ParsedName($"Ryzen {ryzen.Groups[1].Value} {digits}{ryzen.Groups[3].Value}",
                int.Parse(digits[..1]), $"ryzen{ryzen.Groups[1].Value}");
        }

        return new ParsedName(null, null, null);
    }

    /// <summary>
    /// So sánh hai chuỗi tên CPU bỏ qua khác biệt vô hại về khoảng trắng,
    /// dấu (R), (TM) và phần "CPU @ x.xxGHz" ở đuôi.
    /// </summary>
    public static bool NamesMatch(string a, string b) =>
        Normalize(a).Equals(Normalize(b), StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var s = value
            .Replace("(R)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("(TM)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("®", string.Empty)
            .Replace("™", string.Empty);

        var at = s.IndexOf(" @ ", StringComparison.Ordinal);
        if (at > 0) s = s[..at];

        s = s.Replace(" CPU", string.Empty, StringComparison.OrdinalIgnoreCase)
             .Replace(" Processor", string.Empty, StringComparison.OrdinalIgnoreCase);

        return string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
