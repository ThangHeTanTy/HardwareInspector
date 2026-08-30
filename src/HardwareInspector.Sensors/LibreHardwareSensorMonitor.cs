using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Sensors;
using LibreHardwareMonitor.Hardware;

namespace HardwareInspector.Sensors;

/// <summary>
/// Bọc LibreHardwareMonitor để lấy nhiệt độ, xung nhịp, tải, quạt, điện áp và công suất.
/// Thư viện này nạp một driver kernel để đọc MSR và Super I/O nên bắt buộc chạy quyền Administrator;
/// không có quyền thì IsAvailable trả về false và toàn bộ phần cảm biến bị vô hiệu hoá một cách êm ái.
/// </summary>
public sealed class LibreHardwareSensorMonitor : ISensorMonitor
{
    private readonly Computer? _computer;
    private readonly Dictionary<string, SensorStats> _stats = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private readonly UpdateVisitor _visitor = new();

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _opened;

    public bool IsAvailable { get; private set; }
    public event EventHandler<SensorReading>? ReadingCaptured;

    public LibreHardwareSensorMonitor()
    {
        // Toàn bộ khối này nằm trong try, kể cả dòng khởi tạo.
        // LibreHardwareMonitorLib nạp một driver kernel; nếu thiếu quyền Administrator,
        // driver bị chặn, hoặc chính assembly không giải quyết được thì đây là nơi ném lỗi.
        // Mất cảm biến chỉ làm giảm chất lượng đánh giá, không được phép làm sập ứng dụng.
        try
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsStorageEnabled = true,
                IsBatteryEnabled = true,
                IsControllerEnabled = true
            };

            _computer.Open();
            _opened = true;
            IsAvailable = _computer.Hardware.Count > 0;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            InitializationError = ex.Message;
        }
    }

    /// <summary>Thông báo vì sao không đo được cảm biến, để giao diện nói rõ với người dùng.</summary>
    public string? InitializationError { get; }

    public Task StartAsync(TimeSpan interval, CancellationToken ct = default)
    {
        if (!IsAvailable || _loop is not null) return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;

        _loop = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                SampleOnce();
                try { await Task.Delay(interval, token); }
                catch (OperationCanceledException) { break; }
            }
        }, token);

        return Task.CompletedTask;
    }

    /// <summary>Lấy một mẫu ngay lập tức. Dùng khi cần đo trước/sau một bài stress test.</summary>
    public void SampleOnce()
    {
        if (!IsAvailable || _computer is null) return;
        var now = DateTime.UtcNow;

        try
        {
            _computer.Accept(_visitor);

            foreach (var hardware in _computer.Hardware)
            {
                Collect(hardware, now);
                foreach (var sub in hardware.SubHardware)
                    Collect(sub, now);
            }
        }
        catch { /* một lần đọc lỗi không nên làm sập cả phiên đo */ }
    }

    private void Collect(IHardware hardware, DateTime now)
    {
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.Value is not { } value) continue;

            var kind = Map(sensor.SensorType);
            if (kind is null) continue;

            var key = $"{hardware.Name}|{sensor.Name}|{kind}";

            lock (_sync)
            {
                if (!_stats.TryGetValue(key, out var stat))
                {
                    stat = new SensorStats
                    {
                        HardwareName = hardware.Name,
                        SensorName = sensor.Name,
                        Kind = kind.Value
                    };
                    _stats[key] = stat;
                }
                stat.Push(value, now);
            }

            ReadingCaptured?.Invoke(this,
                new SensorReading(hardware.Name, sensor.Name, kind.Value, value, now));
        }
    }

    private static SensorKind? Map(SensorType type) => type switch
    {
        SensorType.Temperature => SensorKind.Temperature,
        SensorType.Load => SensorKind.Load,
        SensorType.Clock => SensorKind.Clock,
        SensorType.Fan => SensorKind.Fan,
        SensorType.Voltage => SensorKind.Voltage,
        SensorType.Power => SensorKind.Power,
        SensorType.Data => SensorKind.Data,
        SensorType.Throughput => SensorKind.Throughput,
        SensorType.Level => SensorKind.Level,
        _ => null
    };

    public async Task StopAsync()
    {
        if (_cts is null) return;
        _cts.Cancel();
        if (_loop is not null)
        {
            try { await _loop; } catch { }
        }
        _loop = null;
        _cts.Dispose();
        _cts = null;
    }

    public IReadOnlyCollection<SensorStats> GetStatistics()
    {
        lock (_sync) return _stats.Values.Where(s => s.HasData).ToList();
    }

    public void ResetStatistics()
    {
        lock (_sync) _stats.Clear();
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        if (_opened && _computer is not null)
        {
            try { _computer.Close(); } catch { }
            _opened = false;
        }
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware) sub.Accept(this);
        }

        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}
