using HardwareInspector.Analysis.Scoring;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Assessment;
using HardwareInspector.Core.Models.Diagnostics;
using HardwareInspector.Core.Models.Sensors;
using HardwareInspector.Core.Utils;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Analysis.Analyzers;

/// <summary>
/// Card đồ hoạ không có đồng hồ đếm giờ như ổ cứng, nên phải đọc gián tiếp:
/// nhiệt độ dưới tải, chênh lệch giữa nhiệt độ GPU và điểm nóng (hotspot),
/// và bộ nhớ VRAM có báo lỗi hay không.
///
/// Phần lớn các phép kiểm tra ở đây chỉ có nghĩa khi card đã chạy tải nặng.
/// Lúc nghỉ, card tắt quạt và chênh lệch điểm nóng chỉ vài độ, nên một card
/// đã tách keo vẫn trông như card khoẻ. Vì vậy analyzer phân biệt rõ hai trường hợp
/// đã và chưa chạy bài kiểm tra GPU.
/// </summary>
public sealed class GraphicsAnalyzer : IComponentAnalyzer
{
    public ComponentKind Kind => ComponentKind.Gpu;

    public bool CanAnalyze(SystemSnapshot snapshot) => snapshot.GraphicsAdapters.Count > 0;

    public ComponentAssessment Analyze(SystemSnapshot snapshot)
    {
        var gpu = snapshot.PrimaryGpu!;
        var card = new ScoreCard(Kind, gpu.Name.OrDash());

        card.Metric(S("Nhà sản xuất", "Manufacturer"), gpu.Vendor.OrDash())
            .Metric(S("Loại", "Type"), gpu.IsDiscrete ? S("Card rời", "Discrete card") : S("Đồ hoạ tích hợp", "Integrated graphics"))
            .MetricIf(gpu.VideoMemoryBytes > 0, S("Bộ nhớ đồ hoạ", "Video memory"), gpu.VideoMemoryBytes.ToSize(0))
            .Metric("Driver", $"{gpu.DriverVersion.OrDash()} ({gpu.DriverDate:dd/MM/yyyy})")
            .MetricIf(gpu.VideoBiosVersion is not null, "vBIOS", gpu.VideoBiosVersion.OrDash());

        if (snapshot.GraphicsAdapters.Count > 1)
            card.Metric(S("Các card khác", "Other adapters"),
                string.Join(", ", snapshot.GraphicsAdapters.Skip(1).Select(g => g.Name)));

        var temps = snapshot.SensorSnapshot
            .Where(s => s.Kind == SensorKind.Temperature && IsGpuSensor(s))
            .ToList();
        var clocks = snapshot.SensorSnapshot
            .Where(s => s.Kind == SensorKind.Clock && IsGpuSensor(s))
            .ToList();
        var fans = snapshot.SensorSnapshot
            .Where(s => s.Kind == SensorKind.Fan && IsGpuSensor(s))
            .ToList();
        card.AttachSensors(temps.Concat(clocks).Concat(fans));

        var test = snapshot.GpuTest;
        var underLoad = test is { ComputeChecks: > 0 };

        var core = temps.FirstOrDefault(t => t.SensorName.Contains("Core", StringComparison.OrdinalIgnoreCase))
                   ?? temps.FirstOrDefault();
        var hotspot = temps.FirstOrDefault(t =>
            t.SensorName.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase) ||
            t.SensorName.Contains("Junction", StringComparison.OrdinalIgnoreCase));
        var memoryTemp = temps.FirstOrDefault(t =>
            t.SensorName.Contains("Memory", StringComparison.OrdinalIgnoreCase));

        if (core is not null)
        {
            card.Metric(S("Nhiệt độ GPU (min / TB / max)", "GPU temperature (min / avg / max)"),
                $"{core.Min:0} / {core.Average:0} / {core.Max:0} °C",
                underLoad ? null : S("Số liệu lúc nghỉ — chưa chạy bài tải GPU",
                                     "Idle readings — the GPU stress test has not been run"));

            if (core.Max >= 90)
                card.Add("GPU-TEMP-001", Kind, Severity.Warning,
                    S($"GPU đạt {core.Max:0} °C", $"GPU reached {core.Max:0} °C"),
                    S("Nhiệt độ này khiến card tự hạ xung. Nguyên nhân phổ biến là keo tản nhiệt khô hoặc quạt bám bụi.",
                      "At this temperature the card throttles. Usual causes are dried thermal paste or dusty fans."),
                    S("Vệ sinh và thay keo tản nhiệt cho card.", "Clean the card and replace its thermal paste."),
                    penalty: 15, confidence: 0.85, source: EvidenceSource.Sensor);
        }
        else
        {
            card.Metric(S("Nhiệt độ GPU", "GPU temperature"), S("Không đọc được", "Unreadable"),
                S("Cần quyền Administrator, hoặc card không phơi bày cảm biến", "Needs Administrator rights, or the card does not expose sensors"), Severity.Notice);
        }

        // Chênh lệch lớn giữa nhiệt độ nhân và điểm nóng nhất là dấu hiệu kinh điển
        // của keo/pad tản nhiệt đã xuống cấp — thường gặp ở card từng chạy đào coin.
        if (core is not null && hotspot is not null)
        {
            var delta = hotspot.Max - core.Max;
            card.Metric(S("Chênh lệch nhân ↔ điểm nóng", "Core ↔ hotspot delta"), $"{delta:0} °C");

            if (delta > 25)
                card.Add("GPU-TEMP-002", Kind, Severity.Critical,
                    S($"Chênh lệch điểm nóng lên tới {delta:0} °C", $"Hotspot delta as high as {delta:0} °C"),
                    S("Trên card khoẻ, chênh lệch này thường dưới 15 °C. " +
                      "Mức cao bất thường cho thấy keo tản nhiệt đã tách lớp hoặc lắp lệch, " +
                      "hay gặp ở card đã chạy tải nặng liên tục nhiều tháng.",
                      "On a healthy card this delta is usually under 15 °C. " +
                      "An abnormally high value means the thermal paste has separated or the cooler sits unevenly, " +
                      "common on cards that ran heavy load for months."),
                    S("Yêu cầu người bán thay keo và pad trước khi giao, hoặc trừ chi phí bảo dưỡng.",
                      "Ask the seller to replace the paste and pads before handover, or deduct the service cost."),
                    penalty: 28, confidence: 0.85, source: EvidenceSource.Sensor);
            else if (delta > 18)
                card.Add("GPU-TEMP-003", Kind, Severity.Warning,
                    S($"Chênh lệch điểm nóng {delta:0} °C", $"Hotspot delta {delta:0} °C"),
                    S("Cao hơn mức bình thường. Keo tản nhiệt bắt đầu xuống cấp.", "Higher than normal. The thermal paste is starting to degrade."),
                    penalty: 12, confidence: 0.7, source: EvidenceSource.Sensor);
        }

        if (memoryTemp is not null)
        {
            card.Metric(S("Nhiệt độ VRAM (max)", "VRAM temperature (max)"), $"{memoryTemp.Max:0} °C");
            if (memoryTemp.Max >= 95)
                card.Add("GPU-VRAM-001", Kind, Severity.Warning,
                    S($"VRAM nóng {memoryTemp.Max:0} °C", $"VRAM running hot at {memoryTemp.Max:0} °C"),
                    S("GDDR6 hoạt động ổn định dưới khoảng 95 °C. Vượt mức này thường do pad tản nhiệt đã chai.",
                      "GDDR6 is stable below about 95 °C. Going above usually means the thermal pads have hardened."),
                    S("Thay pad tản nhiệt cho chip nhớ.", "Replace the memory thermal pads."),
                    penalty: 14, confidence: 0.8, source: EvidenceSource.Sensor);
        }

        if (fans.Count > 0)
        {
            foreach (var fan in fans.Take(3))
                card.Metric(S($"Quạt {fan.SensorName}", $"Fan {fan.SensorName}"), $"{fan.Min:0} – {fan.Max:0} RPM");

            // Lúc nghỉ, card hiện đại tắt hẳn quạt — không quay chưa nói lên điều gì.
            // Chỉ khi đã chạy tải nặng mà quạt vẫn đứng yên thì mới là dấu hiệu hỏng.
            if (underLoad && fans.All(f => f.Max < 100))
                card.Add("GPU-FAN-001", Kind, Severity.Warning,
                    S("Quạt card không quay dù GPU đang tải nặng",
                      "Card fans never spun even under heavy GPU load"),
                    S("Card hiện đại tắt quạt khi nghỉ, nhưng dưới tải nặng quạt bắt buộc phải chạy. " +
                      "Quạt đứng yên thường do quạt hỏng, dây cắm bị đứt, hoặc cảm biến tốc độ không còn hoạt động.",
                      "Modern cards stop their fans at idle, but under heavy load the fans must spin. " +
                      "Fans standing still usually means a dead fan, a broken fan cable, or a failed tach sensor."),
                    S("Nhìn trực tiếp quạt card trong lúc chạy bài tải để xác nhận.",
                      "Watch the card's fans directly while the stress test runs to confirm."),
                    penalty: 12, confidence: 0.75, source: EvidenceSource.Sensor);
        }

        var clock = clocks.FirstOrDefault(c => c.SensorName.Contains("Core", StringComparison.OrdinalIgnoreCase));
        if (clock is not null)
            card.Metric(S("Xung nhân (min / TB / max)", "Core clock (min / avg / max)"), $"{clock.Min:0} / {clock.Average:0} / {clock.Max:0} MHz");

        if (gpu.DriverDate is { } dd && (DateTime.Now - dd).TotalDays > 730)
            card.Add("GPU-DRV-001", Kind, Severity.Notice,
                S($"Driver đồ hoạ đã cũ ({dd:MM/yyyy})", $"Graphics driver is old ({dd:MM/yyyy})"),
                S("Driver quá cũ thường gây lỗi hiển thị và không hỗ trợ tựa game/ứng dụng mới.",
                  "Very old drivers often cause display glitches and do not support new games/apps."),
                S("Cài driver mới nhất từ trang của hãng card.", "Install the latest driver from the card vendor's site."),
                penalty: 5, confidence: 0.9);

        if (test is not null)
            AddStressTestResults(card, test);
        else if (gpu.IsDiscrete)
            card.Add("GPU-CHECK-001", Kind, Severity.Notice,
                S("Chưa chạy bài kiểm tra GPU", "GPU test has not been run"),
                S("Chưa có số liệu dưới tải nên các phép kiểm tra nhiệt độ, điểm nóng, quạt và VRAM " +
                  "ở trên chưa có giá trị. Card đã tách keo hay có VRAM lỗi vẫn trông bình thường khi nghỉ.",
                  "Without readings under load, the temperature, hotspot, fan and VRAM checks above " +
                  "carry little weight. A card with failed paste or bad VRAM still looks normal at idle."),
                S("Nhấn \"Chạy tải nặng\": ứng dụng sẽ tải nặng GPU khoảng 5 phút rồi kiểm tra VRAM ngay khi card còn nóng.",
                  "Press \"Run stress test\": the app loads the GPU for about 5 minutes, then tests VRAM while the card is still hot."),
                penalty: 0, confidence: 1.0);

        foreach (var f in snapshot.AuthenticityFindings.Where(f => f.Component == ComponentKind.Gpu))
            card.Add(f);

        foreach (var f in snapshot.ReliabilityFindings.Where(f => f.Component == ComponentKind.Gpu))
            card.Add(f);

        return card.Build();
    }

    /// <summary>
    /// Kết quả bài kiểm tra GPU trong ứng dụng. Lỗi VRAM và lệch kết quả tính toán
    /// là bằng chứng cứng: phần mềm tự so từng bit, không suy diễn.
    /// </summary>
    /// <summary>
    /// So sức tính và băng thông VRAM đo được với thông số chính hãng của model card đang khai.
    ///
    /// Phép đo đơn giản thường đạt 60–95% con số lý thuyết. Card bị nạp vBIOS đổi tên (GT 730
    /// khai là GTX 1050, GTX 1050 khai là GTX 1060...) mang nhân và bus nhớ đời thấp nên hụt
    /// xuống dưới một nửa ở cả hai con số. Đòi cả hai cùng thấp mới kết luận, để một lần đo
    /// lệch vì máy đang bận không bị coi là gian lận.
    /// </summary>
    private void CheckAgainstSpec(ScoreCard card, GpuTestResult test)
    {
        if (test.PeakGflops is not { } gflops || test.MemoryBandwidthGBps is not { } gbps) return;

        card.Metric(S("Sức tính FP32 đo được", "Measured FP32 compute"), $"{gflops / 1000:0.00} TFLOPS")
            .Metric(S("Băng thông VRAM đo được", "Measured VRAM bandwidth"), $"{gbps:0} GB/s");

        if (Core.Gpu.GpuSpecCatalog.Find(test.AdapterName) is not { } spec) return;

        var computeRatio = gflops / (spec.Fp32Tflops * 1000);
        var bandwidthRatio = gbps / spec.BandwidthGBps;
        var vramGb = test.DedicatedVideoMemoryBytes / 1024.0 / 1024 / 1024;

        card.Metric(S($"So với thông số {spec.Model}", $"Against {spec.Model} spec"),
            S($"tính toán {computeRatio:P0} · băng thông {bandwidthRatio:P0}", $"compute {computeRatio:P0} · bandwidth {bandwidthRatio:P0}"),
            S($"Chính hãng: {spec.Fp32Tflops:0.##} TFLOPS, {spec.BandwidthGBps:0} GB/s. Card khoẻ thường đạt 60–95%.",
              $"Official: {spec.Fp32Tflops:0.##} TFLOPS, {spec.BandwidthGBps:0} GB/s. A healthy card usually reaches 60–95%."),
            computeRatio < 0.45 && bandwidthRatio < 0.45 ? Severity.Critical : Severity.Info);

        if (computeRatio < 0.45 && bandwidthRatio < 0.45)
            card.Add("GPU-SPEC-001", Kind, Severity.Critical,
                S($"Hiệu năng chỉ bằng {computeRatio:P0} sức tính và {bandwidthRatio:P0} băng thông của {spec.Model}",
                  $"Performance is only {computeRatio:P0} of the compute and {bandwidthRatio:P0} of the bandwidth of a {spec.Model}"),
                S($"Đo được {gflops / 1000:0.00} TFLOPS và {gbps:0} GB/s, trong khi {spec.Model} chính hãng đạt {spec.Fp32Tflops:0.##} TFLOPS " +
                  $"và {spec.BandwidthGBps:0} GB/s. Thấp cả hai cùng lúc như vậy là dấu hiệu điển hình của card đời thấp được nạp vBIOS " +
                  "để báo tên model cao hơn — loại hàng hay gặp ở thị trường card cũ giá rẻ.",
                  $"Measured {gflops / 1000:0.00} TFLOPS and {gbps:0} GB/s, while a genuine {spec.Model} reaches {spec.Fp32Tflops:0.##} TFLOPS " +
                  $"and {spec.BandwidthGBps:0} GB/s. Both being this low at once is the classic sign of a lower-end card flashed with a vBIOS " +
                  "that reports a higher model — common among cheap used graphics cards."),
                S("Đối chiếu bằng GPU-Z: xem mục GPU (tên chip), Shaders và Bus Width so với thông số chính hãng. " +
                  "Đóng mọi ứng dụng rồi chạy lại bài kiểm tra; nếu vẫn thấp, không mua.",
                  "Cross-check with GPU-Z: compare the GPU (chip name), Shaders and Bus Width fields with the official specs. " +
                  "Close all apps and run the test again; if it is still low, do not buy."),
                penalty: 50, confidence: 0.75, source: EvidenceSource.Benchmark);
        else if (bandwidthRatio < 0.35)
            card.Add("GPU-SPEC-002", Kind, Severity.Warning,
                S($"Băng thông VRAM chỉ bằng {bandwidthRatio:P0} thông số {spec.Model}", $"VRAM bandwidth is only {bandwidthRatio:P0} of the {spec.Model} spec"),
                S("Bộ nhớ card chạy chậm bất thường: có thể là bản dùng bus nhớ hẹp hơn hoặc chip nhớ đời cũ, hoặc VRAM đang bị hạ xung.",
                  "The card's memory is abnormally slow: it may be a variant with a narrower bus or older memory chips, or the VRAM is down-clocked."),
                S("Xem mục Bus Width và Memory Type trong GPU-Z, so với thông số chính hãng.",
                  "Check Bus Width and Memory Type in GPU-Z against the official specs."),
                penalty: 18, confidence: 0.6, source: EvidenceSource.Benchmark);

        if (vramGb > 0 && vramGb < spec.MinVramGb * 0.9)
            card.Add("GPU-SPEC-003", Kind, Severity.Warning,
                S($"VRAM chỉ có {vramGb:0.#} GB, ít hơn mọi phiên bản {spec.Model} ({spec.MinVramGb:0.#} GB trở lên)",
                  $"Only {vramGb:0.#} GB of VRAM, less than any {spec.Model} variant ({spec.MinVramGb:0.#} GB or more)"),
                S("Dung lượng VRAM do chính card báo về thấp hơn mọi bản chính hãng của model này.",
                  "The VRAM size reported by the card is below every official variant of this model."),
                S("Đối chiếu mục Memory Size trong GPU-Z.", "Cross-check Memory Size in GPU-Z."),
                penalty: 20, confidence: 0.7, source: EvidenceSource.Benchmark);
    }

    private void AddStressTestResults(ScoreCard card, GpuTestResult test)
    {
        if (test.SetupError is not null && !test.DeviceLost)
        {
            card.Metric(S("Bài kiểm tra GPU", "GPU test"), S("Không chạy được", "Could not run"),
                test.SetupError, Severity.Notice);
            return;
        }

        CheckAgainstSpec(card, test);

        if (test.ComputeChecks > 0)
            card.Metric(S("Bài tải GPU", "GPU stress run"),
                S("{0:0.#} phút · {1} lần đối chiếu · {2} lần lệch", "{0:0.#} min · {1} cross-checks · {2} mismatches",
                    test.StressDuration.TotalMinutes, test.ComputeChecks, test.ComputeMismatches),
                test.AdapterName);

        if (test.ThroughputRetention is { } keep)
            card.Metric(S("Hiệu năng giữ được cuối bài", "Performance held at the end"), $"{keep * 100:0}%",
                S("Phút cuối so với phút đầu", "Last minute versus first minute"),
                keep < 0.85 ? Severity.Warning : Severity.Info);

        if (test.VramTestSkipped)
            card.Metric("VRAM", S("Không kiểm", "Not tested"), test.VramSkipReason);
        else if (test.VramTestedBytes > 0)
            card.Metric(S("Kiểm tra VRAM", "VRAM test"),
                S("{0} × {1} lượt · {2} lỗi", "{0} × {1} passes · {2} errors",
                    test.VramTestedBytes.ToSize(1), test.VramPasses, test.VramErrors),
                S("{0:0}% VRAM riêng", "{0:0}% of dedicated VRAM",
                    test.DedicatedVideoMemoryBytes > 0 ? 100.0 * test.VramTestedBytes / test.DedicatedVideoMemoryBytes : 0),
                test.VramErrors > 0 ? Severity.Critical : Severity.Info);

        if (test.DeviceLost)
            card.Add("GPU-TDR-002", Kind, Severity.Critical,
                S("Driver đồ hoạ sập ngay trong bài tải", "The graphics driver crashed during the stress test"),
                S("Windows phải reset GPU giữa chừng ({0}). Card khoẻ chạy tải nặng bao lâu cũng không làm driver sập. " +
                  "Nguyên nhân thường gặp: quá nhiệt, VRAM lỗi, nguồn cấp cho card yếu hoặc chip đồ hoạ đã xuống cấp.",
                  "Windows had to reset the GPU mid-test ({0}). A healthy card never crashes its driver under load, however long. " +
                  "Common causes: overheating, bad VRAM, weak power delivery to the card, or a degraded graphics chip.",
                    test.DeviceLostReason),
                S("Cài driver mới nhất rồi chạy lại. Nếu vẫn sập, không nên mua — đây là lỗi phần cứng khó sửa.",
                  "Install the latest driver and run again. If it still crashes, walk away — this is a hard-to-fix hardware fault."),
                penalty: 35, confidence: 0.8, source: EvidenceSource.Benchmark);

        if (test.VramErrors > 0)
            card.Add("GPU-VRAM-ERR", Kind, Severity.Critical,
                S("VRAM trả về sai dữ liệu: {0} lỗi", "VRAM returned corrupted data: {0} errors", test.VramErrors),
                S("Dữ liệu ghi vào bộ nhớ đồ hoạ đọc ra không khớp (lỗi đầu tiên ở vị trí 0x{0:X}). " +
                  "Đây là bằng chứng trực tiếp của chip nhớ hỏng — biểu hiện ngoài đời là sọc, chấm màu, " +
                  "treo game hoặc màn hình đen. Thường gặp ở card từng đào coin hoặc pad tản nhiệt VRAM đã chai.",
                  "Data written to graphics memory did not read back intact (first error at offset 0x{0:X}). " +
                  "This is direct evidence of failing memory chips — in practice: stripes, coloured dots, " +
                  "game crashes or black screens. Common on ex-mining cards or cards with worn VRAM pads.",
                    test.VramFirstErrorOffset ?? 0),
                S("Không nên mua. Thay chip nhớ cần máy hàn BGA và chi phí thường vượt giá trị card.",
                  "Do not buy. Replacing memory chips needs BGA rework and usually costs more than the card is worth."),
                penalty: 50, confidence: 0.95, source: EvidenceSource.Benchmark);

        if (test.ComputeMismatches > 0)
            card.Add("GPU-CALC-001", Kind, Severity.Critical,
                S("GPU tính sai khi nóng: {0}/{1} lần đối chiếu bị lệch",
                  "The GPU computes wrong results when hot: {0}/{1} cross-checks mismatched",
                    test.ComputeMismatches, test.ComputeChecks),
                S("Cùng một phép tính trên cùng dữ liệu, card khoẻ luôn cho kết quả giống hệt đến từng bit. " +
                  "Kết quả thay đổi nghĩa là nhân đồ hoạ không còn ổn định dưới tải — do ép xung, " +
                  "vBIOS bị sửa, nguồn cấp yếu hoặc chip đã xuống cấp.",
                  "Given the same computation on the same data, a healthy card returns bit-identical results every time. " +
                  "Changing results mean the graphics core is unstable under load — from overclocking, " +
                  "a modified vBIOS, weak power delivery or a degraded chip."),
                S("Đưa card về xung mặc định (tắt MSI Afterburner, nạp lại vBIOS gốc) rồi chạy lại. Vẫn lệch thì không nên mua.",
                  "Return the card to stock clocks (close MSI Afterburner, reflash the original vBIOS) and run again. If it still mismatches, do not buy."),
                penalty: 40, confidence: 0.9, source: EvidenceSource.Benchmark);

        if (test.ThroughputRetention is < 0.85 and var retention)
            card.Add("GPU-THROT-001", Kind, retention < 0.7 ? Severity.Warning : Severity.Notice,
                S("Hiệu năng GPU tụt {0:0}% sau vài phút tải", "GPU performance dropped {0:0}% after a few minutes of load",
                    (1 - retention) * 100),
                S("Card đang tự hạ xung vì nóng hoặc chạm giới hạn công suất. Trên card có tản nhiệt tốt, " +
                  "hiệu năng gần như không đổi suốt bài tải.",
                  "The card is throttling from heat or hitting its power limit. With healthy cooling, " +
                  "performance stays nearly flat throughout the run."),
                S("Vệ sinh, thay keo và pad tản nhiệt. Với laptop, nhớ cắm sạc khi kiểm tra.",
                  "Clean the cooler and replace paste and pads. On a laptop, keep the charger plugged in while testing."),
                penalty: retention < 0.7 ? 15 : 6, confidence: 0.7, source: EvidenceSource.Benchmark);

        if (test.Completed && !test.HasHardErrors)
            card.Add("GPU-TEST-OK", Kind, Severity.Info,
                S("Qua bài kiểm tra GPU: không lỗi VRAM, không lệch tính toán, driver ổn định",
                  "Passed the GPU test: no VRAM errors, no compute mismatches, stable driver"),
                S("Phần mềm không thấy được artifact trên màn hình. Nên chạy thêm một bài tải đồ hoạ 3D " +
                  "(FurMark, Superposition, 3DMark) và nhìn trực tiếp xem có sọc hay chấm màu không — xem tab Kiểm tra tay.",
                  "Software cannot see on-screen artifacts. Also run a 3D graphics load (FurMark, Superposition, 3DMark) " +
                  "and look for stripes or coloured dots yourself — see the Manual checks tab."),
                penalty: 0, confidence: 1.0, source: EvidenceSource.Benchmark);
    }

    private static bool IsGpuSensor(SensorStats s) =>
        s.HardwareName.Contains("GPU", StringComparison.OrdinalIgnoreCase) ||
        s.HardwareName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
        s.HardwareName.Contains("AMD Radeon", StringComparison.OrdinalIgnoreCase) ||
        s.HardwareName.Contains("Intel Arc", StringComparison.OrdinalIgnoreCase);
}
