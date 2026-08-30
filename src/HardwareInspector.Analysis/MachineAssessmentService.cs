using HardwareInspector.Analysis.Analyzers;
using HardwareInspector.Analysis.Trust;
using HardwareInspector.Analysis.Warranty;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Assessment;
using HardwareInspector.Core.Utils;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Analysis;

/// <summary>
/// Chạy toàn bộ analyzer, tính điểm tổng và sinh kết luận về độ tin cậy.
/// </summary>
public sealed class MachineAssessmentService
{
    private readonly IReadOnlyList<IComponentAnalyzer> _analyzers;
    private readonly SecondHandTrustEngine _trustEngine;
    private readonly ComponentAuthenticityEngine _authenticityEngine = new();
    private readonly WarrantyLookupService _warrantyService = new();

    /// <summary>
    /// Trọng số phản ánh mức độ ảnh hưởng của từng linh kiện tới trải nghiệm
    /// và tới rủi ro tài chính khi mua máy cũ. Firmware và ổ cứng nặng nhất
    /// vì đó là hai thứ vừa khó sửa vừa dễ mất dữ liệu.
    /// </summary>
    private static readonly Dictionary<ComponentKind, double> Weights = new()
    {
        [ComponentKind.Firmware] = 1.6,
        [ComponentKind.Storage] = 1.5,
        [ComponentKind.Cpu] = 1.2,
        [ComponentKind.Gpu] = 1.0,
        [ComponentKind.Memory] = 1.0,
        [ComponentKind.Battery] = 0.9,
        [ComponentKind.Display] = 0.9,
        [ComponentKind.Motherboard] = 0.8
    };

    public MachineAssessmentService(IEnumerable<IComponentAnalyzer>? analyzers = null,
        SecondHandTrustEngine? trustEngine = null)
    {
        _analyzers = analyzers?.ToList() ?? new List<IComponentAnalyzer>
        {
            new FirmwareAnalyzer(),
            new ProcessorAnalyzer(),
            new GraphicsAnalyzer(),
            new MemoryAnalyzer(),
            new StorageAnalyzer(),
            new BatteryAnalyzer(),
            new DisplayAnalyzer(),
            new MotherboardAnalyzer()
        };
        _trustEngine = trustEngine ?? new SecondHandTrustEngine();
    }

    public MachineAssessment Assess(SystemSnapshot snapshot)
    {
        var assessment = new MachineAssessment
        {
            MachineTitle = BuildTitle(snapshot),
            Firmware = snapshot.Firmware
        };

        // Chạy trước các analyzer để chúng nhặt được kết quả vào bảng điểm của mình.
        snapshot.AuthenticityFindings.Clear();
        try { snapshot.AuthenticityFindings.AddRange(_authenticityEngine.Inspect(snapshot)); }
        catch { }

        snapshot.WarrantyLookups.Clear();
        try { snapshot.WarrantyLookups.AddRange(_warrantyService.Build(snapshot)); }
        catch { }

        foreach (var analyzer in _analyzers)
        {
            if (!analyzer.CanAnalyze(snapshot)) continue;
            try { assessment.Components.Add(analyzer.Analyze(snapshot)); }
            catch (Exception ex)
            {
                assessment.Components.Add(new ComponentAssessment
                {
                    Kind = analyzer.Kind,
                    DisplayName = analyzer.Kind.ToString(),
                    Rating = HealthRating.Unknown,
                    Headline = S($"Phân tích thất bại: {ex.Message}", $"Analysis failed: {ex.Message}")
                });
            }
        }

        assessment.WarrantyLookups.AddRange(snapshot.WarrantyLookups);
        assessment.OverallScore = ComputeOverall(assessment.Components);
        assessment.OverallRating = RatingScale.FromScore(assessment.OverallScore);
        assessment.Trust = _trustEngine.Evaluate(snapshot, assessment.Components);
        assessment.Summary = BuildSummary(assessment);

        return assessment;
    }

    private static int ComputeOverall(IReadOnlyList<ComponentAssessment> components)
    {
        var scored = components.Where(c => c.Rating != HealthRating.Unknown).ToList();
        if (scored.Count == 0) return 0;

        var totalWeight = 0d;
        var weighted = 0d;

        foreach (var c in scored)
        {
            var w = Weights.TryGetValue(c.Kind, out var weight) ? weight : 1.0;
            weighted += c.Score * w;
            totalWeight += w;
        }

        var average = weighted / totalWeight;

        // Trung bình có trọng số dễ che giấu một linh kiện đang hỏng.
        // Nếu có linh kiện nào ở mức Yếu, điểm tổng không được vượt quá ngưỡng Khá.
        var worst = scored.Min(c => c.Score);
        if (worst < RatingScale.AverageThreshold)
            average = Math.Min(average, RatingScale.FairThreshold - 1);
        if (scored.Any(c => c.HasCritical))
            average = Math.Min(average, RatingScale.GoodThreshold - 1);

        return (int)Math.Round(Math.Clamp(average, 0, 100));
    }

    private static string BuildTitle(SystemSnapshot snapshot)
    {
        var b = snapshot.Baseboard;
        var name = $"{b.SystemManufacturer} {b.SystemProductName}".Trim();
        return string.IsNullOrWhiteSpace(name) ? Environment.MachineName : name;
    }

    private static string BuildSummary(MachineAssessment assessment)
    {
        var weak = assessment.Components
            .Where(c => c.Rating is HealthRating.Poor or HealthRating.Average)
            .OrderBy(c => c.Score)
            .ToList();

        var criticalCount = assessment.Components.SelectMany(c => c.Findings)
            .Count(f => f.Severity == Severity.Critical);

        var parts = new List<string>
        {
            S($"Đánh giá tổng thể: {assessment.OverallRating.ToText()} ({assessment.OverallScore}/100).", $"Overall rating: {assessment.OverallRating.ToText()} ({assessment.OverallScore}/100).")
        };

        if (weak.Count == 0)
            parts.Add(S("Không có linh kiện nào ở mức đáng lo ngại.", "No component is in worrying shape."));
        else
            parts.Add(S("Điểm yếu nhất nằm ở: ", "Weakest areas: ") +
                      string.Join(", ", weak.Take(3).Select(c => $"{Translate(c.Kind)} ({c.Rating.ToText()})")) + ".");

        if (criticalCount > 0)
            parts.Add(S($"Có {criticalCount} phát hiện ở mức nghiêm trọng cần xử lý.", $"{criticalCount} critical finding(s) need attention."));

        parts.Add(S($"Mức tin cậy khi mua lại: {assessment.Trust.Level.ToText()} ({assessment.Trust.TrustScore}/100).", $"Second-hand trust level: {assessment.Trust.Level.ToText()} ({assessment.Trust.TrustScore}/100)."));

        return string.Join(" ", parts);
    }

    /// <summary>Giữ lại để nơi gọi cũ không phải sửa; nội dung đã dời sang RatingScale.</summary>
    public static string Translate(ComponentKind kind) => kind.ToText();
}
