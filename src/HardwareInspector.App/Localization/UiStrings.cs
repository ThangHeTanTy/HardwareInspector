using System.ComponentModel;
using HardwareInspector.Core.Localization;

namespace HardwareInspector.App.Localization;

/// <summary>
/// Chuỗi giao diện, tra theo khoá.
///
/// Khác với câu chữ trong phần phân tích (dùng Loc.S ngay tại chỗ), chuỗi giao diện
/// phải tra theo khoá vì XAML chỉ gọi được indexer chứ không gọi được hàm hai tham số.
/// Lớp này phát PropertyChanged cho chỉ mục rỗng khi đổi ngôn ngữ, nhờ đó mọi binding
/// trong cửa sổ tự cập nhật mà không cần dựng lại giao diện.
///
/// Cách dùng trong XAML:
///   Text="{Binding [Btn.Scan], Source={StaticResource Ui}}"
/// </summary>
public sealed class UiStrings : INotifyPropertyChanged
{
    public static UiStrings Instance { get; } = new();

    /// <summary>
    /// Constructor để public có chủ đích: XAML tự tạo một thực thể riêng trong
    /// từ điển tài nguyên, còn code dùng Instance. Hai thực thể không cần biết nhau
    /// vì cả hai đều tự lắng nghe Loc.Changed, nên chỉ cần đổi Loc.Current là
    /// cả giao diện lẫn code cùng cập nhật.
    /// </summary>
    public UiStrings()
    {
        Loc.Changed += (_, _) => Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Chuỗi rỗng báo cho WPF rằng mọi chỉ mục đều đã thay đổi.</summary>
    public void Refresh() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));

    public string this[string key] =>
        Table.TryGetValue(key, out var pair)
            ? Loc.S(pair.Vi, pair.En)
            : key;

    private readonly record struct Pair(string Vi, string En);

    private static readonly Dictionary<string, Pair> Table = new(StringComparer.Ordinal)
    {
        // --- Thanh lệnh ---
        ["App.Eyebrow"] = new("THẨM ĐỊNH PHẦN CỨNG", "HARDWARE INSPECTION"),
        ["Btn.Scan"] = new("Bắt đầu kiểm tra", "Start inspection"),
        ["Btn.Stress"] = new("Chạy tải nặng", "Run stress test"),
        ["Btn.Stress.Tip"] = new("Ép CPU, RAM, ổ đĩa rồi GPU chạy hết công suất, kiểm tra VRAM khi card còn nóng, sau đó đo lại nhiệt độ (khoảng 9 phút)",
                                "Push CPU, RAM, disk and then the GPU to full load, test VRAM while the card is still hot, then re-measure temperatures (about 9 minutes)"),
        ["Btn.ScreenTest"] = new("Kiểm tra màn hình", "Test display"),
        ["Btn.ExportHtml"] = new("Xuất biên bản", "Export report"),
        ["Btn.ExportJson"] = new("Xuất JSON", "Export JSON"),
        ["Btn.Cancel"] = new("Dừng", "Stop"),
        ["Btn.CopyAll"] = new("Sao chép tất cả", "Copy all"),
        ["Btn.CopyAll.Tip"] = new("Chép toàn bộ thông số và phát hiện của linh kiện này ra clipboard",
                                  "Copy every metric and finding for this component to the clipboard"),
        ["Btn.OpenWarranty"] = new("Mở trang tra cứu", "Open lookup page"),
        ["Combo.Display.Tip"] = new("Chọn màn hình sẽ chạy bài kiểm tra màu",
                                    "Pick the display that the colour test will run on"),
        ["Combo.Language.Tip"] = new("Đổi ngôn ngữ hiển thị", "Change display language"),

        // --- Thẻ kết luận ---
        ["Verdict.Trust"] = new("MỨC TIN CẬY KHI MUA LẠI", "SECOND-HAND TRUST LEVEL"),
        ["Verdict.Firmware"] = new("TÌNH TRẠNG BIOS", "BIOS STATUS"),

        // --- Tên các thẻ ---
        ["Tab.Components"] = new("Linh kiện", "Components"),
        ["Tab.Firmware"] = new("BIOS & bảo mật", "BIOS & security"),
        ["Tab.CrossCheck"] = new("Đối chiếu chéo", "Cross-checks"),
        ["Tab.Benchmark"] = new("Kiểm tra tải", "Stress test"),
        ["Tab.Warranty"] = new("Bảo hành", "Warranty"),
        ["Tab.Manual"] = new("Kiểm tra tay", "Manual checks"),

        // --- Khối chi tiết linh kiện ---
        ["Detail.Specs"] = new("Thông số kỹ thuật", "Specifications"),
        ["Detail.Sensors"] = new("Cảm biến (min / trung bình / max)", "Sensors (min / average / max)"),
        ["Detail.Findings"] = new("Phát hiện và khuyến nghị", "Findings and recommendations"),
        ["Detail.Score"] = new("Điểm ", "Score "),
        ["Detail.Action"] = new("Nên làm: ", "What to do: "),

        // --- Thẻ BIOS ---
        ["Fw.Eyebrow"] = new("KIỂM TRA CAN THIỆP FIRMWARE", "FIRMWARE TAMPERING CHECKS"),
        ["Fw.Intro"] = new(
            "Các phép kiểm tra dưới đây soi bốn tầng: chuỗi khởi động Secure Boot, bảng SMBIOS và ACPI do firmware cung cấp, cấu hình khởi động của Windows, và nội dung phân vùng EFI. Mỗi phát hiện đều ghi rõ mức độ chắc chắn — dấu hiệu suy đoán cần được đối chiếu thêm trước khi kết luận.",
            "These checks cover four layers: the Secure Boot chain, the SMBIOS and ACPI tables supplied by firmware, Windows boot configuration, and the contents of the EFI partition. Every finding states its confidence — inferred signals need corroboration before you draw a conclusion."),

        // --- Thẻ đối chiếu chéo ---
        ["Cross.Eyebrow"] = new("ĐỐI CHIẾU GIỮA CÁC NGUỒN DỮ LIỆU ĐỘC LẬP",
                                "CROSS-CHECKS BETWEEN INDEPENDENT DATA SOURCES"),
        ["Cross.Intro"] = new(
            "Mỗi linh kiện mang một loại đồng hồ riêng: số giờ chạy của ổ cứng, số chu kỳ của pin, năm sản xuất tấm nền, ngày phát hành BIOS. Trên một máy nguyên bản và được mô tả trung thực, tất cả kể cùng một câu chuyện. Phần này tìm chỗ chúng mâu thuẫn.",
            "Every component carries its own clock: drive power-on hours, battery cycle count, panel manufacture year, BIOS release date. On an untouched machine described honestly, they all tell the same story. This section looks for where they contradict each other."),

        // --- Thẻ kiểm tra tải ---
        ["Bench.Eyebrow"] = new("KẾT QUẢ CHẠY TẢI", "STRESS TEST RESULTS"),
        ["Bench.Intro"] = new(
            "Các con số dưới đây không nhằm so sánh hiệu năng với máy khác. Mục đích của bài chạy tải là dựng lên tình huống nóng thật sự, để nhiệt độ và xung nhịp bộc lộ vấn đề tản nhiệt mà lúc máy nghỉ không thể thấy. Hãy xem lại tab Linh kiện sau khi chạy xong.",
            "These numbers are not for comparing against other machines. The point of the stress run is to create genuine heat, so that temperatures and clock speeds expose cooling problems that stay hidden at idle. Revisit the Components tab once it finishes."),

        // --- Thẻ bảo hành ---
        ["War.Eyebrow"] = new("TRA BẢO HÀNH THEO SERIAL", "WARRANTY LOOKUP BY SERIAL"),
        ["War.Intro"] = new(
            "Ứng dụng không tự gửi serial của bạn đi đâu cả. Bấm vào một dòng sẽ chép serial vào clipboard và mở trang tra cứu của hãng để bạn tự đối chiếu. Điều cần nhìn không chỉ là còn hạn hay không, mà là model mà trang bảo hành trả về có trùng với máy đang cầm hay không — lệch model nghĩa là serial không thuộc máy này.",
            "The app never sends your serial anywhere. Clicking a row copies the serial to your clipboard and opens the vendor's lookup page so you can check it yourself. What matters is not only whether coverage is still active, but whether the model the page returns matches the machine in your hands — a mismatch means the serial does not belong to this machine."),
        ["War.Vendor"] = new("Hãng: ", "Vendor: "),
        ["War.Serial"] = new("   Serial: ", "   Serial: "),
        ["War.NoSerial"] = new("không đọc được", "not readable"),

        // --- Thẻ kiểm tra tay ---
        ["Man.Eyebrow"] = new("PHẦN MỀM KHÔNG THAY THẾ ĐƯỢC", "WHAT SOFTWARE CANNOT REPLACE"),
        ["Man.Intro"] = new(
            "Danh sách này thay đổi theo những gì vừa phát hiện được trên máy. Hãy làm đủ trước khi trả tiền. Nút bên dưới mỗi mục mở trang chính thức của công cụ chuyên dụng — đừng tải từ trang trung gian, đó là nguồn mã độc quen thuộc.",
            "This list adapts to what was just found on this machine. Work through all of it before you hand over money. The buttons under each item open the official page of a specialist tool — avoid third-party download sites, a common source of malware."),

        // --- Trạng thái ---
        ["Status.Ready"] = new("Sẵn sàng. Nhấn \"Bắt đầu kiểm tra\" để quét toàn bộ máy.",
                              "Ready. Press \"Start inspection\" to scan the whole machine."),
        ["Status.Cancelled"] = new("Đã huỷ giữa chừng.", "Cancelled part-way through."),
        ["Status.StressStopped"] = new("Đã dừng bài kiểm tra tải.", "Stress test stopped."),
        ["Status.Starting"] = new("Đang khởi động", "Starting up"),
        ["Status.StressPrep"] = new("Chuẩn bị chạy tải", "Preparing stress run"),
        ["Status.Rescan"] = new("Chạy tải xong. Đang quét lại để cập nhật nhiệt độ và xung nhịp dưới tải…",
                               "Stress run finished. Re-scanning to capture temperatures and clocks under load…"),
        ["Status.StressDone"] = new("Hoàn tất bài kiểm tra tải. Số liệu nhiệt độ giờ đã phản ánh trạng thái tải nặng.",
                                   "Stress test complete. Temperature figures now reflect the machine under load."),
        ["Status.NoWarrantyLink"] = new("Hãng này chưa có liên kết tra cứu sẵn — xem ghi chú bên cạnh.",
                                       "No ready-made lookup link for this vendor — see the note beside it."),
        ["Msg.ScanFailed"] = new("Kiểm tra thất bại: {0}", "Inspection failed: {0}"),
        ["Msg.StressFailed"] = new("Bài kiểm tra tải gặp lỗi: {0}", "Stress test failed: {0}"),
        ["Msg.Saved"] = new("Đã lưu báo cáo {0}: {1}", "Saved {0} report: {1}"),
        ["Msg.ExportFailed"] = new("Không xuất được báo cáo: {0}", "Could not export the report: {0}"),
        ["Msg.SerialCopied"] = new("Đã sao serial {0} vào clipboard.", "Copied serial {0} to the clipboard."),
        ["Msg.CopyFailed"] = new("Không chép được: {0}", "Copy failed: {0}"),
        ["Msg.DetailCopied"] = new("Đã chép chi tiết \"{0}\" vào clipboard.",
                                  "Copied the details of \"{0}\" to the clipboard."),
        ["Msg.ToolOpened"] = new("Đã mở trang {0}: {1}", "Opened the {0} page: {1}"),
        ["Msg.LookupFailed"] = new("Không mở được trang tra cứu: {0}", "Could not open the lookup page: {0}"),

        // --- Nhãn dùng khi chép ra văn bản ---
        ["Copy.Specs"] = new("THÔNG SỐ", "SPECIFICATIONS"),
        ["Copy.Sensors"] = new("CẢM BIẾN (min / trung bình / max)", "SENSORS (min / average / max)"),
        ["Copy.Findings"] = new("PHÁT HIỆN", "FINDINGS"),
        ["Copy.Action"] = new("Nên làm", "What to do")
    };
}
