namespace HardwareInspector.Core.Localization;

public enum AppLanguage
{
    Vietnamese = 0,
    English = 1
}

/// <summary>
/// Bộ chuyển ngữ dùng chung cho toàn bộ ứng dụng.
///
/// Thiết kế có chủ đích: hai bản dịch được viết ngay cạnh nhau tại chỗ dùng,
/// thay vì tách ra file tài nguyên với khoá riêng. Lý do là phần lớn câu chữ
/// trong phần mềm này là văn giải thích dài, gắn chặt với logic phân tích ngay
/// bên cạnh. Tách khoá ra file khác sẽ khiến người sửa logic không thấy được
/// câu chữ, và ngược lại — đó là nguồn gốc kinh điển của bản dịch lệch nội dung.
///
/// Kết quả trả về là chuỗi thường, giải quyết ngay lúc gọi. Nhờ vậy không phải
/// luồn kiểu dữ liệu đa ngữ qua toàn bộ model. Khi người dùng đổi ngôn ngữ,
/// ứng dụng chỉ cần chấm điểm lại từ ảnh chụp phần cứng đã có — việc này không
/// đụng tới phần cứng nên chạy tức thì.
/// </summary>
public static class Loc
{
    private static AppLanguage _current = AppLanguage.Vietnamese;

    public static AppLanguage Current
    {
        get => _current;
        set
        {
            if (_current == value) return;
            _current = value;
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    public static event EventHandler? Changed;

    public static bool IsEnglish => _current == AppLanguage.English;

    /// <summary>Chọn một trong hai bản dịch theo ngôn ngữ đang dùng.</summary>
    public static string S(string vi, string en) => _current == AppLanguage.English ? en : vi;

    /// <summary>Bản có tham số. Hai chuỗi phải dùng cùng bộ chỉ số {0}, {1}...</summary>
    public static string S(string vi, string en, params object?[] args) =>
        string.Format(S(vi, en), args);

    public static IReadOnlyList<LanguageOption> Options { get; } = new[]
    {
        new LanguageOption(AppLanguage.Vietnamese, "Tiếng Việt", "vi-VN"),
        new LanguageOption(AppLanguage.English, "English", "en-US")
    };
}

public sealed record LanguageOption(AppLanguage Language, string DisplayName, string CultureCode)
{
    /// <summary>Khoá tra hình cờ trong từ điển tài nguyên của giao diện.</summary>
    public string FlagKey => Language == AppLanguage.English ? "FlagEnglish" : "FlagVietnamese";
}
