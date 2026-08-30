namespace HardwareInspector.Collectors.Smart;

/// <summary>
/// Bảng tra tên thuộc tính S.M.A.R.T. và cờ "pre-failure".
/// Chỉ liệt kê các ID thực sự có ý nghĩa khi thẩm định ổ cứng đã qua sử dụng.
/// </summary>
public static class SmartAttributeCatalog
{
    public sealed record Definition(string Name, bool PreFailure, string Meaning);

    private static readonly Dictionary<byte, Definition> Map = new()
    {
        [0x01] = new("Read Error Rate", true, "Tỉ lệ lỗi khi đọc từ mặt đĩa"),
        [0x03] = new("Spin-Up Time", true, "Thời gian mô-tơ đạt tốc độ vòng quay"),
        [0x04] = new("Start/Stop Count", false, "Số lần khởi động/dừng mô-tơ"),
        [0x05] = new("Reallocated Sectors Count", true, "Số sector hỏng đã được thay thế — tăng dần là ổ đang chết"),
        [0x07] = new("Seek Error Rate", true, "Tỉ lệ lỗi định vị đầu đọc"),
        [0x09] = new("Power-On Hours", false, "Tổng số giờ ổ đã cấp điện"),
        [0x0A] = new("Spin Retry Count", true, "Số lần phải quay lại mô-tơ"),
        [0x0C] = new("Power Cycle Count", false, "Số lần bật/tắt nguồn"),
        [0xA9] = new("Percent Life Remaining", true, "Tuổi thọ SSD còn lại"),
        [0xAA] = new("Available Reserved Space", true, "Vùng dự phòng còn lại của SSD"),
        [0xAB] = new("Program Fail Count", false, "Số lần ghi thất bại"),
        [0xAC] = new("Erase Fail Count", false, "Số lần xoá khối thất bại"),
        [0xAE] = new("Unexpected Power Loss", false, "Số lần mất điện đột ngột"),
        [0xB1] = new("Wear Range Delta", false, "Chênh lệch hao mòn giữa các khối"),
        [0xB8] = new("End-to-End Error", true, "Lỗi toàn tuyến dữ liệu"),
        [0xBB] = new("Reported Uncorrectable Errors", true, "Lỗi không thể sửa được báo về"),
        [0xBC] = new("Command Timeout", false, "Lệnh quá hạn — thường do cáp/nguồn"),
        [0xBE] = new("Airflow Temperature", false, "Nhiệt độ luồng khí"),
        [0xBF] = new("G-Sense Error Rate", false, "Lỗi do va đập — dấu hiệu máy từng bị rơi"),
        [0xC0] = new("Unsafe Shutdown Count", false, "Số lần tắt máy không an toàn"),
        [0xC2] = new("Temperature", false, "Nhiệt độ ổ"),
        [0xC4] = new("Reallocation Event Count", true, "Số lần thực hiện tráo sector"),
        [0xC5] = new("Current Pending Sector", true, "Sector đang chờ tráo — nguy hiểm, đọc lỗi ngay"),
        [0xC6] = new("Offline Uncorrectable", true, "Sector không thể sửa khi quét offline"),
        [0xC7] = new("UDMA CRC Error Count", false, "Lỗi truyền trên cáp SATA"),
        [0xE7] = new("SSD Life Left", true, "Tuổi thọ SSD còn lại"),
        [0xE9] = new("Media Wearout Indicator", true, "Chỉ số hao mòn NAND"),
        [0xF1] = new("Total LBAs Written", false, "Tổng dung lượng đã ghi"),
        [0xF2] = new("Total LBAs Read", false, "Tổng dung lượng đã đọc")
    };

    public static Definition Lookup(byte id) =>
        Map.TryGetValue(id, out var d) ? d : new Definition($"Attribute 0x{id:X2}", false, "Không có mô tả");
}
