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
    /// <summary>
    /// Chỉ số thiết bị trong danh sách của snapshot (ổ thứ mấy, màn thứ mấy).
    /// Giữ để hai ổ giống hệt nhau vẫn là hai mô hình riêng, xoay độc lập.
    /// </summary>
    public int DeviceIndex { get; init; }

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

    /// <summary>
    /// Cổng ở mặt sau/mặt bên bo mạch, mỗi ký tự một cổng theo thứ tự lắp:
    /// P = PS/2, U = USB-A, C = USB-C, L = LAN, H = HDMI, D = DisplayPort, V = VGA,
    /// I = DVI, A = jack âm thanh, S = cổng COM.
    /// </summary>
    public string RearPorts { get; init; } = "PUUUUUUCLHDAAA";

    public int SataPorts { get; init; } = 4;
    public int FanHeaders { get; init; } = 3;
    public int UsbHeaders { get; init; } = 2;

    /// <summary>Dùng trong cảnh lắp ráp: tô chip RAM trên bo theo đánh giá RAM.</summary>
    public Highlight BoardRamChips { get; init; }

    /// <summary>Dùng trong cảnh lắp ráp: số khe M.2 đang có ổ, không vẽ tấm tản nhiệt che lên.</summary>
    public int M2Occupied { get; init; }

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

/// <summary>
/// Cảnh lắp ráp tổng thể: các linh kiện đọc được đặt đúng chỗ của chúng — CPU trong socket,
/// RAM trong khe, card đồ hoạ trong khe PCIe, ổ M.2 trong khe M.2, ổ SATA nối cáp về bo,
/// màn hình nối cáp về card xuất hình. Mỗi linh kiện có một cột mốc tô màu theo mức đánh giá.
/// </summary>
public sealed record AssemblySpec
{
    public bool IsLaptop { get; init; }
    public required ComponentModelSpec Board { get; init; }
    public ComponentModelSpec? Cpu { get; init; }
    public ComponentModelSpec? Gpu { get; init; }
    public ComponentModelSpec? Memory { get; init; }
    public ComponentModelSpec? Battery { get; init; }
    public ComponentModelSpec? Firmware { get; init; }
    public IReadOnlyList<ComponentModelSpec> Drives { get; init; } = Array.Empty<ComponentModelSpec>();
    public IReadOnlyList<ComponentModelSpec> Displays { get; init; } = Array.Empty<ComponentModelSpec>();

    /// <summary>Mức đánh giá của từng linh kiện, quyết định màu cột mốc.</summary>
    public IReadOnlyDictionary<ComponentKind, HealthRating> Ratings { get; init; } =
        new Dictionary<ComponentKind, HealthRating>();
}
