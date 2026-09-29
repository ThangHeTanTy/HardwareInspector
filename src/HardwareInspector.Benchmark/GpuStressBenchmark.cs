using HardwareInspector.Benchmark.Gpu;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Diagnostics;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Benchmark;

/// <summary>
/// Tải nặng GPU rồi kiểm VRAM ngay khi card còn nóng.
///
/// Thứ tự có chủ đích: chip nhớ hỏng nhẹ thường chỉ trả sai dữ liệu khi đã nóng,
/// nên kiểm VRAM lúc card nguội sẽ bỏ sót đúng loại lỗi hay gặp nhất ở card cũ
/// (card từng đào coin, pad tản nhiệt VRAM đã chai).
///
/// Trong lúc chạy, vòng đo cảm biến vẫn tiếp tục ghi nhiệt độ nhân, điểm nóng,
/// VRAM và tốc độ quạt; các phép kiểm tra nhiệt trong GraphicsAnalyzer nhờ đó
/// mới có số liệu dưới tải để làm việc.
/// </summary>
public sealed class GpuStressBenchmark : IBenchmark
{
    private readonly TimeSpan _stressDuration;
    private readonly TimeSpan _vramBudget;

    public GpuStressBenchmark(TimeSpan? stressDuration = null, TimeSpan? vramBudget = null)
    {
        _stressDuration = stressDuration ?? TimeSpan.FromMinutes(5);
        _vramBudget = vramBudget ?? TimeSpan.FromSeconds(90);
    }

    public string Name => S("Tải nặng GPU + kiểm tra VRAM", "GPU stress + VRAM test");
    public ComponentKind Target => ComponentKind.Gpu;
    public TimeSpan EstimatedDuration => _stressDuration + _vramBudget;

    /// <summary>Kết quả chi tiết của lượt chạy gần nhất, để đưa vào snapshot cho analyzer chấm điểm.</summary>
    public GpuTestResult? LastResult { get; private set; }

    public async Task<BenchmarkResult> RunAsync(IProgress<double>? progress, CancellationToken ct = default)
    {
        LastResult = null;
        var result = new GpuTestResult();

        // Luồng riêng, chạy dài: mỗi lệnh đồng bộ với GPU chặn luồng hàng chục mili giây,
        // không nên chiếm luồng của thread pool trong nhiều phút.
        await Task.Factory.StartNew(() => Run(result, progress, ct),
            ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        LastResult = result;
        progress?.Report(1);
        return ToBenchmarkResult(result);
    }

    private void Run(GpuTestResult result, IProgress<double>? progress, CancellationToken ct)
    {
        D3D11GpuTester tester;
        try
        {
            tester = D3D11GpuTester.Create();
        }
        catch (Exception ex)
        {
            result.SetupError = ex.Message;
            return;
        }

        var stressShare = _stressDuration / (_stressDuration + _vramBudget);
        var stressProgress = new Progress<double>(p => progress?.Report(p * stressShare));
        var vramProgress = new Progress<double>(p => progress?.Report(stressShare + p * (1 - stressShare)));

        try
        {
            result.AdapterName = tester.AdapterName;
            result.DedicatedVideoMemoryBytes = tester.DedicatedVideoMemory;

            tester.RunStress(result, _stressDuration, stressProgress, ct);
            tester.RunVramTest(result, _vramBudget, vramProgress, ct);
            result.Completed = true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (D3D11GpuTester.IsDeviceLost(ex))
        {
            result.DeviceLost = true;
            result.DeviceLostReason = tester.DescribeDeviceLoss(ex);
        }
        catch (Exception ex)
        {
            result.SetupError = ex.Message;
        }
        finally
        {
            try { tester.Dispose(); } catch { }
        }
    }

    private BenchmarkResult ToBenchmarkResult(GpuTestResult r)
    {
        if (r.SetupError is not null && !r.DeviceLost)
            return new BenchmarkResult(Name, "—", 0, S("Không chạy được: {0}", "Could not run: {0}", r.SetupError));

        var parts = new List<string> { r.AdapterName };

        if (r.DeviceLost)
            parts.Add(S("DRIVER BỊ RESET GIỮA CHỪNG ({0})", "DRIVER RESET MID-TEST ({0})", r.DeviceLostReason));

        if (r.ComputeChecks > 0)
            parts.Add(S("{0:0.#} phút tải, {1} lần đối chiếu tính toán, {2} lần lệch",
                "{0:0.#} min under load, {1} compute cross-checks, {2} mismatches",
                r.StressDuration.TotalMinutes, r.ComputeChecks, r.ComputeMismatches));

        if (r.ThroughputRetention is { } keep)
            parts.Add(S("hiệu năng cuối bài giữ được {0:0}%", "{0:0}% of performance held at the end", keep * 100));

        if (r.VramTestSkipped)
            parts.Add(r.VramSkipReason ?? S("bỏ qua kiểm tra VRAM", "VRAM test skipped"));
        else if (r.VramTestedBytes > 0)
            parts.Add(S("VRAM {0:0.0} GB × {1} lượt: {2} lỗi", "VRAM {0:0.0} GB × {1} passes: {2} errors",
                r.VramTestedBytes / 1024.0 / 1024 / 1024, r.VramPasses, r.VramErrors));

        return new BenchmarkResult(
            Name,
            S("tỉ phép/giây", "GFLOP/s"),
            Math.Round(r.FinalThroughput, 1),
            string.Join(" · ", parts),
            new Dictionary<string, string>
            {
                ["Adapter"] = r.AdapterName,
                ["ComputeChecks"] = r.ComputeChecks.ToString(),
                ["ComputeMismatches"] = r.ComputeMismatches.ToString(),
                ["VramTestedBytes"] = r.VramTestedBytes.ToString(),
                ["VramPasses"] = r.VramPasses.ToString(),
                ["VramErrors"] = r.VramErrors.ToString(),
                ["DeviceLost"] = r.DeviceLost.ToString()
            });
    }
}
