namespace HardwareInspector.Core.Models;

/// <summary>Nhóm linh kiện được đánh giá độc lập.</summary>
public enum ComponentKind
{
    Cpu,
    Gpu,
    Memory,
    Storage,
    Battery,
    Display,
    Motherboard,
    Firmware,
    Cooling,
    OperatingSystem
}

/// <summary>Bốn mức đánh giá theo yêu cầu nghiệp vụ (+ Unknown khi thiếu dữ liệu).</summary>
public enum HealthRating
{
    Unknown = 0,
    Poor = 1,      // Yếu
    Average = 2,   // Trung bình
    Fair = 3,      // Khá
    Good = 4       // Tốt
}

public enum Severity
{
    Info = 0,
    Notice = 1,
    Warning = 2,
    Critical = 3
}

/// <summary>Mức độ tin cậy tổng thể khi mua/nhận máy đã qua sử dụng.</summary>
public enum TrustLevel
{
    Unknown = 0,
    DoNotBuy = 1,     // Không nên nhận máy
    Suspicious = 2,   // Cần kiểm tra tay / mặc cả
    Acceptable = 3,   // Chấp nhận được
    Trustworthy = 4   // Đáng tin
}

public enum IntegrityStatus
{
    Unknown = 0,
    Compromised = 1,   // Có bằng chứng can thiệp
    Suspicious = 2,    // Dấu hiệu bất thường
    Unverified = 3,    // Không đủ quyền / thiếu dữ liệu để kết luận
    Clean = 4
}

public enum SensorKind
{
    Temperature,
    Load,
    Clock,
    Fan,
    Voltage,
    Power,
    Data,
    Throughput,
    Level
}

/// <summary>Nguồn gốc của một dữ kiện — dùng để chấm trọng số độ tin cậy.</summary>
public enum EvidenceSource
{
    Wmi,
    Smbios,
    AcpiTable,
    UefiVariable,
    Registry,
    SmartAta,
    SmartNvme,
    Sensor,
    EventLog,
    Benchmark,
    Edid,
    FileSystem,
    UserInput
}
