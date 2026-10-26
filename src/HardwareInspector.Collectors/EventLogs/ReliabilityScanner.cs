using System.Diagnostics;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Collectors.EventLogs;

/// <summary>
/// Event Log là cuốn nhật ký mà người bán không xoá được dễ dàng.
/// Ở đây ta truy các sự kiện phần cứng thật sự có giá trị chẩn đoán:
/// WHEA (lỗi CPU/RAM/PCIe), lỗi đọc ghi ổ đĩa, driver đồ hoạ bị reset, và bugcheck (màn hình xanh).
/// </summary>
public sealed class ReliabilityScanner : IInfoCollector, IRelocalizable
{
    public string Name => S("Nhật ký sự cố phần cứng", "Hardware event log");
    public int Order => 80;

    // Duyệt 90 ngày nhật ký hệ thống, thường là bước lâu nhất trong cả lượt quét.
    public int Weight => 6;

    private static readonly TimeSpan LookBack = TimeSpan.FromDays(90);

    /// <summary>Số đếm của lượt quét gần nhất, giữ lại để dựng lại phát hiện khi đổi ngôn ngữ.</summary>
    private EventCounts _counts = new(0, 0, 0, 0, 0, null);

    private sealed record EventCounts(int Whea, int DiskErrors, int Bugchecks, int ControllerErrors,
        int GpuResets, DateTime? LastBugcheck);

    public Task CollectAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        _counts = ScanSystemLog(ct);
        AddFindings(snapshot, _counts);
        return Task.CompletedTask;
    }

    public void Relocalize(SystemSnapshot snapshot)
    {
        snapshot.ReliabilityFindings.RemoveAll(f => f.Code.StartsWith("EVT-", StringComparison.Ordinal));
        AddFindings(snapshot, _counts);
    }

    private static EventCounts ScanSystemLog(CancellationToken ct)
    {
        EventLog? log = null;
        try { log = new EventLog("System"); }
        catch { return new EventCounts(0, 0, 0, 0, 0, null); }

        var since = DateTime.Now - LookBack;
        var whea = 0;
        var diskErrors = 0;
        var bugchecks = 0;
        var controllerErrors = 0;
        var gpuResets = 0;
        DateTime? lastBugcheck = null;

        try
        {
            for (var i = log.Entries.Count - 1; i >= 0; i--)
            {
                ct.ThrowIfCancellationRequested();
                var entry = log.Entries[i];
                if (entry.TimeGenerated < since) break;

                var source = entry.Source;
                var id = entry.InstanceId & 0xFFFF;

                if (source.StartsWith("Microsoft-Windows-WHEA", StringComparison.OrdinalIgnoreCase))
                    whea++;
                else if (source.Equals("disk", StringComparison.OrdinalIgnoreCase) && id is 7 or 11 or 51 or 153)
                    diskErrors++;
                else if (source.Equals("BugCheck", StringComparison.OrdinalIgnoreCase))
                {
                    bugchecks++;
                    lastBugcheck ??= entry.TimeGenerated;
                }
                // "Display driver stopped responding and has successfully recovered" (TDR).
                else if (source.Equals("Display", StringComparison.OrdinalIgnoreCase) && id == 4101)
                    gpuResets++;
                else if (source.Contains("storahci", StringComparison.OrdinalIgnoreCase) ||
                         source.Contains("stornvme", StringComparison.OrdinalIgnoreCase) ||
                         source.Contains("iaStor", StringComparison.OrdinalIgnoreCase))
                    controllerErrors++;
            }
        }
        catch { }
        finally { log.Dispose(); }

        return new EventCounts(whea, diskErrors, bugchecks, controllerErrors, gpuResets, lastBugcheck);
    }

    private static void AddFindings(SystemSnapshot snapshot, EventCounts c)
    {
        AddIfAny(snapshot, c.Whea, "EVT-WHEA-001", ComponentKind.Motherboard,
            S("Lỗi phần cứng WHEA được ghi nhận", "WHEA hardware errors recorded"),
            S("WHEA ghi lại lỗi ở mức CPU, RAM hoặc bus PCIe. Số lượng lớn thường đi kèm RAM lỗi, " +
              "CPU ép xung không ổn định hoặc khe PCIe tiếp xúc kém.",
              "WHEA logs errors at the CPU, RAM or PCIe bus level. Large counts usually go with faulty RAM, " +
              "an unstable CPU overclock or a poorly seated PCIe slot."),
            S("Chạy MemTest86 qua đêm và kiểm tra lại khe RAM/PCIe trước khi quyết định mua.",
              "Run MemTest86 overnight and recheck the RAM/PCIe slots before deciding to buy."),
            threshold: 1, penaltyPerEvent: 6, maxPenalty: 35);

        AddIfAny(snapshot, c.DiskErrors, "EVT-DISK-001", ComponentKind.Storage,
            S("Lỗi đọc/ghi ổ đĩa trong 90 ngày", "Disk read/write errors in 90 days"),
            S("Event ID 7/51/153 nghĩa là hệ điều hành gặp lỗi khi truy cập ổ. Đây là dấu hiệu sớm hơn cả S.M.A.R.T.",
              "Event IDs 7/51/153 mean the OS hit errors accessing the drive. This shows up even earlier than S.M.A.R.T."),
            S("Sao lưu dữ liệu ngay và chạy quét bề mặt toàn bộ ổ.", "Back up your data now and run a full surface scan."),
            threshold: 1, penaltyPerEvent: 5, maxPenalty: 40);

        AddIfAny(snapshot, c.ControllerErrors, "EVT-CTRL-001", ComponentKind.Storage,
            S("Lỗi ở tầng controller lưu trữ", "Storage controller errors"),
            S("Driver AHCI/NVMe báo lỗi reset thiết bị — thường do cáp, nguồn hoặc bản thân ổ sắp hỏng.",
              "The AHCI/NVMe driver reported device resets — usually the cable, power, or a failing drive."),
            S("Đổi cáp SATA hoặc thử ổ ở máy khác để khoanh vùng.", "Swap the SATA cable or try the drive in another machine to narrow it down."),
            threshold: 1, penaltyPerEvent: 4, maxPenalty: 25);

        AddIfAny(snapshot, c.GpuResets, "EVT-GPU-001", ComponentKind.Gpu,
            S("Driver đồ hoạ từng bị reset (TDR)", "The graphics driver has been reset (TDR)"),
            S("Event 4101 nghĩa là GPU ngừng phản hồi quá 2 giây và Windows phải khởi động lại driver. " +
              "Thỉnh thoảng một lần có thể do driver lỗi; lặp lại nhiều lần thường là card quá nhiệt, " +
              "VRAM lỗi hoặc nguồn cấp cho card không ổn định.",
              "Event 4101 means the GPU stopped responding for over 2 seconds and Windows had to restart the driver. " +
              "An occasional one can be a driver bug; repeated ones usually mean an overheating card, " +
              "bad VRAM or unstable power to the card."),
            S("Chạy bài tải nặng GPU trong ứng dụng và xem driver có sập lại không.",
              "Run the in-app GPU stress test and see whether the driver crashes again."),
            threshold: 1, penaltyPerEvent: 5, maxPenalty: 30);

        if (c.Bugchecks > 0)
        {
            var bugchecks = c.Bugchecks;
            snapshot.ReliabilityFindings.Add(new Finding
            {
                Code = "EVT-BSOD-001",
                Component = ComponentKind.Motherboard,
                Severity = bugchecks >= 3 ? Severity.Critical : Severity.Warning,
                Title = S($"Máy đã gặp {bugchecks} lần màn hình xanh trong 90 ngày", $"{bugchecks} blue screens in the last 90 days"),
                Detail = c.LastBugcheck is { } last ? S($"Lần gần nhất: {last:dd/MM/yyyy HH:mm}", $"Most recent: {last:dd/MM/yyyy HH:mm}") : null,
                Recommendation = S("Yêu cầu người bán giải thích. Kiểm tra minidump trong C:\\Windows\\Minidump để xác định driver hoặc phần cứng gây lỗi.",
                                   "Ask the seller to explain. Check the minidumps in C:\\Windows\\Minidump to find the driver or hardware at fault."),
                Source = EvidenceSource.EventLog,
                ScorePenalty = Math.Min(30, bugchecks * 10),
                Confidence = 0.95
            });
        }
    }

    private static void AddIfAny(SystemSnapshot snapshot, int count, string code, ComponentKind kind,
        string title, string detail, string recommendation, int threshold, int penaltyPerEvent, int maxPenalty)
    {
        if (count < threshold) return;
        snapshot.ReliabilityFindings.Add(new Finding
        {
            Code = code,
            Component = kind,
            Severity = count >= 10 ? Severity.Critical : count >= 3 ? Severity.Warning : Severity.Notice,
            Title = S($"{title}: {count} sự kiện", $"{title}: {count} events"),
            Detail = detail,
            Recommendation = recommendation,
            Source = EvidenceSource.EventLog,
            ScorePenalty = Math.Min(maxPenalty, count * penaltyPerEvent),
            Confidence = 0.9
        });
    }
}
