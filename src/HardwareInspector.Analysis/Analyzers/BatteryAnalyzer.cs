using HardwareInspector.Analysis.Scoring;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Assessment;
using HardwareInspector.Core.Utils;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Analysis.Analyzers;

/// <summary>
/// Pin là đồng hồ đếm tuổi thật của một chiếc laptop. Người bán có thể cài lại Windows,
/// thay ổ cứng, lau sạch vỏ — nhưng số chu kỳ sạc và độ chai pin thì nằm trong
/// vi điều khiển của chính viên pin.
/// </summary>
public sealed class BatteryAnalyzer : IComponentAnalyzer
{
    public ComponentKind Kind => ComponentKind.Battery;

    public bool CanAnalyze(SystemSnapshot snapshot) => snapshot.Batteries.Count > 0;

    public ComponentAssessment Analyze(SystemSnapshot snapshot)
    {
        var battery = snapshot.PrimaryBattery;

        if (battery is null || !battery.IsPresent)
            return new ScoreCard(Kind, S("Pin", "Battery"))
                .BuildUnknown(S("Máy không có pin — bỏ qua hạng mục này", "No battery — this item is skipped"));

        var card = new ScoreCard(Kind, battery.Name.OrDash());

        card.Metric(S("Hãng sản xuất", "Manufacturer"), battery.Manufacturer.OrDash())
            .Metric(S("Loại pin", "Chemistry"), battery.Chemistry.OrDash())
            .Metric(S("Trạng thái", "Status"), battery.PowerState)
            .Metric(S("Mức sạc hiện tại", "Current charge"), $"{battery.ChargeRemainingPercent}%");

        if (battery.DesignCapacityMwh > 0)
            card.Metric(S("Dung lượng thiết kế", "Design capacity"), $"{battery.DesignCapacityMwh:N0} mWh");
        if (battery.FullChargeCapacityMwh > 0)
            card.Metric(S("Dung lượng sạc đầy hiện tại", "Current full-charge capacity"), $"{battery.FullChargeCapacityMwh:N0} mWh");

        if (battery.ManufactureDate is { } made)
        {
            var age = DateTime.Now - made;
            card.Metric(S("Ngày sản xuất pin", "Battery manufacture date"), made.ToString("dd/MM/yyyy"),
                S($"Đã {age.TotalDays / 365:0.#} năm", $"{age.TotalDays / 365:0.#} years old"));
        }

        if (battery.HealthPercent is { } health)
        {
            card.Health(health);
            card.Metric(S("Độ chai pin", "Battery wear"), S($"Còn {health:0.#}% so với thiết kế", $"{health:0.#}% of design capacity left"));

            if (health < 50)
                card.Add("BAT-HEALTH-001", Kind, Severity.Critical,
                    S($"Pin chỉ còn {health:0}% dung lượng thiết kế", $"Battery has only {health:0}% of its design capacity"),
                    S("Thời lượng dùng thực tế chưa tới một nửa lúc mới. Pin ở mức này cũng dễ sụt áp đột ngột gây tắt máy.",
                      "Real runtime is under half of what it was new. A battery this worn can also sag suddenly and shut the machine down."),
                    S("Tính chi phí thay pin vào giá mua. Pin chính hãng thường là khoản không nhỏ.",
                      "Factor a replacement battery into the price. Genuine batteries are not cheap."),
                    penalty: 45, confidence: 0.95);
            else if (health < 70)
                card.Add("BAT-HEALTH-002", Kind, Severity.Warning,
                    S($"Pin còn {health:0}% dung lượng thiết kế", $"Battery has {health:0}% of its design capacity"),
                    S("Pin đã chai đáng kể nhưng vẫn dùng được cho nhu cầu di chuyển ngắn.",
                      "The battery is noticeably worn but still fine for short trips."),
                    S("Thương lượng giảm giá tương ứng chi phí thay pin.", "Negotiate a discount matching the cost of a new battery."),
                    penalty: 22, confidence: 0.95);
            else if (health < 85)
                card.Add("BAT-HEALTH-003", Kind, Severity.Notice,
                    S($"Pin còn {health:0}% dung lượng thiết kế", $"Battery has {health:0}% of its design capacity"),
                    S("Mức chai bình thường với máy dùng 2-3 năm.", "Normal wear for a machine used for 2-3 years."),
                    penalty: 8, confidence: 0.95);
        }
        else
        {
            card.Add("BAT-INFO-001", Kind, Severity.Notice,
                S("Không đọc được dung lượng thiết kế của pin", "Could not read the battery's design capacity"),
                S("Cần quyền Administrator để truy cập namespace root\\WMI.", "Administrator rights are needed to access the root\\WMI namespace."),
                S("Chạy lại ứng dụng với quyền Administrator, hoặc dùng lệnh 'powercfg /batteryreport' để đối chứng.",
                  "Run the app again as Administrator, or use 'powercfg /batteryreport' to cross-check."),
                penalty: 5, confidence: 0.9);
        }

        if (battery.CycleCount is { } cycles and > 0)
        {
            card.Metric(S("Số chu kỳ sạc", "Charge cycles"), $"{cycles:N0}");

            if (cycles > 1000)
                card.Add("BAT-CYCLE-001", Kind, Severity.Critical,
                    S($"Pin đã qua {cycles:N0} chu kỳ sạc", $"Battery has been through {cycles:N0} charge cycles"),
                    S("Vượt xa mức 300-500 chu kỳ mà hầu hết nhà sản xuất công bố. " +
                      "Máy này được dùng cường độ cao trong thời gian dài, bất kể vỏ ngoài còn đẹp.",
                      "Far beyond the 300-500 cycles most manufacturers rate. " +
                      "This machine has seen heavy use for a long time, however good the case looks."),
                    S("Đối chiếu với lời người bán về thời gian sử dụng. Chênh lệch lớn là dấu hiệu không trung thực.",
                      "Compare with what the seller says about usage. A big mismatch is a sign of dishonesty."),
                    penalty: 30, confidence: 0.95);
            else if (cycles > 500)
                card.Add("BAT-CYCLE-002", Kind, Severity.Warning,
                    S($"Pin đã qua {cycles:N0} chu kỳ sạc", $"Battery has been through {cycles:N0} charge cycles"),
                    S("Đã vượt ngưỡng bảo hành dung lượng của phần lớn hãng.", "Beyond the capacity warranty threshold of most manufacturers."),
                    penalty: 15, confidence: 0.95);
        }
        else
        {
            card.Metric(S("Số chu kỳ sạc", "Charge cycles"), S("Không đọc được", "Unreadable"),
                S("Nhiều dòng pin không phơi bày trường này qua WMI", "Many batteries do not expose this field through WMI"), Severity.Notice);
        }

        var drain = snapshot.DeviceTests;
        if (drain.BatteryRuntimeHours is { } realHours)
        {
            card.Metric(S("Thời lượng thực đo", "Measured runtime"),
                S($"{realHours:0.0} giờ", $"{realHours:0.0} h"),
                S($"Đo xả {drain.BatteryTestMinutes:0} phút khi rút sạc, công suất {drain.BatteryDrainWatts:0.0} W",
                  $"{drain.BatteryTestMinutes:0} min unplugged discharge test at {drain.BatteryDrainWatts:0.0} W"),
                realHours < 1.5 ? Severity.Warning : Severity.Info);

            if (realHours < 1.5)
                card.Add("BAT-RUN-001", Kind, realHours < 0.75 ? Severity.Critical : Severity.Warning,
                    S($"Pin thực tế chỉ dùng được khoảng {realHours:0.0} giờ", $"The battery really lasts only about {realHours:0.0} hours"),
                    S("Đo khi dùng nhẹ mà chưa tới 1,5 giờ thì gần như phải cắm sạc cả ngày. " +
                      "Con số này đáng tin hơn phần trăm độ chai vì đo trực tiếp lúc máy chạy.",
                      "Under 1.5 hours of light use means staying plugged in most of the day. " +
                      "This is more trustworthy than the wear percentage because it is measured while the machine runs."),
                    S("Tính chi phí thay pin vào giá mua.", "Factor a replacement battery into the price."),
                    penalty: realHours < 0.75 ? 30 : 15, confidence: 0.8, source: EvidenceSource.Benchmark);
        }

        if (battery.EstimatedRuntimeMinutes is { } runtime)
            card.Metric(S("Thời lượng ước tính còn lại", "Estimated runtime left"), S($"{runtime} phút", $"{runtime} min"));

        return card.Build();
    }
}
