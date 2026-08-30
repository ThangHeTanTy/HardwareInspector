using System.Text.Json;
using System.Text.Json.Serialization;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Assessment;

namespace HardwareInspector.Reporting.Exporters;

/// <summary>Xuất toàn bộ dữ liệu thô để đối chiếu hoặc đưa vào hệ thống khác.</summary>
public sealed class JsonReportExporter : IReportExporter
{
    public string Format => "JSON";
    public string FileExtension => ".json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<string> ExportAsync(MachineAssessment assessment, SystemSnapshot snapshot,
        string outputPath, CancellationToken ct = default)
    {
        var payload = new
        {
            generatedAt = assessment.GeneratedAtLocal,
            machine = assessment.MachineTitle,
            overallScore = assessment.OverallScore,
            overallRating = assessment.OverallRating.ToString(),
            summary = assessment.Summary,
            trust = assessment.Trust,
            components = assessment.Components,
            snapshot
        };

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await using var stream = File.Create(outputPath);
        await JsonSerializer.SerializeAsync(stream, payload, Options, ct);
        return outputPath;
    }
}
