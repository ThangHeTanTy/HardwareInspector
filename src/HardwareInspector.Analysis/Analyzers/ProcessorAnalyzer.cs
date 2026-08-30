using HardwareInspector.Analysis.Scoring;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Assessment;
using HardwareInspector.Core.Models.Sensors;
using HardwareInspector.Core.Utils;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Analysis.Analyzers;

/// <summary>
/// CPU hiếm khi "chết dần" như ổ cứng, nên điểm số ở đây phản ánh ba thứ khác:
/// khả năng tản nhiệt còn tốt không, xung nhịp có đạt như thiết kế không,
/// và bản thân con CPU có phải hàng thật không.
/// </summary>
public sealed class ProcessorAnalyzer : IComponentAnalyzer
{
    public ComponentKind Kind => ComponentKind.Cpu;

    public bool CanAnalyze(SystemSnapshot snapshot) => snapshot.PrimaryCpu is not null;

    public ComponentAssessment Analyze(SystemSnapshot snapshot)
    {
        var cpu = snapshot.PrimaryCpu!;
        var card = new ScoreCard(Kind, cpu.Name);

        card.Metric("Số nhân / luồng", $"{cpu.PhysicalCores} nhân, {cpu.LogicalCores} luồng")
            .Metric("Kiến trúc", cpu.Architecture)
            .Metric("Socket", cpu.Socket)
            .MetricIf(cpu.L3CacheKb > 0, "Cache L3", $"{cpu.L3CacheKb / 1024.0:0.#} MB")
            .Metric("Xung nhịp thiết kế", $"{cpu.MaxClockMhz} MHz")
            .MetricIf(cpu.MicrocodeCurrent is not null, "Microcode", cpu.MicrocodeCurrent ?? "—")
            .Metric("Ảo hoá phần cứng", cpu.VirtualizationEnabled ? "Đã bật" : "Chưa bật");

        // Khối xác thực danh tính: đây là những dòng cần nhìn đầu tiên khi nghi ngờ
        // CPU bị dán nhãn lại, vì chúng đến thẳng từ silicon chứ không qua BIOS.
        if (cpu.CpuId.IsValid)
        {
            card.Metric("Chuỗi tên do chính CPU báo", cpu.CpuId.BrandString.OrDash(),
                    "Đọc từ CPUID leaf 0x80000002-04, nằm trong silicon")
                .Metric("Chữ ký CPUID", cpu.CpuId.SignatureText,
                    "Family/Model/Stepping — không sửa được bằng phần mềm")
                .MetricIf(cpu.ActualMicroarchitecture is not null, "Vi kiến trúc thật",
                    cpu.ActualMicroarchitecture.OrDash() +
                    (cpu.ActualGeneration is { } g ? $" — thế hệ {g}" : string.Empty))
                .MetricIf(cpu.SmbiosProcessorVersion is not null, "Tên CPU do BIOS công bố",
                    cpu.SmbiosProcessorVersion.OrDash(), "SMBIOS Type 4 — trường này sửa được")
                .MetricIf(cpu.CpuId.MaxFrequencyMhz > 0, "Xung tối đa theo CPUID",
                    $"{cpu.CpuId.MaxFrequencyMhz} MHz");
        }

        var temps = Sensors(snapshot, SensorKind.Temperature, "CPU", "Core", "Package", "Tdie", "Tctl");
        var clocks = Sensors(snapshot, SensorKind.Clock, "CPU", "Core");
        var loads = Sensors(snapshot, SensorKind.Load, "CPU", "Total");
        card.AttachSensors(temps.Concat(clocks).Concat(loads));

        AnalyzeThermals(card, temps, snapshot.IsLaptop);
        AnalyzeClocks(card, cpu, clocks);

        if (cpu.IsEngineeringSample)
            card.Add("CPU-ES-001", Kind, Severity.Critical,
                "CPU có dấu hiệu là mẫu kỹ thuật (ES/QS)",
                $"Chuỗi định danh: \"{cpu.Name}\". Mẫu ES/QS là bản chưa phát hành chính thức, " +
                "không được bảo hành, thường thiếu tính năng và có thể mất ổn định khi cập nhật microcode.",
                "Tra mã sSpec/OPN trên trang chính thức của Intel hoặc AMD. Máy dùng CPU ES cần giảm giá đáng kể.",
                penalty: 30, confidence: 0.6);

        if (cpu.PhysicalCores == 0 || cpu.LogicalCores == 0)
            card.Add("CPU-INFO-001", Kind, Severity.Notice,
                "Không đọc được đầy đủ cấu hình nhân",
                penalty: 5, confidence: 0.9);

        foreach (var f in snapshot.ReliabilityFindings.Where(f => f.Component == ComponentKind.Motherboard))
            card.Add(f);

        foreach (var f in snapshot.AuthenticityFindings.Where(f => f.Component == ComponentKind.Cpu))
            card.Add(f);

        return card.Build();
    }

    private void AnalyzeThermals(ScoreCard card, List<SensorStats> temps, bool isLaptop)
    {
        var packageTemp = temps.FirstOrDefault(t =>
            t.SensorName.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
            t.SensorName.Contains("Tdie", StringComparison.OrdinalIgnoreCase))
            ?? temps.OrderByDescending(t => t.Max).FirstOrDefault();

        if (packageTemp is null)
        {
            card.Metric("Nhiệt độ", "Không đọc được",
                "Cần chạy ứng dụng bằng quyền Administrator để nạp driver cảm biến", Severity.Notice);
            return;
        }

        card.Metric("Nhiệt độ (min / TB / max)",
            $"{packageTemp.Min:0} / {packageTemp.Average:0} / {packageTemp.Max:0} °C",
            $"Đo trong {packageTemp.Window.TotalSeconds:0} giây, {packageTemp.Samples} mẫu");

        // Ngưỡng khác nhau giữa laptop và desktop: laptop mỏng chạm 95 °C là bình thường theo thiết kế,
        // desktop chạm mốc đó nghĩa là tản nhiệt có vấn đề.
        var criticalMax = isLaptop ? 99 : 95;
        var warningMax = isLaptop ? 95 : 88;
        var idleWarn = isLaptop ? 60 : 55;

        if (packageTemp.Max >= criticalMax)
            card.Add("CPU-TEMP-001", Kind, Severity.Critical,
                $"CPU chạm ngưỡng cắt nhiệt: {packageTemp.Max:0} °C",
                "Ở mức này CPU đã tự hạ xung để tự bảo vệ, hiệu năng thực tế thấp hơn thông số nhiều.",
                "Tháo máy vệ sinh, thay keo tản nhiệt và kiểm tra quạt. Với laptop mua lại, hãy trừ chi phí này vào giá.",
                penalty: 25, confidence: 0.9, source: EvidenceSource.Sensor);
        else if (packageTemp.Max >= warningMax)
            card.Add("CPU-TEMP-002", Kind, Severity.Warning,
                $"Nhiệt độ tối đa cao: {packageTemp.Max:0} °C",
                "Chưa tới ngưỡng cắt nhưng đã sát. Keo tản nhiệt nhiều khả năng đã khô.",
                "Lên kế hoạch bảo dưỡng tản nhiệt trong vòng vài tháng tới.",
                penalty: 12, confidence: 0.85, source: EvidenceSource.Sensor);

        if (packageTemp.Min >= idleWarn)
            card.Add("CPU-TEMP-003", Kind, Severity.Warning,
                $"Nhiệt độ thấp nhất đã là {packageTemp.Min:0} °C",
                "Nhiệt độ nghỉ cao cho thấy luồng gió bị chặn hoặc keo tản nhiệt đã mất tác dụng, " +
                "chứ không phải do tải nặng.",
                "Vệ sinh khe tản nhiệt và thay keo.",
                penalty: 10, confidence: 0.75, source: EvidenceSource.Sensor);

        var spread = packageTemp.Max - packageTemp.Min;
        if (spread > 0)
            card.Metric("Biên độ nhiệt", $"{spread:0} °C",
                spread < 10 ? "Biên độ hẹp — có thể chưa chạy đủ tải để đánh giá" : null);
    }

    private void AnalyzeClocks(ScoreCard card, Core.Models.Components.ProcessorInfo cpu, List<SensorStats> clocks)
    {
        var coreClock = clocks
            .Where(c => c.SensorName.Contains("Core", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.Max)
            .FirstOrDefault();

        if (coreClock is null) return;

        card.Metric("Xung nhịp thực tế (min / TB / max)",
            $"{coreClock.Min:0} / {coreClock.Average:0} / {coreClock.Max:0} MHz");

        if (cpu.MaxClockMhz <= 0) return;

        var achieved = coreClock.Max / cpu.MaxClockMhz;
        if (achieved < 0.75)
            card.Add("CPU-CLK-001", Kind, Severity.Warning,
                $"Xung nhịp cao nhất chỉ đạt {achieved:P0} so với thiết kế",
                $"Đo được tối đa {coreClock.Max:0} MHz trên mức thiết kế {cpu.MaxClockMhz} MHz. " +
                "Nguyên nhân thường gặp: giới hạn công suất trong BIOS, pin/adapter yếu, hoặc CPU bị hạ xung do nhiệt.",
                "Chạy lại bài kiểm tra khi máy đã cắm sạc và đặt chế độ nguồn ở mức hiệu năng cao.",
                penalty: 15, confidence: 0.6, source: EvidenceSource.Sensor);
    }

    private static List<SensorStats> Sensors(SystemSnapshot snapshot, SensorKind kind, params string[] keywords) =>
        snapshot.SensorSnapshot
            .Where(s => s.Kind == kind &&
                        keywords.Any(k =>
                            s.HardwareName.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                            s.SensorName.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .ToList();
}
