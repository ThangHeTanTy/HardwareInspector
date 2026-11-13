using HardwareInspector.Core.Models.Components;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Core.Models.Diagnostics;

/// <summary>
/// Loại lỗi điểm ảnh, suy ra từ nền đang hiển thị lúc người dùng đánh dấu.
/// Trùng với ba loại của ISO 9241-307: sáng (luôn bật), tối (luôn tắt), lỗi một điểm ảnh phụ.
/// </summary>
public enum PixelDefectKind
{
    /// <summary>Chấm sáng trên nền đen: điểm ảnh luôn bật (loại 1).</summary>
    Bright,
    /// <summary>Chấm đen trên nền trắng: điểm ảnh chết (loại 2).</summary>
    Dark,
    /// <summary>Chấm lệch màu trên nền đỏ/lục/lam: hỏng một điểm ảnh phụ (loại 3).</summary>
    Subpixel,
    /// <summary>Vết khác thấy trên nền xám hoặc dải màu: loang, ố, bụi. Không tính theo ISO.</summary>
    Blemish
}

/// <summary>Một chỗ lỗi người dùng đánh dấu trong bài kiểm tra màn hình.</summary>
public sealed class PixelDefectMark
{
    /// <summary>Định danh màn hình (MonitorDeviceId), rỗng khi không biết đang thử màn nào.</summary>
    public string DisplayKey { get; set; } = string.Empty;

    /// <summary>Toạ độ chuẩn hoá 0–1 theo bề rộng/cao màn, không phụ thuộc DPI.</summary>
    public double X { get; set; }
    public double Y { get; set; }

    public PixelDefectKind Kind { get; set; }

    /// <summary>
    /// Nằm ở vùng giữa — một phần ba chính giữa theo cả hai chiều, chỗ mắt nhìn nhiều nhất.
    /// Lỗi ở đây khó chịu hơn hẳn lỗi sát viền dù cùng số lượng.
    /// </summary>
    public bool InCenter => X is >= 1 / 3d and <= 2 / 3d && Y is >= 1 / 3d and <= 2 / 3d;

    public string KindText => KindName(Kind);

    public static string KindName(PixelDefectKind kind) => kind switch
    {
        PixelDefectKind.Bright => S("điểm sáng", "bright pixel"),
        PixelDefectKind.Dark => S("điểm chết", "dead pixel"),
        PixelDefectKind.Subpixel => S("lỗi kênh màu", "subpixel defect"),
        _ => S("vết loang/ố", "blemish")
    };
}

/// <summary>Kết quả đối chiếu các điểm đã đánh dấu của một màn với ISO 9241-307 lớp II.</summary>
public sealed record PixelDefectSummary(
    int Bright, int Dark, int Subpixel, int Blemish, int InCenter,
    int AllowedBright, int AllowedDark, int AllowedSubpixel)
{
    public int Total => Bright + Dark + Subpixel + Blemish;
    public bool Any => Total > 0;

    public bool ExceedsIso => Bright > AllowedBright || Dark > AllowedDark || Subpixel > AllowedSubpixel;

    /// <summary>
    /// Đủ nặng để coi màn là không đạt: vượt ngưỡng ISO, hoặc có điểm sáng/điểm chết ở vùng giữa.
    /// Một điểm sáng giữa màn rất khó chịu dù ISO vẫn cho phép.
    /// </summary>
    public bool Serious => ExceedsIso || InCenter > 0;

    public string CountsText
    {
        get
        {
            var parts = new List<string>();
            if (Bright > 0) parts.Add($"{Bright} {PixelDefectMark.KindName(PixelDefectKind.Bright)}");
            if (Dark > 0) parts.Add($"{Dark} {PixelDefectMark.KindName(PixelDefectKind.Dark)}");
            if (Subpixel > 0) parts.Add($"{Subpixel} {PixelDefectMark.KindName(PixelDefectKind.Subpixel)}");
            if (Blemish > 0) parts.Add($"{Blemish} {PixelDefectMark.KindName(PixelDefectKind.Blemish)}");
            var text = parts.Count > 0 ? string.Join(", ", parts) : S("không có", "none");
            return InCenter > 0 ? text + S($" ({InCenter} ở vùng giữa)", $" ({InCenter} in the centre zone)") : text;
        }
    }

    public string IsoText => S(
        $"ISO 9241-307 lớp II cho phép tối đa {AllowedBright} điểm sáng, {AllowedDark} điểm chết, {AllowedSubpixel} lỗi kênh màu ở độ phân giải này",
        $"ISO 9241-307 class II allows at most {AllowedBright} bright, {AllowedDark} dead and {AllowedSubpixel} subpixel defects at this resolution");
}

public static class PixelDefectPolicy
{
    /// <summary>Định danh dùng để gắn điểm đánh dấu với màn hình.</summary>
    public static string KeyOf(DisplayInfo? display) =>
        display is null ? string.Empty
        : !string.IsNullOrWhiteSpace(display.MonitorDeviceId) ? display.MonitorDeviceId
        : display.AdapterDeviceName;

    /// <summary>
    /// Các điểm thuộc màn này. Điểm đánh dấu lúc chưa quét máy (không rõ màn nào)
    /// được tính cho màn chính, vì bài kiểm tra mặc định chạy trên màn chính.
    /// </summary>
    public static IEnumerable<PixelDefectMark> MarksFor(DeviceTestResult tests, DisplayInfo display)
    {
        var key = KeyOf(display);
        return tests.PixelDefects.Where(m =>
            string.Equals(m.DisplayKey, key, StringComparison.OrdinalIgnoreCase) ||
            (m.DisplayKey.Length == 0 && display.IsPrimary));
    }

    /// <summary>
    /// ISO 9241-307 lớp II (mức của đa số màn phổ thông): mỗi triệu điểm ảnh được phép
    /// 2 điểm sáng, 2 điểm chết và 5 lỗi điểm ảnh phụ.
    /// </summary>
    public static PixelDefectSummary Summarize(IEnumerable<PixelDefectMark> marks, int width, int height)
    {
        var list = marks.ToList();
        var megapixels = width > 0 && height > 0 ? width * (double)height / 1_000_000 : 2.07;
        int Allowed(double perMillion) => (int)Math.Floor(perMillion * megapixels);

        return new PixelDefectSummary(
            list.Count(m => m.Kind == PixelDefectKind.Bright),
            list.Count(m => m.Kind == PixelDefectKind.Dark),
            list.Count(m => m.Kind == PixelDefectKind.Subpixel),
            list.Count(m => m.Kind == PixelDefectKind.Blemish),
            list.Count(m => m.InCenter && m.Kind is PixelDefectKind.Bright or PixelDefectKind.Dark),
            Allowed(2), Allowed(2), Allowed(5));
    }

    public static PixelDefectSummary Summarize(DeviceTestResult tests, DisplayInfo display) =>
        Summarize(MarksFor(tests, display),
            display.NativeWidthPx ?? display.WidthPx, display.NativeHeightPx ?? display.HeightPx);
}
