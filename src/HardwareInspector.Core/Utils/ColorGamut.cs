namespace HardwareInspector.Core.Utils;

/// <summary>Toạ độ CIE 1931 xy của ba màu gốc và điểm trắng.</summary>
public sealed record Chromaticity(
    double RedX, double RedY,
    double GreenX, double GreenY,
    double BlueX, double BlueY,
    double WhiteX, double WhiteY);

/// <summary>
/// Tính độ phủ màu từ toạ độ ba màu gốc mà tấm nền khai trong EDID.
///
/// Đây là con số hãng tấm nền khai, không phải đo: muốn đo thật phải áp máy đo màu
/// (colorimeter) lên mặt kính. Dù vậy nó đủ để phân biệt tấm nền dải màu hẹp
/// ("45% NTSC", màu nhợt) với tấm nền sRGB đầy đủ hay dải màu rộng DCI-P3.
///
/// Tính trên mặt phẳng CIE 1931 xy — cũng là cách các hãng dùng khi quảng cáo "% NTSC".
/// Độ phủ (coverage) là phần diện tích tam giác chuẩn nằm trong tam giác của tấm nền,
/// nên không bao giờ vượt 100%; còn tỉ lệ NTSC là tỉ số diện tích, có thể vượt 100%.
/// </summary>
public static class ColorGamut
{
    public static readonly Chromaticity Srgb = new(0.640, 0.330, 0.300, 0.600, 0.150, 0.060, 0.3127, 0.3290);
    public static readonly Chromaticity DciP3 = new(0.680, 0.320, 0.265, 0.690, 0.150, 0.060, 0.3127, 0.3290);
    public static readonly Chromaticity Ntsc1953 = new(0.670, 0.330, 0.210, 0.710, 0.140, 0.080, 0.3101, 0.3162);
    public static readonly Chromaticity AdobeRgb = new(0.640, 0.330, 0.210, 0.710, 0.150, 0.060, 0.3127, 0.3290);

    /// <summary>
    /// Toạ độ có hợp lý không. EDID lỗi hoặc để trống cho toàn số 0, hoặc ba màu
    /// thẳng hàng; khi đó mọi phép tính độ phủ đều vô nghĩa.
    /// </summary>
    public static bool IsPlausible(Chromaticity c)
    {
        double[] all = { c.RedX, c.RedY, c.GreenX, c.GreenY, c.BlueX, c.BlueY, c.WhiteX, c.WhiteY };
        if (all.Any(v => v <= 0 || v >= 1)) return false;
        // Màu đỏ phải nằm bên phải, lục ở trên, lam ở dưới — thứ tự sai là dữ liệu rác.
        if (!(c.RedX > c.GreenX && c.RedX > c.BlueX && c.GreenY > c.RedY && c.GreenY > c.BlueY)) return false;
        return Area(c) > 0.02;
    }

    /// <summary>Tấm nền khai trùng khít toạ độ sRGB — thường là hãng điền giá trị mặc định chứ không đo.</summary>
    public static bool IsTextbookSrgb(Chromaticity c) =>
        Math.Abs(c.RedX - Srgb.RedX) < 0.002 && Math.Abs(c.RedY - Srgb.RedY) < 0.002 &&
        Math.Abs(c.GreenX - Srgb.GreenX) < 0.002 && Math.Abs(c.GreenY - Srgb.GreenY) < 0.002 &&
        Math.Abs(c.BlueX - Srgb.BlueX) < 0.002 && Math.Abs(c.BlueY - Srgb.BlueY) < 0.002;

    /// <summary>Phần trăm diện tích của <paramref name="reference"/> nằm trong dải màu của tấm nền.</summary>
    public static double Coverage(Chromaticity panel, Chromaticity reference)
    {
        var refArea = Area(reference);
        if (refArea <= 0) return 0;
        var clipped = Clip(Triangle(reference), Triangle(panel));
        return Math.Min(100, PolygonArea(clipped) / refArea * 100);
    }

    /// <summary>Tỉ số diện tích so với chuẩn (cách tính "% NTSC" của các hãng), có thể vượt 100%.</summary>
    public static double AreaRatio(Chromaticity panel, Chromaticity reference) =>
        Area(reference) <= 0 ? 0 : Area(panel) / Area(reference) * 100;

    public static double Area(Chromaticity c) => PolygonArea(Triangle(c));

    private static List<(double X, double Y)> Triangle(Chromaticity c) =>
        new() { (c.RedX, c.RedY), (c.GreenX, c.GreenY), (c.BlueX, c.BlueY) };

    private static double PolygonArea(IReadOnlyList<(double X, double Y)> p)
    {
        if (p.Count < 3) return 0;
        var sum = 0d;
        for (var i = 0; i < p.Count; i++)
        {
            var (x1, y1) = p[i];
            var (x2, y2) = p[(i + 1) % p.Count];
            sum += x1 * y2 - x2 * y1;
        }
        return Math.Abs(sum) / 2;
    }

    /// <summary>Cắt đa giác <paramref name="subject"/> bằng tam giác lồi <paramref name="clip"/> (Sutherland–Hodgman).</summary>
    private static List<(double X, double Y)> Clip(List<(double X, double Y)> subject, List<(double X, double Y)> clip)
    {
        // Đưa tam giác cắt về chiều ngược kim đồng hồ để phép "nằm bên trái cạnh" đúng nghĩa.
        if (Signed(clip) < 0) clip.Reverse();

        var output = subject;
        for (var i = 0; i < clip.Count && output.Count > 0; i++)
        {
            var a = clip[i];
            var b = clip[(i + 1) % clip.Count];
            var input = output;
            output = new List<(double X, double Y)>();

            for (var j = 0; j < input.Count; j++)
            {
                var cur = input[j];
                var prev = input[(j + input.Count - 1) % input.Count];
                var curIn = Side(a, b, cur) >= 0;
                var prevIn = Side(a, b, prev) >= 0;

                if (curIn)
                {
                    if (!prevIn) output.Add(Intersect(prev, cur, a, b));
                    output.Add(cur);
                }
                else if (prevIn)
                {
                    output.Add(Intersect(prev, cur, a, b));
                }
            }
        }
        return output;
    }

    private static double Signed(IReadOnlyList<(double X, double Y)> p)
    {
        var sum = 0d;
        for (var i = 0; i < p.Count; i++)
        {
            var (x1, y1) = p[i];
            var (x2, y2) = p[(i + 1) % p.Count];
            sum += x1 * y2 - x2 * y1;
        }
        return sum;
    }

    private static double Side((double X, double Y) a, (double X, double Y) b, (double X, double Y) p) =>
        (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

    private static (double X, double Y) Intersect((double X, double Y) p1, (double X, double Y) p2,
        (double X, double Y) a, (double X, double Y) b)
    {
        var s1 = Side(a, b, p1);
        var s2 = Side(a, b, p2);
        var t = s1 / (s1 - s2);
        return (p1.X + (p2.X - p1.X) * t, p1.Y + (p2.Y - p1.Y) * t);
    }
}
