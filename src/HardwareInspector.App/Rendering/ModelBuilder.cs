namespace HardwareInspector.App.Rendering;

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
    private readonly List<(V3 Offset, double RotX, double RotY, double RotZ, double Scale)> _placements = new();

    public IReadOnlyList<Surface> Surfaces => _order;

    public double MinY => _order.SelectMany(s => s.Positions).Select(p => p.Y).DefaultIfEmpty(0).Min();

    /// <summary>
    /// Đặt mọi hình vẽ tiếp theo vào vị trí, góc xoay và tỉ lệ cho trước, cho tới khi
    /// đối tượng trả về bị Dispose. Dùng để ghép mô hình từng linh kiện vào cảnh lắp ráp
    /// mà không phải sửa toạ độ bên trong từng mô hình. Gọi lồng nhau được.
    /// </summary>
    public IDisposable Place(V3 offset, double rotX = 0, double rotY = 0, double rotZ = 0, double scale = 1)
    {
        _placements.Add((offset, rotX, rotY, rotZ, scale));
        return new PlacementScope(this);
    }

    private sealed class PlacementScope(ModelBuilder owner) : IDisposable
    {
        private bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            owner._placements.RemoveAt(owner._placements.Count - 1);
        }
    }

    private V3 ToWorld(V3 p)
    {
        for (var i = _placements.Count - 1; i >= 0; i--)
        {
            var t = _placements[i];
            p = Rotate(p, t.RotX, t.RotY, t.RotZ) * t.Scale + t.Offset;
        }
        return p;
    }

    private V3 NormalToWorld(V3 n)
    {
        for (var i = _placements.Count - 1; i >= 0; i--)
        {
            var t = _placements[i];
            n = Rotate(n, t.RotX, t.RotY, t.RotZ);
        }
        return n;
    }

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
            var normal = NormalToWorld(Rotate(n, rotX, rotY, rotZ));
            var start = s.Positions.Count;

            foreach (var (su, sv) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            {
                var local = c + du * su + dv * sv;
                s.Positions.Add(ToWorld(center + Rotate(local, rotX, rotY, rotZ)));
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
    private void Quad(Surface s, V3 p0, V3 p1, V3 p2, V3 p3, V3 n01, V3 n23)
    {
        (p0, p1, p2, p3) = (ToWorld(p0), ToWorld(p1), ToWorld(p2), ToWorld(p3));
        (n01, n23) = (NormalToWorld(n01), NormalToWorld(n23));

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
