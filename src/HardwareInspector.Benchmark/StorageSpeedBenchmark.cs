using System.Diagnostics;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Benchmark;

/// <summary>
/// Đo tốc độ đọc/ghi tuần tự trên ổ chứa hệ điều hành.
/// Ghi bằng FileOptions.WriteThrough để bỏ qua cache của Windows,
/// nếu không con số đo được sẽ là tốc độ RAM chứ không phải tốc độ ổ.
/// </summary>
public sealed class StorageSpeedBenchmark : IBenchmark
{
    private const int FileSizeMb = 512;
    private const int ChunkSize = 4 * 1024 * 1024;

    public string Name => S("Tốc độ đọc/ghi tuần tự", "Sequential read/write speed");
    public ComponentKind Target => ComponentKind.Storage;
    public TimeSpan EstimatedDuration => TimeSpan.FromSeconds(30);

    public async Task<BenchmarkResult> RunAsync(IProgress<double>? progress, CancellationToken ct = default)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hwinspect_{Guid.NewGuid():N}.tmp");
        var chunk = new byte[ChunkSize];
        Random.Shared.NextBytes(chunk);
        var chunks = FileSizeMb * 1024 * 1024 / ChunkSize;

        double writeMbps, readMbps;

        try
        {
            var sw = Stopwatch.StartNew();
            await using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
                             ChunkSize, FileOptions.WriteThrough | FileOptions.SequentialScan))
            {
                for (var i = 0; i < chunks; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    await fs.WriteAsync(chunk, ct);
                    progress?.Report(0.5 * (i + 1) / chunks);
                }
                await fs.FlushAsync(ct);
            }
            sw.Stop();
            writeMbps = FileSizeMb / sw.Elapsed.TotalSeconds;

            sw.Restart();
            var readBuffer = new byte[ChunkSize];
            await using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None,
                             ChunkSize, FileOptions.SequentialScan))
            {
                var index = 0;
                while (await fs.ReadAsync(readBuffer, ct) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report(0.5 + 0.5 * (++index) / chunks);
                }
            }
            sw.Stop();
            readMbps = FileSizeMb / sw.Elapsed.TotalSeconds;
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        progress?.Report(1);

        return BenchmarkResult.Localized(() => new BenchmarkResult(
            Name, "MB/s", Math.Round(readMbps, 0),
            S($"Đọc {readMbps:0} MB/s · Ghi {writeMbps:0} MB/s trên ổ chứa thư mục tạm",
              $"Read {readMbps:0} MB/s · Write {writeMbps:0} MB/s on the temp folder's drive"),
            new Dictionary<string, string>
            {
                ["ReadMBps"] = readMbps.ToString("0"),
                ["WriteMBps"] = writeMbps.ToString("0"),
                ["FileSizeMB"] = FileSizeMb.ToString()
            }));
    }
}
