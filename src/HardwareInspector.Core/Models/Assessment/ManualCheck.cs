namespace HardwareInspector.Core.Models.Assessment;

/// <summary>
/// Một công cụ chuyên dụng nên dùng cho một mục kiểm tra tay.
///
/// Ứng dụng chỉ đưa đường dẫn tới trang chính thức của nhà phát triển, không tự tải
/// hay đóng gói kèm: phần lớn các công cụ này không cho phép phân phối lại, và
/// bản "tải nhanh" trên các trang trung gian là nguồn mã độc quen thuộc.
/// </summary>
/// <param name="Name">Tên công cụ.</param>
/// <param name="Url">Trang tải chính thức, hoặc trang kiểm tra trực tuyến.</param>
/// <param name="Purpose">Dùng công cụ này để kiểm tra gì, nhìn vào đâu.</param>
/// <param name="Cost">Miễn phí / trả phí / chạy trên trình duyệt.</param>
public sealed record ToolLink(string Name, string Url, string Purpose, string Cost);

/// <summary>Một việc cần làm bằng tay trước khi trả tiền, kèm công cụ giúp làm việc đó chuẩn hơn.</summary>
public sealed record ManualCheckItem(string Text, IReadOnlyList<ToolLink> Tools)
{
    public ManualCheckItem(string text) : this(text, Array.Empty<ToolLink>()) { }

    public bool HasTools => Tools.Count > 0;
}
