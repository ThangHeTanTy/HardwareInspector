namespace HardwareInspector.Benchmark.Gpu;

/// <summary>
/// Mã HLSL cho bài kiểm tra GPU, biên dịch lúc chạy bằng d3dcompiler_47.dll
/// (có sẵn trong Windows 10 trở lên, không cần kèm theo file nào).
///
/// Sáu kernel:
/// - <c>Stress</c> + <c>Compare</c>: tải nặng bằng chuỗi phép nhân-cộng. Cùng đầu vào thì
///   GPU khoẻ luôn cho ra kết quả giống hệt nhau đến từng bit. Lệch dù một bit nghĩa là
///   nhân tính toán không còn ổn định khi nóng — dấu hiệu của chip đã xuống cấp,
///   ép xung quá tay hoặc nguồn cấp cho card không đủ.
/// - <c>Fill</c> + <c>Verify</c>: ghi mẫu dữ liệu giả ngẫu nhiên lên VRAM rồi đọc lại so sánh,
///   giống cách MemTest làm với RAM. Mẫu phụ thuộc cả địa chỉ lẫn hạt giống của từng lượt,
///   nên lỗi dính bit, lỗi địa chỉ và lỗi rò giữa các ô nhớ đều lộ ra.
/// - <c>Peak</c> + <c>Copy</c>: đo sức tính FP32 cực đại và băng thông VRAM để so với thông số
///   chính hãng của model card khai. Card đời thấp bị nạp vBIOS đổi tên vẫn mang nhân và bus nhớ
///   của nó, nên hụt rất xa ở cả hai con số.
/// </summary>
internal static class GpuShaders
{
    public const uint ThreadsPerGroup = 256;

    /// <summary>Số phần tử mỗi luồng xử lý trong kernel VRAM, để số nhóm không vượt 65535.</summary>
    public const uint ElementsPerThread = 16;

    /// <summary>Số phép toán dấu phẩy động mỗi vòng lặp của một luồng trong kernel Stress (2 × mad float4).</summary>
    public const double FlopsPerIteration = 16;

    /// <summary>Số phép toán mỗi vòng của kernel Peak: 8 bộ tích luỹ × mad float4 × 2 phép.</summary>
    public const double PeakFlopsPerIteration = 64;

    public const string Source = """
        cbuffer Params : register(b0)
        {
            uint ElementCount; // Stress/Compare: số luồng. Fill/Verify: số uint trong vùng nhớ.
            uint Seed;
            uint Iterations;
            uint Invert;
            uint Stride;       // tổng số luồng của một lượt Fill/Verify
            uint3 Padding;
        };

        RWByteAddressBuffer Data   : register(u0);
        RWByteAddressBuffer Result : register(u1); // [0] số lỗi, [1] vị trí lỗi đầu, [2] giá trị đọc, [3] giá trị mong đợi
        RWByteAddressBuffer Ref    : register(u2);

        uint Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352du;
            x ^= x >> 15; x *= 0x846ca68bu;
            x ^= x >> 16;
            return x;
        }

        uint Pattern(uint i)
        {
            uint v = Hash(i ^ Seed);
            return Invert != 0 ? ~v : v;
        }

        void ReportError(uint index, uint got, uint expected)
        {
            uint previous;
            Result.InterlockedAdd(0, 1, previous);
            if (previous == 0)
            {
                Result.Store(4, index);
                Result.Store(8, got);
                Result.Store(12, expected);
            }
        }

        [numthreads(256, 1, 1)]
        void Stress(uint3 id : SV_DispatchThreadID)
        {
            if (id.x >= ElementCount) return;

            float4 x = frac(float4(id.x, id.x + 1, id.x + 2, id.x + 3) * 0.000123 + Seed * 0.001);
            float4 y = frac(x * 1.618034 + 0.25);
            const float4 k = float4(1.0001, 0.9997, 1.0003, 0.9999);

            [loop]
            for (uint n = 0; n < Iterations; n++)
            {
                x = frac(mad(x, k, y));
                y = frac(mad(y, x, 0.6180339));
            }

            Data.Store4(id.x * 16, asuint(x + y));
        }

        [numthreads(256, 1, 1)]
        void Compare(uint3 id : SV_DispatchThreadID)
        {
            if (id.x >= ElementCount) return;
            uint4 a = Data.Load4(id.x * 16);
            uint4 b = Ref.Load4(id.x * 16);
            if (any(a != b))
                ReportError(id.x, a.x ^ b.x, 0);
        }

        [numthreads(256, 1, 1)]
        void Fill(uint3 id : SV_DispatchThreadID)
        {
            [unroll]
            for (uint k = 0; k < 16; k++)
            {
                uint i = k * Stride + id.x;
                if (i < ElementCount)
                    Data.Store(i * 4, Pattern(i));
            }
        }

        // Chỉ toàn phép nhân-cộng độc lập, không frac hay rẽ nhánh, để đạt gần sức tính lý thuyết.
        [numthreads(256, 1, 1)]
        void Peak(uint3 id : SV_DispatchThreadID)
        {
            float4 a0 = float4(id.x & 1023, 1, 2, 3) * 1e-7;
            float4 a1 = a0 + 0.1, a2 = a0 + 0.2, a3 = a0 + 0.3;
            float4 a4 = a0 + 0.4, a5 = a0 + 0.5, a6 = a0 + 0.6, a7 = a0 + 0.7;
            const float4 m = float4(0.9999, 1.0001, 0.99995, 1.00005);
            const float4 c = float4(1e-7, 2e-7, 3e-7, 4e-7);

            [loop]
            for (uint n = 0; n < Iterations; n++)
            {
                a0 = mad(a0, m, c); a1 = mad(a1, m, c); a2 = mad(a2, m, c); a3 = mad(a3, m, c);
                a4 = mad(a4, m, c); a5 = mad(a5, m, c); a6 = mad(a6, m, c); a7 = mad(a7, m, c);
            }

            if (id.x < ElementCount)
                Data.Store4(id.x * 16, asuint(a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7));
        }

        // ElementCount = số khối 16 byte; Stride = tổng số luồng của lệnh.
        [numthreads(256, 1, 1)]
        void Copy(uint3 id : SV_DispatchThreadID)
        {
            for (uint i = id.x; i < ElementCount; i += Stride)
                Ref.Store4(i * 16, Data.Load4(i * 16));
        }

        [numthreads(256, 1, 1)]
        void Verify(uint3 id : SV_DispatchThreadID)
        {
            [unroll]
            for (uint k = 0; k < 16; k++)
            {
                uint i = k * Stride + id.x;
                if (i < ElementCount)
                {
                    uint got = Data.Load(i * 4);
                    uint expected = Pattern(i);
                    if (got != expected)
                        ReportError(i, got, expected);
                }
            }
        }
        """;
}
