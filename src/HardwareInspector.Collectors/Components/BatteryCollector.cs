using HardwareInspector.Collectors.Wmi;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Components;

namespace HardwareInspector.Collectors.Components;

/// <summary>
/// Ghép dữ liệu pin từ ba lớp: Win32_Battery (trạng thái),
/// BatteryStaticData (dung lượng thiết kế, ngày sản xuất) và
/// BatteryFullChargedCapacity / BatteryCycleCount (hao mòn thực tế).
/// </summary>
public sealed class BatteryCollector : IInfoCollector
{
    public string Name => "Pin";
    public int Order => 50;

    // Ba truy vấn sang namespace root\\WMI, mỗi truy vấn đều có độ trễ đáng kể.
    public int Weight => 2;

    public Task CollectAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        foreach (var mo in WmiQuery.Query(
            "SELECT Name, DeviceID, Chemistry, EstimatedChargeRemaining, EstimatedRunTime, " +
            "DesignVoltage, BatteryStatus FROM Win32_Battery"))
        {
            ct.ThrowIfCancellationRequested();

            var battery = new BatteryInfo
            {
                IsPresent = true,
                Name = mo.Str("Name"),
                Chemistry = TranslateChemistry(mo.Int("Chemistry", -1)),
                ChargeRemainingPercent = mo.Int("EstimatedChargeRemaining"),
                PowerState = TranslateStatus(mo.Int("BatteryStatus", -1)),
                VoltageV = mo.Long("DesignVoltage") / 1000d
            };

            var runtime = mo.Int("EstimatedRunTime");
            if (runtime is > 0 and < 71582788) battery.EstimatedRuntimeMinutes = runtime;

            EnrichFromWmiNamespace(battery);
            snapshot.Batteries.Add(battery);
        }

        if (snapshot.Batteries.Count == 0)
            snapshot.Batteries.Add(new BatteryInfo { IsPresent = false, Name = "Không có pin (máy bàn)" });

        return Task.CompletedTask;
    }

    private static void EnrichFromWmiNamespace(BatteryInfo battery)
    {
        var stat = WmiQuery.QuerySingle("SELECT * FROM BatteryStaticData", WmiQuery.WmiNamespace);
        if (stat is not null)
        {
            battery.DesignCapacityMwh = stat.Int("DesignedCapacity");
            battery.Manufacturer = stat.Str("ManufactureName");
            battery.SerialNumber = stat.Str("SerialNumber");
            battery.ManufactureDate = ParseManufactureDate(stat);
            if (string.IsNullOrWhiteSpace(battery.Chemistry))
                battery.Chemistry = stat.Str("Chemistry");
        }

        var full = WmiQuery.QuerySingle("SELECT * FROM BatteryFullChargedCapacity", WmiQuery.WmiNamespace);
        if (full is not null) battery.FullChargeCapacityMwh = full.Int("FullChargedCapacity");

        var cycle = WmiQuery.QuerySingle("SELECT * FROM BatteryCycleCount", WmiQuery.WmiNamespace);
        if (cycle is not null)
        {
            var count = cycle.Int("CycleCount");
            if (count > 0) battery.CycleCount = count;
        }
    }

    private static DateTime? ParseManufactureDate(System.Management.ManagementBaseObject stat)
    {
        // BatteryStaticData phơi bày ManufactureDate dạng bit-packed của Smart Battery Spec:
        // bit 0-4 = ngày, bit 5-8 = tháng, bit 9-15 = năm kể từ 1980.
        var packed = stat.Int("ManufactureDate");
        if (packed <= 0) return null;
        try
        {
            var day = packed & 0x1F;
            var month = (packed >> 5) & 0x0F;
            var year = 1980 + ((packed >> 9) & 0x7F);
            if (day is < 1 or > 31 || month is < 1 or > 12) return null;
            return new DateTime(year, month, day);
        }
        catch { return null; }
    }

    private static string TranslateChemistry(int code) => code switch
    {
        3 => "Lead Acid",
        4 => "Nickel Cadmium",
        5 => "Nickel Metal Hydride",
        6 => "Lithium-ion",
        7 => "Zinc Air",
        8 => "Lithium Polymer",
        _ => "Không xác định"
    };

    private static string TranslateStatus(int code) => code switch
    {
        1 => "Đang xả",
        2 => "Cắm sạc (AC)",
        3 => "Đã đầy",
        4 => "Yếu",
        5 => "Rất yếu",
        6 => "Đang sạc",
        11 => "Đang sạc, mức cao",
        _ => "Không xác định"
    };
}
