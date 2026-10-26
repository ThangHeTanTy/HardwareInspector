using System.Text.RegularExpressions;

namespace HardwareInspector.Core.Gpu;

/// <summary>Thông số chính hãng của một model card đồ hoạ để bàn.</summary>
/// <param name="Model">Tên model chuẩn hoá, ví dụ "GTX 1060".</param>
/// <param name="Fp32Tflops">Sức tính FP32 ở xung boost công bố.</param>
/// <param name="BandwidthGBps">Băng thông bộ nhớ công bố.</param>
/// <param name="MinVramGb">Bản VRAM nhỏ nhất của model (model có nhiều bản dung lượng).</param>
/// <param name="MaxVramGb">Bản VRAM lớn nhất.</param>
public sealed record GpuSpec(string Model, double Fp32Tflops, double BandwidthGBps, double MinVramGb, double MaxVramGb);

/// <summary>
/// Bảng thông số các card để bàn hay gặp ở thị trường máy cũ, lấy theo thông số công bố
/// của NVIDIA/AMD (xung boost tham chiếu). Dùng để phát hiện card đời thấp bị nạp vBIOS
/// đổi tên: tên thì sửa được, nhưng số nhân và độ rộng bus nhớ thì không.
///
/// Với RDNA3 (RX 7000), con số FP32 hãng công bố tính cả chế độ phát hai lệnh cùng lúc,
/// loại phép đo đơn giản không chạm tới được; bảng ghi một nửa con số đó cho công bằng.
/// Card laptop không có trong bảng vì xung và công suất mỗi máy mỗi khác.
/// </summary>
public static class GpuSpecCatalog
{
    private static readonly GpuSpec[] Specs =
    {
        // --- NVIDIA ---
        new("GTX 750 TI", 1.39, 86.4, 2, 4),
        new("GTX 950", 1.83, 105.6, 2, 4),
        new("GTX 960", 2.41, 112, 2, 4),
        new("GTX 970", 3.92, 224, 4, 4),
        new("GTX 980 TI", 6.06, 336, 6, 6),
        new("GTX 980", 4.98, 224, 4, 4),
        new("GTX 1050 TI", 2.14, 112, 4, 4),
        new("GTX 1050", 1.86, 112, 2, 3),
        new("GTX 1060", 3.94, 192, 3, 6),
        new("GTX 1070 TI", 8.19, 256, 8, 8),
        new("GTX 1070", 6.46, 256, 8, 8),
        new("GTX 1080 TI", 11.34, 484, 11, 11),
        new("GTX 1080", 8.87, 320, 8, 8),
        new("GTX 1630", 1.82, 96, 4, 4),
        new("GTX 1650 SUPER", 4.42, 192, 4, 4),
        new("GTX 1650", 2.98, 128, 4, 4),
        new("GTX 1660 SUPER", 5.03, 336, 6, 6),
        new("GTX 1660 TI", 5.44, 288, 6, 6),
        new("GTX 1660", 5.03, 192, 6, 6),
        new("RTX 2060 SUPER", 7.18, 448, 8, 8),
        new("RTX 2060", 6.45, 336, 6, 12),
        new("RTX 2070 SUPER", 9.06, 448, 8, 8),
        new("RTX 2070", 7.47, 448, 8, 8),
        new("RTX 2080 SUPER", 11.15, 496, 8, 8),
        new("RTX 2080 TI", 13.45, 616, 11, 11),
        new("RTX 2080", 10.07, 448, 8, 8),
        new("RTX 3050", 9.1, 224, 6, 8),
        new("RTX 3060 TI", 16.2, 448, 8, 8),
        new("RTX 3060", 12.74, 360, 8, 12),
        new("RTX 3070 TI", 21.75, 608, 8, 8),
        new("RTX 3070", 20.31, 448, 8, 8),
        new("RTX 3080 TI", 34.1, 912, 12, 12),
        new("RTX 3080", 29.77, 760, 10, 12),
        new("RTX 3090 TI", 40.0, 1008, 24, 24),
        new("RTX 3090", 35.58, 936, 24, 24),
        new("RTX 4060 TI", 22.06, 288, 8, 16),
        new("RTX 4060", 15.11, 272, 8, 8),
        new("RTX 4070 TI SUPER", 44.1, 672, 16, 16),
        new("RTX 4070 TI", 40.09, 504, 12, 12),
        new("RTX 4070 SUPER", 35.48, 504, 12, 12),
        new("RTX 4070", 29.15, 504, 12, 12),
        new("RTX 4080 SUPER", 52.2, 736, 16, 16),
        new("RTX 4080", 48.74, 717, 16, 16),
        new("RTX 4090", 82.58, 1008, 24, 24),

        // --- AMD ---
        new("RX 550", 1.21, 112, 2, 4),
        new("RX 560", 2.6, 112, 2, 4),
        new("RX 570", 5.1, 224, 4, 8),
        new("RX 580", 6.17, 256, 4, 8),
        new("RX 590", 7.12, 256, 8, 8),
        new("RX 470", 4.94, 211, 4, 8),
        new("RX 480", 5.83, 256, 4, 8),
        new("RX 5500 XT", 5.2, 224, 4, 8),
        new("RX 5600 XT", 7.19, 288, 6, 6),
        new("RX 5700 XT", 9.75, 448, 8, 8),
        new("RX 5700", 7.95, 448, 8, 8),
        new("RX 6400", 3.57, 128, 4, 4),
        new("RX 6500 XT", 5.77, 144, 4, 8),
        new("RX 6600 XT", 10.6, 256, 8, 8),
        new("RX 6650 XT", 10.8, 280, 8, 8),
        new("RX 6600", 8.93, 224, 8, 8),
        new("RX 6700 XT", 13.21, 384, 12, 12),
        new("RX 6750 XT", 13.31, 432, 12, 12),
        new("RX 6800 XT", 20.74, 512, 16, 16),
        new("RX 6800", 16.17, 512, 16, 16),
        new("RX 6900 XT", 23.04, 512, 16, 16),
        new("RX 7600", 10.9, 288, 8, 16),
        new("RX 7700 XT", 17.6, 432, 12, 12),
        new("RX 7800 XT", 18.7, 624, 16, 16),
        new("RX 7900 XTX", 30.7, 960, 24, 24),
        new("RX 7900 XT", 26.0, 800, 20, 20),
    };

    private static readonly string[] LaptopMarkers = { "LAPTOP", "MOBILE", "MAX-Q", "MAX Q", "(M)" };

    /// <summary>
    /// Tìm thông số theo tên card. Thử tên dài trước để "RTX 3060 Ti" không bị khớp nhầm thành "RTX 3060".
    /// Trả null với card laptop, card tích hợp và model không có trong bảng.
    /// </summary>
    public static GpuSpec? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = Regex.Replace(name.ToUpperInvariant().Replace("-", " "), @"\s+", " ");
        if (LaptopMarkers.Any(n.Contains)) return null;

        // "GTX 1660S" → "GTX 1660 SUPER", "RTX 3060TI" → "RTX 3060 TI"
        n = Regex.Replace(n, @"(\d{3,4})(TI|S)\b", m => $"{m.Groups[1].Value} {(m.Groups[2].Value == "S" ? "SUPER" : "TI")}");

        return Specs
            .OrderByDescending(s => s.Model.Length)
            .FirstOrDefault(s => Regex.IsMatch(n, $@"\b{Regex.Escape(s.Model)}\b"));
    }
}
