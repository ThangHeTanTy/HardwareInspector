namespace HardwareInspector.Collectors.Smart;

/// <summary>
/// Bảng tra tên thuộc tính S.M.A.R.T. và cờ "pre-failure".
/// Chỉ liệt kê các ID thực sự có ý nghĩa khi thẩm định ổ cứng đã qua sử dụng.
/// </summary>
public static class SmartAttributeCatalog
{
    /// <summary>Meaning chỉ để người đọc code hiểu thuộc tính; giao diện hiển thị Name chuẩn của S.M.A.R.T.</summary>
    public sealed record Definition(string Name, bool PreFailure, string Meaning);

    private static readonly Dictionary<byte, Definition> Map = new()
    {
        [0x01] = new("Read Error Rate", true, "Rate of errors reading from the platter"),
        [0x03] = new("Spin-Up Time", true, "Time for the motor to reach full speed"),
        [0x04] = new("Start/Stop Count", false, "Motor start/stop count"),
        [0x05] = new("Reallocated Sectors Count", true, "Bad sectors already replaced — a rising count means the drive is dying"),
        [0x07] = new("Seek Error Rate", true, "Head seek error rate"),
        [0x09] = new("Power-On Hours", false, "Total hours the drive has been powered"),
        [0x0A] = new("Spin Retry Count", true, "Motor spin-up retries"),
        [0x0C] = new("Power Cycle Count", false, "Power on/off count"),
        [0xA9] = new("Percent Life Remaining", true, "Remaining SSD life"),
        [0xAA] = new("Available Reserved Space", true, "Remaining SSD spare area"),
        [0xAB] = new("Program Fail Count", false, "Failed program operations"),
        [0xAC] = new("Erase Fail Count", false, "Failed block erases"),
        [0xAE] = new("Unexpected Power Loss", false, "Sudden power losses"),
        [0xB1] = new("Wear Range Delta", false, "Wear difference between blocks"),
        [0xB8] = new("End-to-End Error", true, "End-to-end data path errors"),
        [0xBB] = new("Reported Uncorrectable Errors", true, "Reported uncorrectable errors"),
        [0xBC] = new("Command Timeout", false, "Command timeouts — usually cable/power"),
        [0xBE] = new("Airflow Temperature", false, "Airflow temperature"),
        [0xBF] = new("G-Sense Error Rate", false, "Shock errors — a sign the machine was dropped"),
        [0xC0] = new("Unsafe Shutdown Count", false, "Unsafe shutdown count"),
        [0xC2] = new("Temperature", false, "Drive temperature"),
        [0xC4] = new("Reallocation Event Count", true, "Sector reallocation events"),
        [0xC5] = new("Current Pending Sector", true, "Sectors pending reallocation — dangerous, already unreadable"),
        [0xC6] = new("Offline Uncorrectable", true, "Sectors uncorrectable in offline scan"),
        [0xC7] = new("UDMA CRC Error Count", false, "Transfer errors on the SATA cable"),
        [0xE7] = new("SSD Life Left", true, "Remaining SSD life"),
        [0xE9] = new("Media Wearout Indicator", true, "NAND wear indicator"),
        [0xF1] = new("Total LBAs Written", false, "Total data written"),
        [0xF2] = new("Total LBAs Read", false, "Total data read")
    };

    public static Definition Lookup(byte id) =>
        Map.TryGetValue(id, out var d) ? d : new Definition($"Attribute 0x{id:X2}", false, "No description");
}
