using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;

namespace HardwareInspector.Sensors;

/// <summary>
/// Đưa kết quả đo cảm biến vào snapshot. Chạy sau cùng để bao gồm cả
/// dữ liệu thu được trong lúc benchmark.
/// </summary>
public sealed class SensorSnapshotCollector : IInfoCollector
{
    private readonly ISensorMonitor _monitor;

    public SensorSnapshotCollector(ISensorMonitor monitor) => _monitor = monitor;

    public string Name => "Cảm biến nhiệt độ và xung nhịp";
    public int Order => 100;

    public Task CollectAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        if (_monitor is LibreHardwareSensorMonitor lhm) lhm.SampleOnce();

        snapshot.SensorSnapshot.Clear();
        snapshot.SensorSnapshot.AddRange(_monitor.GetStatistics());
        return Task.CompletedTask;
    }
}
