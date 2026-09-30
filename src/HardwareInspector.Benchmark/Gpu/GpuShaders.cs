namespace HardwareInspector.Benchmark.Gpu;

/// <summary>
/// Mã HLSL cho bài kiểm tra GPU, biên dịch lúc chạy bằng d3dcompiler_47.dll
/// (có sẵn trong Windows 10 trở lên, không cần kèm theo file nào).
///
/// Bốn kernel, hai cặp:
/// - <c>Stress</c> + <c>Compare</c>: tải nặng bằng chuỗi phép nhân-cộng. Cùng đầu vào thì
///   GPU khoẻ luôn cho ra kết quả giống hệt nhau đến từng bit. Lệch dù một bit nghĩa là
///   nhân tính toán không còn ổn định khi nóng — dấu hiệu của chip đã xuống cấp,
///   ép xung quá tay hoặc nguồn cấp cho card không đủ.
/// - <c>Fill</c> + <c>Verify</c>: ghi mẫu dữ liệu giả ngẫu nhiên lên VRAM rồi đọc lại so sánh,
///   giống cách MemTest làm với RAM. Mẫu phụ thuộc cả địa chỉ lẫn hạt giống của từng lượt,
///   nên lỗi dính bit, lỗi địa chỉ và lỗi rò giữa các ô nhớ đều lộ ra.
/// </summary>
internal static class GpuShaders
{
    public const uint ThreadsPerGroup = 256;

    /// <summary>Số phần tử mỗi luồng xử lý trong kernel VRAM, để số nhóm không vượt 65535.</summary>
    public const uint ElementsPerThread = 16;

    /// <summary>Số phép toán dấu phẩy động mỗi vòng lặp của một luồng trong kernel Stress (2 × mad float4).</summary>
    public const double FlopsPerIteration = 16;

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
