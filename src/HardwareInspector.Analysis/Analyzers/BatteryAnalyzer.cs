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
            return new ScoreCard(Kind, "Pin")
                .BuildUnknown("Máy không có pin — bỏ qua hạng mục này");

        var card = new ScoreCard(Kind, battery.Name.OrDash());

        card.Metric("Hãng sản xuất", battery.Manufacturer.OrDash())
            .Metric("Loại pin", battery.Chemistry.OrDash())
            .Metric("Trạng thái", battery.PowerState)
            .Metric("Mức sạc hiện tại", $"{battery.ChargeRemainingPercent}%");

        if (battery.DesignCapacityMwh > 0)
            card.Metric("Dung lượng thiết kế", $"{battery.DesignCapacityMwh:N0} mWh");
        if (battery.FullChargeCapacityMwh > 0)
            card.Metric("Dung lượng sạc đầy hiện tại", $"{battery.FullChargeCapacityMwh:N0} mWh");

        if (battery.ManufactureDate is { } made)
        {
            var age = DateTime.Now - made;
            card.Metric("Ngày sản xuất pin", made.ToString("dd/MM/yyyy"),
                $"Đã {age.TotalDays / 365:0.#} năm");
        }

        if (battery.HealthPercent is { } health)
        {
            card.Health(health);
            card.Metric("Độ chai pin", $"Còn {health:0.#}% so với thiết kế");

            if (health < 50)
                card.Add("BAT-HEALTH-001", Kind, Severity.Critical,
                    $"Pin chỉ còn {health:0}% dung lượng thiết kế",
                    "Thời lượng dùng thực tế chưa tới một nửa lúc mới. Pin ở mức này cũng dễ sụt áp đột ngột gây tắt máy.",
                    "Tính chi phí thay pin vào giá mua. Pin chính hãng thường là khoản không nhỏ.",
                    penalty: 45, confidence: 0.95);
            else if (health < 70)
                card.Add("BAT-HEALTH-002", Kind, Severity.Warning,
                    $"Pin còn {health:0}% dung lượng thiết kế",
                    "Pin đã chai đáng kể nhưng vẫn dùng được cho nhu cầu di chuyển ngắn.",
                    "Thương lượng giảm giá tương ứng chi phí thay pin.",
                    penalty: 22, confidence: 0.95);
            else if (health < 85)
                card.Add("BAT-HEALTH-003", Kind, Severity.Notice,
                    $"Pin còn {health:0}% dung lượng thiết kế",
                    "Mức chai bình thường với máy dùng 2-3 năm.",
                    penalty: 8, confidence: 0.95);
        }
        else
        {
            card.Add("BAT-INFO-001", Kind, Severity.Notice,
                "Không đọc được dung lượng thiết kế của pin",
                "Cần quyền Administrator để truy cập namespace root\\WMI.",
                "Chạy lại ứng dụng với quyền Administrator, hoặc dùng lệnh 'powercfg /batteryreport' để đối chứng.",
                penalty: 5, confidence: 0.9);
        }

        if (battery.CycleCount is { } cycles and > 0)
        {
            card.Metric("Số chu kỳ sạc", $"{cycles:N0}");

            if (cycles > 1000)
                card.Add("BAT-CYCLE-001", Kind, Severity.Critical,
                    $"Pin đã qua {cycles:N0} chu kỳ sạc",
                    "Vượt xa mức 300-500 chu kỳ mà hầu hết nhà sản xuất công bố. " +
                    "Máy này được dùng cường độ cao trong thời gian dài, bất kể vỏ ngoài còn đẹp.",
                    "Đối chiếu với lời người bán về thời gian sử dụng. Chênh lệch lớn là dấu hiệu không trung thực.",
                    penalty: 30, confidence: 0.95);
            else if (cycles > 500)
                card.Add("BAT-CYCLE-002", Kind, Severity.Warning,
                    $"Pin đã qua {cycles:N0} chu kỳ sạc",
                    "Đã vượt ngưỡng bảo hành dung lượng của phần lớn hãng.",
                    penalty: 15, confidence: 0.95);
        }
        else
        {
            card.Metric("Số chu kỳ sạc", "Không đọc được",
                "Nhiều dòng pin không phơi bày trường này qua WMI", Severity.Notice);
        }

        if (battery.EstimatedRuntimeMinutes is { } runtime)
            card.Metric("Thời lượng ước tính còn lại", $"{runtime} phút");

        return card.Build();
    }
}
