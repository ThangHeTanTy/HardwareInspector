namespace HardwareInspector.Core.Models.Assessment;

public enum PurchaseDecision { Unknown, Buy, Negotiate, Avoid }

/// <summary>Sửa: khoản phải bỏ ra để máy chạy ổn. Nâng cấp: không bắt buộc, nhưng đáng cân nhắc.</summary>
public enum CostKind { Repair, Upgrade }

/// <summary>Một khoản chi ước tính, tính bằng đồng. Giá tham khảo thị trường, thực tế tuỳ nơi.</summary>
public sealed record CostItem(CostKind Kind, ComponentKind Component, string Title, string Reason, long MinVnd, long MaxVnd);

/// <summary>
/// Kết luận dành cho người mua ít kinh nghiệm. Điểm số cho biết máy tốt tới đâu;
/// phần này trả lời câu họ thật sự cần: có nên trả tiền không, và nếu mua thì trả giá ra sao.
/// </summary>
public sealed class PurchaseAdvice
{
    public PurchaseDecision Decision { get; set; } = PurchaseDecision.Unknown;

    /// <summary>Câu kết luận ngắn, viết hoa ở giao diện: "NÊN MUA", "KHÔNG NÊN MUA"...</summary>
    public string Headline { get; set; } = string.Empty;

    /// <summary>Một hai câu giải thích vì sao.</summary>
    public string Explanation { get; set; } = string.Empty;

    /// <summary>Lý do không nên mua — thứ không sửa được bằng tiền hoặc là dấu hiệu gian lận.</summary>
    public List<string> Blockers { get; } = new();

    /// <summary>Việc người bán phải làm xong trước khi trả tiền (gỡ mật khẩu BIOS, gỡ khỏi tổ chức...).</summary>
    public List<string> Conditions { get; } = new();

    public List<CostItem> Costs { get; } = new();

    public long RepairMinVnd => Costs.Where(c => c.Kind == CostKind.Repair).Sum(c => c.MinVnd);
    public long RepairMaxVnd => Costs.Where(c => c.Kind == CostKind.Repair).Sum(c => c.MaxVnd);
    public bool HasRepairs => Costs.Any(c => c.Kind == CostKind.Repair);

    /// <summary>Gợi ý trả giá cụ thể, dựa trên chi phí khắc phục.</summary>
    public string NegotiationTip { get; set; } = string.Empty;

    public string ColorHex => Decision switch
    {
        PurchaseDecision.Buy => "#2FBF71",
        PurchaseDecision.Negotiate => "#F4A259",
        PurchaseDecision.Avoid => "#E5544B",
        _ => "#8A8F98"
    };
}
