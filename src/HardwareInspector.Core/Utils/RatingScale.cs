using HardwareInspector.Core.Localization;
using HardwareInspector.Core.Models;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Core.Utils;

/// <summary>
/// Quy đổi điểm 0..100 sang bốn mức đánh giá, và chuyển các enum sang câu chữ
/// theo ngôn ngữ đang chọn. Ngưỡng tập trung ở một chỗ để mọi linh kiện
/// dùng chung một thước đo.
/// </summary>
public static class RatingScale
{
    public const int GoodThreshold = 80;
    public const int FairThreshold = 65;
    public const int AverageThreshold = 45;

    public static HealthRating FromScore(int score) => score switch
    {
        >= GoodThreshold => HealthRating.Good,
        >= FairThreshold => HealthRating.Fair,
        >= AverageThreshold => HealthRating.Average,
        _ => HealthRating.Poor
    };

    public static string ToText(this HealthRating rating) => rating switch
    {
        HealthRating.Good => S("Tốt", "Good"),
        HealthRating.Fair => S("Khá", "Fair"),
        HealthRating.Average => S("Trung bình", "Average"),
        HealthRating.Poor => S("Yếu", "Poor"),
        _ => S("Chưa xác định", "Not determined")
    };

    public static string ToText(this TrustLevel level) => level switch
    {
        TrustLevel.Trustworthy => S("Đáng tin", "Trustworthy"),
        TrustLevel.Acceptable => S("Chấp nhận được", "Acceptable"),
        TrustLevel.Suspicious => S("Cần cảnh giác", "Treat with caution"),
        TrustLevel.DoNotBuy => S("Không nên nhận máy", "Do not buy"),
        _ => S("Chưa đủ dữ liệu", "Not enough data")
    };

    public static string ToText(this Severity severity) => severity switch
    {
        Severity.Critical => S("Nghiêm trọng", "Critical"),
        Severity.Warning => S("Cảnh báo", "Warning"),
        Severity.Notice => S("Lưu ý", "Notice"),
        _ => S("Thông tin", "Information")
    };

    public static string ToText(this IntegrityStatus status) => status switch
    {
        IntegrityStatus.Clean => S("Không phát hiện can thiệp", "No tampering detected"),
        IntegrityStatus.Unverified => S("Không đủ dữ liệu để kết luận", "Not enough data to conclude"),
        IntegrityStatus.Suspicious => S("Có dấu hiệu bất thường", "Anomalies present"),
        IntegrityStatus.Compromised => S("Phát hiện dấu hiệu can thiệp", "Signs of tampering found"),
        _ => S("Chưa kiểm tra", "Not checked")
    };

    public static string ToText(this ComponentKind kind) => kind switch
    {
        ComponentKind.Cpu => S("CPU", "CPU"),
        ComponentKind.Gpu => S("Card đồ hoạ", "Graphics"),
        ComponentKind.Memory => S("RAM", "Memory"),
        ComponentKind.Storage => S("Ổ lưu trữ", "Storage"),
        ComponentKind.Battery => S("Pin", "Battery"),
        ComponentKind.Display => S("Màn hình", "Display"),
        ComponentKind.Motherboard => S("Bo mạch chủ", "Motherboard"),
        ComponentKind.Firmware => S("BIOS & bảo mật", "BIOS & security"),
        ComponentKind.Cooling => S("Tản nhiệt", "Cooling"),
        ComponentKind.OperatingSystem => S("Hệ điều hành", "Operating system"),
        _ => kind.ToString()
    };

    /// <summary>Mã màu dùng chung cho UI và báo cáo HTML.</summary>
    public static string ToHexColor(this HealthRating rating) => rating switch
    {
        HealthRating.Good => "#2FBF71",
        HealthRating.Fair => "#8AC926",
        HealthRating.Average => "#F4A259",
        HealthRating.Poor => "#E5544B",
        _ => "#8A8F98"
    };
}
