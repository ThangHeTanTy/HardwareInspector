using static HardwareInspector.Core.Localization.Loc;

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
    /// <summary>Mã BatteryStatus của Win32_Battery; -1 khi không đọc được.</summary>
    public int PowerStateCode { get; set; } = -1;

    public string PowerState => PowerStateCode switch
    {
        1 => S("Đang xả", "Discharging"),
        2 => S("Cắm sạc (AC)", "On AC power"),
        3 => S("Đã đầy", "Fully charged"),
        4 => S("Yếu", "Low"),
        5 => S("Rất yếu", "Critical"),
        6 => S("Đang sạc", "Charging"),
        11 => S("Đang sạc, mức cao", "Charging, high"),
        _ => S("Không xác định", "Unknown")
    };

    /// <summary>Sức khoẻ pin = dung lượng sạc đầy / dung lượng thiết kế.</summary>
    public double? HealthPercent =>
        DesignCapacityMwh > 0 ? Math.Round(FullChargeCapacityMwh * 100d / DesignCapacityMwh, 1) : null;
}
