namespace HardwareInspector.Core.Models.Sensors;

public sealed record SensorReading(
    string HardwareName,
    string SensorName,
    SensorKind Kind,
    float Value,
    DateTime TimestampUtc);

/// <summary>Thống kê min / max / trung bình của một cảm biến trong phiên đo.</summary>
public sealed class SensorStats
{
    public required string HardwareName { get; init; }
    public required string SensorName { get; init; }
    public required SensorKind Kind { get; init; }

    public float Min { get; private set; } = float.MaxValue;
    public float Max { get; private set; } = float.MinValue;
    public float Current { get; private set; }
    public double Sum { get; private set; }
    public long Samples { get; private set; }
    public DateTime FirstSampleUtc { get; private set; }
    public DateTime LastSampleUtc { get; private set; }

    public float Average => Samples == 0 ? 0f : (float)(Sum / Samples);
    public bool HasData => Samples > 0;
    public TimeSpan Window => Samples == 0 ? TimeSpan.Zero : LastSampleUtc - FirstSampleUtc;

    public void Push(float value, DateTime utc)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return;
        if (Samples == 0) FirstSampleUtc = utc;
        LastSampleUtc = utc;
        Current = value;
        if (value < Min) Min = value;
        if (value > Max) Max = value;
        Sum += value;
        Samples++;
    }

    public string Unit => Kind switch
    {
        SensorKind.Temperature => "°C",
        SensorKind.Load => "%",
        SensorKind.Clock => "MHz",
        SensorKind.Fan => "RPM",
        SensorKind.Voltage => "V",
        SensorKind.Power => "W",
        SensorKind.Data => "GB",
        SensorKind.Throughput => "MB/s",
        _ => string.Empty
    };
}
