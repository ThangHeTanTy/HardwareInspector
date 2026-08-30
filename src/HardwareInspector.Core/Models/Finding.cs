namespace HardwareInspector.Core.Models;

/// <summary>
/// Một phát hiện đơn lẻ trong quá trình kiểm tra. Đây là đơn vị nhỏ nhất
/// mà mọi lớp phân tích, cảnh báo và báo cáo đều tiêu thụ.
/// </summary>
public sealed record Finding
{
    public required string Code { get; init; }              // ví dụ: FW-WPBT-001
    public required ComponentKind Component { get; init; }
    public required Severity Severity { get; init; }
    public required string Title { get; init; }
    public string? Detail { get; init; }
    public string? Recommendation { get; init; }
    public EvidenceSource Source { get; init; } = EvidenceSource.Wmi;

    /// <summary>Điểm trừ áp vào ScoreCard của linh kiện (0..100).</summary>
    public int ScorePenalty { get; init; }

    /// <summary>Mức độ chắc chắn 0..1. Heuristic thì thấp, bằng chứng cứng thì cao.</summary>
    public double Confidence { get; init; } = 1.0;

    /// <summary>Dữ liệu thô kèm theo để người dùng tự đối chiếu.</summary>
    public IReadOnlyDictionary<string, string>? Evidence { get; init; }

    public static Finding Info(string code, ComponentKind kind, string title, string? detail = null) =>
        new() { Code = code, Component = kind, Severity = Severity.Info, Title = title, Detail = detail };
}
