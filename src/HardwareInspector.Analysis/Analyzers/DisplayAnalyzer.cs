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
            .Select((d, i) => AnalyzeOne(d, snapshot, prefix: S($"Màn {i + 1}", $"Display {i + 1}")))
            .ToList();
        var worst = results.OrderBy(r => r.Score).First();

        var merged = new ScoreCard(Kind, S($"{displays.Count} màn hình đang kết nối", $"{displays.Count} displays connected"))
            .Headline(S($"Màn yếu nhất: {worst.DisplayName} — {worst.Rating.ToText()}", $"Weakest display: {worst.DisplayName} — {worst.Rating.ToText()}"));

        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            var name = S($"Màn {i + 1}", $"Display {i + 1}");
            merged.Metric(name, $"{r.DisplayName} — {r.Rating.ToText()} ({r.Score}/100)",
                displays[i].ConnectionText, anchor: $"display:{i}");
            foreach (var m in r.Metrics) merged.Metric($"  {name} · {m.Label}", m.Value, m.Note, m.Severity);
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

        card.Metric(S("Cổng kết nối", "Connection"), display.ConnectionText, ConnectionNote(display))
            .Metric(S("Card xuất hình", "Output adapter"), display.AdapterDescription.OrDash())
            .Metric(S("Vai trò", "Role"), display.IsPrimary ? S("Màn hình chính", "Primary display") : S("Màn hình phụ", "Secondary display"))
            .Metric(S("Độ phân giải đang chạy", "Current resolution"), display.Resolution)
            .MetricIf(display.RefreshHz.HasValue, S("Tần số quét đang chạy", "Current refresh rate"), $"{display.RefreshHz} Hz")
            .MetricIf(display.BitsPerPixel.HasValue, S("Độ sâu màu hệ thống", "System colour depth"), $"{display.BitsPerPixel} bit");

        if (!display.EdidAvailable)
        {
            card.Metric(S("Dữ liệu EDID", "EDID data"), S("Không đọc được", "Unreadable"), null, Severity.Notice);
            card.Add("DSP-EDID-000", Kind, Severity.Notice,
                S("Không đọc được EDID của màn hình này", "Could not read this display's EDID"),
                S("Không có EDID thì không biết mã tấm nền thật và năm sản xuất — hai dữ kiện " +
                  "dùng để phát hiện màn đã bị thay. Thường gặp khi màn nối qua bộ chia, " +
                  "adapter chuyển đổi, hoặc KVM.",
                  "Without EDID there is no way to know the real panel model and manufacture date — the two facts " +
                  "used to spot a replaced screen. Common when the display goes through a splitter, " +
                  "converter or KVM."),
                S("Cắm màn trực tiếp vào card đồ hoạ, bỏ qua mọi bộ chuyển đổi, rồi chạy lại.",
                  "Connect the display directly to the graphics card, bypassing any converter, and run again."),
                penalty: 10, confidence: 0.9, source: EvidenceSource.Edid);
        }
        else
        {
            card.Metric(S("Hãng tấm nền", "Panel manufacturer"), $"{display.ManufacturerName} ({display.ManufacturerCode})")
                .Metric(S("Mã tấm nền", "Panel model"), display.PanelModel.OrDash())
                .Metric(S("Độ phân giải gốc", "Native resolution"), display.NativeResolution)
                .MetricIf(display.MaxRefreshHz.HasValue, S("Tần số quét tối đa của tấm nền", "Panel maximum refresh rate"), $"{display.MaxRefreshHz} Hz")
                .MetricIf(display.DiagonalInches.HasValue, S("Kích thước", "Size"), $"{display.DiagonalInches}\"")
                .MetricIf(display.BitsPerColor.HasValue, S("Độ sâu màu tấm nền", "Panel colour depth"), S($"{display.BitsPerColor} bit/kênh", $"{display.BitsPerColor} bit/channel"))
                .Metric(S("Serial màn hình", "Display serial"), FormatHelpers.Mask(display.SerialNumber));

            AnalyzeAge(card, display);
            AnalyzeMode(card, display);

            if (display.BitsPerColor is 6)
                card.Add("DSP-PANEL-001", Kind, Severity.Notice,
                    S("Tấm nền 6 bit màu", "6-bit colour panel"),
                    S("Tấm nền 6 bit dùng kỹ thuật nhấp nháy để giả lập 8 bit, dải màu hẹp hơn rõ rệt. " +
                      "Đây thường là tấm nền cấp thấp — cần đối chiếu xem có đúng cấu hình gốc của máy không.",
                      "6-bit panels use dithering to fake 8-bit colour, with a noticeably narrower gamut. " +
                      "These are usually budget panels — check whether it matches the machine's original configuration."),
                    penalty: 6, confidence: 0.8, source: EvidenceSource.Edid);
        }

        card.Add("DSP-MANUAL-001", Kind, Severity.Info,
            S("Cần chạy bài kiểm tra màn hình bằng mắt", "A visual screen test is needed"),
            S("Điểm chết, điểm sáng, hở sáng viền, ám màu và bóng mờ đều không thể phát hiện bằng phần mềm đọc thông số.",
              "Dead pixels, stuck pixels, backlight bleed, colour tint and ghosting cannot be detected by reading specs."),
            S("Mở mục Kiểm tra màn hình, chọn đúng màn này trong danh sách rồi chạy trong phòng tối.",
              "Open the Screen test, pick this display in the list and run it in a dark room."),
            penalty: 0, confidence: 1.0);

        return card.Build();
    }

    private static string? ConnectionNote(DisplayInfo display)
    {
        if (display.ConnectionSetByUser)
            return S($"Do bạn chọn tay. Phần mềm tự nhận diện là {DisplayInfo.LongName(display.DetectedConnection)}.",
                     $"Set manually by you. Auto-detection said {DisplayInfo.LongName(display.DetectedConnection)}.");

        return display.ViaConverter && display.Connection == DisplayConnection.Vga
            ? S($"Windows báo {DisplayInfo.LongName(display.ReportedConnection!.Value)} vì đó là đầu ra của card đồ hoạ. " +
                "EDID của màn khai tín hiệu analog, nên cổng thật là VGA — đi qua adapter rời " +
                "hoặc chip chuyển đổi gắn sẵn sau cổng VGA trên bo mạch.",
                $"Windows reports {DisplayInfo.LongName(display.ReportedConnection!.Value)} because that is the graphics card's output. " +
                "The display's EDID declares an analog input, so the real port is VGA — through a separate adapter " +
                "or a converter chip built in behind the motherboard's VGA port.")
            : null;
    }

    private static void AnalyzeAge(ScoreCard card, DisplayInfo display)
    {
        if (display.ManufactureYear is not { } year) return;

        var weekText = display.ManufactureWeek is { } w ? S($"tuần {w}/", $"week {w}/") : string.Empty;
        var age = DateTime.Now.Year - year;
        card.Metric(S("Ngày sản xuất tấm nền", "Panel manufacture date"), $"{weekText}{year}", S($"Khoảng {age} năm tuổi", $"About {age} years old"));
        card.Uptime(TimeSpan.FromDays(age * 365.0));
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
                S($"Đang chạy {display.Resolution} trong khi tấm nền gốc là {display.NativeResolution}",
                  $"Running at {display.Resolution} while the panel's native resolution is {display.NativeResolution}"),
                S("Ở độ phân giải không phải gốc, mọi thứ hiển thị đều phải nội suy nên chữ bị nhoè. " +
                  "Cũng có khi người bán cố tình đặt thấp để che điểm chết.",
                  "At a non-native resolution everything is interpolated, so text looks blurry. " +
                  "Sellers sometimes set it low on purpose to hide dead pixels."),
                S("Đặt lại đúng độ phân giải gốc trong Cài đặt hiển thị rồi kiểm tra lại bằng mắt.",
                  "Set the native resolution in Display settings and check the screen again by eye."),
                penalty: 5, confidence: 0.85, source: EvidenceSource.Edid);

        if (display.MaxRefreshHz is { } max && display.RefreshHz is { } current && max - current >= 15)
            card.Add("DSP-MODE-002", Kind, Severity.Notice,
                S($"Đang chạy {current} Hz trong khi tấm nền hỗ trợ tới {max} Hz", $"Running at {current} Hz while the panel supports up to {max} Hz"),
                S("Windows hay mặc định về 60 Hz sau khi cài driver mới. " +
                  "Đây là lỗi cấu hình chứ không phải hỏng hóc.",
                  "Windows often falls back to 60 Hz after a driver install. " +
                  "This is a settings issue, not a fault."),
                S("Vào Cài đặt hiển thị nâng cao và chọn tần số quét cao nhất.", "Open Advanced display settings and choose the highest refresh rate."),
                penalty: 3, confidence: 0.7, source: EvidenceSource.Edid);
    }
}
