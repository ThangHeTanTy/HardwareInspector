using HardwareInspector.Core.Models.Sensors;

namespace HardwareInspector.Core.Models.Assessment;

/// <summary>Một dòng thông số hiển thị trong bảng chi tiết linh kiện.</summary>
public sealed record MetricLine(string Label, string Value, string? Note = null, Severity Severity = Severity.Info);

public sealed class ComponentAssessment
{
    public required ComponentKind Kind { get; init; }
    public required string DisplayName { get; init; }
    public string Headline { get; set; } = string.Empty;

    public int Score { get; set; } = 100;
    public HealthRating Rating { get; set; } = HealthRating.Unknown;

    public List<MetricLine> Metrics { get; } = new();
    public List<Finding> Findings { get; } = new();
    public List<SensorStats> Sensors { get; } = new();

    /// <summary>Ước tính thời gian đã hoạt động (nếu linh kiện có đồng hồ đếm giờ).</summary>
    public TimeSpan? OperatingTime { get; set; }

    /// <summary>Sức khoẻ còn lại tính theo % (pin, SSD...). Null nếu không đo được.</summary>
    public double? HealthPercent { get; set; }

    public bool HasCritical => Findings.Any(f => f.Severity == Severity.Critical);
}

/// <summary>Một phép đối chiếu chéo giữa nhiều nguồn dữ liệu độc lập.</summary>
public sealed record CrossCheckResult(
    string Name,
    bool Passed,
    string Explanation,
    Severity SeverityIfFailed = Severity.Warning);

public sealed class TrustReport
{
    public TrustLevel Level { get; set; } = TrustLevel.Unknown;
    public int TrustScore { get; set; } = 100;
    public string Verdict { get; set; } = string.Empty;
    public List<CrossCheckResult> CrossChecks { get; } = new();
    public List<Finding> RedFlags { get; } = new();
    public List<ManualCheckItem> ManualChecklist { get; } = new();
}

public sealed class MachineAssessment
{
    public DateTime GeneratedAtLocal { get; set; } = DateTime.Now;
    public string MachineTitle { get; set; } = string.Empty;
    public int OverallScore { get; set; }
    public HealthRating OverallRating { get; set; } = HealthRating.Unknown;
    public string Summary { get; set; } = string.Empty;

    public List<ComponentAssessment> Components { get; } = new();
    public List<WarrantyLookup> WarrantyLookups { get; } = new();
    public TrustReport Trust { get; set; } = new();
    public Models.Firmware.FirmwareIntegrityReport Firmware { get; set; } = new();

    public ComponentAssessment? this[ComponentKind kind] =>
        Components.FirstOrDefault(c => c.Kind == kind);
}
