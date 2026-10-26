using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Assessment;
using HardwareInspector.Core.Models.Components;
using HardwareInspector.Core.Utils;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Analysis.Advice;

/// <summary>
/// Biến kết quả kỹ thuật thành lời khuyên mua bán cho người không rành máy tính.
///
/// Ba nhóm, theo thứ tự ưu tiên:
/// 1. Không nên mua — thứ tiền không sửa được, hoặc là dấu hiệu gian lận (CPU/BIOS bị sửa,
///    VRAM hỏng, máy vẫn thuộc tổ chức khác).
/// 2. Điều kiện — việc người bán phải làm xong ngay tại chỗ (gỡ mật khẩu BIOS, gỡ chống trộm).
/// 3. Chi phí — hỏng hóc sửa được bằng tiền, quy ra đồng để làm cơ sở trả giá.
///
/// Giá là mức tham khảo thị trường Việt Nam cho linh kiện phổ thông kèm công thay,
/// cố ý để khoảng rộng; người dùng nên hỏi thêm cửa hàng quen.
/// </summary>
public sealed class PurchaseAdvisor
{
    public PurchaseAdvice Advise(SystemSnapshot snapshot, MachineAssessment a)
    {
        var advice = new PurchaseAdvice();
        var codes = a.Components.SelectMany(c => c.Findings).Select(f => f.Code)
            .Concat(a.Trust.RedFlags.Select(f => f.Code))
            .ToHashSet(StringComparer.Ordinal);
        bool Has(params string[] c) => c.Any(codes.Contains);

        CollectBlockers(snapshot, a, advice, Has);
        CollectConditions(snapshot, advice, Has);
        CollectCosts(snapshot, advice, Has);
        Decide(a, advice);
        return advice;
    }

    // ------------------------------------------------------------------
    private static void CollectBlockers(SystemSnapshot s, MachineAssessment a, PurchaseAdvice advice, Func<string[], bool> has)
    {
        bool Has(params string[] c) => has(c);
        var b = advice.Blockers;

        if (s.Ownership.AutopilotProfilePresent)
            b.Add(S("Máy vẫn đăng ký Windows Autopilot của một tổ chức — cài lại Windows là bị khoá.",
                    "The machine is still registered to an organisation's Windows Autopilot — it locks after a reinstall."));
        if (s.Ownership.MdmEnrollments.Count > 0)
            b.Add(S("Máy đang bị một tổ chức quản lý từ xa (MDM), có thể bị khoá hoặc xoá bất cứ lúc nào.",
                    "The machine is remotely managed by an organisation (MDM) and can be locked or wiped at any time."));
        if (Has("AUTH-CPU-001", "AUTH-CPU-002"))
            b.Add(S("Tên CPU đã bị sửa để trông như đời cao hơn — dấu hiệu gian lận.",
                    "The CPU name was altered to look like a newer model — a sign of fraud."));
        if (Has("AUTH-GPU-001"))
            b.Add(S("Tên card đồ hoạ không khớp mã phần cứng — card có thể bị nạp vBIOS của model khác.",
                    "The graphics card name does not match its hardware ID — it may carry another model's vBIOS."));
        if (Has("GPU-SPEC-003"))
            b.Add(S("Card đồ hoạ có ít VRAM hơn mọi phiên bản chính hãng của model nó khai.",
                    "The graphics card has less VRAM than any official variant of the model it claims."));
        if (Has("GPU-SPEC-001"))
            b.Add(S("Hiệu năng card đồ hoạ thấp hơn nhiều so với model nó khai — nhiều khả năng là card đời thấp bị đổi tên.",
                    "Graphics performance is far below the model it claims — most likely a lower-end card renamed."));
        if (Has("GPU-VRAM-ERR", "GPU-CALC-001", "GPU-TDR-002"))
            b.Add(S("Card đồ hoạ lỗi khi chạy tải (VRAM hỏng, tính sai hoặc driver sập) — sửa thường đắt hơn giá trị card.",
                    "The graphics card fails under load (bad VRAM, wrong results or driver crashes) — repair usually costs more than the card."));
        if (Has("FW-BIOS-001") || s.Firmware.Status == IntegrityStatus.Compromised)
            b.Add(S("BIOS/firmware có dấu hiệu bị chỉnh sửa hoặc can thiệp.",
                    "The BIOS/firmware shows signs of modification or tampering."));
        if (Has("CPU-ES-001"))
            b.Add(S("CPU là mẫu kỹ thuật (ES/QS), không được bảo hành và có thể chạy không ổn định.",
                    "The CPU is an engineering sample (ES/QS) with no warranty and possible instability."));
        if (Has("SSD-NVME-001", "HDD-FAIL-001") && s.StorageDevices.Count == 1)
            b.Add(S("Ổ lưu trữ duy nhất của máy đang tự báo sắp hỏng — dữ liệu và hệ điều hành có thể mất bất cứ lúc nào.",
                    "The machine's only drive is reporting imminent failure — data and the OS can be lost at any moment."));
    }

    // ------------------------------------------------------------------
    private static void CollectConditions(SystemSnapshot s, PurchaseAdvice advice, Func<string[], bool> has)
    {
        var c = advice.Conditions;
        var o = s.Ownership;

        if (o.BiosPasswordSet == true)
            c.Add(S("Người bán gỡ mật khẩu BIOS ngay tại chỗ, bạn tự vào BIOS thử lại.",
                    "The seller removes the BIOS password on the spot, and you enter the BIOS yourself to check."));
        if (o.AbsoluteAgentFound)
            c.Add(S("Người bán xác nhận đã gỡ máy khỏi tài khoản chống trộm Absolute/Computrace.",
                    "The seller confirms the machine was removed from their Absolute/Computrace anti-theft account."));
        if (o.IntelAmtActive)
            c.Add(S("Tắt Intel AMT trong BIOS/MEBx (người bán cung cấp mật khẩu MEBx).",
                    "Turn off Intel AMT in the BIOS/MEBx (the seller provides the MEBx password)."));
        if (o.AzureAdJoined && !o.AutopilotProfilePresent && o.MdmEnrollments.Count == 0)
            c.Add(S("Người bán ngắt máy khỏi tài khoản tổ chức (Cài đặt → Tài khoản → Truy cập cơ quan hoặc trường học).",
                    "The seller disconnects the machine from the organisation account (Settings → Accounts → Access work or school)."));
        if (has(new[] { "FW-SB-001" }))
            c.Add(S("Khôi phục khoá Secure Boot gốc (Restore Factory Keys) và bật lại Secure Boot.",
                    "Restore the original Secure Boot keys (Restore Factory Keys) and turn Secure Boot back on."));
        if (!s.OperatingSystem.IsActivated)
            c.Add(S("Hỏi rõ Windows có bản quyền không — hiện chưa kích hoạt.",
                    "Ask whether Windows is licensed — it is not activated right now."));
    }

    // ------------------------------------------------------------------
    private static void CollectCosts(SystemSnapshot s, PurchaseAdvice advice, Func<string[], bool> has)
    {
        bool Has(params string[] c) => has(c);
        var costs = advice.Costs;
        var laptop = s.IsLaptop;

        // --- Ổ lưu trữ: tính theo từng ổ đang có vấn đề ---
        foreach (var d in s.StorageDevices)
        {
            var failing = d.SmartPredictFailure || d.NvmeCriticalWarning is > 0 ||
                          d.RemainingLifePercent is < 30 ||
                          d.AvailableSparePercent is { } sp && d.AvailableSpareThresholdPercent is { } th && sp <= th ||
                          (d.ReallocatedSectors ?? 0) + (d.PendingSectors ?? 0) + (d.UncorrectableSectors ?? 0) + (d.MediaErrors ?? 0) > 0 ||
                          d.PowerOnHours is > 35_000;
            if (!failing) continue;

            costs.Add(new CostItem(CostKind.Repair, ComponentKind.Storage,
                S($"Thay ổ {d.Model.OrDash()}", $"Replace drive {d.Model.OrDash()}"),
                S("Ổ đã hỏng sector, cạn tuổi thọ ghi hoặc tự báo sắp hỏng. Giá cho SSD 512 GB mới.",
                  "The drive has bad sectors, exhausted endurance or predicts its own failure. Price for a new 512 GB SSD."),
                800_000, 1_500_000));
        }

        if (Has("HDD-DEF-005"))
            costs.Add(new CostItem(CostKind.Repair, ComponentKind.Storage,
                S("Thay cáp SATA", "Replace the SATA cable"),
                S("Có lỗi truyền dữ liệu trên cáp. Thường chỉ cần cáp mới.", "Transfer errors on the cable. Usually a new cable fixes it."),
                30_000, 80_000));

        // --- Pin ---
        var shortRuntime = s.DeviceTests.BatteryRuntimeHours is < 1.5;
        if (s.PrimaryBattery is { } bat && (bat.HealthPercent is < 70 || bat.CycleCount is > 800 || shortRuntime))
            costs.Add(new CostItem(CostKind.Repair, ComponentKind.Battery,
                S("Thay pin laptop", "Replace the laptop battery"),
                S($"Pin còn {bat.HealthPercent:0}% dung lượng thiết kế{(bat.CycleCount is { } cy ? $", {cy:N0} chu kỳ" : string.Empty)}" +
                  $"{(s.DeviceTests.BatteryRuntimeHours is { } rh ? $", đo thực tế dùng được {rh:0.0} giờ" : string.Empty)}. Pin chính hãng ở mức giá cao.",
                  $"Battery at {bat.HealthPercent:0}% of design capacity{(bat.CycleCount is { } cy2 ? $", {cy2:N0} cycles" : string.Empty)}" +
                  $"{(s.DeviceTests.BatteryRuntimeHours is { } rh2 ? $", measured runtime {rh2:0.0} h" : string.Empty)}. Genuine batteries cost toward the top of the range."),
                600_000, 1_500_000));

        // --- Tản nhiệt ---
        if (Has("CPU-TEMP-001", "CPU-TEMP-002", "CPU-TEMP-003", "GPU-TEMP-001", "CPU-CLK-001"))
            costs.Add(new CostItem(CostKind.Repair, ComponentKind.Cooling,
                S("Vệ sinh máy, thay keo tản nhiệt CPU", "Clean the machine and replace CPU thermal paste"),
                S("Nhiệt độ cao hoặc CPU tụt xung — keo tản nhiệt đã khô hoặc bụi bám.",
                  "High temperatures or clock throttling — dried paste or dust build-up."),
                laptop ? 200_000 : 150_000, laptop ? 500_000 : 300_000));

        if (Has("GPU-TEMP-002", "GPU-TEMP-003", "GPU-VRAM-001"))
            costs.Add(new CostItem(CostKind.Repair, ComponentKind.Gpu,
                S("Thay keo và pad tản nhiệt card đồ hoạ", "Replace graphics card paste and thermal pads"),
                S("Điểm nóng hoặc VRAM quá nhiệt — keo/pad đã xuống cấp.", "Hotspot or VRAM overheating — paste/pads have degraded."),
                300_000, 700_000));

        if (Has("MB-FAN-001", "GPU-FAN-001"))
            costs.Add(new CostItem(CostKind.Repair, ComponentKind.Cooling,
                S("Thay quạt tản nhiệt", "Replace a cooling fan"),
                S("Có quạt không quay khi máy chạy tải.", "A fan does not spin under load."),
                laptop ? 250_000 : 100_000, laptop ? 600_000 : 350_000));

        // --- Thiết bị vào ra người dùng đã thử trong ứng dụng ---
        var t = s.DeviceTests;
        if (t.KeyboardOk == false)
        {
            var bad = t.ChatterKeys.Concat(t.StuckKeys).Distinct().ToList();
            var detail = bad.Count > 0
                ? S($"Phím lỗi: {string.Join(", ", bad.Take(10))}.", $"Faulty keys: {string.Join(", ", bad.Take(10))}.")
                : t.UntestedKeys.Count > 0
                    ? S($"Phím không nhận: {string.Join(", ", t.UntestedKeys.Take(10))}.", $"Keys not registering: {string.Join(", ", t.UntestedKeys.Take(10))}.")
                    : string.Empty;
            costs.Add(new CostItem(CostKind.Repair, ComponentKind.Motherboard,
                laptop ? S("Thay bàn phím laptop", "Replace the laptop keyboard") : S("Thay bàn phím", "Replace the keyboard"),
                (S("Bài thử bàn phím không đạt. ", "The keyboard test failed. ") + detail).Trim(),
                laptop ? 300_000 : 150_000, laptop ? 900_000 : 500_000));
        }

        if (t.TouchpadOk == false && laptop)
            costs.Add(new CostItem(CostKind.Repair, ComponentKind.Motherboard,
                S("Sửa hoặc thay touchpad", "Repair or replace the touchpad"),
                S($"Bài thử touchpad không đạt (phủ được {t.TouchpadCoveragePercent:0}% khung).",
                  $"The touchpad test failed ({t.TouchpadCoveragePercent:0}% of the area covered)."),
                300_000, 1_000_000));

        if (laptop && (t.SpeakerLeftOk == false || t.SpeakerRightOk == false))
            costs.Add(new CostItem(CostKind.Repair, ComponentKind.Motherboard,
                S("Thay loa laptop", "Replace the laptop speakers"),
                S($"Loa {(t.SpeakerLeftOk == false && t.SpeakerRightOk == false ? "cả hai bên" : t.SpeakerLeftOk == false ? "trái" : "phải")} không đạt.",
                  $"{(t.SpeakerLeftOk == false && t.SpeakerRightOk == false ? "Both speakers" : t.SpeakerLeftOk == false ? "Left speaker" : "Right speaker")} failed."),
                200_000, 600_000));

        if (laptop && t.MicOk == false)
            costs.Add(new CostItem(CostKind.Repair, ComponentKind.Motherboard,
                S("Sửa micro", "Repair the microphone"),
                S("Micro không thu được tiếng. Nếu không cần gọi video, có thể dùng tai nghe có micro thay thế.",
                  "The microphone picks up nothing. If you do not need video calls, a headset with a mic is a workaround."),
                150_000, 450_000));

        if (t.UsbOk == false)
            costs.Add(new CostItem(CostKind.Repair, ComponentKind.Motherboard,
                S("Sửa cổng USB", "Repair USB ports"),
                S($"Bài thử cổng USB không đạt ({t.UsbPortsSeen.Count}{(t.UsbPortsExpected is { } e ? $"/{e}" : string.Empty)} cổng nhận thiết bị). Thường là hàn lại chân cổng.",
                  $"The USB port test failed ({t.UsbPortsSeen.Count}{(t.UsbPortsExpected is { } e2 ? $"/{e2}" : string.Empty)} ports responded). Usually the connector needs resoldering."),
                laptop ? 200_000 : 100_000, laptop ? 600_000 : 400_000));

        if (laptop && t.CameraOk == false)
            costs.Add(new CostItem(CostKind.Repair, ComponentKind.Motherboard,
                S("Thay webcam", "Replace the webcam"),
                S("Webcam không đạt. Webcam rời USB là phương án rẻ nếu không cần camera trên máy.",
                  "The webcam failed. An external USB webcam is a cheap alternative."),
                250_000, 700_000));

        // --- Nâng cấp đáng cân nhắc (không bắt buộc) ---
        var ddr = s.Memory.Modules.Select(m => m.MemoryType).FirstOrDefault(t => t.StartsWith("DDR", StringComparison.OrdinalIgnoreCase));
        var canUpgradeRam = !s.Upgrade.MemoryIsSoldered;
        if (canUpgradeRam && Has("RAM-CAP-001"))
            costs.Add(new CostItem(CostKind.Upgrade, ComponentKind.Memory,
                S($"Thêm RAM {ddr ?? string.Empty} 8 GB".Replace("  ", " "), $"Add 8 GB {ddr ?? string.Empty} RAM".Replace("  ", " ")),
                S("Dưới 8 GB RAM, Windows và trình duyệt chạy chậm rõ rệt.", "Below 8 GB of RAM, Windows and browsers are noticeably slow."),
                500_000, 1_000_000));
        else if (canUpgradeRam && Has("RAM-CH-001"))
            costs.Add(new CostItem(CostKind.Upgrade, ComponentKind.Memory,
                S("Thêm một thanh RAM cùng loại để chạy dual channel", "Add a matching module for dual channel"),
                S("Máy đang chạy một kênh, băng thông bộ nhớ chỉ còn một nửa.", "The machine runs single channel, halving memory bandwidth."),
                500_000, 1_000_000));

        var system = s.StorageDevices.FirstOrDefault(d => d.DeviceIndex == 0) ?? s.StorageDevices.FirstOrDefault();
        var systemIsHdd = system is not null && !system.IsSsd &&
                          (system.MediaType.Contains("HDD", StringComparison.OrdinalIgnoreCase) || system.SpindleSpeedRpm is > 0);
        if (systemIsHdd && costs.All(c => c.Component != ComponentKind.Storage))
            costs.Add(new CostItem(CostKind.Upgrade, ComponentKind.Storage,
                S("Thay ổ hệ thống sang SSD", "Move the system drive to an SSD"),
                S("Ổ hệ thống là ổ cơ (HDD) — đổi sang SSD là nâng cấp cảm nhận rõ nhất.", "The system drive is a hard disk — an SSD is the most noticeable upgrade."),
                800_000, 1_500_000));
    }

    // ------------------------------------------------------------------
    private static void Decide(MachineAssessment a, PurchaseAdvice advice)
    {
        var repairs = advice.Costs.Where(c => c.Kind == CostKind.Repair).ToList();
        var rangeText = FormatHelpers.VndRange(advice.RepairMinVnd, advice.RepairMaxVnd);

        if (advice.Blockers.Count > 0 || a.Trust.Level == TrustLevel.DoNotBuy)
        {
            advice.Decision = PurchaseDecision.Avoid;
            advice.Headline = S("KHÔNG NÊN MUA", "DO NOT BUY");
            advice.Explanation = advice.Blockers.Count > 0
                ? S("Máy có vấn đề không giải quyết được bằng cách sửa chữa thông thường, hoặc có dấu hiệu bị làm giả cấu hình.",
                    "The machine has problems that ordinary repair cannot fix, or signs that its specs were faked.")
                : S("Nhiều dấu hiệu bất thường cùng lúc — rủi ro quá lớn so với số tiền tiết kiệm được.",
                    "Too many warning signs at once — the risk outweighs the money saved.");
        }
        else if (repairs.Count > 0 || advice.Conditions.Count > 0 ||
                 a.Trust.Level is TrustLevel.Suspicious ||
                 a.OverallRating is HealthRating.Average or HealthRating.Poor)
        {
            advice.Decision = PurchaseDecision.Negotiate;
            advice.Headline = S("MUA ĐƯỢC, NHƯNG CẦN THƯƠNG LƯỢNG", "OK TO BUY, BUT NEGOTIATE");
            advice.Explanation = repairs.Count > 0
                ? S($"Máy dùng được nhưng cần bỏ thêm khoảng {rangeText} để khắc phục. Dùng con số này để trả giá.",
                    $"The machine is usable but needs about {rangeText} of fixes. Use that figure to negotiate.")
                : S("Máy dùng được, nhưng có những điểm người bán phải xử lý hoặc giải thích trước khi trả tiền.",
                    "The machine is usable, but there are points the seller must fix or explain before you pay.");
        }
        else
        {
            advice.Decision = PurchaseDecision.Buy;
            advice.Headline = S("NÊN MUA", "GOOD TO BUY");
            advice.Explanation = S("Không thấy hỏng hóc cần sửa hay dấu hiệu gian lận. Vẫn nên làm các bước kiểm tra tay trước khi trả tiền.",
                "No faults needing repair and no signs of fraud. Still do the manual checks before paying.");
        }

        advice.NegotiationTip = repairs.Count > 0 && advice.Decision != PurchaseDecision.Avoid
            ? S($"Đề nghị người bán giảm ít nhất {advice.RepairMinVnd.ToVnd()} (chi phí khắc phục thấp nhất), " +
                $"hoặc tự sửa hết các mục trên trước khi giao. Mức hợp lý là giảm trong khoảng {rangeText}.",
                $"Ask the seller for at least {advice.RepairMinVnd.ToVnd()} off (the minimum repair cost), " +
                $"or to fix all the items above before handover. A fair reduction is within {rangeText}.")
            : string.Empty;
    }
}
