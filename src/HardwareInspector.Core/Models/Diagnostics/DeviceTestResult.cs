namespace HardwareInspector.Core.Models.Diagnostics;

/// <summary>
/// Kết quả các bài thử thiết bị vào ra làm ngay trong ứng dụng: bàn phím, touchpad/chuột,
/// loa, micro, webcam. Phần mềm đo được một phần (phím nào đã nhận, phím nào dội, micro
/// thu được bao nhiêu), phần còn lại người dùng tự đánh giá Đạt / Không đạt.
/// Null ở các trường Ok nghĩa là chưa thử.
/// </summary>
public sealed class DeviceTestResult
{
    // --- Bàn phím ---
    public int KeysTotal { get; set; }
    public int KeysPressed { get; set; }
    public List<string> UntestedKeys { get; } = new();

    /// <summary>Phím dội: một lần bấm mà máy nhận hai lần.</summary>
    public List<string> ChatterKeys { get; } = new();

    /// <summary>Phím bị giữ quá lâu mà không nhả — nghi kẹt.</summary>
    public List<string> StuckKeys { get; } = new();

    /// <summary>Số phím nhận được cùng lúc nhiều nhất.</summary>
    public int MaxSimultaneousKeys { get; set; }

    public bool? KeyboardOk { get; set; }

    // --- Touchpad / chuột ---
    public double TouchpadCoveragePercent { get; set; }
    public bool LeftClickSeen { get; set; }
    public bool RightClickSeen { get; set; }
    public bool ScrollSeen { get; set; }
    public bool? TouchpadOk { get; set; }

    // --- Loa ---
    public bool? SpeakerLeftOk { get; set; }
    public bool? SpeakerRightOk { get; set; }

    // --- Micro ---
    /// <summary>Mức đỉnh thu được, dBFS (0 là to nhất). Null khi chưa thu.</summary>
    public double? MicPeakDb { get; set; }
    public bool? MicOk { get; set; }

    // --- Webcam ---
    public List<string> Cameras { get; } = new();
    public bool? CameraOk { get; set; }

    public bool Any =>
        KeyboardOk.HasValue || TouchpadOk.HasValue || SpeakerLeftOk.HasValue || SpeakerRightOk.HasValue ||
        MicOk.HasValue || CameraOk.HasValue;
}
