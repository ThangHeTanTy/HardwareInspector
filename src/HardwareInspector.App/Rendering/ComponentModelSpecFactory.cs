using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Assessment;
using HardwareInspector.Core.Models.Components;
using HardwareInspector.Core.Models.Sensors;

namespace HardwareInspector.App.Rendering;

/// <summary>
/// Suy ra hình dáng thật của linh kiện từ dữ liệu đã thu thập.
///
/// Phần mềm không biết vỏ ngoài cụ thể của từng sản phẩm (cùng một chip RTX 3070
/// có hàng chục kiểu tản nhiệt), nhưng biết đủ để dựng đúng chủng loại và tỉ lệ:
/// socket quyết định kích thước và kiểu chân CPU, loại RAM quyết định chiều dài thanh
/// và vị trí khấc, chuẩn kết nối quyết định ổ M.2 hay 2,5"/3,5", điện áp pin cho biết
/// số cell nối tiếp, bảng SMBIOS cho biết bo mạch có mấy khe RAM và khe nào đang cắm.
/// </summary>
public static class ComponentModelSpecFactory
{
    public static ComponentModelSpec Create(SystemSnapshot snapshot, ComponentAssessment assessment,
        int? gpuFanOverride = null) => assessment.Kind switch
    {
        ComponentKind.Cpu => Cpu(snapshot, assessment),
        ComponentKind.Gpu => Gpu(snapshot, assessment, gpuFanOverride),
        ComponentKind.Memory => Memory(snapshot, assessment),
        ComponentKind.Storage => Storage(snapshot),
        ComponentKind.Battery => Battery(snapshot),
        ComponentKind.Display => Display(snapshot),
        ComponentKind.Motherboard => Board(snapshot),
        ComponentKind.Firmware => new ComponentModelSpec(ComponentKind.Firmware)
        {
            FirmwareChip = snapshot.Firmware.Status switch
            {
                IntegrityStatus.Compromised => Highlight.Fault,
                IntegrityStatus.Suspicious => Highlight.Warn,
                _ => Highlight.None
            }
        },
        _ => new ComponentModelSpec(assessment.Kind)
    };

    // ------------------------------------------------------------------

    private static ComponentModelSpec Cpu(SystemSnapshot snapshot, ComponentAssessment a)
    {
        var cpu = snapshot.PrimaryCpu;
        var socket = (cpu?.Socket ?? string.Empty).ToUpperInvariant().Replace(" ", string.Empty);
        var isAmd = (cpu?.Manufacturer ?? cpu?.Name ?? string.Empty).Contains("AMD", StringComparison.OrdinalIgnoreCase);

        // Kích thước đế theo đặc tả cơ khí của từng socket (mm).
        var (package, w, d) = socket switch
        {
            _ when socket.Contains("BGA") || socket.StartsWith("FP") => (CpuPackage.Bga, isAmd ? 35.0 : 50.0, 25.0),
            _ when socket.Contains("AM5") => (CpuPackage.AmdLga, 40.0, 40.0),
            _ when socket.Contains("AM4") || socket.Contains("AM3") => (CpuPackage.AmdPga, 40.0, 40.0),
            _ when socket.Contains("1700") || socket.Contains("1851") => (CpuPackage.IntelLga, 45.0, 37.5),
            _ when socket.Contains("LGA") || socket.Contains("115") || socket.Contains("1200") => (CpuPackage.IntelLga, 37.5, 37.5),
            _ when snapshot.IsLaptop => (CpuPackage.Bga, isAmd ? 35.0 : 50.0, 25.0),
            _ when isAmd => (CpuPackage.AmdPga, 40.0, 40.0),
            _ => (CpuPackage.IntelLga, 37.5, 37.5)
        };

        return new ComponentModelSpec(ComponentKind.Cpu)
        {
            CpuPackage = package,
            CpuWidthMm = w,
            CpuDepthMm = d,
            // CPU laptop Intel đời H/U trước Meteor Lake có thêm khuôn PCH bên cạnh nhân.
            CpuHasPchDie = package == CpuPackage.Bga && !isAmd,
            CpuDie = FromFindings(a, "CPU-TEMP", "CPU-CLK")
        };
    }

    private static ComponentModelSpec Gpu(SystemSnapshot snapshot, ComponentAssessment a, int? fanOverride)
    {
        var gpu = snapshot.PrimaryGpu;
        var form = gpu is not { IsDiscrete: true } ? GpuForm.Integrated
                 : snapshot.IsLaptop ? GpuForm.LaptopDiscrete
                 : GpuForm.Desktop;

        var (fans, source) = ResolveFans(snapshot, form, fanOverride);

        // Số chip VRAM: GDDR6 phổ biến loại 1 GB và 2 GB mỗi chip. Card tới 12 GB thường
        // dùng chip 1 GB, card lớn hơn dùng chip 2 GB.
        var gb = (int)Math.Round((gpu?.VideoMemoryBytes ?? 0) / 1024.0 / 1024 / 1024);
        var chips = gb <= 0 ? 8 : gb <= 12 ? Math.Clamp(gb, 2, 12) : Math.Clamp(gb / 2, 6, 12);

        return new ComponentModelSpec(ComponentKind.Gpu)
        {
            GpuForm = form,
            FanCount = fans,
            FanSource = source,
            VramChips = chips,
            GpuCore = FromFindings(a, "GPU-TEMP-001", "GPU-TEMP-002", "GPU-TEMP-003", "GPU-CALC", "GPU-TDR", "GPU-THROT"),
            Vram = FromFindings(a, "GPU-VRAM"),
            Fans = FromFindings(a, "GPU-FAN")
        };
    }

    /// <summary>
    /// Đếm quạt của card đồ hoạ từ cảm biến. Mỗi cảm biến tốc độ quạt thường ứng với
    /// một bộ điều khiển; card ba quạt hay ghép hai quạt chung một bộ, nên con số này
    /// chỉ là giá trị gợi ý — người dùng chỉnh lại được ngay trên mô hình.
    /// </summary>
    private static (int Count, FanCountSource Source) ResolveFans(SystemSnapshot snapshot, GpuForm form, int? fanOverride)
    {
        // Card rời PC tối đa 3 quạt; laptop thường 1–2 quạt thổi cho cả cụm CPU/GPU.
        var max = form == GpuForm.Desktop ? 3 : 2;
        if (fanOverride is > 0) return (Math.Clamp(fanOverride.Value, 1, max), FanCountSource.User);

        var sensors = snapshot.SensorSnapshot
            .Where(s => s.Kind == SensorKind.Fan && IsGpuSensor(s))
            .Select(s => s.SensorName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (sensors > 0) return (Math.Clamp(sensors, 1, max), FanCountSource.Sensor);
        return (form == GpuForm.Desktop ? 2 : 1, FanCountSource.Default);
    }

    private static ComponentModelSpec Memory(SystemSnapshot snapshot, ComponentAssessment a)
    {
        var modules = snapshot.Memory.Modules;
        var first = modules.FirstOrDefault();
        var type = first?.MemoryType ?? snapshot.Upgrade.MemorySlots.FirstOrDefault(s => s.IsOccupied)?.MemoryType ?? "DDR4";
        var formText = first?.FormFactor ?? string.Empty;

        var form = snapshot.Upgrade.MemoryIsSoldered || type.StartsWith("LP", StringComparison.OrdinalIgnoreCase)
            ? RamForm.Soldered
            : formText.Contains("SODIMM", StringComparison.OrdinalIgnoreCase) || (formText.Length == 0 && snapshot.IsLaptop)
                ? RamForm.SoDimm
                : RamForm.Dimm;

        var generation = type.Contains('5') ? 5 : type.Contains('3') ? 3 : 4;
        var gbPerModule = first is null ? 8 : first.CapacityBytes / 1024.0 / 1024 / 1024;

        // Thanh DDR5 16 GB thường chỉ một mặt chip; DDR4 từ 16 GB đã phải dùng hai mặt.
        var doubleSided = generation >= 5 ? gbPerModule >= 32 : gbPerModule >= 16;

        return new ComponentModelSpec(ComponentKind.Memory)
        {
            RamForm = form,
            DdrGeneration = generation,
            ModuleCount = Math.Clamp(modules.Count, 1, 4),
            ChipsPerSide = form == RamForm.SoDimm ? 4 : 8,
            DoubleSided = doubleSided,
            RamChips = FromSeverity(a.Findings.Where(f => f.Severity >= Severity.Warning))
        };
    }

    private static ComponentModelSpec Storage(SystemSnapshot snapshot)
    {
        // Ổ mang hệ điều hành thường là ổ số 0 — là ổ người mua quan tâm nhất.
        var drive = snapshot.StorageDevices.OrderBy(d => d.DeviceIndex).FirstOrDefault();
        if (drive is null) return new ComponentModelSpec(ComponentKind.Storage);

        var isHdd = drive.MediaType.Contains("HDD", StringComparison.OrdinalIgnoreCase) || drive.SpindleSpeedRpm > 0;
        var isNvme = drive.BusType.Contains("NVMe", StringComparison.OrdinalIgnoreCase);

        var form = isHdd ? (snapshot.IsLaptop ? DriveForm.Hdd25 : DriveForm.Hdd35)
                 : isNvme ? DriveForm.M2
                 : DriveForm.Ssd25;

        var gb = drive.CapacityBytes / 1e9;
        var defects = (drive.ReallocatedSectors ?? 0) + (drive.PendingSectors ?? 0) +
                      (drive.UncorrectableSectors ?? 0) + (drive.MediaErrors ?? 0);
        var media = drive.SmartPredictFailure || defects > 0 || drive.PercentageUsed >= 100 ? Highlight.Fault
                  : drive.PercentageUsed >= 80 || drive.RemainingLifePercent is < 20 ? Highlight.Warn
                  : Highlight.None;

        return new ComponentModelSpec(ComponentKind.Storage)
        {
            DriveForm = form,
            NandPackages = gb <= 600 ? 1 : 2,
            DriveMedia = media
        };
    }

    private static ComponentModelSpec Battery(SystemSnapshot snapshot)
    {
        var battery = snapshot.PrimaryBattery;

        // Cell lithium danh định 3,7–3,85 V: 11,1–11,55 V là 3 cell, 15,2–15,4 V là 4 cell.
        var cells = battery?.VoltageV is > 5 and var v ? (int)Math.Round(v / 3.8) : 3;

        var health = battery is { DesignCapacityMwh: > 0 }
            ? battery.FullChargeCapacityMwh / (double)battery.DesignCapacityMwh
            : 1;

        return new ComponentModelSpec(ComponentKind.Battery)
        {
            SeriesCells = Math.Clamp(cells, 2, 6),
            BatteryCells = health < 0.6 ? Highlight.Fault : health < 0.8 ? Highlight.Warn : Highlight.None
        };
    }

    private static ComponentModelSpec Display(SystemSnapshot snapshot)
    {
        var display = snapshot.Displays.FirstOrDefault(d => d.IsPrimary) ?? snapshot.Displays.FirstOrDefault();
        var (w, h) = display is { NativeWidthPx: > 0, NativeHeightPx: > 0 }
            ? (display.NativeWidthPx!.Value, display.NativeHeightPx!.Value)
            : (display?.WidthPx ?? 16, display?.HeightPx ?? 9);
        var aspect = h > 0 ? Math.Clamp(w / (double)h, 1.2, 3.6) : 16.0 / 9;

        return new ComponentModelSpec(ComponentKind.Display)
        {
            PanelForm = display is { IsInternal: true } ? PanelForm.LaptopLid : PanelForm.Monitor,
            Aspect = aspect
        };
    }

    private static ComponentModelSpec Board(SystemSnapshot snapshot)
    {
        var up = snapshot.Upgrade;

        var slots = up.MemorySlots.Count > 0
            ? string.Concat(up.MemorySlots.Take(8).Select(s => s.IsOccupied ? '1' : '0'))
            : string.Concat(Enumerable.Range(0, snapshot.IsLaptop ? 2 : 4)
                .Select(i => i < snapshot.Memory.Modules.Count ? '1' : '0'));
        if (up.MemoryIsSoldered && up.MemorySlots.Count == 0) slots = string.Empty;

        var pcie = up.ExpansionSlots.Where(s => s.Kind == ExpansionSlotKind.PciExpress).ToList();
        var m2 = up.ExpansionSlots.Count(s => s.Kind is ExpansionSlotKind.M2KeyM or ExpansionSlotKind.M2KeyB);

        return new ComponentModelSpec(ComponentKind.Motherboard)
        {
            BoardForm = snapshot.IsLaptop ? BoardForm.Laptop : BoardForm.Atx,
            DimmSlots = slots,
            PcieX16Slots = up.DataAvailable && pcie.Count > 0 ? Math.Clamp(pcie.Count(s => s.PcieLanes >= 8), 0, 3) : 1,
            PcieSmallSlots = up.DataAvailable && pcie.Count > 0 ? Math.Clamp(pcie.Count(s => s.PcieLanes is null or < 8), 0, 3) : 2,
            M2Slots = up.DataAvailable && m2 > 0 ? Math.Clamp(m2, 1, 4) : 1
        };
    }

    // ------------------------------------------------------------------

    private static Highlight FromFindings(ComponentAssessment a, params string[] codePrefixes) =>
        FromSeverity(a.Findings.Where(f => codePrefixes.Any(p => f.Code.StartsWith(p, StringComparison.Ordinal))));

    private static Highlight FromSeverity(IEnumerable<Finding> findings)
    {
        var worst = findings.Select(f => f.Severity).DefaultIfEmpty(Severity.Info).Max();
        return worst switch
        {
            Severity.Critical => Highlight.Fault,
            Severity.Warning => Highlight.Warn,
            _ => Highlight.None
        };
    }

    private static bool IsGpuSensor(SensorStats s) =>
        s.HardwareName.Contains("GPU", StringComparison.OrdinalIgnoreCase) ||
        s.HardwareName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
        s.HardwareName.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
        s.HardwareName.Contains("Intel Arc", StringComparison.OrdinalIgnoreCase);
}
