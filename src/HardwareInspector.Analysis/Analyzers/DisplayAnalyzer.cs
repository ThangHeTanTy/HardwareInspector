using HardwareInspector.Analysis.Scoring;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Assessment;
using HardwareInspector.Core.Models.Components;
using HardwareInspector.Core.Utils;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Analysis.Analyzers;

/// <summary>
/// Đánh giá tất cả màn hình đang cắm, từng cái một theo cổng xuất hình.
/// EDID trả lời câu hỏi quan trọng nhất khi mua máy cũ: tấm nền này có phải
/// tấm nền nguyên bản không. Phần còn lại phải nhìn bằng mắt.
/// </summary>
public sealed class DisplayAnalyzer : IComponentAnalyzer
{
    public ComponentKind Kind => ComponentKind.Display;

    public bool CanAnalyze(SystemSnapshot snapshot) => snapshot.Displays.Count > 0;

    public ComponentAssessment Analyze(SystemSnapshot snapshot)
    {
        var displays = snapshot.Displays;

        if (displays.Count == 1) return AnalyzeOne(displays[0], snapshot, prefix: null);

        var results = displays
            .Select((d, i) => AnalyzeOne(d, snapshot, prefix: $"Màn {i + 1}"))
            .ToList();
        var worst = results.OrderBy(r => r.Score).First();

        var merged = new ScoreCard(Kind, $"{displays.Count} màn hình đang kết nối")
            .Headline($"Màn yếu nhất: {worst.DisplayName} — {worst.Rating.ToText()}");

        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            merged.Metric($"Màn {i + 1}", $"{r.DisplayName} — {r.Rating.ToText()} ({r.Score}/100)",
                displays[i].ConnectionText);
            foreach (var m in r.Metrics) merged.Metric($"  Màn {i + 1} · {m.Label}", m.Value, m.Note, m.Severity);
            foreach (var f in r.Findings) merged.Add(f);
        }

        return merged.Build();
    }

    private ComponentAssessment AnalyzeOne(DisplayInfo display, SystemSnapshot snapshot, string? prefix)
    {
        var title = !string.IsNullOrWhiteSpace(display.PanelModel)
            ? display.PanelModel
            : !string.IsNullOrWhiteSpace(display.FriendlyName)
                ? display.FriendlyName
                : $"{display.ManufacturerName} {display.Resolution}".Trim();

        var card = new ScoreCard(Kind, title);

        card.Metric("Cổng kết nối", display.ConnectionText)
            .Metric("Card xuất hình", display.AdapterDescription.OrDash())
            .Metric("Vai trò", display.IsPrimary ? "Màn hình chính" : "Màn hình phụ")
            .Metric("Độ phân giải đang chạy", display.Resolution)
            .MetricIf(display.RefreshHz.HasValue, "Tần số quét đang chạy", $"{display.RefreshHz} Hz")
            .MetricIf(display.BitsPerPixel.HasValue, "Độ sâu màu hệ thống", $"{display.BitsPerPixel} bit");

        if (!display.EdidAvailable)
        {
            card.Metric("Dữ liệu EDID", "Không đọc được", null, Severity.Notice);
            card.Add("DSP-EDID-000", Kind, Severity.Notice,
                "Không đọc được EDID của màn hình này",
                "Không có EDID thì không biết mã tấm nền thật và năm sản xuất — hai dữ kiện " +
                "dùng để phát hiện màn đã bị thay. Thường gặp khi màn nối qua bộ chia, " +
                "adapter chuyển đổi, hoặc KVM.",
                "Cắm màn trực tiếp vào card đồ hoạ, bỏ qua mọi bộ chuyển đổi, rồi chạy lại.",
                penalty: 10, confidence: 0.9, source: EvidenceSource.Edid);
        }
        else
        {
            card.Metric("Hãng tấm nền", $"{display.ManufacturerName} ({display.ManufacturerCode})")
                .Metric("Mã tấm nền", display.PanelModel.OrDash())
                .Metric("Độ phân giải gốc", display.NativeResolution)
                .MetricIf(display.MaxRefreshHz.HasValue, "Tần số quét tối đa của tấm nền", $"{display.MaxRefreshHz} Hz")
                .MetricIf(display.DiagonalInches.HasValue, "Kích thước", $"{display.DiagonalInches}\"")
                .MetricIf(display.BitsPerColor.HasValue, "Độ sâu màu tấm nền", $"{display.BitsPerColor} bit/kênh")
                .Metric("Serial màn hình", FormatHelpers.Mask(display.SerialNumber));

            AnalyzeAge(card, display, snapshot);
            AnalyzeMode(card, display);

            if (display.BitsPerColor is 6)
                card.Add("DSP-PANEL-001", Kind, Severity.Notice,
                    "Tấm nền 6 bit màu",
                    "Tấm nền 6 bit dùng kỹ thuật nhấp nháy để giả lập 8 bit, dải màu hẹp hơn rõ rệt. " +
                    "Đây thường là tấm nền cấp thấp — cần đối chiếu xem có đúng cấu hình gốc của máy không.",
                    penalty: 6, confidence: 0.8, source: EvidenceSource.Edid);
        }

        card.Add("DSP-MANUAL-001", Kind, Severity.Info,
            "Cần chạy bài kiểm tra màn hình bằng mắt",
            "Điểm chết, điểm sáng, hở sáng viền, ám màu và bóng mờ đều không thể phát hiện bằng phần mềm đọc thông số.",
            "Mở mục Kiểm tra màn hình, chọn đúng màn này trong danh sách rồi chạy trong phòng tối.",
            penalty: 0, confidence: 1.0);

        return card.Build();
    }

    private void AnalyzeAge(ScoreCard card, DisplayInfo display, SystemSnapshot snapshot)
    {
        if (display.ManufactureYear is not { } year) return;

        var weekText = display.ManufactureWeek is { } w ? $"tuần {w}/" : string.Empty;
        var age = DateTime.Now.Year - year;
        card.Metric("Ngày sản xuất tấm nền", $"{weekText}{year}", $"Khoảng {age} năm tuổi");
        card.Uptime(TimeSpan.FromDays(age * 365.0));

        // Phép đối chiếu chỉ có ý nghĩa với tấm nền tích hợp: màn rời vốn được mua
        // rời khỏi máy nên lệch tuổi là chuyện bình thường.
        if (!display.IsInternal) return;
        if (snapshot.Bios.ReleaseDate is not { } biosDate) return;

        var gap = year - biosDate.Year;

        if (gap >= 2)
            card.Add("DSP-SWAP-001", Kind, Severity.Warning,
                $"Tấm nền sản xuất năm {year}, sau ngày phát hành BIOS ({biosDate:MM/yyyy}) {gap} năm",
                "Chênh lệch này cho thấy màn hình nhiều khả năng đã được thay. " +
                "Bản thân việc thay màn không xấu — nhưng nó cho biết máy từng bị va đập hoặc hỏng, " +
                "và tấm nền thay thế có thể không cùng chất lượng với tấm nguyên bản.",
                "Hỏi thẳng người bán về lịch sử sửa chữa. Kiểm tra ốc vỏ và viền màn có dấu tháo lắp không.",
                penalty: 12, confidence: 0.7, source: EvidenceSource.Edid);
        else if (gap <= -3)
            card.Add("DSP-SWAP-002", Kind, Severity.Notice,
                $"Tấm nền sản xuất năm {year}, trước BIOS ({biosDate:MM/yyyy}) {-gap} năm",
                "Tấm nền cũ hơn máy đáng kể. Có thể là tấm nền tháo từ máy khác sang.",
                "Kiểm tra kỹ tình trạng hiển thị và dấu vết tháo lắp.",
                penalty: 8, confidence: 0.5, source: EvidenceSource.Edid);
    }

    /// <summary>
    /// Màn chạy sai độ phân giải gốc sẽ mờ vì phải nội suy. Chạy dưới tần số quét
    /// tối đa thì lãng phí khả năng của tấm nền — hai lỗi cấu hình rất hay gặp
    /// và người bán thường để nguyên vì không biết.
    /// </summary>
    private void AnalyzeMode(ScoreCard card, DisplayInfo display)
    {
        if (display.RunningAtNativeResolution is false)
            card.Add("DSP-MODE-001", Kind, Severity.Notice,
                $"Đang chạy {display.Resolution} trong khi tấm nền gốc là {display.NativeResolution}",
                "Ở độ phân giải không phải gốc, mọi thứ hiển thị đều phải nội suy nên chữ bị nhoè. " +
                "Cũng có khi người bán cố tình đặt thấp để che điểm chết.",
                "Đặt lại đúng độ phân giải gốc trong Cài đặt hiển thị rồi kiểm tra lại bằng mắt.",
                penalty: 5, confidence: 0.85, source: EvidenceSource.Edid);

        if (display.MaxRefreshHz is { } max && display.RefreshHz is { } current && max - current >= 15)
            card.Add("DSP-MODE-002", Kind, Severity.Notice,
                $"Đang chạy {current} Hz trong khi tấm nền hỗ trợ tới {max} Hz",
                "Windows hay mặc định về 60 Hz sau khi cài driver mới. " +
                "Đây là lỗi cấu hình chứ không phải hỏng hóc.",
                "Vào Cài đặt hiển thị nâng cao và chọn tần số quét cao nhất.",
                penalty: 3, confidence: 0.7, source: EvidenceSource.Edid);
    }
}
