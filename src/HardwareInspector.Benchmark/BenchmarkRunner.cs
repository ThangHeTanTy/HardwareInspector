using HardwareInspector.Core.Abstractions;

namespace HardwareInspector.Benchmark;

/// <summary>Chạy tuần tự các bài kiểm tra và báo tiến độ tổng.</summary>
public sealed class BenchmarkRunner
{
    private readonly IReadOnlyList<IBenchmark> _benchmarks;

    public BenchmarkRunner(IEnumerable<IBenchmark>? benchmarks = null)
        => _benchmarks = benchmarks?.ToList() ?? new List<IBenchmark>
        {
            new CpuStressBenchmark(),
            new MemoryBandwidthBenchmark(),
            new StorageSpeedBenchmark(),
            new GpuStressBenchmark()
        };

    public IReadOnlyList<IBenchmark> Benchmarks => _benchmarks;

    public async Task<IReadOnlyList<BenchmarkResult>> RunAllAsync(
        IProgress<(string Stage, double Progress)>? progress = null,
        CancellationToken ct = default)
    {
        var results = new List<BenchmarkResult>();

        for (var i = 0; i < _benchmarks.Count; i++)
        {
            var benchmark = _benchmarks[i];
            var index = i;

            var inner = new Progress<double>(p =>
                progress?.Report((benchmark.Name, (index + p) / _benchmarks.Count)));

            try
            {
                results.Add(await benchmark.RunAsync(inner, ct));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                results.Add(new BenchmarkResult(benchmark.Name, "—", 0, $"Không chạy được: {ex.Message}"));
            }
        }

        return results;
    }
}
