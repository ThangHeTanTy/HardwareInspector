using System.Diagnostics;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;

namespace HardwareInspector.Benchmark;

/// <summary>
/// Ép CPU chạy hết công suất trên mọi luồng. Mục tiêu không phải là chấm điểm hiệu năng
/// mà là dựng lên một tình huống tải nặng để cảm biến nhiệt độ và xung nhịp
/// bộc lộ vấn đề tản nhiệt hoặc hiện tượng tự hạ xung.
///
/// Bài toán dùng ở đây là tính số nguyên tố bằng phép chia thử — chọn có chủ đích
/// vì nó chạy hoàn toàn trong ALU, không phụ thuộc bộ nhớ, nên tải sinh ra ổn định.
/// </summary>
public sealed class CpuStressBenchmark : IBenchmark
{
    private readonly TimeSpan _duration;

    public CpuStressBenchmark(TimeSpan? duration = null)
        => _duration = duration ?? TimeSpan.FromSeconds(60);

    public string Name => "Tải nặng CPU toàn bộ nhân";
    public ComponentKind Target => ComponentKind.Cpu;
    public TimeSpan EstimatedDuration => _duration;

    public async Task<BenchmarkResult> RunAsync(IProgress<double>? progress, CancellationToken ct = default)
    {
        var threads = Environment.ProcessorCount;
        var totalOps = 0L;
        var sw = Stopwatch.StartNew();

        var reporter = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested && sw.Elapsed < _duration)
            {
                progress?.Report(Math.Clamp(sw.Elapsed.TotalSeconds / _duration.TotalSeconds, 0, 1));
                try { await Task.Delay(250, ct); } catch { break; }
            }
        }, ct);

        var workers = Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            var local = 0L;
            var candidate = 100_000L;
            while (!ct.IsCancellationRequested && sw.Elapsed < _duration)
            {
                if (IsPrime(candidate)) local++;
                candidate++;
            }
            Interlocked.Add(ref totalOps, local);
        }, ct)).ToArray();

        try { await Task.WhenAll(workers); }
        catch (OperationCanceledException) { }

        await reporter;
        sw.Stop();
        progress?.Report(1);

        var score = totalOps / Math.Max(sw.Elapsed.TotalSeconds, 0.001);

        return new BenchmarkResult(
            Name,
            "số nguyên tố/giây",
            Math.Round(score, 1),
            $"{threads} luồng chạy trong {sw.Elapsed.TotalSeconds:0} giây",
            new Dictionary<string, string>
            {
                ["Threads"] = threads.ToString(),
                ["TotalPrimes"] = totalOps.ToString("N0"),
                ["Seconds"] = sw.Elapsed.TotalSeconds.ToString("0.0")
            });
    }

    private static bool IsPrime(long n)
    {
        if (n < 2) return false;
        if (n % 2 == 0) return n == 2;
        var limit = (long)Math.Sqrt(n);
        for (var i = 3L; i <= limit; i += 2)
            if (n % i == 0) return false;
        return true;
    }
}
