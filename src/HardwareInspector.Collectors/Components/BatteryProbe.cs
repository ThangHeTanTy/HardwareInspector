using HardwareInspector.Collectors.Wmi;

namespace HardwareInspector.Collectors.Components;

/// <summary>Một lần đọc trạng thái pin trong lúc đo xả.</summary>
/// <param name="RemainingMwh">Dung lượng còn lại, mWh. Null nếu pin không báo.</param>
/// <param name="DischargeRateMw">Công suất đang xả, mW. Null hoặc 0 khi đang cắm sạc.</param>
/// <param name="OnAc">Đang cắm sạc.</param>
/// <param name="Percent">Phần trăm pin còn lại.</param>
public sealed record BatterySample(DateTime Time, double? RemainingMwh, double? DischargeRateMw, bool OnAc, int? Percent);

/// <summary>
/// Đọc trạng thái pin tức thời cho bài đo xả. root\WMI\BatteryStatus cho mWh và công suất xả
/// chính xác; máy nào không có thì lùi về phần trăm của Win32_Battery.
/// </summary>
public static class BatteryProbe
{
    public static BatterySample Read()
    {
        var now = DateTime.UtcNow;
        double? remaining = null, rate = null;
        bool? onAc = null;

        var status = WmiQuery.QuerySingle(
            "SELECT RemainingCapacity, DischargeRate, PowerOnline FROM BatteryStatus", WmiQuery.WmiNamespace);
        if (status is not null)
        {
            var r = status.Long("RemainingCapacity", -1);
            if (r > 0) remaining = r;
            var d = status.Long("DischargeRate", -1);
            if (d > 0) rate = d;
            onAc = status.Bool("PowerOnline");
        }

        var battery = WmiQuery.QuerySingle("SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery");
        int? percent = null;
        if (battery is not null && battery.Int("EstimatedChargeRemaining", -1) is var p && p >= 0) percent = p;

        // BatteryStatus 1 = đang xả; 2 trở lên là đang cắm sạc hoặc đã đầy.
        onAc ??= battery is not null && battery.Int("BatteryStatus", 2) != 1;

        return new BatterySample(now, remaining, rate, onAc.Value, percent);
    }

    /// <summary>Dung lượng sạc đầy hiện tại, mWh — mẫu số để quy công suất xả ra số giờ dùng.</summary>
    public static double? FullChargeMwh()
    {
        var full = WmiQuery.QuerySingle("SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", WmiQuery.WmiNamespace);
        var value = full?.Long("FullChargedCapacity", -1) ?? -1;
        return value > 0 ? value : null;
    }
}
