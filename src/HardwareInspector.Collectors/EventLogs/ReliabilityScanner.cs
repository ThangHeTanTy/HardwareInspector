using System.Diagnostics;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;

namespace HardwareInspector.Collectors.EventLogs;

/// <summary>
/// Event Log là cuốn nhật ký mà người bán không xoá được dễ dàng.
/// Ở đây ta truy các sự kiện phần cứng thật sự có giá trị chẩn đoán:
/// WHEA (lỗi CPU/RAM/PCIe), lỗi đọc ghi ổ đĩa, driver đồ hoạ bị reset, và bugcheck (màn hình xanh).
/// </summary>
public sealed class ReliabilityScanner : IInfoCollector
{
    public string Name => "Nhật ký sự cố phần cứng";
    public int Order => 80;

    // Duyệt 90 ngày nhật ký hệ thống, thường là bước lâu nhất trong cả lượt quét.
    public int Weight => 6;

    private static readonly TimeSpan LookBack = TimeSpan.FromDays(90);

    public Task CollectAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        ScanSystemLog(snapshot, ct);
        return Task.CompletedTask;
    }

    private static void ScanSystemLog(SystemSnapshot snapshot, CancellationToken ct)
    {
        EventLog? log = null;
        try { log = new EventLog("System"); }
        catch { return; }

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

        AddIfAny(snapshot, whea, "EVT-WHEA-001", ComponentKind.Motherboard,
            "Lỗi phần cứng WHEA được ghi nhận",
            "WHEA ghi lại lỗi ở mức CPU, RAM hoặc bus PCIe. Số lượng lớn thường đi kèm RAM lỗi, " +
            "CPU ép xung không ổn định hoặc khe PCIe tiếp xúc kém.",
            "Chạy MemTest86 qua đêm và kiểm tra lại khe RAM/PCIe trước khi quyết định mua.",
            threshold: 1, penaltyPerEvent: 6, maxPenalty: 35);

        AddIfAny(snapshot, diskErrors, "EVT-DISK-001", ComponentKind.Storage,
            "Lỗi đọc/ghi ổ đĩa trong 90 ngày",
            "Event ID 7/51/153 nghĩa là hệ điều hành gặp lỗi khi truy cập ổ. Đây là dấu hiệu sớm hơn cả S.M.A.R.T.",
            "Sao lưu dữ liệu ngay và chạy quét bề mặt toàn bộ ổ.",
            threshold: 1, penaltyPerEvent: 5, maxPenalty: 40);

        AddIfAny(snapshot, controllerErrors, "EVT-CTRL-001", ComponentKind.Storage,
            "Lỗi ở tầng controller lưu trữ",
            "Driver AHCI/NVMe báo lỗi reset thiết bị — thường do cáp, nguồn hoặc bản thân ổ sắp hỏng.",
            "Đổi cáp SATA hoặc thử ổ ở máy khác để khoanh vùng.",
            threshold: 1, penaltyPerEvent: 4, maxPenalty: 25);

        AddIfAny(snapshot, gpuResets, "EVT-GPU-001", ComponentKind.Gpu,
            "Driver đồ hoạ từng bị reset (TDR)",
            "Event 4101 nghĩa là GPU ngừng phản hồi quá 2 giây và Windows phải khởi động lại driver. " +
            "Thỉnh thoảng một lần có thể do driver lỗi; lặp lại nhiều lần thường là card quá nhiệt, " +
            "VRAM lỗi hoặc nguồn cấp cho card không ổn định.",
            "Chạy bài tải nặng GPU trong ứng dụng và xem driver có sập lại không.",
            threshold: 1, penaltyPerEvent: 5, maxPenalty: 30);

        if (bugchecks > 0)
        {
            snapshot.ReliabilityFindings.Add(new Finding
            {
                Code = "EVT-BSOD-001",
                Component = ComponentKind.Motherboard,
                Severity = bugchecks >= 3 ? Severity.Critical : Severity.Warning,
                Title = $"Máy đã gặp {bugchecks} lần màn hình xanh trong 90 ngày",
                Detail = lastBugcheck.HasValue ? $"Lần gần nhất: {lastBugcheck:dd/MM/yyyy HH:mm}" : null,
                Recommendation = "Yêu cầu người bán giải thích. Kiểm tra minidump trong C:\\Windows\\Minidump để xác định driver hoặc phần cứng gây lỗi.",
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
            Title = $"{title}: {count} sự kiện",
            Detail = detail,
            Recommendation = recommendation,
            Source = EvidenceSource.EventLog,
            ScorePenalty = Math.Min(maxPenalty, count * penaltyPerEvent),
            Confidence = 0.9
        });
    }
}
