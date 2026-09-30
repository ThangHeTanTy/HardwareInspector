using System.Diagnostics;
using System.Runtime.InteropServices;
using HardwareInspector.Core.Models.Diagnostics;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Benchmark.Gpu;

/// <summary>
/// Chạy bài kiểm tra GPU bằng compute shader Direct3D 11.
///
/// Chọn compute thay vì vẽ một cảnh 3D: không cần cửa sổ, không phụ thuộc màn hình
/// đang cắm vào card nào, và quan trọng nhất là kết quả có thể tự kiểm chứng.
/// Một cảnh 3D chỉ cho người dùng "nhìn xem có sọc không"; ở đây phần mềm tự so
/// từng bit, nên lỗi hiếm và thoáng qua cũng bị bắt.
///
/// Mọi lệnh GPU đều chia nhỏ dưới ~250 ms rồi đồng bộ một lần. Windows sẽ reset driver
/// (TDR) nếu một lệnh chiếm GPU quá 2 giây, và ta không muốn tự gây ra điều đó
/// rồi đổ lỗi cho card.
/// </summary>
internal sealed class D3D11GpuTester : IDisposable
{
    private const uint StressGroups = 1024;
    private const uint StressThreads = StressGroups * GpuShaders.ThreadsPerGroup;
    private const uint StressBufferBytes = StressThreads * 16;

    /// <summary>
    /// Kích thước mỗi khối VRAM. 128 MB là mức Direct3D 11 bảo đảm mọi card đều cấp được
    /// cho một buffer (D3D11_REQ_RESOURCE_SIZE_IN_MEGABYTES_EXPRESSION_A_TERM).
    /// </summary>
    private const uint ChunkBytes = 128u * 1024 * 1024;

    private const double TargetDispatchMs = 25;
    private const double TargetBatchMs = 250;

    /// <summary>Card có ít hơn mức này VRAM riêng coi như đồ hoạ tích hợp, dùng chung RAM hệ thống.</summary>
    private const long MinDedicatedForVramTest = 512L * 1024 * 1024;

    // Mã lỗi DXGI cho biết driver đã sập hoặc bị reset giữa chừng.
    private static readonly HashSet<int> DeviceLostCodes = new()
    {
        unchecked((int)0x887A0005), // DXGI_ERROR_DEVICE_REMOVED
        unchecked((int)0x887A0006), // DXGI_ERROR_DEVICE_HUNG
        unchecked((int)0x887A0007), // DXGI_ERROR_DEVICE_RESET
        unchecked((int)0x887A0020)  // DXGI_ERROR_DRIVER_INTERNAL_ERROR
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct Params
    {
        public uint ElementCount;
        public uint Seed;
        public uint Iterations;
        public uint Invert;
        public uint Stride;
        public uint Pad0, Pad1, Pad2;
    }

    private readonly IDXGIAdapter1 _adapter;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _ctx;
    private readonly ID3D11ComputeShader _stress, _compare, _fill, _verify;
    private readonly ID3D11Buffer _params, _result, _resultStaging;
    private readonly ID3D11UnorderedAccessView _resultUav;

    public string AdapterName { get; }
    public long DedicatedVideoMemory { get; }

    private D3D11GpuTester(IDXGIAdapter1 adapter)
    {
        _adapter = adapter;
        var desc = adapter.Description1;
        AdapterName = desc.Description.Trim();
        DedicatedVideoMemory = (long)(ulong)desc.DedicatedVideoMemory;

        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.None,
            new[] { FeatureLevel.Level_11_0 }, out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
        _device = device!;
        _ctx = context!;

        _stress = CreateShader("Stress");
        _compare = CreateShader("Compare");
        _fill = CreateShader("Fill");
        _verify = CreateShader("Verify");

        _params = _device.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<Params>(), BindFlags.ConstantBuffer));
        _result = CreateRawBuffer(16);
        _resultUav = CreateRawUav(_result, 16);
        _resultStaging = _device.CreateBuffer(new BufferDescription(16, BindFlags.None,
            ResourceUsage.Staging, CpuAccessFlags.Read));
    }

    /// <summary>
    /// Chọn card mạnh nhất. Trên laptop hai GPU (Optimus, AMD Switchable),
    /// nếu để mặc định Windows sẽ đưa ứng dụng vào GPU tích hợp tiết kiệm điện,
    /// và bài kiểm tra sẽ chẳng chạm gì tới card rời mà người bán đang quảng cáo.
    /// </summary>
    public static D3D11GpuTester Create()
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        IDXGIAdapter1? chosen = null;

        using (var factory6 = factory.QueryInterfaceOrNull<IDXGIFactory6>())
        {
            if (factory6 is not null)
            {
                for (uint i = 0; factory6.EnumAdapterByGpuPreference(i, GpuPreference.HighPerformance,
                         out IDXGIAdapter1? candidate).Success; i++)
                {
                    if (IsHardware(candidate!)) { chosen = candidate; break; }
                    candidate!.Dispose();
                }
            }
        }

        if (chosen is null)
        {
            // Windows cũ không có IDXGIFactory6: tự chọn card có nhiều VRAM riêng nhất.
            for (uint i = 0; factory.EnumAdapters1(i, out var candidate).Success; i++)
            {
                if (IsHardware(candidate) &&
                    (chosen is null || candidate.Description1.DedicatedVideoMemory > chosen.Description1.DedicatedVideoMemory))
                {
                    chosen?.Dispose();
                    chosen = candidate;
                }
                else candidate.Dispose();
            }
        }

        if (chosen is null)
            throw new InvalidOperationException(S(
                "Không tìm thấy GPU phần cứng hỗ trợ Direct3D 11.",
                "No hardware GPU with Direct3D 11 support was found."));

        try { return new D3D11GpuTester(chosen); }
        catch { chosen.Dispose(); throw; }
    }

    private static bool IsHardware(IDXGIAdapter1 adapter) =>
        (adapter.Description1.Flags & AdapterFlags.Software) == 0;

    public static bool IsDeviceLost(Exception ex) =>
        ex is SharpGenException sg && DeviceLostCodes.Contains(sg.ResultCode.Code);

    /// <summary>Lý do driver báo khi thiết bị bị gỡ, dạng mã hex để tra cứu.</summary>
    public string DescribeDeviceLoss(Exception ex)
    {
        var code = ex is SharpGenException sg ? sg.ResultCode.Code : 0;
        var reason = 0;
        try { reason = _device.DeviceRemovedReason.Code; } catch { }
        return $"0x{code:X8} (DeviceRemovedReason 0x{reason:X8})";
    }

    // ------------------------------------------------------------------
    // Tải nặng
    // ------------------------------------------------------------------

    public void RunStress(GpuTestResult result, TimeSpan duration, IProgress<double>? progress, CancellationToken ct)
    {
        using var output = CreateRawBuffer(StressBufferBytes);
        using var outputUav = CreateRawUav(output, StressBufferBytes);
        using var reference = CreateRawBuffer(StressBufferBytes);
        using var referenceUav = CreateRawUav(reference, StressBufferBytes);

        _ctx.CSSetConstantBuffer(0, _params);
        _ctx.CSSetUnorderedAccessViews(0, new[] { outputUav, _resultUav, referenceUav });

        const uint seed = 20240917;
        var iterations = Calibrate(seed, ct);
        var dispatchMs = TimeDispatch(iterations, seed);
        var dispatchesPerBatch = (int)Math.Clamp(TargetBatchMs / Math.Max(dispatchMs, 0.1), 1, 64);

        // Kết quả chuẩn lấy ngay từ đầu khi card còn nguội.
        SetParams(new Params { ElementCount = StressThreads, Seed = seed, Iterations = iterations });
        _ctx.CSSetShader(_stress);
        _ctx.Dispatch(StressGroups, 1, 1);
        _ctx.CopyResource(reference, output);
        ClearResult();
        ReadResult();

        var samples = new List<(double Seconds, double Gops)>();
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed < duration)
        {
            ct.ThrowIfCancellationRequested();

            var batchStart = sw.Elapsed.TotalSeconds;
            _ctx.CSSetShader(_stress);
            for (var d = 0; d < dispatchesPerBatch; d++)
                _ctx.Dispatch(StressGroups, 1, 1);

            _ctx.CSSetShader(_compare);
            _ctx.Dispatch(StressGroups, 1, 1);
            var errors = ReadResult();
            ClearResult();

            var seconds = sw.Elapsed.TotalSeconds - batchStart;
            var flops = (double)StressThreads * iterations * GpuShaders.FlopsPerIteration * dispatchesPerBatch;
            samples.Add((sw.Elapsed.TotalSeconds, flops / Math.Max(seconds, 1e-6) / 1e9));

            result.ComputeChecks++;
            if (errors[0] > 0) result.ComputeMismatches++;

            progress?.Report(Math.Clamp(sw.Elapsed / duration, 0, 1));
        }

        result.StressDuration = sw.Elapsed;

        // So phút đầu với phút cuối. Bài ngắn thì lấy 20% đầu và 20% cuối.
        var total = sw.Elapsed.TotalSeconds;
        var window = Math.Min(60, total * 0.2);
        result.InitialThroughput = Average(samples.Where(s => s.Seconds <= window + 1).Select(s => s.Gops));
        result.FinalThroughput = Average(samples.Where(s => s.Seconds >= total - window).Select(s => s.Gops));
    }

    /// <summary>Tìm số vòng lặp để mỗi lệnh chạy khoảng 25 ms trên card đang kiểm.</summary>
    private uint Calibrate(uint seed, CancellationToken ct)
    {
        uint iterations = 256;
        TimeDispatch(iterations, seed); // lượt khởi động: driver biên dịch shader sang mã máy

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var ms = TimeDispatch(iterations, seed);
            if (ms >= TargetDispatchMs || iterations >= 1u << 20) return iterations;

            var scale = ms <= 0.5 ? 8.0 : Math.Min(8.0, TargetDispatchMs / ms);
            iterations = (uint)Math.Min(1u << 20, Math.Max(iterations * 2.0, iterations * scale));
        }
    }

    private double TimeDispatch(uint iterations, uint seed)
    {
        SetParams(new Params { ElementCount = StressThreads, Seed = seed, Iterations = iterations });
        _ctx.CSSetShader(_stress);
        var sw = Stopwatch.StartNew();
        _ctx.Dispatch(StressGroups, 1, 1);
        ReadResult();
        return sw.Elapsed.TotalMilliseconds;
    }

    // ------------------------------------------------------------------
    // VRAM
    // ------------------------------------------------------------------

    public void RunVramTest(GpuTestResult result, TimeSpan budget, IProgress<double>? progress, CancellationToken ct)
    {
        if (DedicatedVideoMemory < MinDedicatedForVramTest)
        {
            result.VramTestSkipped = true;
            result.VramSkipReason = S(
                "GPU tích hợp dùng chung RAM hệ thống, không có VRAM riêng để kiểm. Hãy kiểm RAM bằng MemTest86.",
                "Integrated GPUs share system RAM and have no dedicated VRAM to test. Check RAM with MemTest86 instead.");
            return;
        }

        var target = (long)(DedicatedVideoMemory * 0.8);

        // Chừa chỗ cho Windows và các ứng dụng đang mở. Chiếm vượt ngân sách thì
        // Windows lặng lẽ đẩy bớt dữ liệu sang RAM hệ thống, và ta sẽ kiểm nhầm RAM.
        using (var adapter3 = _adapter.QueryInterfaceOrNull<IDXGIAdapter3>())
        {
            if (adapter3 is not null)
            {
                var info = adapter3.QueryVideoMemoryInfo(0, MemorySegmentGroup.Local);
                var free = (long)info.Budget - (long)info.CurrentUsage - 256L * 1024 * 1024;
                if (free > 0) target = Math.Min(target, free);
            }
        }

        var chunks = new List<(ID3D11Buffer Buffer, ID3D11UnorderedAccessView Uav)>();
        try
        {
            while ((long)(chunks.Count + 1) * ChunkBytes <= target)
            {
                ct.ThrowIfCancellationRequested();
                ID3D11Buffer buffer;
                try { buffer = CreateRawBuffer(ChunkBytes); }
                catch (SharpGenException ex) when (!DeviceLostCodes.Contains(ex.ResultCode.Code))
                {
                    break; // hết chỗ: kiểm phần đã cấp được
                }
                chunks.Add((buffer, CreateRawUav(buffer, ChunkBytes)));
            }

            if (chunks.Count == 0)
            {
                result.VramTestSkipped = true;
                result.VramSkipReason = S(
                    "Không cấp phát được vùng VRAM nào để kiểm (card đang bị ứng dụng khác chiếm dụng?).",
                    "Could not allocate any VRAM to test (is another application holding the card?).");
                return;
            }

            result.VramTestedBytes = (long)chunks.Count * ChunkBytes;

            const uint elements = ChunkBytes / 4;
            var groups = (elements + GpuShaders.ThreadsPerGroup * GpuShaders.ElementsPerThread - 1)
                         / (GpuShaders.ThreadsPerGroup * GpuShaders.ElementsPerThread);
            var stride = groups * GpuShaders.ThreadsPerGroup;

            _ctx.CSSetConstantBuffer(0, _params);

            const int minPasses = 2, maxPasses = 8;
            var sw = Stopwatch.StartNew();
            var workUnits = chunks.Count * 2;

            for (var pass = 0; pass < maxPasses; pass++)
            {
                if (pass >= minPasses && sw.Elapsed > budget) break;

                // Lượt chẵn ghi mẫu gốc, lượt lẻ ghi mẫu đảo bit: mỗi bit đều được thử ở cả 0 lẫn 1.
                var p = new Params
                {
                    ElementCount = elements,
                    Seed = unchecked(0x9E3779B9u * (uint)(pass + 1)),
                    Invert = (uint)(pass & 1),
                    Stride = stride
                };
                SetParams(p);

                // Ghi toàn bộ trước rồi mới đọc: dữ liệu phải nằm yên trên chip nhớ một lúc,
                // trong khi các khối khác đang bị ghi, thì lỗi rò và lỗi giữ dữ liệu mới lộ ra.
                _ctx.CSSetShader(_fill);
                for (var c = 0; c < chunks.Count; c++)
                {
                    ct.ThrowIfCancellationRequested();
                    BindData(chunks[c].Uav);
                    _ctx.Dispatch(groups, 1, 1);
                    if (c % 4 == 3) ReadResult(); // đồng bộ định kỳ để không dồn lệnh quá lâu
                    ReportPass(pass, c, workUnits, sw, budget, minPasses, progress);
                }

                _ctx.CSSetShader(_verify);
                for (var c = 0; c < chunks.Count; c++)
                {
                    ct.ThrowIfCancellationRequested();
                    BindData(chunks[c].Uav);
                    ClearResult();
                    _ctx.Dispatch(groups, 1, 1);
                    var r = ReadResult();

                    if (r[0] > 0)
                    {
                        result.VramErrors += r[0];
                        result.VramFirstErrorOffset ??= (long)c * ChunkBytes + (long)r[1] * 4;
                    }
                    ReportPass(pass, chunks.Count + c, workUnits, sw, budget, minPasses, progress);
                }

                result.VramPasses = pass + 1;
            }
        }
        finally
        {
            foreach (var (buffer, uav) in chunks)
            {
                uav.Dispose();
                buffer.Dispose();
            }
        }
    }

    private static void ReportPass(int pass, int unit, int unitsPerPass, Stopwatch sw, TimeSpan budget,
        int minPasses, IProgress<double>? progress)
    {
        if (progress is null) return;
        var byPasses = (pass + (unit + 1) / (double)unitsPerPass) / minPasses;
        var byTime = sw.Elapsed / budget;
        progress.Report(Math.Clamp(Math.Max(byPasses, byTime), 0, 1));
    }

    // ------------------------------------------------------------------
    // Tiện ích
    // ------------------------------------------------------------------

    private void BindData(ID3D11UnorderedAccessView uav) =>
        _ctx.CSSetUnorderedAccessViews(0, new[] { uav, _resultUav });

    private void SetParams(Params p) => _ctx.UpdateSubresource(in p, _params);

    private void ClearResult() => _ctx.UpdateSubresource(new uint[4], _result);

    /// <summary>Đọc bộ đếm lỗi về CPU. Map chờ GPU làm xong mọi lệnh trước đó nên đây cũng là điểm đồng bộ.</summary>
    private uint[] ReadResult()
    {
        _ctx.CopyResource(_resultStaging, _result);
        var mapped = _ctx.Map(_resultStaging, 0, MapMode.Read);
        try
        {
            var values = new uint[4];
            for (var i = 0; i < 4; i++)
                values[i] = unchecked((uint)Marshal.ReadInt32(mapped.DataPointer, i * 4));
            return values;
        }
        finally
        {
            _ctx.Unmap(_resultStaging, 0);
        }
    }

    private ID3D11ComputeShader CreateShader(string entryPoint)
    {
        var bytecode = Compiler.Compile(GpuShaders.Source, entryPoint, "HardwareInspectorGpuTest.hlsl",
            "cs_5_0", ShaderFlags.OptimizationLevel3);
        return _device.CreateComputeShader(bytecode.Span);
    }

    private ID3D11Buffer CreateRawBuffer(uint bytes) =>
        _device.CreateBuffer(new BufferDescription(bytes, BindFlags.UnorderedAccess, ResourceUsage.Default,
            CpuAccessFlags.None, ResourceOptionFlags.BufferAllowRawViews));

    private ID3D11UnorderedAccessView CreateRawUav(ID3D11Buffer buffer, uint bytes) =>
        _device.CreateUnorderedAccessView(buffer,
            new UnorderedAccessViewDescription(buffer, Format.R32_Typeless, 0, bytes / 4, BufferUnorderedAccessViewFlags.Raw));

    private static double Average(IEnumerable<double> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? 0 : list.Average();
    }

    public void Dispose()
    {
        try { _ctx.ClearState(); } catch { }
        _resultUav.Dispose();
        _resultStaging.Dispose();
        _result.Dispose();
        _params.Dispose();
        _verify.Dispose();
        _fill.Dispose();
        _compare.Dispose();
        _stress.Dispose();
        _ctx.Dispose();
        _device.Dispose();
        _adapter.Dispose();
    }
}
