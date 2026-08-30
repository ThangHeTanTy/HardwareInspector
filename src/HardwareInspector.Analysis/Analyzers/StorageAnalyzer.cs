using HardwareInspector.Analysis.Scoring;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Assessment;
using HardwareInspector.Core.Models.Components;
using HardwareInspector.Core.Utils;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Analysis.Analyzers;

/// <summary>
/// Ổ lưu trữ là linh kiện nói thật nhiều nhất về lịch sử một cái máy:
/// nó tự đếm số giờ chạy, số lần bật tắt và số sector đã hỏng, và người bán
/// không xoá được các con số này bằng cách format.
/// </summary>
public sealed class StorageAnalyzer : IComponentAnalyzer
{
    public ComponentKind Kind => ComponentKind.Storage;

    public bool CanAnalyze(SystemSnapshot snapshot) => snapshot.StorageDevices.Count > 0;

    public ComponentAssessment Analyze(SystemSnapshot snapshot)
    {
        var results = snapshot.StorageDevices.Select(d => AnalyzeOne(d, snapshot)).ToList();
        var worst = results.OrderBy(r => r.Score).First();

        if (results.Count == 1)
        {
            // Bảng điểm đã dựng xong nên chèn thẳng phần nâng cấp vào danh sách thông số.
            AppendUpgradeMetrics(worst, snapshot);
            return worst;
        }

        var merged = new ScoreCard(Kind, $"{results.Count} ổ lưu trữ")
            .Headline($"Ổ yếu nhất: {worst.DisplayName} — {worst.Rating.ToText()}");

        foreach (var r in results)
        {
            merged.Metric(r.DisplayName, $"{r.Rating.ToText()} ({r.Score}/100)",
                r.OperatingTime.HasValue ? $"Đã chạy {r.OperatingTime.Value.ToHumanDuration()}" : null);
            foreach (var m in r.Metrics) merged.Metric($"  {r.DisplayName} · {m.Label}", m.Value, m.Note, m.Severity);
            foreach (var f in r.Findings) merged.Add(f);
        }

        var healths = results.Where(r => r.HealthPercent.HasValue).Select(r => r.HealthPercent!.Value).ToList();
        if (healths.Count > 0) merged.Health(healths.Min());

        var times = results.Where(r => r.OperatingTime.HasValue).Select(r => r.OperatingTime!.Value).ToList();
        if (times.Count > 0) merged.Uptime(times.Max());

        var built = merged.Build();
        AppendUpgradeMetrics(built, snapshot);
        return built;
    }

    private ComponentAssessment AnalyzeOne(StorageDeviceInfo d, SystemSnapshot snapshot)
    {
        var card = new ScoreCard(Kind, $"{d.Model.OrDash()} ({d.CapacityBytes.ToSize(0)})");

        card.Metric("Loại / giao tiếp", $"{d.MediaType.OrDash()} qua {d.BusType.OrDash()}")
            .MetricIf(d.SpindleSpeedRpm is > 0, "Tốc độ vòng quay", $"{d.SpindleSpeedRpm} vòng/phút")
            .Metric("Firmware", d.FirmwareRevision.OrDash())
            .Metric("Serial", FormatHelpers.Mask(d.SerialNumber))
            .MetricIf(d.SmartSource is not null, "Nguồn dữ liệu sức khoẻ", d.SmartSource.OrDash());

        if (!d.SmartAvailable)
        {
            card.Add("HDD-SMART-000", Kind, Severity.Warning,
                "Không đọc được S.M.A.R.T. của ổ này",
                d.SmartFailureReason ?? "Không xác định được nguyên nhân.",
                "Không có S.M.A.R.T. thì không biết ổ đã chạy bao lâu và đã hỏng sector nào chưa — " +
                "tức là mất hoàn toàn cơ sở để định giá ổ. Hãy khắc phục theo hướng dẫn ở trên " +
                "rồi chạy lại trước khi quyết định mua.",
                penalty: 18, confidence: 0.9, source: EvidenceSource.SmartAta);
            return card.Cap(65).Build();
        }

        AnalyzeNvmeWarnings(card, d);
        AnalyzeLifetime(card, d);
        AnalyzeDefects(card, d);
        AnalyzeTemperature(card, d);

        foreach (var f in snapshot.ReliabilityFindings.Where(f => f.Component == ComponentKind.Storage))
            card.Add(f);

        foreach (var f in snapshot.AuthenticityFindings.Where(f => f.Component == ComponentKind.Storage))
            card.Add(f);

        var keyAttrs = d.Attributes
            .Where(a => a.IsPreFailure || a.Id is 0x09 or 0x0C or 0xC7 or 0xF1 or 0xF2)
            .ToList();

        foreach (var a in keyAttrs.Take(14))
            card.Metric($"S.M.A.R.T. {a.Id:X2} · {a.Name}",
                $"raw {a.RawValue:N0}",
                a.IsFailing ? $"ĐÃ VƯỢT NGƯỠNG ({a.Current} ≤ {a.Threshold})" : $"giá trị {a.Current}, ngưỡng {a.Threshold}",
                a.IsFailing ? Severity.Critical : Severity.Info);

        return card.Build();
    }

    /// <summary>
    /// NVMe có một byte cờ cảnh báo tới hạn. Bất kỳ bit nào được bật đều là
    /// lời tuyên bố của chính ổ rằng nó đang gặp vấn đề nghiêm trọng.
    /// </summary>
    private void AnalyzeNvmeWarnings(ScoreCard card, StorageDeviceInfo d)
    {
        var warnings = d.NvmeWarnings.ToList();
        if (warnings.Count == 0) return;

        card.Add("SSD-NVME-001", Kind, Severity.Critical,
            "Ổ NVMe đang bật cờ cảnh báo tới hạn",
            string.Join("\n", warnings.Select(w => "• " + w)),
            "Sao lưu dữ liệu ngay lập tức. Cờ này do firmware của ổ tự đặt, không phải suy đoán của phần mềm.",
            penalty: 50, confidence: 1.0, source: EvidenceSource.SmartNvme);
    }

    private void AnalyzeLifetime(ScoreCard card, StorageDeviceInfo d)
    {
        if (d.PowerOnHours is { } hours && hours > 0)
        {
            var span = TimeSpan.FromHours(hours);
            card.Uptime(span);
            card.Metric("Thời gian đã hoạt động", FormatHelpers.HoursToHuman(hours));

            if (hours > 35_000)
                card.Add("HDD-AGE-001", Kind, Severity.Critical,
                    $"Ổ đã chạy {hours:N0} giờ (khoảng {hours / 8760.0:0.#} năm)",
                    "Vượt xa vòng đời thiết kế phổ biến. Kể cả khi S.M.A.R.T. chưa báo lỗi, " +
                    "rủi ro hỏng đột ngột đã rất cao.",
                    "Chỉ nhận máy nếu người bán trừ hẳn giá một ổ mới vào giá bán.",
                    penalty: 35, confidence: 0.95, source: EvidenceSource.SmartAta);
            else if (hours > 20_000)
                card.Add("HDD-AGE-002", Kind, Severity.Warning,
                    $"Ổ đã chạy {hours:N0} giờ (khoảng {hours / 8760.0:0.#} năm)",
                    "Mức sử dụng cao. Với ổ cơ, đây là giai đoạn tỉ lệ hỏng bắt đầu tăng nhanh.",
                    "Sao lưu thường xuyên và chuẩn bị ngân sách thay ổ.",
                    penalty: 18, confidence: 0.9, source: EvidenceSource.SmartAta);
            else if (hours > 10_000)
                card.Add("HDD-AGE-003", Kind, Severity.Notice,
                    $"Ổ đã chạy {hours:N0} giờ",
                    "Mức sử dụng trung bình khá, tương đương 3-4 năm dùng văn phòng.",
                    penalty: 7, confidence: 0.9, source: EvidenceSource.SmartAta);
        }
        else
        {
            card.Metric("Thời gian đã hoạt động", "Không có trong dữ liệu ổ trả về", null, Severity.Notice);
        }

        if (d.PowerCycleCount is { } cycles and > 0)
        {
            card.Metric("Số lần bật/tắt", $"{cycles:N0} lần");

            if (d.PowerOnHours is { } h and > 0)
            {
                var hoursPerCycle = h / (double)cycles;
                if (hoursPerCycle < 1.5 && cycles > 500)
                    card.Add("HDD-CYCLE-001", Kind, Severity.Notice,
                        "Số lần bật/tắt rất cao so với số giờ chạy",
                        $"Trung bình mỗi lần bật máy chỉ chạy {hoursPerCycle:0.#} giờ. " +
                        "Hay gặp ở máy dùng làm quầy thu ngân, máy trưng bày hoặc máy bị mất điện đột ngột nhiều lần.",
                        penalty: 5, confidence: 0.5, source: EvidenceSource.SmartAta);
            }
        }

        var life = d.RemainingLifePercent;
        if (life.HasValue)
        {
            card.Health(life.Value);
            card.Metric("Tuổi thọ ghi còn lại", $"{life.Value}%",
                d.PercentageUsed is { } used ? $"Đã dùng {used}% quỹ ghi" : null);

            if (life.Value < 10)
                card.Add("SSD-LIFE-001", Kind, Severity.Critical,
                    $"SSD chỉ còn {life.Value}% tuổi thọ ghi",
                    "Khi cạn quỹ ghi, ổ chuyển sang chế độ chỉ đọc hoặc mất dữ liệu.",
                    "Coi như phải thay ổ ngay.",
                    penalty: 45, confidence: 0.95, source: EvidenceSource.SmartNvme);
            else if (life.Value < 30)
                card.Add("SSD-LIFE-002", Kind, Severity.Warning,
                    $"SSD còn {life.Value}% tuổi thọ ghi",
                    "Ổ đã dùng phần lớn quỹ ghi. Thường thấy ở máy từng chạy máy ảo, dựng phim hoặc làm node blockchain.",
                    "Trừ giá và lên kế hoạch thay trong 1-2 năm.",
                    penalty: 22, confidence: 0.9, source: EvidenceSource.SmartNvme);
        }

        if (d.AvailableSparePercent is { } spare && d.AvailableSpareThresholdPercent is { } threshold)
        {
            card.Metric("Vùng dự phòng còn lại", $"{spare}%", $"Ngưỡng an toàn: {threshold}%",
                spare <= threshold ? Severity.Critical : Severity.Info);

            if (spare <= threshold)
                card.Add("SSD-SPARE-001", Kind, Severity.Critical,
                    $"Vùng dự phòng đã cạn: còn {spare}%, ngưỡng {threshold}%",
                    "SSD dùng vùng dự phòng để thay các khối NAND hỏng. Cạn vùng này nghĩa là " +
                    "ổ không còn khả năng tự sửa chữa nữa.",
                    "Thay ổ ngay.",
                    penalty: 45, confidence: 1.0, source: EvidenceSource.SmartNvme);
        }

        if (d.TeraBytesWritten is { } tbw)
        {
            card.Metric("Tổng dữ liệu đã ghi", $"{tbw:0.##} TB");
            if (tbw > 200)
                card.Add("SSD-TBW-001", Kind, Severity.Warning,
                    $"Đã ghi {tbw:0} TB — khối lượng rất lớn",
                    "Con số này vượt xa mức sử dụng cá nhân thông thường (thường dưới 30 TB sau vài năm).",
                    "Hỏi rõ máy trước đây dùng vào việc gì.",
                    penalty: 12, confidence: 0.7, source: EvidenceSource.SmartAta);
        }

        if (d.TeraBytesRead is { } tbr) card.Metric("Tổng dữ liệu đã đọc", $"{tbr:0.##} TB");

        if (d.UnsafeShutdowns is { } unsafeCount and > 100)
            card.Add("HDD-PWR-001", Kind, Severity.Notice,
                $"{unsafeCount:N0} lần mất điện đột ngột",
                "Mỗi lần mất điện giữa chừng đều có nguy cơ làm hỏng bảng ánh xạ của SSD.",
                penalty: 6, confidence: 0.7, source: EvidenceSource.SmartAta);
    }

    private void AnalyzeDefects(ScoreCard card, StorageDeviceInfo d)
    {
        if (d.SmartPredictFailure)
            card.Add("HDD-FAIL-001", Kind, Severity.Critical,
                "Ổ tự báo sắp hỏng (thuộc tính pre-failure đã dưới ngưỡng)",
                "Firmware của chính ổ đã kết luận rằng nó sẽ hỏng. Đây là mức cảnh báo cao nhất.",
                "Không dùng ổ này cho dữ liệu quan trọng. Từ chối mua nếu người bán không thay ổ.",
                penalty: 70, confidence: 1.0, source: EvidenceSource.SmartAta);

        void Defect(long? value, string code, string label, string meaning, int perUnit, int cap, Severity sev)
        {
            if (value is not { } v || v <= 0) return;
            card.Metric(label, $"{v:N0}", null, sev);
            card.Add(code, Kind, sev, $"{label}: {v:N0}", meaning,
                "Sao lưu ngay và chạy quét bề mặt toàn ổ để xem con số có tiếp tục tăng không.",
                penalty: Math.Min(cap, (int)(v * perUnit)), confidence: 0.95, source: EvidenceSource.SmartAta);
        }

        Defect(d.ReallocatedSectors, "HDD-DEF-001", "Sector đã bị tráo",
            "Ổ đã phải thay sector hỏng bằng sector dự phòng. Con số tăng dần theo thời gian là dấu hiệu ổ đang chết.",
            5, 45, Severity.Critical);

        Defect(d.PendingSectors, "HDD-DEF-002", "Sector đang chờ tráo",
            "Đây là các sector đọc lỗi nhưng chưa được thay. Dữ liệu nằm trên đó có thể mất bất cứ lúc nào.",
            8, 50, Severity.Critical);

        Defect(d.UncorrectableSectors, "HDD-DEF-003", "Sector không thể sửa",
            "Lỗi vĩnh viễn, dữ liệu ở vùng này đã mất.", 8, 50, Severity.Critical);

        Defect(d.MediaErrors, "HDD-DEF-004", "Lỗi media (NVMe)",
            "Bộ điều khiển NVMe ghi nhận lỗi không sửa được ở tầng NAND.", 6, 40, Severity.Critical);

        Defect(d.ReportedUncorrectable, "HDD-DEF-007", "Lỗi không sửa được báo về",
            "Ổ gặp lỗi mà cơ chế sửa lỗi nội bộ không xử lý nổi.", 4, 30, Severity.Warning);

        if (d.CrcErrors is { } crc and > 0)
            card.Add("HDD-DEF-005", Kind, crc > 50 ? Severity.Warning : Severity.Notice,
                $"Lỗi CRC trên đường truyền: {crc:N0}",
                "Lỗi CRC nằm ở cáp và cổng chứ không phải ở bản thân ổ. " +
                "Thường do cáp SATA kém chất lượng, cổng lỏng hoặc từng bị va đập.",
                "Thay cáp SATA và cắm lại. Nếu con số ngừng tăng thì ổ vẫn tốt.",
                penalty: Math.Min(15, (int)crc / 5), confidence: 0.8, source: EvidenceSource.SmartAta);

        if (d.ShockErrors is { } shock and > 50)
            card.Add("HDD-DEF-006", Kind, Severity.Warning,
                $"Cảm biến va đập ghi nhận {shock:N0} lần",
                "Ổ cơ trong máy từng bị rơi hoặc rung mạnh nhiều lần. Đầu đọc có thể đã bị lệch.",
                "Với laptop mua lại, đây là dấu hiệu máy từng bị rơi — kiểm tra kỹ bản lề và khung vỏ.",
                penalty: 12, confidence: 0.7, source: EvidenceSource.SmartAta);

        if (d.ErrorLogEntries is { } errors and > 0)
            card.Metric("Số mục trong nhật ký lỗi của ổ", $"{errors:N0}");
    }

    /// <summary>
    /// Trả lời hai câu hỏi khi cân nhắc nâng cấp ổ: máy còn khe nào trống,
    /// và khe đó cho tốc độ tối đa bao nhiêu.
    ///
    /// Dữ liệu lấy từ SMBIOS Type 9. Từ SMBIOS 3.x, mã loại khe đã mã hoá sẵn
    /// thế hệ PCIe và số làn, nên suy ra được băng thông trần mà không cần
    /// đọc thanh ghi cấu hình PCI.
    /// </summary>
    private static void AppendUpgradeMetrics(ComponentAssessment assessment, SystemSnapshot snapshot)
    {
        var up = snapshot.Upgrade;
        if (!up.DataAvailable) return;

        var ssdSlots = up.SsdSlots.ToList();
        if (ssdSlots.Count == 0)
        {
            assessment.Metrics.Add(new MetricLine(
                "Khe lắp thêm ổ SSD",
                "Firmware không khai báo khe M.2 hoặc U.2 nào",
                "Máy có thể chỉ dùng cổng SATA, hoặc BIOS không liệt kê khe trong bảng SMBIOS",
                Severity.Notice));
        }
        else
        {
            foreach (var slot in ssdSlots)
            {
                var speed = slot.TheoreticalMBps is { } mbps
                    ? $"trần lý thuyết ~{mbps:N0} MB/s"
                    : "chưa xác định được thế hệ PCIe";

                assessment.Metrics.Add(new MetricLine(
                    $"Khe {slot.Designation.OrDash()}",
                    $"{slot.TypeText} — {(slot.IsAvailable ? "TRỐNG" : slot.IsUnavailable ? "không dùng được" : "đã lắp ổ")}",
                    speed,
                    slot.IsAvailable ? Severity.Info : Severity.Info));
            }

            var free = up.FreeSsdSlots.ToList();
            if (free.Count > 0)
            {
                var best = free.Where(s => s.TheoreticalMBps.HasValue)
                               .OrderByDescending(s => s.TheoreticalMBps)
                               .FirstOrDefault() ?? free[0];

                assessment.Findings.Add(new Finding
                {
                    Code = "HDD-UP-001",
                    Component = ComponentKind.Storage,
                    Severity = Severity.Info,
                    Title = $"Còn {free.Count} khe SSD trống — lắp thêm được mà không phải bỏ ổ cũ",
                    Detail = $"Khe nhanh nhất đang trống: {best.Designation.OrDash()} ({best.TypeText})" +
                             (best.TheoreticalMBps is { } m ? $", cho phép ổ chạy tới khoảng {m:N0} MB/s." : ".") +
                             "\n\nĐây thường là cách nâng cấp đáng tiền nhất trên máy cũ: giữ nguyên ổ hiện tại " +
                             "làm nơi chứa dữ liệu, lắp ổ mới làm ổ hệ thống.",
                    Recommendation = "Kiểm tra chiều dài khe (2280, 2242...) trước khi mua ổ. " +
                                     "Với laptop, cần xem cả việc máy có sẵn ốc và đế đỡ cho khe đó không.",
                    Source = EvidenceSource.Smbios,
                    ScorePenalty = 0,
                    Confidence = 0.8
                });
            }
        }

        // So tốc độ trần của khe với loại ổ đang dùng — chỗ này hay lộ ra
        // cơ hội nâng cấp lớn mà chủ máy không biết.
        var systemDrive = snapshot.StorageDevices.FirstOrDefault(d => d.DeviceIndex == 0)
                          ?? snapshot.StorageDevices.FirstOrDefault();
        var fastest = up.FastestSsdSlot;

        if (systemDrive is not null && fastest?.TheoreticalMBps is { } ceiling)
        {
            var isSata = systemDrive.BusType.Contains("SATA", StringComparison.OrdinalIgnoreCase) ||
                         systemDrive.BusType.Contains("ATA", StringComparison.OrdinalIgnoreCase);

            if (isSata && ceiling > 1500)
                assessment.Findings.Add(new Finding
                {
                    Code = "HDD-UP-002",
                    Component = ComponentKind.Storage,
                    Severity = Severity.Notice,
                    Title = "Ổ hệ thống đang chạy SATA trong khi máy có khe nhanh hơn nhiều",
                    Detail = $"Ổ hiện tại nối qua {systemDrive.BusType} — trần thực tế khoảng 550 MB/s. " +
                             $"Máy có khe {fastest.TypeText} cho tới khoảng {ceiling:N0} MB/s, " +
                             $"tức nhanh gấp {ceiling / 550.0:0.#} lần.",
                    Recommendation = "Nếu định giữ máy dùng lâu, thay ổ hệ thống sang NVMe là khoản nâng cấp " +
                                     "cho cảm giác khác biệt rõ nhất, hơn cả nâng RAM.",
                    Source = EvidenceSource.Smbios,
                    ScorePenalty = 0,
                    Confidence = 0.75
                });
            else if (!isSata)
                assessment.Metrics.Add(new MetricLine(
                    "Trần tốc độ ổ của máy",
                    $"~{ceiling:N0} MB/s qua {fastest.TypeText}",
                    "Đây là giới hạn của khe, ổ thực tế có thể chậm hơn"));
        }

        if (up.ExpansionSlots.Count > 0)
            assessment.Metrics.Add(new MetricLine(
                "Tổng số khe mở rộng",
                $"{up.ExpansionSlots.Count} khe, {up.ExpansionSlots.Count(s => s.IsAvailable)} còn trống",
                "Bao gồm cả khe PCIe cho card rời và khe Wi-Fi"));
    }

    private void AnalyzeTemperature(ScoreCard card, StorageDeviceInfo d)
    {
        if (d.TemperatureC is not { } temp) return;
        card.Metric("Nhiệt độ hiện tại", $"{temp:0} °C");

        if (temp >= 70)
            card.Add("HDD-TEMP-001", Kind, Severity.Warning,
                $"Ổ đang nóng {temp:0} °C",
                "SSD NVMe quá nhiệt sẽ tự hạ tốc độ; ổ cơ trên 60 °C có tuổi thọ giảm rõ rệt.",
                "Gắn tản nhiệt cho khe M.2 hoặc cải thiện luồng gió trong máy.",
                penalty: 10, confidence: 0.85, source: EvidenceSource.SmartAta);
    }
}
