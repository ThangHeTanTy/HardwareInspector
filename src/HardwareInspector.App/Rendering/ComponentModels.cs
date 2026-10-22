using HardwareInspector.Core.Models;

namespace HardwareInspector.App.Rendering;

/// <summary>
/// Mô hình 3D dựng bằng code cho từng loại linh kiện.
///
/// Không dùng file mô hình có sẵn: ứng dụng phải chạy được offline và không mang
/// theo tài nguyên nặng. Mỗi linh kiện được ghép từ các khối hộp và trụ đơn giản,
/// đủ để người dùng nhận ra ngay đang xem CPU, card đồ hoạ hay thanh RAM.
///
/// Phần này cố ý không phụ thuộc WPF — chỉ sinh đỉnh, pháp tuyến và chỉ số tam giác.
/// Nhờ vậy hình học kiểm tra được ở bất kỳ đâu, còn
/// <see cref="Views.ComponentModelView"/> chỉ việc đổi sang Model3D để hiển thị.
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
            ["smd"] = new("#B08B5A", 10),
            ["slot_grey"] = new("#7D838D", 10),
            ["cap"] = new("#2A2F38", 40),
            ["cell_blue"] = new("#2E6FD8", 30),
            ["label"] = new("#DCDFE4", 5),
            ["accent"] = new("#4C8DFF", 30, Emissive: true),
            ["screen"] = new("#1D3A66", 80, Emissive: true),
            [RatingMaterial] = new("#8A8F98", 0, Emissive: true)
        };

    public static IReadOnlyList<Surface> Build(ComponentKind kind)
    {
        var b = new ModelBuilder();
        switch (kind)
        {
            case ComponentKind.Cpu: Cpu(b); break;
            case ComponentKind.Gpu: Gpu(b); break;
            case ComponentKind.Memory: Ram(b); break;
            case ComponentKind.Storage: Ssd(b); break;
            case ComponentKind.Battery: Battery(b); break;
            case ComponentKind.Display: Monitor(b); break;
            case ComponentKind.Motherboard: Motherboard(b); break;
            case ComponentKind.Firmware: BiosChip(b); break;
            case ComponentKind.Cooling: Fan(b, new V3(0, 0.2, 0), 1.6, 9); break;
            default: b.AddBox("plastic", new V3(0, 0.6, 0), new V3(1.2, 1.2, 1.2)); break;
        }

        b.Normalize(FitRadius);

        // Bệ đỡ thêm sau khi chuẩn hoá để có kích thước như nhau ở mọi linh kiện.
        var floor = b.MinY - 0.12;
        b.AddCylinder("plastic", new V3(0, floor - 0.06, 0), 2.45, 0.08, 72);
        b.AddRing(RatingMaterial, new V3(0, floor - 0.06, 0), 2.56, 2.45, 0.1, 72);

        return b.Surfaces;
    }

    // ------------------------------------------------------------------

    private static void Cpu(ModelBuilder b)
    {
        b.AddBox("pcb_green", new V3(0, 0.08, 0), new V3(4, 0.16, 4));
        b.AddBox("metal", new V3(0, 0.30, 0), new V3(3.1, 0.28, 3.1));
        b.AddBox("metal", new V3(0, 0.47, 0), new V3(2.7, 0.06, 2.7));

        // Linh kiện SMD dọc hai cạnh và tam giác đánh dấu chân số 1.
        for (var i = 0; i < 7; i++)
        {
            b.AddBox("smd", new V3(-1.5 + i * 0.5, 0.18, 1.78), new V3(0.18, 0.05, 0.1));
            b.AddBox("smd", new V3(1.78, 0.18, -1.5 + i * 0.5), new V3(0.1, 0.05, 0.18));
        }
        b.AddBox("gold", new V3(-1.75, 0.17, -1.75), new V3(0.25, 0.02, 0.25));

        // Mặt dưới: lưới tiếp điểm mạ vàng.
        for (var x = 0; x < 12; x++)
        for (var z = 0; z < 12; z++)
            b.AddBox("gold", new V3(-1.65 + x * 0.3, -0.01, -1.65 + z * 0.3), new V3(0.14, 0.02, 0.14));
    }

    private static void Gpu(ModelBuilder b)
    {
        b.AddBox("metal_dark", new V3(0, -0.08, 0), new V3(7, 0.05, 2.6));      // backplate
        b.AddBox("pcb_dark", new V3(0, 0, 0), new V3(7, 0.1, 2.6));

        // Lá tản nhiệt lộ ra giữa PCB và vỏ.
        for (var x = -3.0; x <= 3.0; x += 0.16)
            b.AddBox("metal", new V3(x, 0.25, 0), new V3(0.04, 0.3, 2.3));

        b.AddBox("plastic_dark", new V3(0, 0.55, 0), new V3(6.6, 0.3, 2.5));
        b.AddBox("accent", new V3(0, 0.71, 1.15), new V3(6.2, 0.02, 0.08));

        Fan(b, new V3(-1.65, 0.72, 0), 1.05, 9);
        Fan(b, new V3(1.65, 0.72, 0), 1.05, 9);

        // Chân cắm PCIe.
        for (var i = 0; i < 22; i++)
            b.AddBox("gold", new V3(-2.4 + i * 0.13, -0.01, -1.36), new V3(0.08, 0.12, 0.14));

        // Ke gắn vỏ máy với các cổng xuất hình.
        b.AddBox("metal", new V3(-3.55, 0.25, 0), new V3(0.06, 1.0, 2.9));
        for (var i = 0; i < 3; i++)
            b.AddBox("plastic_dark", new V3(-3.6, 0.25, -0.8 + i * 0.8), new V3(0.04, 0.22, 0.5));
    }

    private static void Fan(ModelBuilder b, V3 center, double radius, int blades)
    {
        b.AddRing("plastic", center, radius, radius - 0.07, 0.08, 48);
        b.AddCylinder("plastic_dark", center + new V3(0, 0.01, 0), radius * 0.3, 0.1, 32);
        for (var i = 0; i < blades; i++)
        {
            var a = i * 2 * Math.PI / blades;
            var r = radius * 0.62;
            b.AddBox("fan_blade",
                center + new V3(Math.Cos(a) * r, 0, Math.Sin(a) * r),
                new V3(radius * 0.7, 0.03, radius * 0.27),
                rotX: 0.35, rotY: -a);
        }
    }

    private static void Ram(ModelBuilder b)
    {
        b.AddBox("pcb_green", new V3(0, 0.65, 0), new V3(5.4, 1.3, 0.06));
        for (var i = 0; i < 8; i++)
        {
            var x = -2.17 + i * 0.62;
            b.AddBox("chip", new V3(x, 0.78, 0.055), new V3(0.5, 0.48, 0.05));
            b.AddBox("chip", new V3(x, 0.78, -0.055), new V3(0.5, 0.48, 0.05));
        }
        b.AddBox("label", new V3(-1.2, 1.12, 0.035), new V3(1.6, 0.18, 0.005));

        for (var i = 0; i < 44; i++)
        {
            var x = -2.6 + i * 0.12;
            if (Math.Abs(x - 0.45) < 0.12) continue;   // khấc chống cắm ngược
            b.AddBox("gold", new V3(x, 0.1, 0.035), new V3(0.07, 0.18, 0.012));
            b.AddBox("gold", new V3(x, 0.1, -0.035), new V3(0.07, 0.18, 0.012));
        }
    }

    private static void Ssd(ModelBuilder b)
    {
        b.AddBox("pcb_dark", new V3(0, 0, 0), new V3(4.5, 0.08, 1.3));
        b.AddBox("chip", new V3(-1.3, 0.09, 0), new V3(0.8, 0.1, 0.8));       // controller
        b.AddBox("chip", new V3(-0.5, 0.08, 0), new V3(0.45, 0.08, 0.6));     // DRAM cache
        b.AddBox("chip", new V3(0.35, 0.09, 0), new V3(1.0, 0.1, 0.95));      // NAND
        b.AddBox("chip", new V3(1.5, 0.09, 0), new V3(1.0, 0.1, 0.95));
        b.AddBox("label", new V3(0.9, 0.15, 0), new V3(2.4, 0.01, 1.05));

        for (var i = 0; i < 14; i++)
        {
            var z = -0.55 + i * 0.085;
            if (Math.Abs(z - 0.23) < 0.05) continue;    // khấc khoá M-key
            b.AddBox("gold", new V3(-2.13, 0.0, z), new V3(0.24, 0.09, 0.05));
        }

        b.AddRing("metal", new V3(2.18, 0, 0), 0.2, 0.11, 0.09, 24);
        for (var i = 0; i < 6; i++)
            b.AddBox("smd", new V3(-0.85 + i * 0.12, 0.06, 0.5), new V3(0.06, 0.04, 0.1));
    }

    private static void Battery(ModelBuilder b)
    {
        b.AddBox("plastic_dark", new V3(0, 0.15, 0), new V3(4.6, 0.3, 1.6));
        for (var i = 0; i < 3; i++)
            b.AddCylinder("cell_blue", new V3(-1.5 + i * 1.5, 0.33, 0), 0.36, 1.35, 32, Axis.X);
        b.AddBox("label", new V3(0, 0.31, 0.6), new V3(4.0, 0.01, 0.3));
        b.AddBox("plastic_white", new V3(2.45, 0.15, 0), new V3(0.3, 0.22, 0.6));
        for (var i = 0; i < 5; i++)
            b.AddBox("gold", new V3(2.6, 0.15, -0.2 + i * 0.1), new V3(0.02, 0.1, 0.05));
    }

    private static void Monitor(ModelBuilder b)
    {
        b.AddBox("plastic_dark", new V3(0, 2.1, 0), new V3(5.2, 3.1, 0.18));
        b.AddBox("screen", new V3(0, 2.15, 0.095), new V3(4.95, 2.8, 0.01));
        b.AddBox("plastic", new V3(0, 2.0, -0.2), new V3(2.0, 1.6, 0.25));
        b.AddBox("metal_dark", new V3(0, 0.8, -0.3), new V3(0.35, 1.4, 0.2));
        b.AddBox("metal_dark", new V3(0, 0.04, -0.15), new V3(2.2, 0.08, 1.4));
        b.AddBox("accent", new V3(2.3, 0.62, 0.1), new V3(0.06, 0.03, 0.01));   // đèn nguồn
    }

    private static void Motherboard(ModelBuilder b)
    {
        b.AddBox("pcb_dark", new V3(0, 0, 0), new V3(4.8, 0.08, 4.8));

        b.AddBox("metal", new V3(-0.4, 0.1, -0.6), new V3(1.2, 0.12, 1.2));     // khung socket
        b.AddBox("plastic_dark", new V3(-0.4, 0.13, -0.6), new V3(0.9, 0.08, 0.9));

        b.AddBox("metal_dark", new V3(-1.35, 0.24, -0.6), new V3(0.3, 0.4, 1.4)); // VRM
        b.AddBox("metal_dark", new V3(-0.4, 0.24, -1.55), new V3(1.4, 0.4, 0.3));
        for (var i = 0; i < 6; i++)
            b.AddCylinder("cap", new V3(-1.0 + i * 0.22, 0.13, 0.15), 0.07, 0.2, 16);

        for (var i = 0; i < 4; i++)
            b.AddBox(i % 2 == 0 ? "plastic_dark" : "slot_grey",
                new V3(0.75 + i * 0.22, 0.12, -0.5), new V3(0.12, 0.18, 2.6));

        b.AddBox("plastic_dark", new V3(-0.6, 0.11, 1.2), new V3(2.8, 0.16, 0.14));  // PCIe x16
        b.AddBox("metal", new V3(-0.6, 0.2, 1.2), new V3(2.85, 0.02, 0.18));
        b.AddBox("plastic_dark", new V3(-1.4, 0.11, 1.85), new V3(0.6, 0.16, 0.14)); // PCIe x1
        b.AddBox("metal_dark", new V3(0.4, 0.08, 0.55), new V3(1.8, 0.06, 0.4));      // tản M.2

        b.AddBox("metal", new V3(1.4, 0.12, 1.55), new V3(0.9, 0.16, 0.9));          // chipset
        b.AddBox("accent", new V3(1.4, 0.205, 1.55), new V3(0.5, 0.01, 0.05));

        b.AddBox("metal", new V3(-2.2, 0.3, -1.3), new V3(0.3, 0.55, 2.0));          // cụm cổng I/O
        b.AddBox("plastic_white", new V3(2.2, 0.16, -0.5), new V3(0.25, 0.25, 0.9)); // nguồn 24 pin
        b.AddCylinder("metal", new V3(1.0, 0.07, 2.0), 0.3, 0.06, 32);               // pin CMOS
    }

    private static void BiosChip(ModelBuilder b)
    {
        b.AddBox("pcb_green", new V3(0, 0, 0), new V3(2.8, 0.08, 2.2));
        b.AddBox("chip", new V3(-0.3, 0.2, 0), new V3(1.0, 0.3, 0.8));
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

public enum Axis { X, Y, Z }

public sealed record MaterialSpec(string Hex, double SpecularPower, bool Emissive = false);

public readonly record struct V3(double X, double Y, double Z)
{
    public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static V3 operator *(V3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
}

/// <summary>Một lưới tam giác dùng chung một vật liệu.</summary>
public sealed class Surface
{
    public Surface(string material) => Material = material;

    public string Material { get; }
    public List<V3> Positions { get; } = new();
    public List<V3> Normals { get; } = new();
    public List<int> Indices { get; } = new();
}

/// <summary>Ghép hộp, trụ và vành thành các lưới, gom theo vật liệu để giảm số lệnh vẽ.</summary>
public sealed class ModelBuilder
{
    private readonly Dictionary<string, Surface> _byMaterial = new();
    private readonly List<Surface> _order = new();

    public IReadOnlyList<Surface> Surfaces => _order;

    public double MinY => _order.SelectMany(s => s.Positions).Select(p => p.Y).DefaultIfEmpty(0).Min();

    private Surface Get(string material)
    {
        if (_byMaterial.TryGetValue(material, out var s)) return s;
        s = new Surface(material);
        _byMaterial[material] = s;
        _order.Add(s);
        return s;
    }

    /// <summary>Hộp chữ nhật; góc xoay tính bằng radian, áp theo thứ tự X → Y → Z.</summary>
    public void AddBox(string material, V3 center, V3 size, double rotX = 0, double rotY = 0, double rotZ = 0)
    {
        var s = Get(material);
        var h = size * 0.5;

        // Sáu mặt, mỗi mặt có pháp tuyến và hai trục cạnh riêng.
        (V3 n, V3 u, V3 v)[] faces =
        {
            (new V3(1, 0, 0), new V3(0, 0, -1), new V3(0, 1, 0)),
            (new V3(-1, 0, 0), new V3(0, 0, 1), new V3(0, 1, 0)),
            (new V3(0, 1, 0), new V3(1, 0, 0), new V3(0, 0, -1)),
            (new V3(0, -1, 0), new V3(1, 0, 0), new V3(0, 0, 1)),
            (new V3(0, 0, 1), new V3(1, 0, 0), new V3(0, 1, 0)),
            (new V3(0, 0, -1), new V3(-1, 0, 0), new V3(0, 1, 0))
        };

        foreach (var (n, u, v) in faces)
        {
            var c = Mul(n, h);
            var du = Mul(u, h);
            var dv = Mul(v, h);
            var normal = Rotate(n, rotX, rotY, rotZ);
            var start = s.Positions.Count;

            foreach (var (su, sv) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            {
                var local = c + du * su + dv * sv;
                s.Positions.Add(center + Rotate(local, rotX, rotY, rotZ));
                s.Normals.Add(normal);
            }

            s.Indices.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }
    }

    /// <summary>Trụ tròn đặc, có nắp hai đầu.</summary>
    public void AddCylinder(string material, V3 center, double radius, double height, int segments, Axis axis = Axis.Y)
        => AddRing(material, center, radius, 0, height, segments, axis);

    /// <summary>Vành tròn (trụ rỗng). Bán kính trong bằng 0 thì thành trụ đặc.</summary>
    public void AddRing(string material, V3 center, double outer, double inner, double height,
        int segments, Axis axis = Axis.Y)
    {
        var s = Get(material);
        var half = height / 2;

        V3 Map(double radial1, double along, double radial2) => axis switch
        {
            Axis.X => new V3(along, radial1, radial2),
            Axis.Z => new V3(radial1, radial2, along),
            _ => new V3(radial1, along, radial2)
        };

        for (var i = 0; i < segments; i++)
        {
            var a0 = i * 2 * Math.PI / segments;
            var a1 = (i + 1) * 2 * Math.PI / segments;
            var (c0, s0, c1, s1) = (Math.Cos(a0), Math.Sin(a0), Math.Cos(a1), Math.Sin(a1));

            // Mặt ngoài, pháp tuyến hướng ra.
            Quad(s,
                center + Map(c0 * outer, -half, s0 * outer), center + Map(c1 * outer, -half, s1 * outer),
                center + Map(c1 * outer, half, s1 * outer), center + Map(c0 * outer, half, s0 * outer),
                Map(c0, 0, s0), Map(c1, 0, s1));

            if (inner > 0)
            {
                Quad(s,
                    center + Map(c0 * inner, -half, s0 * inner), center + Map(c1 * inner, -half, s1 * inner),
                    center + Map(c1 * inner, half, s1 * inner), center + Map(c0 * inner, half, s0 * inner),
                    Map(-c0, 0, -s0), Map(-c1, 0, -s1));
            }

            // Hai nắp.
            foreach (var (y, sign) in new[] { (half, 1.0), (-half, -1.0) })
            {
                var n = Map(0, sign, 0);
                Quad(s,
                    center + Map(c0 * inner, y, s0 * inner), center + Map(c1 * inner, y, s1 * inner),
                    center + Map(c1 * outer, y, s1 * outer), center + Map(c0 * outer, y, s0 * outer),
                    n, n);
            }
        }
    }

    /// <summary>
    /// Thêm tứ giác p0-p1-p2-p3. Chiều cuốn tam giác được chọn sao cho mặt trước
    /// hướng theo pháp tuyến — WPF chỉ vẽ mặt trước khi không gán BackMaterial.
    /// </summary>
    private static void Quad(Surface s, V3 p0, V3 p1, V3 p2, V3 p3, V3 n01, V3 n23)
    {
        var start = s.Positions.Count;
        s.Positions.AddRange(new[] { p0, p1, p2, p3 });
        s.Normals.AddRange(new[] { n01, n23, n23, n01 });

        // Tích có hướng của hai đường chéo: vẫn đúng khi tứ giác suy biến thành tam giác
        // (nắp trụ đặc có hai đỉnh trùng nhau ở tâm).
        var faceNormal = Cross(p2 - p0, p3 - p1);
        var wanted = n01 + n23;
        var agrees = Dot(faceNormal, wanted) >= 0;

        if (agrees)
            s.Indices.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        else
            s.Indices.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
    }

    /// <summary>Đưa tâm khối bao về gốc toạ độ và co giãn để vừa khung nhìn.</summary>
    public void Normalize(double radius)
    {
        var all = _order.SelectMany(s => s.Positions).ToList();
        if (all.Count == 0) return;

        var min = new V3(all.Min(p => p.X), all.Min(p => p.Y), all.Min(p => p.Z));
        var max = new V3(all.Max(p => p.X), all.Max(p => p.Y), all.Max(p => p.Z));
        var center = (min + max) * 0.5;
        var extent = (max - min).Length / 2;
        var scale = extent > 0 ? radius / extent : 1;

        foreach (var s in _order)
            for (var i = 0; i < s.Positions.Count; i++)
                s.Positions[i] = (s.Positions[i] - center) * scale;
    }

    private static V3 Mul(V3 a, V3 b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
    private static double Dot(V3 a, V3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static V3 Cross(V3 a, V3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    private static V3 Rotate(V3 p, double rx, double ry, double rz)
    {
        if (rx != 0)
        {
            var (c, s) = (Math.Cos(rx), Math.Sin(rx));
            p = new V3(p.X, p.Y * c - p.Z * s, p.Y * s + p.Z * c);
        }
        if (ry != 0)
        {
            var (c, s) = (Math.Cos(ry), Math.Sin(ry));
            p = new V3(p.X * c + p.Z * s, p.Y, -p.X * s + p.Z * c);
        }
        if (rz != 0)
        {
            var (c, s) = (Math.Cos(rz), Math.Sin(rz));
            p = new V3(p.X * c - p.Y * s, p.X * s + p.Y * c, p.Z);
        }
        return p;
    }
}
