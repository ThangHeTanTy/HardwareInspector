using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Assessment;
using HardwareInspector.Core.Utils;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Analysis.Comparison;

/// <summary>Một máy trong bảng so sánh, kèm thứ hạng và lý do.</summary>
public sealed record ComparisonEntry(MachineRecord Record, double? ValueIndex, bool IsBest, string Note);

/// <summary>
/// So nhiều máy đã đi xem để chọn máy đáng tiền nhất.
///
/// Thước đo: điểm tổng thể chia cho tiền thật phải bỏ ra (giá mua + chi phí sửa) tính theo triệu đồng.
/// Máy "Không nên mua" bị loại khỏi xếp hạng dù rẻ tới đâu, vì rủi ro của nó không quy ra tiền được.
/// </summary>
public static class MachineComparison
{
    public static MachineRecord CreateRecord(SystemSnapshot s, MachineAssessment a, string nickname, long? askedPriceVnd)
    {
        var cpu = s.PrimaryCpu;
        return new MachineRecord
        {
            Nickname = string.IsNullOrWhiteSpace(nickname) ? a.MachineTitle : nickname.Trim(),
            AskedPriceVnd = askedPriceVnd,
            MachineTitle = a.MachineTitle,
            IsLaptop = s.IsLaptop,
            CpuName = cpu?.Name ?? string.Empty,
            Cores = cpu?.PhysicalCores ?? 0,
            Threads = cpu?.LogicalCores ?? 0,
            RamBytes = s.Memory.Modules.Count > 0 ? s.Memory.Modules.Sum(m => m.CapacityBytes) : s.Memory.TotalPhysicalBytes,
            RamType = s.Memory.Modules.Select(m => m.MemoryType).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? string.Empty,
            Drives = s.StorageDevices.Select(d => new DriveSummary(d.Model, d.CapacityBytes, $"{d.BusType} {d.MediaType}".Trim())).ToList(),
            Gpus = s.GraphicsAdapters.OrderByDescending(g => g.IsDiscrete).Select(g => g.Name).ToList(),
            Displays = s.Displays.Select(d => new DisplaySummary(d.DiagonalInches, d.MaxRefreshHz ?? d.RefreshHz)).ToList(),
            BatteryHealthPercent = s.PrimaryBattery?.HealthPercent,
            OverallScore = a.OverallScore,
            OverallRating = a.OverallRating,
            Decision = a.Advice.Decision,
            TrustLevel = a.Trust.Level,
            RepairMinVnd = a.Advice.RepairMinVnd,
            RepairMaxVnd = a.Advice.RepairMaxVnd,
            BlockerCount = a.Advice.Blockers.Count
        };
    }

    public static IReadOnlyList<ComparisonEntry> Rank(IEnumerable<MachineRecord> records)
    {
        var list = records.ToList();
        double? Value(MachineRecord r) =>
            r.Decision != PurchaseDecision.Avoid && r.EffectiveCostVnd is > 0
                ? r.OverallScore / (r.EffectiveCostVnd.Value / 1_000_000d)
                : null;

        var best = list.Where(r => Value(r) is not null).OrderByDescending(r => Value(r)).FirstOrDefault();

        return list
            .OrderByDescending(r => Value(r) ?? -1)
            .ThenByDescending(r => r.OverallScore)
            .Select(r => new ComparisonEntry(r, Value(r), ReferenceEquals(r, best), Note(r, r == best)))
            .ToList();
    }

    /// <summary>Câu gợi ý ở đầu bảng so sánh.</summary>
    public static string Verdict(IReadOnlyList<ComparisonEntry> entries)
    {
        if (entries.Count == 0)
            return S("Chưa lưu máy nào. Quét một máy, nhập giá người bán ở tab \"Mua hay không\" rồi bấm Lưu để so sánh.",
                     "No machines saved yet. Scan a machine, enter the seller's price in the \"Buy or not\" tab and press Save to compare.");
        if (entries.Count == 1)
            return S("Mới có một máy. Lưu thêm máy khác để so sánh.", "Only one machine so far. Save another one to compare.");

        var best = entries.FirstOrDefault(e => e.IsBest);
        if (best is null)
            return S("Chưa xếp hạng được: các máy đều thuộc diện không nên mua hoặc chưa nhập giá.",
                     "Cannot rank yet: every machine is either a do-not-buy or has no price entered.");

        var r = best.Record;
        return S($"Đáng tiền nhất: {r.Nickname} — {r.OverallScore}/100 điểm, tổng chi phí thực khoảng {r.EffectiveCostVnd!.Value.ToVnd()} " +
                 "(giá mua + chi phí sửa).",
                 $"Best value: {r.Nickname} — {r.OverallScore}/100, real total cost about {r.EffectiveCostVnd!.Value.ToVnd()} " +
                 "(price + repairs).");
    }

    /// <summary>Tóm tắt cấu hình một dòng, đúng ngôn ngữ đang chọn.</summary>
    public static string Specs(MachineRecord r)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.CpuName))
            parts.Add(r.Cores > 0 ? S($"{r.CpuName} ({r.Cores} nhân/{r.Threads} luồng)", $"{r.CpuName} ({r.Cores}C/{r.Threads}T)") : r.CpuName);
        if (r.RamBytes > 0) parts.Add($"RAM {r.RamBytes.ToSize(0)} {r.RamType}".Trim());
        foreach (var d in r.Drives) parts.Add($"{d.Bytes.ToSize(0)} {d.Type}".Trim());
        if (r.Gpus.Count > 0) parts.Add(r.Gpus[0]);
        foreach (var d in r.Displays.Where(d => d.Inches is not null))
            parts.Add(d.RefreshHz is { } hz ? $"{d.Inches}\" {hz} Hz" : $"{d.Inches}\"");
        if (r.BatteryHealthPercent is { } bh) parts.Add(S($"pin {bh:0}%", $"battery {bh:0}%"));
        return string.Join(" · ", parts);
    }

    public static string DecisionText(PurchaseDecision d) => d switch
    {
        PurchaseDecision.Buy => S("Nên mua", "Good to buy"),
        PurchaseDecision.Negotiate => S("Thương lượng", "Negotiate"),
        PurchaseDecision.Avoid => S("Không nên mua", "Do not buy"),
        _ => "—"
    };

    private static string Note(MachineRecord r, bool best)
    {
        if (r.Decision == PurchaseDecision.Avoid)
            return S($"Bị loại: có {r.BlockerCount} lý do không nên mua.", $"Excluded: {r.BlockerCount} reason(s) not to buy.");
        if (r.AskedPriceVnd is null)
            return S("Chưa nhập giá — chưa xếp hạng được.", "No price entered — cannot be ranked.");
        return best
            ? S("Đáng tiền nhất trong danh sách.", "Best value in the list.")
            : string.Empty;
    }

    /// <summary>
    /// Đọc giá người dùng gõ: "12500000", "12.500.000", "12,5tr", "12.5 triệu", "12m", "800k", "800 nghìn".
    /// Số nhỏ hơn 1000 không kèm đơn vị hiểu là triệu, vì không ai bán máy dưới một nghìn đồng.
    /// </summary>
    public static long? ParsePrice(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim().ToLowerInvariant().Replace(" ", string.Empty).Replace("đ", string.Empty).Replace("vnd", string.Empty);

        double multiplier = 1;
        foreach (var (suffix, mul) in new[] { ("triệu", 1e6), ("tr", 1e6), ("m", 1e6), ("nghìn", 1e3), ("ngàn", 1e3), ("k", 1e3) })
        {
            if (t.EndsWith(suffix, StringComparison.Ordinal))
            {
                multiplier = mul;
                t = t[..^suffix.Length];
                break;
            }
        }

        if (multiplier == 1)
        {
            // Không đơn vị: dấu chấm/phẩy là phân cách hàng nghìn ("12.500.000").
            var digits = new string(t.Where(char.IsDigit).ToArray());
            if (!long.TryParse(digits, out var raw) || raw <= 0) return null;
            return raw < 1000 ? raw * 1_000_000 : raw;
        }

        if (!double.TryParse(t.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value) || value <= 0)
            return null;
        return (long)Math.Round(value * multiplier);
    }
}
