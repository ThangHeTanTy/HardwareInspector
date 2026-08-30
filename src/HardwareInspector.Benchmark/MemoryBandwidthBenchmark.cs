using System.Diagnostics;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;

namespace HardwareInspector.Benchmark;

/// <summary>
/// Đo băng thông bộ nhớ bằng phép sao chép khối lớn.
/// Kết quả thấp bất thường so với loại RAM đã khai báo là chỉ dấu
/// máy đang chạy single channel hoặc RAM bị hạ xung.
/// </summary>
public sealed class MemoryBandwidthBenchmark : IBenchmark
{
    private const int BufferMb = 256;
    private const int Iterations = 12;

    public string Name => "Băng thông bộ nhớ";
    public ComponentKind Target => ComponentKind.Memory;
    public TimeSpan EstimatedDuration => TimeSpan.FromSeconds(15);

    public Task<BenchmarkResult> RunAsync(IProgress<double>? progress, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var size = BufferMb * 1024 * 1024;
            var source = new byte[size];
            var destination = new byte[size];
            Random.Shared.NextBytes(source.AsSpan(0, Math.Min(1 << 20, size)));

            // Một lượt khởi động để trang bộ nhớ được cấp phát thật sự.
            Buffer.BlockCopy(source, 0, destination, 0, size);

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < Iterations; i++)
            {
                ct.ThrowIfCancellationRequested();
                Buffer.BlockCopy(source, 0, destination, 0, size);
                progress?.Report((i + 1) / (double)Iterations);
            }
            sw.Stop();

            // Mỗi lượt copy chạm bộ nhớ hai lần: một lần đọc, một lần ghi.
            var totalGb = Iterations * (size * 2L) / (1024.0 * 1024 * 1024);
            var bandwidth = totalGb / sw.Elapsed.TotalSeconds;

            return new BenchmarkResult(
                Name, "GB/s", Math.Round(bandwidth, 2),
                $"{Iterations} lượt sao chép khối {BufferMb} MB");
        }, ct);
    }
}
