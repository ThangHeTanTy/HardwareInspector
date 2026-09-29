using HardwareInspector.Analysis;
using HardwareInspector.Collectors.Components;
using HardwareInspector.Collectors.EventLogs;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Assessment;
using HardwareInspector.Core.Models.Diagnostics;
using HardwareInspector.Firmware;
using HardwareInspector.Firmware.Upgrade;
using HardwareInspector.Sensors;

namespace HardwareInspector.App.Services;

public sealed record InspectionProgress(string Stage, double Fraction);

/// <summary>
/// Điều phối một lượt kiểm tra hoàn chỉnh: thu thập dữ liệu, đo cảm biến, phân tích.
///
/// Thứ tự có chủ đích. Cảm biến được bật trước khi thu thập để phần đo min/max
/// có ít nhất một khoảng thời gian ở trạng thái nghỉ làm mốc so sánh với lúc chạy tải.
/// </summary>
public sealed class InspectionPipeline : IDisposable
{
    private readonly List<IInfoCollector> _collectors;
    private readonly MachineAssessmentService _assessmentService = new();

    public ISensorMonitor SensorMonitor { get; }

    /// <summary>
    /// Kết quả bài kiểm tra GPU gần nhất. Giữ lại qua các lượt quét sau đó vì lỗi VRAM
    /// hay driver sập là sự thật về phần cứng, không mất đi khi máy nguội.
    /// </summary>
    public GpuTestResult? LastGpuTest { get; set; }

    public InspectionPipeline()
    {
        SensorMonitor = new LibreHardwareSensorMonitor();

        _collectors = new List<IInfoCollector>
        {
            new BaseboardCollector(),
            new ProcessorCollector(),
            new GraphicsCollector(),
            new MemoryCollector(),
            new StorageCollector(),
            new BatteryCollector(),
            new DisplayCollector(),
            new UpgradeCapabilityCollector(),
            new ReliabilityScanner(),
            new FirmwareIntegrityService(),
            new SensorSnapshotCollector(SensorMonitor)
        };

        _collectors.Sort((a, b) => a.Order.CompareTo(b.Order));
    }

    /// <summary>Bật vòng đo cảm biến. Gọi ngay khi mở ứng dụng để tích luỹ dữ liệu nền.</summary>
    public Task StartSensorsAsync(CancellationToken ct = default) =>
        SensorMonitor.StartAsync(TimeSpan.FromSeconds(1), ct);

    public async Task<(SystemSnapshot Snapshot, MachineAssessment Assessment)> RunAsync(
        IProgress<InspectionProgress>? progress = null, CancellationToken ct = default)
    {
        var snapshot = new SystemSnapshot { GpuTest = LastGpuTest };

        // Bước phân tích cuối cũng chiếm thời gian nên tính luôn vào tổng trọng số,
        // nếu không thanh tiến độ sẽ nhảy tới 100% rồi mới đứng chờ.
        const int analysisWeight = 2;
        var totalWeight = _collectors.Sum(c => c.Weight) + analysisWeight;
        var doneWeight = 0;

        void Report(string stage, int weightDone) =>
            progress?.Report(new InspectionProgress(stage, Math.Clamp(weightDone / (double)totalWeight, 0, 1)));

        foreach (var collector in _collectors)
        {
            ct.ThrowIfCancellationRequested();
            Report(collector.Name, doneWeight);

            try
            {
                // Bắt buộc phải đẩy sang luồng nền.
                //
                // Gần như mọi collector đều làm việc đồng bộ rồi trả Task.CompletedTask,
                // nên await ở đây không hề nhường quyền điều khiển. Gọi thẳng sẽ khiến
                // cả lượt quét chạy trên luồng giao diện: thanh tiến độ vẫn được gán giá trị
                // nhưng cửa sổ không có cơ hội vẽ lại, nhìn như ứng dụng bị treo.
                await Task.Run(() => collector.CollectAsync(snapshot, ct), ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                snapshot.ReliabilityFindings.Add(new Finding
                {
                    Code = "PIPE-ERR",
                    Component = ComponentKind.Motherboard,
                    Severity = Severity.Info,
                    Title = $"Bước \"{collector.Name}\" không hoàn tất",
                    Detail = ex.Message
                });
            }

            doneWeight += collector.Weight;
            Report(collector.Name, doneWeight);
        }

        ct.ThrowIfCancellationRequested();
        Report("Phân tích và chấm điểm", doneWeight);

        var assessment = await Task.Run(() => _assessmentService.Assess(snapshot), ct);

        Report("Hoàn tất", totalWeight);
        return (snapshot, assessment);
    }

    public void Dispose() => SensorMonitor.Dispose();
}
