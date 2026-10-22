using HardwareInspector.Core.Models;

namespace HardwareInspector.App.Rendering;

/// <summary>Mức tô sáng một bộ phận trên mô hình để chỉ ra chỗ có vấn đề.</summary>
public enum Highlight { None, Warn, Fault }

public enum CpuPackage { IntelLga, AmdPga, AmdLga, Bga }
public enum GpuForm { Desktop, LaptopDiscrete, Integrated }
public enum FanCountSource { Default, Sensor, User }
public enum RamForm { Dimm, SoDimm, Soldered }
public enum DriveForm { M2, Ssd25, Hdd35, Hdd25 }
public enum PanelForm { Monitor, LaptopLid }
public enum BoardForm { Atx, Laptop }

/// <summary>
/// Thông số để dựng mô hình 3D sát với linh kiện thật trong máy: hình dáng theo chuẩn
/// (socket CPU, loại RAM, chuẩn ổ), số lượng bộ phận (quạt, chip nhớ, cell pin, khe cắm)
/// và chỗ cần tô sáng theo kết quả đánh giá.
///
/// Dùng record chỉ chứa giá trị đơn để hai thông số giống nhau so sánh bằng nhau —
/// nhờ đó mô hình đã dựng được dùng lại từ bộ nhớ đệm.
/// Mỗi loại linh kiện chỉ dùng nhóm thuộc tính của mình.
/// </summary>
public sealed record ComponentModelSpec(ComponentKind Kind)
{
    // --- CPU ---
    public CpuPackage CpuPackage { get; init; } = CpuPackage.IntelLga;
    public double CpuWidthMm { get; init; } = 37.5;
    public double CpuDepthMm { get; init; } = 37.5;
    public bool CpuHasPchDie { get; init; }
    public Highlight CpuDie { get; init; }

    // --- GPU ---
    public GpuForm GpuForm { get; init; } = GpuForm.Desktop;
    public int FanCount { get; init; } = 2;
    public FanCountSource FanSource { get; init; } = FanCountSource.Default;
    public int VramChips { get; init; } = 8;
    public Highlight GpuCore { get; init; }
    public Highlight Vram { get; init; }
    public Highlight Fans { get; init; }

    // --- RAM ---
    public RamForm RamForm { get; init; } = RamForm.Dimm;
    public int DdrGeneration { get; init; } = 4;
    public int ModuleCount { get; init; } = 1;
    public int ChipsPerSide { get; init; } = 8;
    public bool DoubleSided { get; init; }
    public Highlight RamChips { get; init; }

    // --- Ổ lưu trữ ---
    public DriveForm DriveForm { get; init; } = DriveForm.M2;
    public int NandPackages { get; init; } = 2;
    public Highlight DriveMedia { get; init; }

    // --- Pin ---
    public int SeriesCells { get; init; } = 3;
    public Highlight BatteryCells { get; init; }

    // --- Màn hình ---
    public PanelForm PanelForm { get; init; } = PanelForm.Monitor;
    public double Aspect { get; init; } = 16.0 / 9;

    // --- Bo mạch ---
    public BoardForm BoardForm { get; init; } = BoardForm.Atx;

    /// <summary>Mỗi ký tự là một khe RAM: '1' đang cắm, '0' còn trống.</summary>
    public string DimmSlots { get; init; } = "1100";
    public int PcieX16Slots { get; init; } = 1;
    public int PcieSmallSlots { get; init; } = 2;
    public int M2Slots { get; init; } = 1;

    // --- Firmware ---
    public Highlight FirmwareChip { get; init; }

    /// <summary>
    /// Có bộ phận nào bị tô sáng nằm bên trong vỏ không. Khi có, mô hình được vẽ ở dạng
    /// tách lớp (nhấc vỏ tản nhiệt hoặc nắp ổ lên) để thấy được chỗ hỏng.
    /// </summary>
    public bool Exploded => Kind switch
    {
        ComponentKind.Gpu => GpuForm == GpuForm.Desktop && (GpuCore != Highlight.None || Vram != Highlight.None),
        ComponentKind.Storage => DriveForm != DriveForm.M2 && DriveMedia != Highlight.None,
        _ => false
    };
}
