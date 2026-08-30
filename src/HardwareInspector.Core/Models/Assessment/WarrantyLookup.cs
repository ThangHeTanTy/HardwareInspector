namespace HardwareInspector.Core.Models.Assessment;

/// <summary>
/// Một đầu mối tra bảo hành. Phần mềm không tự gọi API của hãng — các cổng tra cứu
/// đều yêu cầu xác thực hoặc chống bot, và việc tự gửi serial đi nơi khác là điều
/// không nên làm sau lưng người dùng. Thay vào đó, ứng dụng dựng sẵn đường dẫn
/// kèm serial để người dùng bấm mở và tự đối chiếu.
/// </summary>
public sealed record WarrantyLookup
{
    public required ComponentKind Component { get; init; }
    public required string ComponentName { get; init; }
    public required string Vendor { get; init; }

    /// <summary>Số serial dùng để tra. Rỗng nghĩa là không đọc được từ firmware.</summary>
    public string? Serial { get; init; }

    /// <summary>Trang tra cứu, đã điền sẵn serial nếu hãng hỗ trợ qua URL.</summary>
    public string? Url { get; init; }

    /// <summary>Ghi chú về cách tra và những gì cần đối chiếu.</summary>
    public string? Note { get; init; }

    public bool HasSerial => !string.IsNullOrWhiteSpace(Serial);
}
