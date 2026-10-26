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

/// <summary>
/// Collector sinh câu chữ ngay lúc quét (tiêu đề, mô tả phát hiện).
/// Đổi ngôn ngữ thì dựng lại câu chữ đó từ dữ liệu đã đọc, không đụng tới phần cứng.
/// </summary>
public interface IRelocalizable
{
    void Relocalize(SystemSnapshot snapshot);
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
    IReadOnlyDictionary<string, string>? Extra = null)
{
    /// <summary>Dựng lại kết quả theo ngôn ngữ đang chọn từ số đo đã có; null khi không cần.</summary>
    public Func<BenchmarkResult>? Rebuild { get; init; }

    /// <summary>
    /// Dựng kết quả bằng hàm build và gắn luôn cách dựng lại, để đổi ngôn ngữ thì
    /// tên bài, đơn vị và mô tả đổi theo mà không phải chạy lại bài tải.
    /// </summary>
    public static BenchmarkResult Localized(Func<BenchmarkResult> build) =>
        build() with { Rebuild = () => Localized(build) };
}

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
