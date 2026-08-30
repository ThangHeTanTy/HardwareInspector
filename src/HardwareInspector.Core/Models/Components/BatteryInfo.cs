namespace HardwareInspector.Core.Models.Components;

public sealed class BatteryInfo
{
    public bool IsPresent { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string Chemistry { get; set; } = string.Empty;
    public DateTime? ManufactureDate { get; set; }

    public int DesignCapacityMwh { get; set; }
    public int FullChargeCapacityMwh { get; set; }
    public int? CycleCount { get; set; }
    public int ChargeRemainingPercent { get; set; }
    public int? EstimatedRuntimeMinutes { get; set; }
    public double? VoltageV { get; set; }
    public string PowerState { get; set; } = string.Empty;

    /// <summary>Sức khoẻ pin = dung lượng sạc đầy / dung lượng thiết kế.</summary>
    public double? HealthPercent =>
        DesignCapacityMwh > 0 ? Math.Round(FullChargeCapacityMwh * 100d / DesignCapacityMwh, 1) : null;
}
