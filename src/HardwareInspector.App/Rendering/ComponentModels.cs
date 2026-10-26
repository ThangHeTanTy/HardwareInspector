using HardwareInspector.Core.Models;

namespace HardwareInspector.App.Rendering;

/// <summary>
/// Mô hình 3D dựng bằng code cho từng linh kiện, theo thông số suy ra từ máy thật
/// (<see cref="ComponentModelSpec"/>).
///
/// Không dùng file mô hình có sẵn: ứng dụng phải chạy được offline, không mang theo
/// tài nguyên nặng, và không có kho mô hình nào tra được theo mã linh kiện. Thay vào đó
/// mỗi linh kiện được ghép từ khối hộp và trụ theo kích thước chuẩn thật (đơn vị cm):
/// đế CPU theo socket, thanh RAM theo JEDEC, ổ theo chuẩn M.2/2,5"/3,5", card đồ hoạ
/// dài theo số quạt. Cuối cùng mô hình được co giãn để vừa khung nhìn.
///
/// Phần này cố ý không phụ thuộc WPF — chỉ sinh đỉnh, pháp tuyến và chỉ số tam giác,
/// nên kiểm tra được ở bất kỳ đâu.
/// </summary>
public static class ComponentModels
{
    /// <summary>Bán kính khối cầu bao quanh mô hình sau khi chuẩn hoá.</summary>
    public const double FitRadius = 2.6;

    /// <summary>Mã vật liệu dành riêng cho bệ đỡ, được tô theo màu đánh giá của linh kiện.</summary>
    public const string RatingMaterial = "rating";

    public static IReadOnlyDictionary<string, MaterialSpec> Materials { get; } =
        new Dictionary<string, MaterialSpec>
        {
            ["pcb_green"] = new("#1F6B45", 20),
            ["pcb_dark"] = new("#1B2430", 20),
            ["metal"] = new("#C9CED6", 60),
            ["metal_dark"] = new("#4A505B", 40),
            ["plastic"] = new("#2C313A", 10),
            ["plastic_dark"] = new("#16191F", 10),
            ["plastic_white"] = new("#E4E6EA", 10),
            ["fan_blade"] = new("#3A4049", 5),
            ["gold"] = new("#D4A843", 70),
            ["chip"] = new("#101216", 30),
            ["die"] = new("#3A3F48", 90),
            ["smd"] = new("#B08B5A", 10),
            ["slot_grey"] = new("#7D838D", 10),
            ["cap"] = new("#2A2F38", 40),
            ["copper"] = new("#B87333", 60),
            ["pouch"] = new("#9EA7B3", 40),
            ["pouch_seal"] = new("#C7CED6", 20),
            ["key"] = new("#22262D", 10),
            ["platter"] = new("#DDE2E8", 120),
            ["label"] = new("#DCDFE4", 5),
            ["usb_blue"] = new("#2D6CDF", 20),
            ["port_blue"] = new("#2E5FB8", 20),
            ["ps2_purple"] = new("#6E4AA8", 20),
            ["jack_green"] = new("#3FAE49", 20),
            ["jack_pink"] = new("#E58BB3", 20),
            ["jack_blue"] = new("#3F7FD6", 20),
            ["jack_orange"] = new("#E69A3A", 20),
            ["led_green"] = new("#2FBF71", 10, Emissive: true),
            ["led_orange"] = new("#F4A259", 10, Emissive: true),
            ["cable"] = new("#C83A3A", 10),
            ["cable_dark"] = new("#2A2E35", 10),
            ["rating_good"] = new("#2FBF71", 20, Emissive: true),
            ["rating_fair"] = new("#8AC926", 20, Emissive: true),
            ["rating_average"] = new("#F4A259", 20, Emissive: true),
            ["rating_poor"] = new("#E5544B", 20, Emissive: true),
            ["rating_unknown"] = new("#8A8F98", 20, Emissive: true),
            ["accent"] = new("#4C8DFF", 30, Emissive: true),
            ["screen"] = new("#1D3A66", 80, Emissive: true),
            ["warn"] = new("#F4A259", 30, Emissive: true),
            ["fault"] = new("#E5544B", 30, Emissive: true),
            [RatingMaterial] = new("#8A8F98", 0, Emissive: true)
        };

    /// <summary>Dựng mô hình mặc định khi chưa có dữ liệu máy.</summary>
    public static IReadOnlyList<Surface> Build(ComponentKind kind) => Build(new ComponentModelSpec(kind));

    public static IReadOnlyList<Surface> Build(ComponentModelSpec spec)
    {
        var b = new ModelBuilder();
        switch (spec.Kind)
        {
            case ComponentKind.Cpu: Cpu(b, spec); break;
            case ComponentKind.Gpu: Gpu(b, spec); break;
            case ComponentKind.Memory: Ram(b, spec); break;
            case ComponentKind.Storage: Drive(b, spec); break;
            case ComponentKind.Battery: Battery(b, spec); break;
            case ComponentKind.Display: Display(b, spec); break;
            case ComponentKind.Motherboard: Board(b, spec); break;
            case ComponentKind.Firmware: BiosChip(b, spec); break;
            case ComponentKind.Ownership: Padlock(b, spec); break;
            case ComponentKind.Cooling: Fan(b, new V3(0, 0.2, 0), 4.5, 9, Highlight.None); break;
            default: b.AddBox("plastic", new V3(0, 0.6, 0), new V3(1.2, 1.2, 1.2)); break;
        }

        b.Normalize(FitRadius);

        // Bệ đỡ thêm sau khi chuẩn hoá để có kích thước như nhau ở mọi linh kiện.
        var floor = b.MinY - 0.12;
        b.AddCylinder("plastic", new V3(0, floor - 0.06, 0), 2.45, 0.08, 72);
        b.AddRing(RatingMaterial, new V3(0, floor - 0.06, 0), 2.56, 2.45, 0.1, 72);

        return b.Surfaces;
    }

    /// <summary>Đổi vật liệu sang màu cảnh báo khi bộ phận đó có vấn đề.</summary>
    private static string M(Highlight h, string normal) => h switch
    {
        Highlight.Fault => "fault",
        Highlight.Warn => "warn",
        _ => normal
    };

    // ------------------------------------------------------------------
    // CPU — kích thước đế và kiểu chân theo socket
    // ------------------------------------------------------------------

    private static void Cpu(ModelBuilder b, ComponentModelSpec s)
    {
        var w = s.CpuWidthMm / 10;
        var d = s.CpuDepthMm / 10;
        var ihs = M(s.CpuDie, "metal");

        if (s.CpuPackage == CpuPackage.Bga)
        {
            // CPU laptop hàn chết trên bo: không có nắp kim loại, lộ khuôn silicon.
            b.AddBox("pcb_green", new V3(0, 0.05, 0), new V3(w, 0.1, d));
            var dieW = s.CpuHasPchDie ? w * 0.42 : w * 0.5;
            b.AddBox(M(s.CpuDie, "die"), new V3(s.CpuHasPchDie ? -w * 0.14 : 0, 0.14, 0), new V3(dieW, 0.08, d * 0.55));
            if (s.CpuHasPchDie)
                b.AddBox("die", new V3(w * 0.27, 0.14, 0), new V3(w * 0.2, 0.08, d * 0.42));
            for (var i = 0; i < 10; i++)
            {
                b.AddBox("smd", new V3(-w * 0.42 + i * w * 0.093, 0.12, d * 0.4), new V3(0.12, 0.05, 0.07));
                b.AddBox("smd", new V3(-w * 0.42 + i * w * 0.093, 0.12, -d * 0.4), new V3(0.12, 0.05, 0.07));
            }
            for (var x = 0; x < 22; x++)
            for (var z = 0; z < 11; z++)
                b.AddBox("metal", new V3(-w * 0.46 + x * w * 0.044, -0.03, -d * 0.44 + z * d * 0.088),
                    new V3(0.06, 0.05, 0.06));
            return;
        }

        b.AddBox("pcb_green", new V3(0, 0.06, 0), new V3(w, 0.12, d));
        b.AddBox("gold", new V3(-w * 0.45, 0.125, -d * 0.45), new V3(0.25, 0.01, 0.25));   // dấu chân số 1

        switch (s.CpuPackage)
        {
            case CpuPackage.IntelLga:
                // Nắp Intel: thân chữ nhật với gờ mở rộng ở hai cạnh dài.
                b.AddBox(ihs, new V3(0, 0.15, 0), new V3(w * 0.92, 0.06, d * 0.62));
                b.AddBox(ihs, new V3(0, 0.26, 0), new V3(w * 0.72, 0.22, d * 0.86));
                // Hai khấc định vị trên mép đế.
                b.AddBox("plastic_dark", new V3(-w * 0.2, 0.06, -d / 2), new V3(0.25, 0.125, 0.2));
                b.AddBox("plastic_dark", new V3(w * 0.2, 0.06, -d / 2), new V3(0.25, 0.125, 0.2));
                LgaPads(b, w, d);
                break;

            case CpuPackage.AmdPga:
                b.AddBox(ihs, new V3(0, 0.27, 0), new V3(w * 0.86, 0.3, d * 0.86));
                // Chân cắm bên dưới, bỏ trống vùng giữa như AM4 thật.
                for (var x = 0; x < 18; x++)
                for (var z = 0; z < 18; z++)
                {
                    if (x is >= 6 and <= 11 && z is >= 6 and <= 11) continue;
                    b.AddBox("gold", new V3(-w * 0.44 + x * w * 0.052, -0.1, -d * 0.44 + z * d * 0.052),
                        new V3(0.04, 0.2, 0.04));
                }
                break;

            case CpuPackage.AmdLga:
                // Nắp AM5 dạng "bạch tuộc": thân vuông giữa, mỗi cạnh hai chân bám xuống đế,
                // khe giữa các chân để lộ tụ SMD.
                b.AddBox(ihs, new V3(0, 0.25, 0), new V3(w * 0.72, 0.26, d * 0.72));
                foreach (var side in new[] { -1, 1 })
                foreach (var off in new[] { -0.2, 0.2 })
                {
                    b.AddBox(ihs, new V3(side * w * 0.42, 0.15, off * d), new V3(w * 0.13, 0.06, d * 0.12));
                    b.AddBox(ihs, new V3(off * w, 0.15, side * d * 0.42), new V3(w * 0.12, 0.06, d * 0.13));
                }
                foreach (var side in new[] { -1, 1 })
                for (var i = 0; i < 4; i++)
                {
                    b.AddBox("smd", new V3(side * w * 0.42, 0.14, -0.1 + i * 0.07), new V3(0.1, 0.04, 0.05));
                    b.AddBox("smd", new V3(-0.1 + i * 0.07, 0.14, side * d * 0.42), new V3(0.05, 0.04, 0.1));
                }
                LgaPads(b, w, d);
                break;
        }
    }

    private static void LgaPads(ModelBuilder b, double w, double d)
    {
        for (var x = 0; x < 16; x++)
        for (var z = 0; z < 16; z++)
            b.AddBox("gold", new V3(-w * 0.42 + x * w * 0.056, -0.005, -d * 0.42 + z * d * 0.056),
                new V3(0.1, 0.01, 0.1));
    }

    // ------------------------------------------------------------------
    // GPU — chiều dài card theo số quạt, số chip VRAM theo dung lượng
    // ------------------------------------------------------------------

    private static void Gpu(ModelBuilder b, ComponentModelSpec s)
    {
        switch (s.GpuForm)
        {
            case GpuForm.LaptopDiscrete: LaptopGpu(b, s); break;
            case GpuForm.Integrated: IntegratedGpu(b, s); break;
            default: DesktopGpu(b, s); break;
        }
    }

    private static void DesktopGpu(ModelBuilder b, ComponentModelSpec s)
    {
        var fans = Math.Clamp(s.FanCount, 1, 3);
        // Chiều dài điển hình: card 1 quạt (ITX) ~17 cm, 2 quạt ~24 cm, 3 quạt ~30 cm.
        var len = fans switch { 1 => 17.0, 2 => 24.0, _ => 30.5 };
        const double height = 11.5;
        var cooler = fans == 3 ? 4.2 : 3.6;
        // Tách lớp: nhấc cụm tản nhiệt lên và đẩy ra sau để nhìn từ trên thấy nhân và chip nhớ.
        var lift = s.Exploded ? 2.5 : 0;
        var back = s.Exploded ? -height * 1.05 : 0;

        b.AddBox("metal_dark", new V3(0.2, -0.3, 0), new V3(len - 0.4, 0.25, height - 0.2));   // backplate
        b.AddBox("pcb_dark", new V3(0.4, 0, 0), new V3(len - 0.8, 0.16, height - 0.8));

        // Chân PCIe x16 dài 89 mm ở mép dưới, cách ke khoảng 3 cm.
        var fingerStart = -len / 2 + 3.2;
        for (var i = 0; i < 30; i++)
            b.AddBox("gold", new V3(fingerStart + i * 0.29, 0, -(height - 0.8) / 2 - 0.3), new V3(0.18, 0.12, 0.6));

        // Ke gắn vỏ máy với các cổng xuất hình.
        b.AddBox("metal", new V3(-len / 2, cooler / 2, -0.2), new V3(0.12, cooler + 0.6, 12));
        for (var i = 0; i < 4; i++)
            b.AddBox("plastic_dark", new V3(-len / 2 - 0.07, 0.6, -3.5 + i * 2), new V3(0.06, 0.8, 1.6));

        // Đầu cấp nguồn phụ ở mép trên.
        b.AddBox("plastic_dark", new V3(len / 2 - 4, 0.5, height / 2 - 0.6), new V3(1.6, 0.9, 0.6));

        if (s.Exploded)
            GpuBoardDetail(b, s, -len / 2 + len * 0.42, 0.3);

        // Khối tản nhiệt: lá nhôm, ống đồng, vỏ nhựa và quạt.
        var finH = cooler - 1.4;
        for (var x = -len / 2 + 1; x <= len / 2 - 0.8; x += 0.35)
            b.AddBox("metal", new V3(x, 0.08 + finH / 2 + lift, back), new V3(0.06, finH, height - 1.6));
        foreach (var z in new[] { -2.0, 0.0, 2.0 })
            b.AddCylinder("copper", new V3(0, 0.6 + lift, z + back), 0.3, len - 3, 20, Axis.X);

        b.AddBox("plastic_dark", new V3(0, cooler - 0.6 + lift, back), new V3(len - 0.6, 1.2, height - 0.4));
        b.AddBox("accent", new V3(0, cooler + 0.01 + lift, height / 2 - 0.5 + back), new V3(len - 1.6, 0.03, 0.15));

        var diameter = fans == 3 ? 8.6 : 9.2;
        var usable = len - 1.6;
        var pitch = usable / fans;
        for (var i = 0; i < fans; i++)
        {
            var cx = -len / 2 + 1.0 + pitch * (i + 0.5);
            Fan(b, new V3(cx, cooler + 0.08 + lift, -0.1 + back), diameter / 2, 9, s.Fans);
        }
    }

    /// <summary>Nhân GPU và các chip VRAM bao quanh, đúng số chip đã suy ra.</summary>
    private static void GpuBoardDetail(ModelBuilder b, ComponentModelSpec s, double cx, double cz)
    {
        b.AddBox("pcb_green", new V3(cx, 0.15, cz), new V3(4.5, 0.15, 4.5));
        b.AddBox(M(s.GpuCore, "die"), new V3(cx, 0.28, cz), new V3(2.2, 0.12, 2.2));

        // Thứ tự lấp vị trí xen kẽ bốn phía để số chip ít vẫn phân bố đều quanh nhân.
        var slots = new List<V3>();
        for (var k = 0; k < 3; k++)
        {
            var o = (k - 1) * 1.6;
            slots.Add(new V3(cx - 3.4, 0, cz + o));
            slots.Add(new V3(cx + 3.4, 0, cz + o));
            slots.Add(new V3(cx + o, 0, cz + 3.2));
            slots.Add(new V3(cx + o, 0, cz - 3.2));
        }
        foreach (var p in slots.Take(Math.Clamp(s.VramChips, 1, 12)))
            b.AddBox(M(s.Vram, "chip"), new V3(p.X, 0.15, p.Z), new V3(1.4, 0.12, 1.2));

        for (var i = 0; i < 6; i++)   // cuộn cảm của mạch nguồn
            b.AddBox("metal_dark", new V3(cx + 6.0 + (i % 3) * 0.9, 0.35, cz - 1 + (i / 3) * 2), new V3(0.7, 0.5, 0.7));
    }

    private static void LaptopGpu(ModelBuilder b, ComponentModelSpec s)
    {
        var fans = Math.Clamp(s.FanCount, 1, 2);
        var boardZ = fans == 2 ? 12.0 : 9.0;
        b.AddBox("pcb_dark", new V3(1, 0, 0), new V3(16, 0.12, boardZ));

        b.AddBox("pcb_green", new V3(-3, 0.12, 0), new V3(3.5, 0.12, 3.5));
        b.AddBox(M(s.GpuCore, "die"), new V3(-3, 0.22, 0), new V3(1.8, 0.1, 1.8));
        var ring = new List<V3>();
        for (var k = 0; k < 2; k++)
        {
            var o = (k - 0.5) * 1.5;
            ring.Add(new V3(-5.8, 0, o)); ring.Add(new V3(-0.2, 0, o));
            ring.Add(new V3(-3 + o, 0, 2.8)); ring.Add(new V3(-3 + o, 0, -2.8));
        }
        foreach (var p in ring.Take(Math.Clamp(s.VramChips, 1, 8)))
            b.AddBox(M(s.Vram, "chip"), new V3(p.X, 0.12, p.Z), new V3(1.2, 0.1, 1.0));

        // Tấm đồng áp lên nhân, ống dẫn nhiệt chạy tới quạt thổi ở mép máy.
        b.AddBox("copper", new V3(-3, 0.5, 0), new V3(3, 0.12, 3));
        for (var i = 0; i < fans; i++)
        {
            var z = fans == 1 ? 0 : (i == 0 ? -2.8 : 2.8);
            b.AddCylinder("copper", new V3(1.2, 0.55, z * 0.5), 0.3, 8.5, 16, Axis.X);
            b.AddBox("plastic_dark", new V3(6.5, 0.45, z), new V3(5, 0.8, 5));
            Fan(b, new V3(6.5, 0.9, z), 2.0, 11, s.Fans);
            for (var f = 0; f < 12; f++)
                b.AddBox("metal", new V3(9.1, 0.45, z - 2.2 + f * 0.4), new V3(0.3, 0.7, 0.05));
        }
    }

    private static void IntegratedGpu(ModelBuilder b, ComponentModelSpec s)
    {
        // Đồ hoạ tích hợp nằm ngay trên khuôn CPU: vẽ khuôn chia hai vùng, vùng GPU sáng lên.
        b.AddBox("pcb_green", new V3(0, 0.05, 0), new V3(4.6, 0.1, 2.5));
        b.AddBox("die", new V3(-0.55, 0.14, 0), new V3(1.6, 0.08, 1.4));
        b.AddBox(M(s.GpuCore, "accent"), new V3(0.75, 0.14, 0), new V3(1.0, 0.08, 1.4));
        for (var i = 0; i < 10; i++)
            b.AddBox("smd", new V3(-1.9 + i * 0.42, 0.12, 1.0), new V3(0.12, 0.05, 0.07));
    }

    private static void Fan(ModelBuilder b, V3 center, double radius, int blades, Highlight highlight)
    {
        b.AddRing("plastic", center, radius, radius - radius * 0.07, 0.25, 48);
        b.AddCylinder("plastic_dark", center + new V3(0, 0.02, 0), radius * 0.3, 0.3, 32);
        for (var i = 0; i < blades; i++)
        {
            var a = i * 2 * Math.PI / blades;
            var r = radius * 0.62;
            b.AddBox(M(highlight, "fan_blade"),
                center + new V3(Math.Cos(a) * r, 0, Math.Sin(a) * r),
                new V3(radius * 0.7, 0.06, radius * 0.27),
                rotX: 0.35, rotY: -a);
        }
    }

    // ------------------------------------------------------------------
    // RAM — DIMM/SO-DIMM theo JEDEC, vị trí khấc theo thế hệ DDR
    // ------------------------------------------------------------------

    private static void Ram(ModelBuilder b, ComponentModelSpec s)
    {
        if (s.RamForm == RamForm.Soldered)
        {
            // RAM hàn chết: các gói LPDDR nằm sát CPU trên bo.
            b.AddBox("pcb_dark", new V3(0, 0, 0), new V3(10, 0.12, 7));
            b.AddBox("pcb_green", new V3(0, 0.11, 0), new V3(4, 0.1, 2.2));
            b.AddBox("die", new V3(0, 0.19, 0), new V3(1.8, 0.07, 1.2));
            foreach (var (x, z) in new[] { (-3.6, -1.6), (-3.6, 1.6), (3.6, -1.6), (3.6, 1.6) })
                b.AddBox(M(s.RamChips, "chip"), new V3(x, 0.13, z), new V3(1.4, 0.14, 1.2));
            return;
        }

        var isDimm = s.RamForm == RamForm.Dimm;
        var len = isDimm ? 13.335 : 6.96;
        var height = isDimm ? 3.125 : 3.0;

        // Vị trí khấc tính theo tỉ lệ chiều dài thanh (gần đúng theo bản vẽ JEDEC).
        var notch = (isDimm, s.DdrGeneration) switch
        {
            (true, 3) => 0.41,
            (true, 5) => 0.507,
            (true, _) => 0.537,
            (false, 3) => 0.36,
            (false, 5) => 0.5,
            _ => 0.47
        };

        var count = Math.Clamp(s.ModuleCount, 1, 4);
        for (var m = 0; m < count; m++)
        {
            // Giãn cách rộng hơn khe thật để thanh phía sau không bị che hẳn.
            var z = (m - (count - 1) / 2.0) * 2.6;
            b.AddBox("pcb_green", new V3(0, height / 2, z), new V3(len, height, 0.12));

            foreach (var side in s.DoubleSided ? new[] { 1, -1 } : new[] { 1 })
            {
                var chips = s.ChipsPerSide;
                var usable = len * 0.86;
                var gap = s.DdrGeneration >= 5 && isDimm ? 1.4 : 0;   // DDR5 có PMIC ở giữa
                var step = (usable - gap) / chips;
                for (var c = 0; c < chips; c++)
                {
                    var x = -usable / 2 + step * (c + 0.5) + (c >= chips / 2 ? gap : 0);
                    b.AddBox(M(s.RamChips, "chip"), new V3(x, height * 0.56, z + side * 0.11),
                        new V3(Math.Min(0.95, step * 0.8), 1.1, 0.1));
                }
            }

            if (s.DdrGeneration >= 5)
            {
                b.AddBox("chip", new V3(0, height * 0.62, z + 0.1), new V3(0.5, 0.5, 0.08));   // PMIC
                b.AddBox("chip", new V3(0, height * 0.3, z + 0.1), new V3(0.3, 0.3, 0.06));    // SPD hub
            }
            else
            {
                b.AddBox("chip", new V3(len * 0.3, height * 0.2, z + 0.09), new V3(0.3, 0.25, 0.05)); // SPD
            }

            if (m == count - 1)
                b.AddBox("label", new V3(-len * 0.2, height * 0.88, z + 0.07), new V3(len * 0.35, 0.3, 0.01));

            var pins = isDimm ? 72 : 50;
            var notchX = -len / 2 + len * notch;
            for (var p = 0; p < pins; p++)
            {
                var x = -len / 2 + 0.4 + p * (len - 0.8) / (pins - 1);
                if (Math.Abs(x - notchX) < 0.12) continue;
                b.AddBox("gold", new V3(x, 0.2, z + 0.065), new V3(0.06, 0.35, 0.012));
                b.AddBox("gold", new V3(x, 0.2, z - 0.065), new V3(0.06, 0.35, 0.012));
            }
            b.AddBox("plastic_dark", new V3(notchX, 0.18, z), new V3(0.15, 0.38, 0.14));   // khấc
        }
    }

    // ------------------------------------------------------------------
    // Ổ lưu trữ — M.2 2280, SSD 2,5", HDD 2,5"/3,5"
    // ------------------------------------------------------------------

    private static void Drive(ModelBuilder b, ComponentModelSpec s)
    {
        switch (s.DriveForm)
        {
            case DriveForm.M2: M2(b, s); break;
            case DriveForm.Ssd25: Ssd25(b, s); break;
            default: Hdd(b, s, s.DriveForm == DriveForm.Hdd35); break;
        }
    }

    private static void M2(ModelBuilder b, ComponentModelSpec s)
    {
        b.AddBox("pcb_dark", new V3(0, 0, 0), new V3(8.0, 0.08, 2.2));
        b.AddBox("chip", new V3(-2.4, 0.09, 0), new V3(1.0, 0.1, 1.0));       // controller
        b.AddBox("chip", new V3(-1.2, 0.08, 0), new V3(0.6, 0.08, 0.8));      // DRAM cache
        for (var i = 0; i < Math.Clamp(s.NandPackages, 1, 2); i++)
            b.AddBox(M(s.DriveMedia, "chip"), new V3(0.4 + i * 1.9, 0.09, 0), new V3(1.4, 0.1, 1.6));

        // Có lỗi thì bỏ tem để thấy chip NAND đổi màu.
        if (s.DriveMedia == Highlight.None)
            b.AddBox("label", new V3(0.7, 0.15, 0), new V3(5.4, 0.01, 1.9));

        for (var i = 0; i < 26; i++)
        {
            var z = -0.95 + i * 0.075;
            if (Math.Abs(z - 0.4) < 0.08) continue;   // khấc M-key
            b.AddBox("gold", new V3(-3.8, 0, z), new V3(0.4, 0.09, 0.045));
        }
        b.AddRing("metal", new V3(4.0, 0, 0), 0.32, 0.18, 0.09, 24);
    }

    private static void Ssd25(ModelBuilder b, ComponentModelSpec s)
    {
        if (s.Exploded)
        {
            b.AddBox("metal_dark", new V3(0, 0.25, 0), new V3(10, 0.5, 7));
            b.AddBox("pcb_dark", new V3(-0.5, 0.54, 0), new V3(7.5, 0.08, 5.6));
            b.AddBox("chip", new V3(-3, 0.63, 0), new V3(1.2, 0.1, 1.2));
            for (var i = 0; i < 4; i++)
                b.AddBox(M(s.DriveMedia, "chip"), new V3(-1 + (i % 2) * 2.2, 0.63, -1.3 + (i / 2) * 2.6), new V3(1.6, 0.1, 1.6));
            b.AddBox("metal", new V3(0, 1.5, -7.4), new V3(10, 0.12, 7));
            b.AddBox("label", new V3(0.5, 1.57, -7.4), new V3(6, 0.01, 4.5));
        }
        else
        {
            b.AddBox("metal", new V3(0, 0.35, 0), new V3(10, 0.7, 7));
            b.AddBox("label", new V3(0.5, 0.705, 0), new V3(6, 0.01, 4.5));
        }
        SataConnectors(b, -5.0, 0.35, 0.5);
    }

    private static void Hdd(ModelBuilder b, ComponentModelSpec s, bool large)
    {
        var (len, thick, width, platter) = large ? (14.7, 2.6, 10.2, 4.75) : (10.0, 0.95, 7.0, 3.25);
        var baseH = thick - 0.15;

        b.AddBox("metal_dark", new V3(0, baseH / 2, 0), new V3(len, baseH, width));
        b.AddBox("pcb_green", new V3(-len * 0.1, -0.08, 0), new V3(len * 0.75, 0.12, width * 0.85));
        SataConnectors(b, -len / 2, baseH * 0.35, Math.Min(0.6, baseH * 0.5));

        if (s.Exploded)
        {
            // Nhấc nắp để thấy mặt đĩa: phần có sector hỏng đổi màu.
            var px = len * 0.15;
            b.AddCylinder(M(s.DriveMedia, "platter"), new V3(px, baseH + 0.06, 0), platter, 0.12, 64);
            b.AddCylinder("metal", new V3(px, baseH + 0.15, 0), platter * 0.25, 0.2, 32);
            var pivot = new V3(-len * 0.38, baseH + 0.2, width * 0.32);
            var target = new V3(px - platter * 0.5, baseH + 0.2, 0);
            var dir = target - pivot;
            var armLen = dir.Length;
            b.AddBox("metal", (pivot + target) * 0.5, new V3(armLen, 0.08, 0.5),
                rotY: -Math.Atan2(dir.Z, dir.X));
            b.AddCylinder("metal_dark", pivot, 0.6, 0.4, 24);
            // Nắp đặt nghiêng ra phía sau để không che mặt đĩa.
            b.AddBox("metal", new V3(0, thick + 1.5, -width * 1.05), new V3(len - 0.3, 0.15, width - 0.3));
        }
        else
        {
            b.AddBox("metal", new V3(0, baseH + 0.075, 0), new V3(len - 0.3, 0.15, width - 0.3));
            b.AddBox("label", new V3(len * 0.08, baseH + 0.155, 0), new V3(len * 0.55, 0.01, width * 0.6));
            foreach (var (x, z) in new[] { (-0.45, -0.42), (-0.45, 0.42), (0.45, -0.42), (0.45, 0.42) })
                b.AddCylinder("metal_dark", new V3(x * len, baseH + 0.16, z * width), 0.18, 0.04, 12);
        }
    }

    private static void SataConnectors(ModelBuilder b, double edgeX, double y, double h)
    {
        b.AddBox("plastic_dark", new V3(edgeX - 0.15, y, -1.6), new V3(0.3, h, 1.4));   // dữ liệu 7 chân
        b.AddBox("plastic_dark", new V3(edgeX - 0.15, y, 1.0), new V3(0.3, h, 2.2));    // nguồn 15 chân
        for (var i = 0; i < 7; i++)
            b.AddBox("gold", new V3(edgeX - 0.31, y, -2.1 + i * 0.16), new V3(0.02, h * 0.4, 0.06));
        for (var i = 0; i < 15; i++)
            b.AddBox("gold", new V3(edgeX - 0.31, y, 0.05 + i * 0.13), new V3(0.02, h * 0.4, 0.05));
    }

    // ------------------------------------------------------------------
    // Pin — số cell nối tiếp suy từ điện áp
    // ------------------------------------------------------------------

    private static void Battery(ModelBuilder b, ComponentModelSpec s)
    {
        var cells = Math.Clamp(s.SeriesCells, 2, 6);
        const double cellW = 1.9, pitch = 2.1, cellD = 7.5;
        var trayW = cells * pitch + 0.6;

        b.AddBox("plastic_dark", new V3(0, 0.15, 0), new V3(trayW, 0.3, cellD + 0.6));
        for (var i = 0; i < cells; i++)
        {
            var x = -trayW / 2 + 0.3 + pitch * (i + 0.5);
            b.AddBox(M(s.BatteryCells, "pouch"), new V3(x, 0.575, -0.2), new V3(cellW, 0.55, cellD));
            b.AddBox("pouch_seal", new V3(x, 0.4, -0.2 - cellD / 2 - 0.15), new V3(cellW, 0.06, 0.3));
            b.AddBox("metal", new V3(x - 0.4, 0.4, cellD / 2 - 0.05), new V3(0.4, 0.04, 0.5));   // tab cực
            b.AddBox("metal", new V3(x + 0.4, 0.4, cellD / 2 - 0.05), new V3(0.4, 0.04, 0.5));
        }

        // Mạch bảo vệ (BMS) và đầu cắm.
        b.AddBox("pcb_green", new V3(0, 0.38, cellD / 2 + 0.4), new V3(trayW - 0.6, 0.08, 0.9));
        for (var i = 0; i < 4; i++)
            b.AddBox("chip", new V3(-trayW / 3 + i * trayW / 5, 0.45, cellD / 2 + 0.4), new V3(0.4, 0.06, 0.4));
        b.AddBox("plastic_dark", new V3(trayW / 2 + 1.2, 0.3, cellD / 2 + 0.4), new V3(2.4, 0.12, 0.5));
        b.AddBox("plastic_white", new V3(trayW / 2 + 2.6, 0.3, cellD / 2 + 0.4), new V3(0.6, 0.3, 1.0));
    }

    // ------------------------------------------------------------------
    // Màn hình — màn rời có chân đế hoặc nắp laptop, đúng tỉ lệ khung hình
    // ------------------------------------------------------------------

    private static void Display(ModelBuilder b, ComponentModelSpec s)
    {
        var aspect = Math.Clamp(s.Aspect, 1.2, 3.6);
        if (s.PanelForm == PanelForm.LaptopLid) Laptop(b, aspect);
        else Monitor(b, aspect);
    }

    private static void Monitor(ModelBuilder b, double aspect)
    {
        const double h = 30;
        var w = h * aspect;
        var y = 13 + h / 2;

        b.AddBox("plastic_dark", new V3(0, y, 0), new V3(w + 1.2, h + 1.2, 1.4));
        b.AddBox("screen", new V3(0, y + 0.2, 0.71), new V3(w, h - 0.4, 0.02));
        b.AddBox("plastic", new V3(0, y - 1, -1.6), new V3(w * 0.4, h * 0.5, 2));
        b.AddBox("metal_dark", new V3(0, 8, -3.2), new V3(5, 16, 2));
        b.AddBox("metal_dark", new V3(0, 0.6, -1.5), new V3(Math.Min(w * 0.45, 26), 1.2, 18));
        b.AddBox("accent", new V3(w / 2 - 2, y - h / 2 - 0.3, 0.72), new V3(0.6, 0.25, 0.02));   // đèn nguồn
    }

    private static void Laptop(ModelBuilder b, double aspect)
    {
        const double w = 32;
        var screenH = (w - 1.4) / aspect;
        var lidH = screenH + 2.2;
        var deckD = Math.Max(lidH, 21);

        // Thân máy với bàn phím và touchpad.
        b.AddBox("plastic", new V3(0, 0.8, 0), new V3(w, 1.6, deckD));
        for (var row = 0; row < 5; row++)
        for (var col = 0; col < 14; col++)
            b.AddBox("key", new V3(-w * 0.42 + col * w * 0.0646, 1.68, -deckD * 0.3 + row * 1.9), new V3(1.6, 0.16, 1.6));
        b.AddBox("key", new V3(0, 1.68, -deckD * 0.3 + 5 * 1.9), new V3(w * 0.4, 0.16, 1.6));   // phím cách
        b.AddBox("plastic_dark", new V3(0, 1.62, deckD * 0.3), new V3(w * 0.32, 0.05, deckD * 0.25));

        // Nắp màn hình mở ~105°, xoay quanh bản lề ở mép sau.
        const double tilt = -0.26;
        var hinge = new V3(0, 1.6, -deckD / 2);
        var up = new V3(0, Math.Cos(tilt), Math.Sin(tilt));
        var front = new V3(0, -Math.Sin(tilt), Math.Cos(tilt));
        var center = hinge + up * (lidH / 2) + front * 0.35;
        b.AddBox("plastic", center, new V3(w, lidH, 0.6), rotX: tilt);
        b.AddBox("screen", center + front * 0.31 + up * 0.3, new V3(w - 1.4, screenH, 0.02), rotX: tilt);
        b.AddCylinder("metal_dark", hinge + front * 0.3, 0.45, w * 0.7, 24, Axis.X);
    }

    // ------------------------------------------------------------------
    // Bo mạch — đúng số khe RAM/PCIe/M.2, cổng mặt bên và đầu cắm trên bo
    // ------------------------------------------------------------------

    /// <param name="drawDrives">
    /// Sai khi vẽ trong cảnh lắp ráp: khi đó ổ M.2 thật được đặt vào khe riêng,
    /// bo mạch không vẽ ổ hay tấm tản nhiệt che lên khe đang có ổ nữa.
    /// </param>
    private static void Board(ModelBuilder b, ComponentModelSpec s, bool drawDrives = true)
    {
        if (s.BoardForm == BoardForm.Laptop) LaptopBoard(b, s, drawDrives);
        else AtxBoard(b, s, drawDrives);
    }

    private static void AtxBoard(ModelBuilder b, ComponentModelSpec s, bool drawDrives)
    {
        // ATX 30,5 × 24,4 cm: trục X là chiều ngang, trục Z là chiều dọc, cụm cổng I/O ở mép trái phía trên.
        b.AddBox("pcb_dark", new V3(0, 0, 0), new V3(24.4, 0.16, 30.5));

        // Socket CPU với khung giữ và vùng chân.
        b.AddBox("metal", new V3(-2, 0.25, -7.5), new V3(5.6, 0.35, 5.6));
        b.AddBox("plastic_dark", new V3(-2, 0.32, -7.5), new V3(4.2, 0.3, 4.2));
        b.AddBox("metal_dark", new V3(-7.6, 1.3, -7.5), new V3(2.2, 2.5, 7));    // tản nhiệt VRM
        b.AddBox("metal_dark", new V3(-2, 1.3, -12.2), new V3(7, 2.5, 2.2));
        for (var i = 0; i < 6; i++)
            b.AddCylinder("cap", new V3(-5.4, 0.55, -10 + i * 1.0), 0.32, 0.8, 16);

        // Cụm cổng mặt sau: khối che chắn I/O với từng cổng đọc được từ SMBIOS.
        b.AddBox("metal", new V3(-11.4, 1.8, -7.6), new V3(1.6, 3.5, 15));
        RearPanel(b, s.RearPorts, faceX: -12.2, zFrom: -14.9, zTo: -0.3, yFrom: 0.25, yTo: 3.45);
        b.AddBox("plastic_dark", new V3(-9, 0.6, -14.2), new V3(2.2, 1.0, 1.1));  // nguồn CPU 8 pin

        // Khe RAM: đúng số khe và khe nào đang có thanh.
        var slots = s.DimmSlots;
        var ramChip = M(s.BoardRamChips, "chip");
        for (var i = 0; i < slots.Length; i++)
        {
            var x = 4.6 + i * 0.95;
            b.AddBox(i % 2 == 0 ? "plastic_dark" : "slot_grey", new V3(x, 0.5, -6.5), new V3(0.75, 0.9, 14.0));
            b.AddBox("plastic_white", new V3(x, 0.75, -13.7), new V3(0.8, 0.5, 0.35));   // lẫy khoá
            if (slots[i] != '1') continue;
            b.AddBox("pcb_green", new V3(x, 2.4, -6.5), new V3(0.12, 3.1, 13.3));
            for (var c = 0; c < 8; c++)
                b.AddBox(ramChip, new V3(x + 0.11, 2.6, -12 + c * 1.55), new V3(0.1, 1.1, 0.95));
        }

        // Khe PCIe: x16 dài có ốp kim loại, khe nhỏ x1/x4 ngắn; khoảng cách theo bước khe 20,32 mm.
        var z = 1.0;
        for (var i = 0; i < s.PcieX16Slots && z < 14; i++, z += 4.06)
        {
            b.AddBox("plastic_dark", new V3(-6.25, 0.5, z), new V3(8.9, 0.9, 0.75));
            b.AddBox("metal", new V3(-6.25, 0.97, z), new V3(8.95, 0.06, 0.85));
            b.AddBox("plastic_white", new V3(-1.6, 0.7, z), new V3(0.4, 0.5, 0.8));        // lẫy giữ card
        }
        for (var i = 0; i < s.PcieSmallSlots && z < 14; i++, z += 2.03)
            b.AddBox("plastic_dark", new V3(-9.45, 0.5, z), new V3(2.5, 0.9, 0.75));

        // Khe M.2: chân cắm, cọc vít, và tấm tản nhiệt che ổ đang lắp.
        for (var i = 0; i < s.M2Slots; i++)
        {
            var mz = -1.5 + i * 4.06;
            b.AddBox("plastic_dark", new V3(-1.7, 0.25, mz), new V3(0.5, 0.35, 2.3));
            b.AddCylinder("metal", new V3(6.4, 0.2, mz), 0.25, 0.3, 16);
            if (drawDrives && i < s.M2Occupied)
                b.AddBox("metal_dark", new V3(2.5, 0.45, mz), new V3(8, 0.35, 2.2));
        }

        b.AddBox("metal", new V3(8.5, 0.4, 10.5), new V3(3.5, 0.6, 3.5));          // tản chipset
        b.AddBox("accent", new V3(8.5, 0.71, 10.5), new V3(2, 0.02, 0.2));
        b.AddBox("plastic_white", new V3(11.5, 0.9, -3), new V3(1.4, 1.6, 5.2));   // nguồn 24 pin

        // Cổng SATA ở mép phải, xếp chồng từng cặp như trên bo thật.
        for (var i = 0; i < s.SataPorts; i++)
            b.AddBox("plastic_dark", new V3(11.6, 0.5 + (i % 2) * 0.85, 5 + (i / 2) * 1.15), new V3(1.0, 0.8, 0.9));

        // Đầu cắm quạt 4 chân: gần socket, mép trên và mép phải.
        var fanSpots = new[] { (-2.0, -14.4), (3.0, -14.4), (11.6, -9.0), (11.6, 2.5), (6.0, 14.6), (-4.0, 14.6), (-9.0, 9.5), (9.5, 14.6) };
        foreach (var (fx, fz) in fanSpots.Take(Math.Clamp(s.FanHeaders, 0, fanSpots.Length)))
        {
            b.AddBox("plastic_white", new V3(fx, 0.4, fz), new V3(0.95, 0.6, 0.3));
            for (var p = 0; p < 4; p++)
                b.AddBox("gold", new V3(fx - 0.33 + p * 0.22, 0.75, fz), new V3(0.05, 0.12, 0.05));
        }

        // Header USB trên bo ở mép dưới.
        for (var i = 0; i < s.UsbHeaders; i++)
            b.AddBox("plastic_dark", new V3(-1 + i * 2.2, 0.4, 14.6), new V3(1.2, 0.6, 0.55));

        b.AddCylinder("metal", new V3(2, 0.2, 12), 1.0, 0.3, 32);                    // pin CMOS
        b.AddBox("chip", new V3(5.5, 0.17, 12.6), new V3(0.6, 0.18, 0.5));          // chip BIOS
    }

    private static void LaptopBoard(ModelBuilder b, ComponentModelSpec s, bool drawDrives)
    {
        b.AddBox("pcb_dark", new V3(0, 0, 0), new V3(28, 0.12, 17));

        b.AddBox("pcb_green", new V3(-3, 0.11, -2), new V3(5, 0.1, 2.5));         // CPU BGA
        b.AddBox("die", new V3(-3.6, 0.19, -2), new V3(2.1, 0.07, 1.4));
        b.AddBox("copper", new V3(-3, 0.5, -2), new V3(4, 0.12, 3));
        b.AddCylinder("copper", new V3(3.5, 0.55, -2), 0.3, 13, 16, Axis.X);
        b.AddBox("plastic_dark", new V3(11, 0.45, -3.5), new V3(5.5, 0.8, 5.5));  // quạt thổi
        Fan(b, new V3(11, 0.9, -3.5), 2.2, 11, Highlight.None);

        // Khe SO-DIMM nằm ngang, khe nào đang cắm thì có thanh RAM nằm đè lên.
        var ramChip = M(s.BoardRamChips, "chip");
        for (var i = 0; i < s.DimmSlots.Length; i++)
        {
            var z = 3.0 + i * 3.4;
            b.AddBox("plastic_dark", new V3(4.5, 0.3, z - 1.2), new V3(7.5, 0.5, 0.8));
            if (s.DimmSlots[i] != '1') continue;
            b.AddBox("pcb_green", new V3(4.5, 0.62, z), new V3(6.96, 0.1, 3.0));
            for (var c = 0; c < 4; c++)
                b.AddBox(ramChip, new V3(2.2 + c * 1.55, 0.72, z + 0.3), new V3(0.95, 0.1, 1.1));
        }

        for (var i = 0; i < Math.Max(1, s.M2Slots); i++)
        {
            var z = 2.5 + i * 3;
            b.AddBox("plastic_dark", new V3(-11, 0.25, z), new V3(0.6, 0.4, 2.2));
            b.AddCylinder("metal", new V3(-2.7, 0.15, z), 0.22, 0.2, 16);
            if (!drawDrives || i >= s.M2Occupied) continue;
            b.AddBox("pcb_dark", new V3(-6.8, 0.3, z), new V3(8, 0.08, 2.2));
            b.AddBox("label", new V3(-6.5, 0.36, z), new V3(5.4, 0.01, 1.9));
        }

        // Cổng hai bên thân máy: nửa đầu bên trái, nửa sau bên phải.
        var ports = s.RearPorts;
        var half = (ports.Length + 1) / 2;
        SidePorts(b, ports[..half], faceX: -14, zFrom: -6.5, zTo: 6.5, outward: -1);
        SidePorts(b, ports[half..], faceX: 14, zFrom: 1.5, zTo: 8, outward: 1);
        b.AddBox("plastic_white", new V3(0, 0.3, 7.8), new V3(2.5, 0.4, 1.0));             // đầu cắm pin
    }

    /// <summary>
    /// Xếp các cổng lên mặt sau bo mạch PC thành từng cột như tấm I/O thật: USB xếp chồng
    /// hai, jack âm thanh xếp chồng ba, HDMI chồng lên DisplayPort, cổng lớn đứng riêng.
    /// </summary>
    private static void RearPanel(ModelBuilder b, string codes, double faceX, double zFrom, double zTo,
        double yFrom, double yTo)
    {
        var columns = new List<List<char>>();
        foreach (var c in codes)
        {
            var last = columns.LastOrDefault();
            var group = PortGroup(c);
            if (last is not null && PortGroup(last[0]) == group && last.Count < GroupCapacity(group))
                last.Add(c);
            else
                columns.Add(new List<char> { c });
        }

        const double gap = 0.3;
        var widths = columns.Select(col => col.Max(c => PortSize(c).W)).ToList();
        var total = widths.Sum() + gap * (columns.Count - 1);
        var scale = Math.Min(1, (zTo - zFrom) / Math.Max(total, 0.01));

        var z = zFrom;
        var jacks = 0;
        for (var i = 0; i < columns.Count; i++)
        {
            var w = widths[i] * scale;
            var y = yFrom + 0.2;
            foreach (var c in columns[i])
            {
                var (pw, ph) = PortSize(c);
                if (y + ph * scale > yTo) break;
                Port(b, c, new V3(faceX, y + ph * scale / 2, z + w / 2), scale, outward: -1,
                    jackIndex: c == 'A' ? jacks++ : 0);
                y += ph * scale + 0.25;
            }
            z += w + gap * scale;
        }
    }

    private static void SidePorts(ModelBuilder b, string codes, double faceX, double zFrom, double zTo, int outward)
    {
        if (codes.Length == 0) return;
        var widths = codes.Select(c => PortSize(c).W).ToList();
        var total = widths.Sum() + 0.5 * (codes.Length - 1);
        var scale = Math.Min(1, (zTo - zFrom) / Math.Max(total, 0.01));
        var z = zFrom;
        for (var i = 0; i < codes.Length; i++)
        {
            var (pw, ph) = PortSize(codes[i]);
            Port(b, codes[i], new V3(faceX, 0.1 + ph * scale / 2, z + pw * scale / 2), scale, outward, jackIndex: i);
            z += (pw + 0.5) * scale;
        }
    }

    private static int PortGroup(char c) => c switch
    {
        'U' or 'C' => 1,
        'H' or 'D' => 2,
        'A' => 3,
        'P' => 4,
        _ => 0
    };

    private static int GroupCapacity(int group) => group switch { 1 => 2, 2 => 2, 3 => 3, _ => 1 };

    /// <summary>Kích thước mặt cổng (rộng × cao, cm) theo đầu cắm thật.</summary>
    private static (double W, double H) PortSize(char c) => c switch
    {
        'U' => (1.35, 0.6),
        'C' => (0.95, 0.35),
        'L' => (1.6, 1.4),
        'H' => (1.55, 0.6),
        'D' => (1.7, 0.6),
        'V' => (3.1, 1.2),
        'I' => (3.8, 1.2),
        'S' => (3.0, 1.1),
        'A' => (0.9, 0.9),
        'P' => (1.2, 1.2),
        _ => (1.0, 0.6)
    };

    /// <summary>Một cổng nhìn từ ngoài vào: vỏ kim loại hoặc nhựa, lòng cổng tối, lưỡi tiếp điểm.</summary>
    private static void Port(ModelBuilder b, char c, V3 center, double scale, int outward, int jackIndex = 0)
    {
        var (w, h) = PortSize(c);
        w *= scale; h *= scale;
        var o = outward * 0.06;   // nhô ra ngoài mặt bo một chút
        var face = new V3(center.X + o, center.Y, center.Z);
        var inner = new V3(center.X + outward * 0.12, center.Y, center.Z);

        switch (c)
        {
            case 'A':
                b.AddBox("plastic_dark", face, new V3(0.12, h, w));
                var jackColors = new[] { "jack_green", "jack_pink", "jack_blue", "jack_orange", "plastic", "slot_grey" };
                b.AddCylinder(jackColors[jackIndex % jackColors.Length], inner, h * 0.34, 0.06, 20, Axis.X);
                b.AddCylinder("plastic_dark", inner + new V3(outward * 0.02, 0, 0), h * 0.16, 0.06, 16, Axis.X);
                break;
            case 'P':
                b.AddBox("metal", face, new V3(0.12, h, w));
                b.AddCylinder("ps2_purple", inner, h * 0.4, 0.06, 24, Axis.X);
                break;
            case 'L':
                b.AddBox("metal", face, new V3(0.12, h, w));
                b.AddBox("plastic_dark", inner, new V3(0.04, h * 0.62, w * 0.75));
                b.AddBox("led_green", inner + new V3(0, h * 0.4, -w * 0.32), new V3(0.04, h * 0.1, w * 0.14));
                b.AddBox("led_orange", inner + new V3(0, h * 0.4, w * 0.32), new V3(0.04, h * 0.1, w * 0.14));
                break;
            case 'V':
            case 'S':
            case 'I':
                b.AddBox(c == 'V' ? "port_blue" : c == 'I' ? "plastic_white" : "metal_dark", face, new V3(0.12, h, w));
                b.AddBox("metal", inner, new V3(0.04, h * 0.55, w * 0.7));
                break;
            default:
                // USB-A, USB-C, HDMI, DisplayPort: vỏ kim loại, lòng tối, USB-A có lưỡi nhựa xanh.
                b.AddBox("metal", face, new V3(0.12, h, w));
                b.AddBox("plastic_dark", inner, new V3(0.04, h * 0.7, w * 0.85));
                if (c == 'U')
                    b.AddBox("usb_blue", inner + new V3(outward * 0.01, h * 0.12, 0), new V3(0.04, h * 0.22, w * 0.7));
                break;
        }
    }

    // ------------------------------------------------------------------
    // Cảnh lắp ráp tổng thể
    // ------------------------------------------------------------------

    /// <summary>
    /// Ghép mọi linh kiện đọc được thành một cảnh: CPU trong socket, RAM trong khe, card đồ hoạ
    /// cắm khe PCIe x16, ổ M.2 trong khe M.2, ổ SATA nối cáp về cổng SATA, màn hình nối cáp về
    /// cổng xuất hình. Trên mỗi linh kiện có một cột mốc tô màu theo mức đánh giá.
    ///
    /// Trong cảnh này không tách lớp (card và ổ đứng nguyên khối) để giữ đúng tư thế lắp;
    /// chỗ hỏng chi tiết vẫn xem được ở trang của từng linh kiện.
    /// </summary>
    public static IReadOnlyList<Surface> BuildAssembly(AssemblySpec a)
    {
        var b = new ModelBuilder();
        if (a.IsLaptop || a.Board.BoardForm == BoardForm.Laptop) LaptopAssembly(b, a);
        else DesktopAssembly(b, a);

        b.Normalize(FitRadius);
        var floor = b.MinY - 0.12;
        b.AddCylinder("plastic", new V3(0, floor - 0.06, 0), 2.45, 0.08, 72);
        b.AddRing(RatingMaterial, new V3(0, floor - 0.06, 0), 2.56, 2.45, 0.1, 72);
        return b.Surfaces;
    }

    private static void DesktopAssembly(ModelBuilder b, AssemblySpec a)
    {
        Board(b, a.Board, drawDrives: false);
        Pin(b, a, ComponentKind.Motherboard, new V3(9.5, 0.2, -12.5), 6);

        if (a.Cpu is { } cpu)
        {
            using (b.Place(new V3(-2, 0.47, -7.5))) Cpu(b, cpu);
            Pin(b, a, ComponentKind.Cpu, new V3(-2, 1.0, -7.5), 17);
        }

        var ramX = 4.6 + Math.Max(0, a.Board.DimmSlots.Length - 1) * 0.95 / 2;
        if (a.Board.DimmSlots.Contains('1'))
            Pin(b, a, ComponentKind.Memory, new V3(ramX, 4.0, -6.5), 13);

        // Card đồ hoạ đứng vuông góc với bo, chân PCIe cắm vào khe x16 đầu tiên.
        var videoOut = new List<V3>();
        if (a.Gpu is { GpuForm: GpuForm.Desktop } gpu)
        {
            var g = gpu with { GpuCore = Highlight.None, Vram = Highlight.None };
            var len = Math.Clamp(gpu.FanCount, 1, 3) switch { 1 => 17.0, 2 => 24.0, _ => 30.5 };
            var offX = -6.25 - (-len / 2 + 7.4);
            using (b.Place(new V3(offX, 6.65, 1.0), rotX: -Math.PI / 2)) DesktopGpu(b, g);
            Pin(b, a, ComponentKind.Gpu, new V3(offX + len * 0.2, 12.6, 1.0), 5);
            for (var i = 0; i < 4; i++) videoOut.Add(new V3(offX - len / 2 - 0.2, 3.15 + i * 2, 0.4));
        }
        else
        {
            if (a.Gpu is not null) Pin(b, a, ComponentKind.Gpu, new V3(-0.5, 1.0, -6.0), 12);
            for (var i = 0; i < 4; i++) videoOut.Add(new V3(-12.4, 1.2 + (i % 2) * 0.8, -6.5 + i * 0.9));
        }

        // Ổ M.2 vào khe M.2; ổ SATA và ổ dư đặt vào khoang ổ cạnh bo, nối cáp SATA về bo.
        var m2Slot = 0;
        var bay = new List<ComponentModelSpec>();
        foreach (var d in a.Drives)
        {
            if (d.DriveForm == DriveForm.M2 && m2Slot < a.Board.M2Slots)
            {
                var z = -1.5 + m2Slot++ * 4.06;
                using (b.Place(new V3(2.3, 0.45, z))) M2(b, d);
                Pin(b, a, ComponentKind.Storage, new V3(2.3, 0.6, z), 7 + m2Slot);
            }
            else bay.Add(d);
        }
        PlaceBayDrives(b, a, bay, bayX: 26, sataFrom: i => new V3(12.2, 0.5 + (i % 2) * 0.85, 5 + (i / 2) * 1.15));

        PlaceMonitors(b, a, a.Displays, videoOut, rowZ: -24);
        FirmwareMark(b, a, new V3(5.5, 0.28, 12.6));
    }

    private static void LaptopAssembly(ModelBuilder b, AssemblySpec a)
    {
        Board(b, a.Board, drawDrives: false);
        Pin(b, a, ComponentKind.Motherboard, new V3(9, 0.2, 6.5), 6);

        if (a.Cpu is { } cpu)
        {
            b.AddBox(M(cpu.CpuDie, "die"), new V3(-3.6, 0.2, -2), new V3(2.15, 0.08, 1.45));
            Pin(b, a, ComponentKind.Cpu, new V3(-3, 0.6, -2), 9);
        }

        // GPU rời của laptop là chip hàn trên bo: thấy được nhân và VRAM từ trên xuống.
        if (a.Gpu is { GpuForm: GpuForm.LaptopDiscrete } gpu)
        {
            using (b.Place(new V3(3.5, 0.06, -3.5), scale: 0.55)) GpuBoardDetail(b, gpu, 0, 0);
            Pin(b, a, ComponentKind.Gpu, new V3(3.5, 0.3, -3.5), 7);
        }
        else if (a.Gpu is not null)
        {
            Pin(b, a, ComponentKind.Gpu, new V3(-2.4, 0.6, -2), 12);
        }

        if (a.Board.DimmSlots.Contains('1') || a.Memory is { RamForm: RamForm.Soldered })
            Pin(b, a, ComponentKind.Memory, new V3(4.5, 0.8, 3.0), 8);

        var m2Slot = 0;
        var bay = new List<ComponentModelSpec>();
        foreach (var d in a.Drives)
        {
            if (d.DriveForm == DriveForm.M2 && m2Slot < Math.Max(1, a.Board.M2Slots))
            {
                var z = 2.5 + m2Slot++ * 3;
                using (b.Place(new V3(-6.8, 0.35, z))) M2(b, d);
                Pin(b, a, ComponentKind.Storage, new V3(-6.8, 0.5, z), 6 + m2Slot);
            }
            else bay.Add(d);
        }
        PlaceBayDrives(b, a, bay, bayX: 24, sataFrom: _ => new V3(14.2, 0.3, 4));

        // Pin nằm ngay dưới bo, nối cáp về đầu cắm pin.
        if (a.Battery is { } battery)
        {
            var cells = Math.Clamp(battery.SeriesCells, 2, 6);
            var trayW = cells * 2.1 + 0.6;
            using (b.Place(new V3(0, 0, 13.2))) Battery(b, battery);
            Cable(b, "cable_dark", 0.25, new V3(0, 0.3, 8.3), new V3(0, 0.3, 10.5),
                new V3(trayW / 2 + 2.6, 0.3, 10.5), new V3(trayW / 2 + 2.6, 0.3, 16.8));
            Pin(b, a, ComponentKind.Battery, new V3(0, 0.9, 13), 6);
        }

        // Màn tích hợp dựng đứng sau bo như nắp máy, nối cáp eDP; màn rời đặt xa hơn.
        var external = new List<ComponentModelSpec>();
        var lidDrawn = false;
        foreach (var d in a.Displays)
        {
            if (d.PanelForm == PanelForm.LaptopLid && !lidDrawn)
            {
                using (b.Place(new V3(0, 0, -10.5), scale: 0.7)) LidPanel(b, d.Aspect, new V3(0, 0, 0));
                Cable(b, "cable_dark", 0.3, new V3(-1, 0.2, -8.3), new V3(-1, 0.2, -10.2), new V3(-1, 0.5, -10.2));
                Pin(b, a, ComponentKind.Display, new V3(11.8, 0.6, -10.5), 15, anchor: false);
                lidDrawn = true;
            }
            else external.Add(d);
        }
        var videoOut = Enumerable.Range(0, 4).Select(i => new V3(-14.3, 0.4, -5 + i * 1.5)).ToList();
        PlaceMonitors(b, a, external, videoOut, rowZ: -22, pin: !lidDrawn);

        FirmwareMark(b, a, new V3(7.5, 0.15, -6.5));
    }

    private static void PlaceBayDrives(ModelBuilder b, AssemblySpec a, List<ComponentModelSpec> drives,
        double bayX, Func<int, V3> sataFrom)
    {
        var z = -12.0;
        for (var i = 0; i < drives.Count; i++)
        {
            var d = drives[i] with { DriveMedia = drives[i].DriveForm == DriveForm.M2 ? drives[i].DriveMedia : Highlight.None };
            var (len, width, height) = d.DriveForm switch
            {
                DriveForm.Hdd35 => (14.7, 10.2, 2.6),
                DriveForm.Hdd25 => (10.0, 7.0, 0.95),
                DriveForm.Ssd25 => (10.0, 7.0, 0.7),
                _ => (8.0, 2.2, 0.2)
            };
            var center = new V3(bayX + len / 2, 0, z + width / 2);
            using (b.Place(center)) Drive(b, d);

            // Cáp dữ liệu SATA màu đỏ từ cổng trên bo tới đầu cắm của ổ.
            if (d.DriveForm != DriveForm.M2)
            {
                var from = sataFrom(i);
                var to = new V3(center.X - len / 2 - 0.35, Math.Max(0.35, height * 0.35), center.Z - 1.6);
                var midX = (from.X + to.X) / 2;
                Cable(b, "cable", 0.22, from, new V3(midX, from.Y, from.Z), new V3(midX, from.Y, to.Z),
                    new V3(midX, to.Y, to.Z), to);
            }

            Pin(b, a, ComponentKind.Storage, new V3(center.X, height + 0.1, center.Z), 5 + i);
            z += width + 2.5;
        }
    }

    /// <summary>
    /// Màn hình trong cảnh tổng thể được thu nhỏ: ở tỉ lệ thật một màn 27" cao gấp đôi
    /// bo mạch và lấn át mọi linh kiện khác, trong khi mục đích của cảnh là cho thấy
    /// cái gì nối với cái gì.
    /// </summary>
    private const double SceneDisplayScale = 0.45;

    private static void PlaceMonitors(ModelBuilder b, AssemblySpec a, IReadOnlyList<ComponentModelSpec> displays,
        IReadOnlyList<V3> videoOut, double rowZ, bool pin = true)
    {
        if (displays.Count == 0) return;
        const double k = SceneDisplayScale;
        const double gap = 4;
        var widths = displays.Select(d => (30 * Math.Clamp(d.Aspect, 1.2, 3.6) + 1.2) * k).ToList();
        var total = widths.Sum() + gap * (displays.Count - 1);
        var x = -total / 2;

        for (var i = 0; i < displays.Count; i++)
        {
            var cx = x + widths[i] / 2;
            using (b.Place(new V3(cx, 0, rowZ), scale: k)) Monitor(b, Math.Clamp(displays[i].Aspect, 1.2, 3.6));

            // Cáp tín hiệu từ cổng xuất hình đi dọc sàn tới chân đế màn.
            var from = videoOut[Math.Min(i, videoOut.Count - 1)];
            var exitX = from.X - 2.5 - i * 0.6;
            Cable(b, "cable_dark", 0.28, from, new V3(exitX, from.Y, from.Z), new V3(exitX, 0.3, from.Z),
                new V3(exitX, 0.3, rowZ + 6.5), new V3(cx, 0.3, rowZ + 6.5), new V3(cx, 0.3, rowZ + 2),
                new V3(cx, 0.6, rowZ + 2));

            if (pin) Pin(b, a, ComponentKind.Display, new V3(cx + widths[i] / 2 - 1, 44 * k, rowZ), 3 + i);
            x += widths[i] + gap;
        }
    }

    /// <summary>Nắp màn hình laptop dựng đứng: viền, mặt màn và bản lề.</summary>
    private static void LidPanel(ModelBuilder b, double aspect, V3 at)
    {
        const double w = 32;
        var screenH = (w - 1.4) / Math.Clamp(aspect, 1.2, 3.6);
        var lidH = screenH + 2.2;
        b.AddBox("plastic", at + new V3(0, lidH / 2 + 0.6, 0), new V3(w, lidH, 0.6));
        b.AddBox("screen", at + new V3(0, lidH / 2 + 0.9, 0.31), new V3(w - 1.4, screenH, 0.02));
        b.AddCylinder("metal_dark", at + new V3(0, 0.45, 0), 0.45, w * 0.7, 24, Axis.X);
    }

    private static void FirmwareMark(ModelBuilder b, AssemblySpec a, V3 at)
    {
        if (a.Firmware is not { } fw) return;
        b.AddBox(M(fw.FirmwareChip, "chip"), at, new V3(0.7, 0.12, 0.6));
        Pin(b, a, ComponentKind.Firmware, at, 5);
    }

    /// <summary>
    /// Cột mốc đánh dấu một linh kiện trong cảnh lắp ráp: thân mảnh và một khối hình thoi
    /// trên đỉnh, màu theo mức đánh giá của linh kiện đó.
    /// </summary>
    private static void Pin(ModelBuilder b, AssemblySpec a, ComponentKind kind, V3 at, double height, bool anchor = true)
    {
        var rating = a.Ratings.TryGetValue(kind, out var r) ? r : HealthRating.Unknown;
        var material = rating switch
        {
            HealthRating.Good => "rating_good",
            HealthRating.Fair => "rating_fair",
            HealthRating.Average => "rating_average",
            HealthRating.Poor => "rating_poor",
            _ => "rating_unknown"
        };
        b.AddCylinder("metal_dark", at + new V3(0, height / 2, 0), 0.2, height, 10);
        b.AddBox(material, at + new V3(0, height + 1.0, 0), new V3(1.6, 1.6, 1.6), rotX: 0.785, rotZ: 0.615);
        if (anchor) b.AddCylinder(material, at + new V3(0, 0.03, 0), 0.7, 0.06, 20);
    }

    /// <summary>Dây cáp đi qua các điểm cho trước, mỗi đoạn song song một trục toạ độ.</summary>
    private static void Cable(ModelBuilder b, string material, double thickness, params V3[] points)
    {
        for (var i = 1; i < points.Length; i++)
        {
            var (p, q) = (points[i - 1], points[i]);
            var d = q - p;
            if (d.Length < 1e-6) continue;
            var size = new V3(Math.Abs(d.X) + thickness, Math.Abs(d.Y) + thickness, Math.Abs(d.Z) + thickness);
            b.AddBox(material, (p + q) * 0.5, size);
        }
    }

    // ------------------------------------------------------------------
    // Firmware — chip BIOS SPI 8 chân
    // ------------------------------------------------------------------

    /// <summary>
    /// Ổ khoá cho mục "Khoá & quyền sở hữu": máy bị tổ chức khác quản lý thì khoá đóng và đỏ,
    /// còn dấu vết doanh nghiệp thì đóng và cam, sạch thì càng khoá bật lên và xoay mở.
    /// </summary>
    private static void Padlock(ModelBuilder b, ComponentModelSpec s)
    {
        var body = M(s.OwnershipLock, "gold");
        b.AddBox(body, new V3(0, 0.2, 0), new V3(2.0, 1.6, 0.8));
        b.AddBox("metal_dark", new V3(0, 0.2, 0.41), new V3(1.7, 1.3, 0.02));
        b.AddCylinder("plastic_dark", new V3(0, 0.4, 0.43), 0.16, 0.04, 24, Axis.Z);
        b.AddBox("plastic_dark", new V3(0, 0.15, 0.43), new V3(0.12, 0.4, 0.04));

        const double r = 0.72, leg = 0.55, thick = 0.24;
        var open = s.OwnershipLock == Highlight.None;

        // Càng khoá vẽ quanh chân trái làm gốc, để khi mở chỉ cần nhấc lên rồi xoay quanh chân đó.
        const double lift = 0.45;
        using (b.Place(new V3(-r, open ? 1.0 + lift : 1.0, 0), rotY: open ? -Math.PI / 2 : 0))
        {
            // Khi mở, chân trái vẫn cắm trong thân khoá nên kéo dài xuống bằng đúng đoạn nhấc lên.
            var leftLength = open ? leg + lift + 0.2 : leg;
            b.AddCylinder("metal", new V3(0, leg - leftLength / 2, 0), thick / 2, leftLength, 20);
            b.AddCylinder("metal", new V3(2 * r, leg / 2, 0), thick / 2, leg, 20);

            const int segments = 16;
            for (var i = 0; i < segments; i++)
            {
                var a0 = Math.PI * i / segments;
                var a1 = Math.PI * (i + 1) / segments;
                var mid = (a0 + a1) / 2;
                var length = 2 * r * Math.Sin((a1 - a0) / 2) + 0.02;
                b.AddBox("metal", new V3(r + r * Math.Cos(mid), leg + r * Math.Sin(mid), 0),
                    new V3(length, thick, thick), rotZ: mid + Math.PI / 2);
            }
        }
    }

    private static void BiosChip(ModelBuilder b, ComponentModelSpec s)
    {
        b.AddBox("pcb_green", new V3(0, 0, 0), new V3(2.8, 0.08, 2.2));
        b.AddBox(M(s.FirmwareChip, "chip"), new V3(-0.3, 0.2, 0), new V3(1.0, 0.3, 0.8));
        b.AddCylinder("metal_dark", new V3(-0.68, 0.355, -0.24), 0.07, 0.01, 16);

        for (var i = 0; i < 4; i++)
        {
            var x = -0.66 + i * 0.24;
            foreach (var side in new[] { -1, 1 })
            {
                b.AddBox("metal", new V3(x, 0.2, side * 0.43), new V3(0.1, 0.06, 0.08));
                b.AddBox("metal", new V3(x, 0.11, side * 0.48), new V3(0.1, 0.16, 0.04));
                b.AddBox("metal", new V3(x, 0.06, side * 0.56), new V3(0.1, 0.03, 0.14));
                b.AddBox("gold", new V3(x, 0.045, side * 0.85), new V3(0.04, 0.005, 0.5));
            }
        }

        b.AddCylinder("metal", new V3(0.9, 0.1, 0.2), 0.42, 0.12, 40);   // pin CR2032
        b.AddCylinder("metal_dark", new V3(0.9, 0.17, 0.2), 0.3, 0.01, 32);
    }
}
