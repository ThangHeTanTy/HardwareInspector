using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Assessment;
using HardwareInspector.Core.Models.Sensors;

namespace HardwareInspector.Core.Abstractions;

/// <summary>Thu thập một phần dữ liệu vào snapshot. Mỗi collector chỉ lo đúng phần của mình.</summary>
public interface IInfoCollector
{
    string Name { get; }
    int Order { get; }

    /// <summary>
    /// Chi phí thời gian tương đối, dùng để chia tỉ lệ thanh tiến độ.
    /// Các bước không nặng cứ để mặc định; chỉ những bước thật sự tốn thời gian
    /// (quét nhật ký sự kiện, đọc S.M.A.R.T. từng ổ) mới cần khai lại.
    /// Chia đều cho mọi bước sẽ khiến thanh tiến độ đứng im rất lâu ở đúng các bước đó.
    /// </summary>
    int Weight => 1;

    Task CollectAsync(SystemSnapshot snapshot, CancellationToken ct = default);
}

/// <summary>Biến dữ liệu thô của một linh kiện thành đánh giá có điểm số và cảnh báo.</summary>
public interface IComponentAnalyzer
{
    ComponentKind Kind { get; }
    bool CanAnalyze(SystemSnapshot snapshot);
    ComponentAssessment Analyze(SystemSnapshot snapshot);
}

/// <summary>Một phép kiểm tra tính toàn vẹn firmware độc lập.</summary>
public interface IIntegrityCheck
{
    string Id { get; }
    string Description { get; }
    bool RequiresAdmin { get; }
    IEnumerable<Finding> Run(SystemSnapshot snapshot);
}

public interface ISensorMonitor : IDisposable
{
    bool IsAvailable { get; }
    event EventHandler<SensorReading>? ReadingCaptured;
    Task StartAsync(TimeSpan interval, CancellationToken ct = default);
    Task StopAsync();
    IReadOnlyCollection<SensorStats> GetStatistics();
    void ResetStatistics();
}

public sealed record BenchmarkResult(
    string Name,
    string Unit,
    double Value,
    string? Detail = null,
    IReadOnlyDictionary<string, string>? Extra = null);

public interface IBenchmark
{
    string Name { get; }
    ComponentKind Target { get; }
    TimeSpan EstimatedDuration { get; }
    Task<BenchmarkResult> RunAsync(IProgress<double>? progress, CancellationToken ct = default);
}

public interface IReportExporter
{
    string Format { get; }
    string FileExtension { get; }
    Task<string> ExportAsync(MachineAssessment assessment, SystemSnapshot snapshot, string outputPath, CancellationToken ct = default);
}

public interface IAppLogger
{
    void Debug(string message);
    void Info(string message);
    void Warn(string message, Exception? ex = null);
    void Error(string message, Exception? ex = null);
}
