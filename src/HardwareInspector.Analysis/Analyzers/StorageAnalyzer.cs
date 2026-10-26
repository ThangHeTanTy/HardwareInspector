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

        var merged = new ScoreCard(Kind, S($"{results.Count} ổ lưu trữ", $"{results.Count} storage drives"))
            .Headline(S($"Ổ yếu nhất: {worst.DisplayName} — {worst.Rating.ToText()}", $"Weakest drive: {worst.DisplayName} — {worst.Rating.ToText()}"));

        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            merged.Metric(r.DisplayName, $"{r.Rating.ToText()} ({r.Score}/100)",
                r.OperatingTime.HasValue ? S($"Đã chạy {r.OperatingTime.Value.ToHumanDuration()}", $"Powered on for {r.OperatingTime.Value.ToHumanDuration()}") : null,
                anchor: $"storage:{i}");
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

        card.Metric(S("Loại / giao tiếp", "Type / interface"), S($"{d.MediaType.OrDash()} qua {d.BusType.OrDash()}", $"{d.MediaType.OrDash()} over {d.BusType.OrDash()}"))
            .MetricIf(d.SpindleSpeedRpm is > 0, S("Tốc độ vòng quay", "Spindle speed"), $"{d.SpindleSpeedRpm} rpm")
            .Metric("Firmware", d.FirmwareRevision.OrDash())
            .Metric("Serial", FormatHelpers.Mask(d.SerialNumber))
            .MetricIf(d.SmartSource is not null, S("Nguồn dữ liệu sức khoẻ", "Health data source"), d.SmartSource.OrDash());

        if (!d.SmartAvailable)
        {
            card.Add("HDD-SMART-000", Kind, Severity.Warning,
                S("Không đọc được S.M.A.R.T. của ổ này", "Could not read S.M.A.R.T. from this drive"),
                d.SmartFailureReasonText ?? S("Không xác định được nguyên nhân.", "The cause could not be determined."),
                S("Không có S.M.A.R.T. thì không biết ổ đã chạy bao lâu và đã hỏng sector nào chưa — " +
                  "tức là mất hoàn toàn cơ sở để định giá ổ. Hãy khắc phục theo hướng dẫn ở trên " +
                  "rồi chạy lại trước khi quyết định mua.",
                  "Without S.M.A.R.T. there is no way to know how long the drive has run or whether it has bad sectors — " +
                  "the whole basis for valuing the drive is gone. Fix it using the guidance above " +
                  "and run again before deciding to buy."),
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
                a.IsFailing
                    ? S($"ĐÃ VƯỢT NGƯỠNG ({a.Current} ≤ {a.Threshold})", $"THRESHOLD CROSSED ({a.Current} ≤ {a.Threshold})")
                    : S($"giá trị {a.Current}, ngưỡng {a.Threshold}", $"value {a.Current}, threshold {a.Threshold}"),
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
            S("Ổ NVMe đang bật cờ cảnh báo tới hạn", "The NVMe drive has its critical warning flag set"),
            string.Join("\n", warnings.Select(w => "• " + w)),
            S("Sao lưu dữ liệu ngay lập tức. Cờ này do firmware của ổ tự đặt, không phải suy đoán của phần mềm.",
              "Back up your data immediately. This flag is set by the drive's own firmware, not guessed by this software."),
            penalty: 50, confidence: 1.0, source: EvidenceSource.SmartNvme);
    }

    private void AnalyzeLifetime(ScoreCard card, StorageDeviceInfo d)
    {
        if (d.PowerOnHours is { } hours && hours > 0)
        {
            var span = TimeSpan.FromHours(hours);
            card.Uptime(span);
            card.Metric(S("Thời gian đã hoạt động", "Power-on time"), FormatHelpers.HoursToHuman(hours));

            if (hours > 35_000)
                card.Add("HDD-AGE-001", Kind, Severity.Critical,
                    S($"Ổ đã chạy {hours:N0} giờ (khoảng {hours / 8760.0:0.#} năm)", $"Drive has run {hours:N0} hours (about {hours / 8760.0:0.#} years)"),
                    S("Vượt xa vòng đời thiết kế phổ biến. Kể cả khi S.M.A.R.T. chưa báo lỗi, " +
                      "rủi ro hỏng đột ngột đã rất cao.",
                      "Far beyond the typical design life. Even if S.M.A.R.T. reports no errors yet, " +
                      "the risk of sudden failure is very high."),
                    S("Chỉ nhận máy nếu người bán trừ hẳn giá một ổ mới vào giá bán.", "Only accept the machine if the seller knocks the price of a new drive off."),
                    penalty: 35, confidence: 0.95, source: EvidenceSource.SmartAta);
            else if (hours > 20_000)
                card.Add("HDD-AGE-002", Kind, Severity.Warning,
                    S($"Ổ đã chạy {hours:N0} giờ (khoảng {hours / 8760.0:0.#} năm)", $"Drive has run {hours:N0} hours (about {hours / 8760.0:0.#} years)"),
                    S("Mức sử dụng cao. Với ổ cơ, đây là giai đoạn tỉ lệ hỏng bắt đầu tăng nhanh.",
                      "Heavy use. For hard disks this is when the failure rate starts climbing fast."),
                    S("Sao lưu thường xuyên và chuẩn bị ngân sách thay ổ.", "Back up regularly and budget for a replacement drive."),
                    penalty: 18, confidence: 0.9, source: EvidenceSource.SmartAta);
            else if (hours > 10_000)
                card.Add("HDD-AGE-003", Kind, Severity.Notice,
                    S($"Ổ đã chạy {hours:N0} giờ", $"Drive has run {hours:N0} hours"),
                    S("Mức sử dụng trung bình khá, tương đương 3-4 năm dùng văn phòng.", "Moderate use, roughly 3-4 years of office work."),
                    penalty: 7, confidence: 0.9, source: EvidenceSource.SmartAta);
        }
        else
        {
            card.Metric(S("Thời gian đã hoạt động", "Power-on time"), S("Không có trong dữ liệu ổ trả về", "Not included in the drive's data"), null, Severity.Notice);
        }

        if (d.PowerCycleCount is { } cycles and > 0)
        {
            card.Metric(S("Số lần bật/tắt", "Power cycles"), S($"{cycles:N0} lần", $"{cycles:N0}"));

            if (d.PowerOnHours is { } h and > 0)
            {
                var hoursPerCycle = h / (double)cycles;
                if (hoursPerCycle < 1.5 && cycles > 500)
                    card.Add("HDD-CYCLE-001", Kind, Severity.Notice,
                        S("Số lần bật/tắt rất cao so với số giờ chạy", "Very many power cycles for the hours run"),
                        S($"Trung bình mỗi lần bật máy chỉ chạy {hoursPerCycle:0.#} giờ. " +
                          "Hay gặp ở máy dùng làm quầy thu ngân, máy trưng bày hoặc máy bị mất điện đột ngột nhiều lần.",
                          $"On average each power-on lasted only {hoursPerCycle:0.#} hours. " +
                          "Typical of point-of-sale machines, display units, or machines that lost power suddenly many times."),
                        penalty: 5, confidence: 0.5, source: EvidenceSource.SmartAta);
            }
        }

        var life = d.RemainingLifePercent;
        if (life.HasValue)
        {
            card.Health(life.Value);
            card.Metric(S("Tuổi thọ ghi còn lại", "Remaining write endurance"), $"{life.Value}%",
                d.PercentageUsed is { } used ? S($"Đã dùng {used}% quỹ ghi", $"{used}% of rated endurance used") : null);

            if (life.Value < 10)
                card.Add("SSD-LIFE-001", Kind, Severity.Critical,
                    S($"SSD chỉ còn {life.Value}% tuổi thọ ghi", $"SSD has only {life.Value}% write endurance left"),
                    S("Khi cạn quỹ ghi, ổ chuyển sang chế độ chỉ đọc hoặc mất dữ liệu.", "Once endurance runs out the drive goes read-only or loses data."),
                    S("Coi như phải thay ổ ngay.", "Treat the drive as needing replacement now."),
                    penalty: 45, confidence: 0.95, source: EvidenceSource.SmartNvme);
            else if (life.Value < 30)
                card.Add("SSD-LIFE-002", Kind, Severity.Warning,
                    S($"SSD còn {life.Value}% tuổi thọ ghi", $"SSD has {life.Value}% write endurance left"),
                    S("Ổ đã dùng phần lớn quỹ ghi. Thường thấy ở máy từng chạy máy ảo, dựng phim hoặc làm node blockchain.",
                      "Most of the endurance is used. Common on machines that ran VMs, video editing or blockchain nodes."),
                    S("Trừ giá và lên kế hoạch thay trong 1-2 năm.", "Negotiate the price down and plan a replacement within 1-2 years."),
                    penalty: 22, confidence: 0.9, source: EvidenceSource.SmartNvme);
        }

        if (d.AvailableSparePercent is { } spare && d.AvailableSpareThresholdPercent is { } threshold)
        {
            card.Metric(S("Vùng dự phòng còn lại", "Available spare"), $"{spare}%", S($"Ngưỡng an toàn: {threshold}%", $"Safety threshold: {threshold}%"),
                spare <= threshold ? Severity.Critical : Severity.Info);

            if (spare <= threshold)
                card.Add("SSD-SPARE-001", Kind, Severity.Critical,
                    S($"Vùng dự phòng đã cạn: còn {spare}%, ngưỡng {threshold}%", $"Spare area exhausted: {spare}% left, threshold {threshold}%"),
                    S("SSD dùng vùng dự phòng để thay các khối NAND hỏng. Cạn vùng này nghĩa là " +
                      "ổ không còn khả năng tự sửa chữa nữa.",
                      "SSDs use the spare area to replace failed NAND blocks. Running out means " +
                      "the drive can no longer repair itself."),
                    S("Thay ổ ngay.", "Replace the drive now."),
                    penalty: 45, confidence: 1.0, source: EvidenceSource.SmartNvme);
        }

        if (d.TeraBytesWritten is { } tbw)
        {
            card.Metric(S("Tổng dữ liệu đã ghi", "Total data written"), $"{tbw:0.##} TB");
            if (tbw > 200)
                card.Add("SSD-TBW-001", Kind, Severity.Warning,
                    S($"Đã ghi {tbw:0} TB — khối lượng rất lớn", $"{tbw:0} TB written — a very large amount"),
                    S("Con số này vượt xa mức sử dụng cá nhân thông thường (thường dưới 30 TB sau vài năm).",
                      "Far beyond typical personal use (usually under 30 TB after several years)."),
                    S("Hỏi rõ máy trước đây dùng vào việc gì.", "Ask exactly what the machine was used for."),
                    penalty: 12, confidence: 0.7, source: EvidenceSource.SmartAta);
        }

        if (d.TeraBytesRead is { } tbr) card.Metric(S("Tổng dữ liệu đã đọc", "Total data read"), $"{tbr:0.##} TB");

        if (d.UnsafeShutdowns is { } unsafeCount and > 100)
            card.Add("HDD-PWR-001", Kind, Severity.Notice,
                S($"{unsafeCount:N0} lần mất điện đột ngột", $"{unsafeCount:N0} unsafe shutdowns"),
                S("Mỗi lần mất điện giữa chừng đều có nguy cơ làm hỏng bảng ánh xạ của SSD.",
                  "Every sudden power loss risks corrupting the SSD's mapping table."),
                penalty: 6, confidence: 0.7, source: EvidenceSource.SmartAta);
    }

    private void AnalyzeDefects(ScoreCard card, StorageDeviceInfo d)
    {
        if (d.SmartPredictFailure)
            card.Add("HDD-FAIL-001", Kind, Severity.Critical,
                S("Ổ tự báo sắp hỏng (thuộc tính pre-failure đã dưới ngưỡng)", "The drive predicts its own failure (a pre-failure attribute is below threshold)"),
                S("Firmware của chính ổ đã kết luận rằng nó sẽ hỏng. Đây là mức cảnh báo cao nhất.",
                  "The drive's own firmware has concluded it will fail. This is the highest warning level."),
                S("Không dùng ổ này cho dữ liệu quan trọng. Từ chối mua nếu người bán không thay ổ.",
                  "Do not trust this drive with important data. Walk away unless the seller replaces it."),
                penalty: 70, confidence: 1.0, source: EvidenceSource.SmartAta);

        void Defect(long? value, string code, string label, string meaning, int perUnit, int cap, Severity sev)
        {
            if (value is not { } v || v <= 0) return;
            card.Metric(label, $"{v:N0}", null, sev);
            card.Add(code, Kind, sev, $"{label}: {v:N0}", meaning,
                S("Sao lưu ngay và chạy quét bề mặt toàn ổ để xem con số có tiếp tục tăng không.",
                  "Back up now and run a full surface scan to see whether the count keeps rising."),
                penalty: Math.Min(cap, (int)(v * perUnit)), confidence: 0.95, source: EvidenceSource.SmartAta);
        }

        Defect(d.ReallocatedSectors, "HDD-DEF-001", S("Sector đã bị tráo", "Reallocated sectors"),
            S("Ổ đã phải thay sector hỏng bằng sector dự phòng. Con số tăng dần theo thời gian là dấu hiệu ổ đang chết.",
              "The drive has had to swap bad sectors for spares. A count that grows over time means the drive is dying."),
            5, 45, Severity.Critical);

        Defect(d.PendingSectors, "HDD-DEF-002", S("Sector đang chờ tráo", "Pending sectors"),
            S("Đây là các sector đọc lỗi nhưng chưa được thay. Dữ liệu nằm trên đó có thể mất bất cứ lúc nào.",
              "Sectors that failed to read but have not been replaced yet. Data on them can be lost at any moment."),
            8, 50, Severity.Critical);

        Defect(d.UncorrectableSectors, "HDD-DEF-003", S("Sector không thể sửa", "Uncorrectable sectors"),
            S("Lỗi vĩnh viễn, dữ liệu ở vùng này đã mất.", "Permanent errors; data in these areas is already lost."), 8, 50, Severity.Critical);

        Defect(d.MediaErrors, "HDD-DEF-004", S("Lỗi media (NVMe)", "Media errors (NVMe)"),
            S("Bộ điều khiển NVMe ghi nhận lỗi không sửa được ở tầng NAND.", "The NVMe controller logged unrecoverable errors in the NAND."), 6, 40, Severity.Critical);

        Defect(d.ReportedUncorrectable, "HDD-DEF-007", S("Lỗi không sửa được báo về", "Reported uncorrectable errors"),
            S("Ổ gặp lỗi mà cơ chế sửa lỗi nội bộ không xử lý nổi.", "The drive hit errors its internal error correction could not fix."), 4, 30, Severity.Warning);

        if (d.CrcErrors is { } crc and > 0)
            card.Add("HDD-DEF-005", Kind, crc > 50 ? Severity.Warning : Severity.Notice,
                S($"Lỗi CRC trên đường truyền: {crc:N0}", $"Interface CRC errors: {crc:N0}"),
                S("Lỗi CRC nằm ở cáp và cổng chứ không phải ở bản thân ổ. " +
                  "Thường do cáp SATA kém chất lượng, cổng lỏng hoặc từng bị va đập.",
                  "CRC errors come from the cable and port, not the drive itself. " +
                  "Usually a poor SATA cable, a loose port, or past physical knocks."),
                S("Thay cáp SATA và cắm lại. Nếu con số ngừng tăng thì ổ vẫn tốt.", "Replace the SATA cable and reseat it. If the count stops rising, the drive is fine."),
                penalty: Math.Min(15, (int)crc / 5), confidence: 0.8, source: EvidenceSource.SmartAta);

        if (d.ShockErrors is { } shock and > 50)
            card.Add("HDD-DEF-006", Kind, Severity.Warning,
                S($"Cảm biến va đập ghi nhận {shock:N0} lần", $"Shock sensor triggered {shock:N0} times"),
                S("Ổ cơ trong máy từng bị rơi hoặc rung mạnh nhiều lần. Đầu đọc có thể đã bị lệch.",
                  "The hard disk has been dropped or shaken hard many times. The heads may be misaligned."),
                S("Với laptop mua lại, đây là dấu hiệu máy từng bị rơi — kiểm tra kỹ bản lề và khung vỏ.",
                  "On a used laptop this suggests it was dropped — check the hinges and chassis carefully."),
                penalty: 12, confidence: 0.7, source: EvidenceSource.SmartAta);

        if (d.ErrorLogEntries is { } errors and > 0)
            card.Metric(S("Số mục trong nhật ký lỗi của ổ", "Drive error log entries"), $"{errors:N0}");
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
                S("Khe lắp thêm ổ SSD", "Slots for another SSD"),
                S("Firmware không khai báo khe M.2 hoặc U.2 nào", "The firmware declares no M.2 or U.2 slots"),
                S("Máy có thể chỉ dùng cổng SATA, hoặc BIOS không liệt kê khe trong bảng SMBIOS",
                  "The machine may only use SATA ports, or the BIOS does not list slots in its SMBIOS table"),
                Severity.Notice));
        }
        else
        {
            foreach (var slot in ssdSlots)
            {
                var speed = slot.TheoreticalMBps is { } mbps
                    ? S($"trần lý thuyết ~{mbps:N0} MB/s", $"theoretical ceiling ~{mbps:N0} MB/s")
                    : S("chưa xác định được thế hệ PCIe", "PCIe generation unknown");

                assessment.Metrics.Add(new MetricLine(
                    S($"Khe {slot.Designation.OrDash()}", $"Slot {slot.Designation.OrDash()}"),
                    $"{slot.TypeText} — {(slot.IsAvailable ? S("TRỐNG", "FREE") : slot.IsUnavailable ? S("không dùng được", "unusable") : S("đã lắp ổ", "occupied"))}",
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
                    Title = S($"Còn {free.Count} khe SSD trống — lắp thêm được mà không phải bỏ ổ cũ",
                              $"{free.Count} free SSD slot(s) — you can add a drive without removing the old one"),
                    Detail = S($"Khe nhanh nhất đang trống: {best.Designation.OrDash()} ({best.TypeText})",
                               $"Fastest free slot: {best.Designation.OrDash()} ({best.TypeText})") +
                             (best.TheoreticalMBps is { } m ? S($", cho phép ổ chạy tới khoảng {m:N0} MB/s.", $", allowing drives up to about {m:N0} MB/s.") : ".") +
                             S("\n\nĐây thường là cách nâng cấp đáng tiền nhất trên máy cũ: giữ nguyên ổ hiện tại " +
                               "làm nơi chứa dữ liệu, lắp ổ mới làm ổ hệ thống.",
                               "\n\nThis is usually the best-value upgrade on a used machine: keep the current drive " +
                               "for data and install the new one as the system drive."),
                    Recommendation = S("Kiểm tra chiều dài khe (2280, 2242...) trước khi mua ổ. " +
                                       "Với laptop, cần xem cả việc máy có sẵn ốc và đế đỡ cho khe đó không.",
                                       "Check the slot length (2280, 2242...) before buying a drive. " +
                                       "On laptops, also check that the screw and standoff for that slot are present."),
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
                    Title = S("Ổ hệ thống đang chạy SATA trong khi máy có khe nhanh hơn nhiều",
                              "The system drive runs on SATA although the machine has a much faster slot"),
                    Detail = S($"Ổ hiện tại nối qua {systemDrive.BusType} — trần thực tế khoảng 550 MB/s. " +
                               $"Máy có khe {fastest.TypeText} cho tới khoảng {ceiling:N0} MB/s, " +
                               $"tức nhanh gấp {ceiling / 550.0:0.#} lần.",
                               $"The current drive connects over {systemDrive.BusType} — about 550 MB/s in practice. " +
                               $"The machine has a {fastest.TypeText} slot good for about {ceiling:N0} MB/s, " +
                               $"{ceiling / 550.0:0.#} times faster."),
                    Recommendation = S("Nếu định giữ máy dùng lâu, thay ổ hệ thống sang NVMe là khoản nâng cấp " +
                                       "cho cảm giác khác biệt rõ nhất, hơn cả nâng RAM.",
                                       "If you plan to keep the machine, moving the system drive to NVMe is the upgrade " +
                                       "you will feel most, more than adding RAM."),
                    Source = EvidenceSource.Smbios,
                    ScorePenalty = 0,
                    Confidence = 0.75
                });
            else if (!isSata)
                assessment.Metrics.Add(new MetricLine(
                    S("Trần tốc độ ổ của máy", "Machine's drive speed ceiling"),
                    S($"~{ceiling:N0} MB/s qua {fastest.TypeText}", $"~{ceiling:N0} MB/s via {fastest.TypeText}"),
                    S("Đây là giới hạn của khe, ổ thực tế có thể chậm hơn", "This is the slot's limit; the actual drive may be slower")));
        }

        if (up.ExpansionSlots.Count > 0)
            assessment.Metrics.Add(new MetricLine(
                S("Tổng số khe mở rộng", "Total expansion slots"),
                S($"{up.ExpansionSlots.Count} khe, {up.ExpansionSlots.Count(s => s.IsAvailable)} còn trống",
                  $"{up.ExpansionSlots.Count} slots, {up.ExpansionSlots.Count(s => s.IsAvailable)} free"),
                S("Bao gồm cả khe PCIe cho card rời và khe Wi-Fi", "Includes PCIe slots for add-in cards and the Wi-Fi slot")));
    }

    private void AnalyzeTemperature(ScoreCard card, StorageDeviceInfo d)
    {
        if (d.TemperatureC is not { } temp) return;
        card.Metric(S("Nhiệt độ hiện tại", "Current temperature"), $"{temp:0} °C");

        if (temp >= 70)
            card.Add("HDD-TEMP-001", Kind, Severity.Warning,
                S($"Ổ đang nóng {temp:0} °C", $"Drive is running hot at {temp:0} °C"),
                S("SSD NVMe quá nhiệt sẽ tự hạ tốc độ; ổ cơ trên 60 °C có tuổi thọ giảm rõ rệt.",
                  "Overheating NVMe SSDs throttle themselves; hard disks above 60 °C wear out noticeably faster."),
                S("Gắn tản nhiệt cho khe M.2 hoặc cải thiện luồng gió trong máy.", "Fit an M.2 heatsink or improve airflow in the case."),
                penalty: 10, confidence: 0.85, source: EvidenceSource.SmartAta);
    }
}
