using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Assessment;
using HardwareInspector.Core.Utils;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Analysis.Scoring;

/// <summary>
/// Bảng điểm của một linh kiện. Bắt đầu từ 100 và trừ dần theo từng phát hiện.
/// Điểm trừ được nhân với độ tin cậy để một suy đoán yếu không kéo tụt điểm như bằng chứng cứng.
/// </summary>
public sealed class ScoreCard
{
    private readonly ComponentAssessment _assessment;
    private double _score = 100;

    public ScoreCard(ComponentKind kind, string displayName)
    {
        _assessment = new ComponentAssessment { Kind = kind, DisplayName = displayName };
    }

    public ScoreCard Metric(string label, string? value, string? note = null, Severity severity = Severity.Info)
    {
        // Chấp nhận null và quy về gạch ngang thay vì để null lọt xuống giao diện.
        _assessment.Metrics.Add(new MetricLine(label, string.IsNullOrWhiteSpace(value) ? "—" : value, note, severity));
        return this;
    }

    /// <summary>
    /// Thêm dòng thông số khi điều kiện đúng.
    ///
    /// Cẩn thận: đây là method bình thường nên C# tính TẤT CẢ đối số trước khi gọi.
    /// Điều kiện không hề ngăn biểu thức <paramref name="value"/> chạy. Viết
    /// <c>MetricIf(x.Slot is not null, "...", $"{x.Slot!.Speed}")</c> sẽ ném
    /// NullReferenceException khi Slot là null, vì dấu ! chỉ tắt cảnh báo biên dịch
    /// chứ không có tác dụng lúc chạy.
    ///
    /// Khi biểu thức giá trị có thể ném lỗi, hãy dùng overload nhận Func bên dưới,
    /// hoặc tách ra biến rồi kiểm tra bằng if thường.
    /// </summary>
    public ScoreCard MetricIf(bool condition, string label, string? value, string? note = null, Severity severity = Severity.Info)
        => condition ? Metric(label, value, note, severity) : this;

    /// <summary>
    /// Bản lười của <see cref="MetricIf(bool, string, string?, string?, Severity)"/>:
    /// biểu thức giá trị chỉ chạy khi điều kiện đúng, nên an toàn với dữ liệu có thể null.
    /// </summary>
    public ScoreCard MetricIf(bool condition, string label, Func<string?> value,
        string? note = null, Severity severity = Severity.Info)
        => condition ? Metric(label, value(), note, severity) : this;

    public ScoreCard Add(Finding finding)
    {
        _assessment.Findings.Add(finding);
        _score -= finding.ScorePenalty * Math.Clamp(finding.Confidence, 0, 1);
        return this;
    }

    public ScoreCard Add(string code, ComponentKind kind, Severity severity, string title,
        string? detail = null, string? recommendation = null, int penalty = 0,
        double confidence = 1.0, EvidenceSource source = EvidenceSource.Wmi)
        => Add(new Finding
        {
            Code = code,
            Component = kind,
            Severity = severity,
            Title = title,
            Detail = detail,
            Recommendation = recommendation,
            ScorePenalty = penalty,
            Confidence = confidence,
            Source = source
        });

    public ScoreCard Health(double? percent)
    {
        _assessment.HealthPercent = percent;
        return this;
    }

    public ScoreCard Uptime(TimeSpan? span)
    {
        _assessment.OperatingTime = span;
        return this;
    }

    public ScoreCard Headline(string text)
    {
        _assessment.Headline = text;
        return this;
    }

    /// <summary>Áp trần điểm — dùng khi một dữ kiện đơn lẻ đã đủ giới hạn mức đánh giá.</summary>
    public ScoreCard Cap(int maximum)
    {
        _score = Math.Min(_score, maximum);
        return this;
    }

    public ScoreCard AttachSensors(IEnumerable<Core.Models.Sensors.SensorStats> sensors)
    {
        _assessment.Sensors.AddRange(sensors);
        return this;
    }

    public ComponentAssessment Build()
    {
        _assessment.Score = (int)Math.Round(Math.Clamp(_score, 0, 100));
        _assessment.Rating = RatingScale.FromScore(_assessment.Score);
        if (string.IsNullOrWhiteSpace(_assessment.Headline))
            _assessment.Headline = $"{_assessment.Rating.ToText()} — {_assessment.Score}/100";
        return _assessment;
    }

    /// <summary>Không đủ dữ liệu thì trả về Unknown thay vì đoán bừa một con điểm.</summary>
    public ComponentAssessment BuildUnknown(string reason)
    {
        _assessment.Score = 0;
        _assessment.Rating = HealthRating.Unknown;
        _assessment.Headline = reason;
        return _assessment;
    }
}
