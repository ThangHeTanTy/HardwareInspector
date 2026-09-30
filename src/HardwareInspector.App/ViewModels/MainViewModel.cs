using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using HardwareInspector.App.Services;
using HardwareInspector.Analysis;
using HardwareInspector.Benchmark;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Assessment;
using HardwareInspector.Core.Models.Components;
using HardwareInspector.Core.Localization;
using HardwareInspector.App.Localization;
using HardwareInspector.Core.Utils;
using HardwareInspector.Reporting.Exporters;

namespace HardwareInspector.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly InspectionPipeline _pipeline = new();
    private readonly MachineAssessmentService _assessmentService = new();
    private readonly BenchmarkRunner _benchmarkRunner = new();
    private CancellationTokenSource? _cts;

    private SystemSnapshot? _snapshot;
    private MachineAssessment? _assessment;
    private ComponentAssessment? _selectedComponent;
    private DisplayInfo? _selectedDisplay;
    private LanguageOption _selectedLanguage = Loc.Options[0];
    private string _status = UiStrings.Instance["Status.Ready"];
    private double _progress;
    private string _stage = string.Empty;
    private bool _isBusy;

    public MainViewModel()
    {
        ScanCommand = new RelayCommand(ScanAsync, () => !IsBusy);
        StressCommand = new RelayCommand(RunStressAsync, () => !IsBusy);
        ScreenTestCommand = new RelayCommand(OpenScreenTest, () => !IsBusy);
        ExportHtmlCommand = new RelayCommand(() => ExportAsync(new HtmlReportExporter()), () => HasResult && !IsBusy);
        ExportJsonCommand = new RelayCommand(() => ExportAsync(new JsonReportExporter()), () => HasResult && !IsBusy);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
        CopyComponentCommand = new RelayCommand(CopySelectedComponent, () => SelectedComponent is not null);

        _selectedLanguage = Loc.Options.First(o => o.Language == Loc.Current);
        _ = _pipeline.StartSensorsAsync();
    }

    private static UiStrings Ui => UiStrings.Instance;

    public IReadOnlyList<LanguageOption> Languages => Loc.Options;

    /// <summary>
    /// Đổi ngôn ngữ.
    ///
    /// Câu chữ trong phần phân tích được sinh ra lúc chấm điểm chứ không phải lúc hiển thị,
    /// nên phải chấm điểm lại thì chúng mới đổi theo. Việc này chỉ đọc từ ảnh chụp phần cứng
    /// đã có sẵn, không đụng tới thiết bị, nên chạy tức thì và không cần quét lại máy.
    /// </summary>
    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is null || !Set(ref _selectedLanguage, value)) return;

            // Gán Loc.Current phát sự kiện, mọi thực thể UiStrings tự làm mới binding.
            Loc.Current = value.Language;
            RebuildLocalizedContent();
        }
    }

    private void RebuildLocalizedContent()
    {
        if (_snapshot is null)
        {
            Status = UiStrings.Instance["Status.Ready"];
            return;
        }

        var assessment = _assessmentService.Assess(_snapshot);
        Assessment = assessment;

        var selectedKind = SelectedComponent?.Kind;
        Components.Clear();
        foreach (var c in assessment.Components) Components.Add(c);
        SelectedComponent = Components.FirstOrDefault(c => c.Kind == selectedKind)
                            ?? Components.FirstOrDefault();

        WarrantyLookups.Clear();
        foreach (var w in assessment.WarrantyLookups) WarrantyLookups.Add(w);

        Status = assessment.Summary;
    }

    public ObservableCollection<ComponentAssessment> Components { get; } = new();
    public ObservableCollection<BenchmarkResult> BenchmarkResults { get; } = new();
    public ObservableCollection<DisplayInfo> Displays { get; } = new();
    public ObservableCollection<WarrantyLookup> WarrantyLookups { get; } = new();

    public RelayCommand ScanCommand { get; }
    public RelayCommand StressCommand { get; }
    public RelayCommand ScreenTestCommand { get; }
    public RelayCommand ExportHtmlCommand { get; }
    public RelayCommand ExportJsonCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand CopyComponentCommand { get; }

    public MachineAssessment? Assessment
    {
        get => _assessment;
        private set
        {
            Set(ref _assessment, value);
            OnPropertyChanged(nameof(HasResult));
            OnPropertyChanged(nameof(OverallScoreText));
            OnPropertyChanged(nameof(OverallRatingText));
            OnPropertyChanged(nameof(OverallColor));
            OnPropertyChanged(nameof(TrustLevelText));
            OnPropertyChanged(nameof(TrustVerdict));
            OnPropertyChanged(nameof(FirmwareStatusText));
            OnPropertyChanged(nameof(FirmwareColor));
            OnPropertyChanged(nameof(MachineTitle));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(CrossChecks));
            OnPropertyChanged(nameof(ManualChecklist));
            OnPropertyChanged(nameof(FirmwareFindings));
        }
    }

    public ComponentAssessment? SelectedComponent
    {
        get => _selectedComponent;
        set
        {
            if (Set(ref _selectedComponent, value)) CopyComponentCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Màn hình mà bài kiểm tra màu sẽ chạy lên.</summary>
    public DisplayInfo? SelectedDisplay
    {
        get => _selectedDisplay;
        set => Set(ref _selectedDisplay, value);
    }

    public bool HasMultipleDisplays => Displays.Count > 1;

    public string Status { get => _status; private set => Set(ref _status, value); }

    public double Progress
    {
        get => _progress;
        private set
        {
            if (Set(ref _progress, value)) OnPropertyChanged(nameof(ProgressText));
        }
    }

    /// <summary>Tên bước đang chạy, hiển thị cạnh thanh tiến độ.</summary>
    public string Stage
    {
        get => _stage;
        private set => Set(ref _stage, value);
    }

    public string ProgressText => $"{_progress:0}%";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            ScanCommand.RaiseCanExecuteChanged();
            StressCommand.RaiseCanExecuteChanged();
            ScreenTestCommand.RaiseCanExecuteChanged();
            ExportHtmlCommand.RaiseCanExecuteChanged();
            ExportJsonCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasResult => _assessment is not null;
    public string MachineTitle => _assessment?.MachineTitle ?? Environment.MachineName;
    public string SummaryText => _assessment?.Summary ?? string.Empty;
    public string OverallScoreText => _assessment is null ? "—" : _assessment.OverallScore.ToString();
    public string OverallRatingText => _assessment?.OverallRating.ToText() ?? "Chưa kiểm tra";
    public string OverallColor => _assessment?.OverallRating.ToHexColor() ?? "#8A8F98";

    public string TrustLevelText => _assessment is null
        ? "—"
        : $"{_assessment.Trust.Level.ToText()} ({_assessment.Trust.TrustScore}/100)";

    public string TrustVerdict => _assessment?.Trust.Verdict ?? string.Empty;

    public string FirmwareStatusText => _assessment?.Firmware.Status.ToText() ?? "Chưa kiểm tra";

    public string FirmwareColor => _assessment?.Firmware.Status switch
    {
        IntegrityStatus.Clean => "#2FBF71",
        IntegrityStatus.Unverified => "#8A8F98",
        IntegrityStatus.Suspicious => "#F4A259",
        IntegrityStatus.Compromised => "#E5544B",
        _ => "#8A8F98"
    };

    public IEnumerable<CrossCheckResult> CrossChecks =>
        _assessment?.Trust.CrossChecks ?? Enumerable.Empty<CrossCheckResult>();

    public IEnumerable<ManualCheckItem> ManualChecklist =>
        _assessment?.Trust.ManualChecklist ?? Enumerable.Empty<ManualCheckItem>();

    public IEnumerable<Finding> FirmwareFindings =>
        (_assessment?.Firmware.Findings ?? new List<Finding>())
        .Where(f => f.Severity >= Severity.Notice)
        .OrderByDescending(f => f.Severity);

    // -----------------------------------------------------------------

    private async Task ScanAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        Components.Clear();

        Stage = Ui["Status.Starting"];
        Progress = 0;

        var progress = new Progress<InspectionProgress>(p =>
        {
            Stage = p.Stage;
            Progress = p.Fraction * 100;
        });

        try
        {
            var (snapshot, assessment) = await _pipeline.RunAsync(progress, _cts.Token);
            _snapshot = snapshot;
            Assessment = assessment;

            foreach (var c in assessment.Components) Components.Add(c);
            SelectedComponent = Components.FirstOrDefault();

            Displays.Clear();
            foreach (var d in snapshot.Displays) Displays.Add(d);
            SelectedDisplay = Displays.FirstOrDefault(d => d.IsPrimary) ?? Displays.FirstOrDefault();
            OnPropertyChanged(nameof(HasMultipleDisplays));

            WarrantyLookups.Clear();
            foreach (var w in assessment.WarrantyLookups) WarrantyLookups.Add(w);

            Status = assessment.Summary;
        }
        catch (OperationCanceledException)
        {
            Status = Ui["Status.Cancelled"];
        }
        catch (Exception ex)
        {
            Status = string.Format(Ui["Msg.ScanFailed"], ex.Message);
        }
        finally
        {
            Progress = 100;
            Stage = string.Empty;
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// Chạy tải nặng rồi quét lại. Đây là bước quan trọng nhất khi thẩm định máy cũ:
    /// mọi vấn đề tản nhiệt chỉ lộ ra khi máy đã nóng thật sự.
    /// </summary>
    private async Task RunStressAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        BenchmarkResults.Clear();
        _pipeline.SensorMonitor.ResetStatistics();
        _pipeline.LastGpuTest = null;

        Stage = Ui["Status.StressPrep"];
        Progress = 0;

        var progress = new Progress<(string Stage, double Progress)>(p =>
        {
            Stage = p.Stage;
            Progress = p.Progress * 100;
        });

        try
        {
            var results = await _benchmarkRunner.RunAllAsync(progress, _cts.Token);
            foreach (var r in results) BenchmarkResults.Add(r);

            _pipeline.LastGpuTest = _benchmarkRunner.Benchmarks
                .OfType<GpuStressBenchmark>()
                .FirstOrDefault()?.LastResult;

            Status = Ui["Status.Rescan"];

            var rescan = new Progress<InspectionProgress>(p =>
            {
                Stage = p.Stage;
                Progress = p.Fraction * 100;
            });
            var (snapshot, assessment) = await _pipeline.RunAsync(rescan, _cts.Token);
            _snapshot = snapshot;
            Assessment = assessment;

            Components.Clear();
            foreach (var c in assessment.Components) Components.Add(c);
            SelectedComponent = Components.FirstOrDefault();

            Status = Ui["Status.StressDone"];
        }
        catch (OperationCanceledException)
        {
            Status = Ui["Status.StressStopped"];
        }
        catch (Exception ex)
        {
            Status = string.Format(Ui["Msg.StressFailed"], ex.Message);
        }
        finally
        {
            Progress = 100;
            Stage = string.Empty;
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void OpenScreenTest()
    {
        // Không đặt Owner: cửa sổ con luôn bị kéo về cùng màn hình với cửa sổ cha,
        // nên sẽ không sang được màn thứ hai.
        var window = new Views.ScreenTestWindow(SelectedDisplay);
        window.Show();
        window.Activate();
        window.Focus();
    }

    /// <summary>
    /// Xuất toàn bộ chi tiết linh kiện ra dạng văn bản thuần.
    /// Bôi đen từng dòng vẫn được, nhưng khi cần gửi cho người khác xem thì
    /// chép cả khối nhanh hơn nhiều, và giữ được thứ tự lẫn ngữ cảnh.
    /// </summary>
    private void CopySelectedComponent()
    {
        if (SelectedComponent is not { } c) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{MachineAssessmentService.Translate(c.Kind)} — {c.DisplayName}");
        sb.AppendLine(c.Headline);
        sb.AppendLine(new string('-', 60));

        if (c.Metrics.Count > 0)
        {
            sb.AppendLine(Ui["Copy.Specs"]);
            foreach (var m in c.Metrics)
            {
                sb.AppendLine($"  {m.Label}: {m.Value}");
                if (!string.IsNullOrWhiteSpace(m.Note)) sb.AppendLine($"    ({m.Note})");
            }
            sb.AppendLine();
        }

        if (c.Sensors.Count > 0)
        {
            sb.AppendLine(Ui["Copy.Sensors"]);
            foreach (var x in c.Sensors)
                sb.AppendLine($"  {x.SensorName}: {x.Min:0.#} / {x.Average:0.#} / {x.Max:0.#} {x.Unit}");
            sb.AppendLine();
        }

        var findings = c.Findings.Where(f => f.Severity >= Severity.Info).ToList();
        if (findings.Count > 0)
        {
            sb.AppendLine(Ui["Copy.Findings"]);
            var actionLabel = Ui["Copy.Action"];
            foreach (var f in findings)
            {
                sb.AppendLine($"  [{f.Severity.ToText()}] {f.Title}");
                if (!string.IsNullOrWhiteSpace(f.Detail)) sb.AppendLine($"    {f.Detail}");
                if (!string.IsNullOrWhiteSpace(f.Recommendation))
                    sb.AppendLine($"    {actionLabel}: {f.Recommendation}");
            }
        }

        try
        {
            System.Windows.Clipboard.SetText(sb.ToString());
            Status = string.Format(Ui["Msg.DetailCopied"], c.DisplayName);
        }
        catch (Exception ex)
        {
            Status = string.Format(Ui["Msg.CopyFailed"], ex.Message);
        }
    }

    /// <summary>Mở trang tra bảo hành, chép sẵn serial vào clipboard vì nhiều hãng bắt nhập tay.</summary>
    public void OpenWarranty(WarrantyLookup lookup)
    {
        try
        {
            if (lookup.HasSerial)
            {
                System.Windows.Clipboard.SetText(lookup.Serial!);
                Status = string.Format(Ui["Msg.SerialCopied"], lookup.Serial);
            }

            if (!string.IsNullOrWhiteSpace(lookup.Url))
                Process.Start(new ProcessStartInfo(lookup.Url) { UseShellExecute = true });
            else
                Status = Ui["Status.NoWarrantyLink"];
        }
        catch (Exception ex)
        {
            Status = string.Format(Ui["Msg.LookupFailed"], ex.Message);
        }
    }

    /// <summary>Mở trang chính thức của một công cụ kiểm tra trong trình duyệt mặc định.</summary>
    public void OpenTool(ToolLink tool)
    {
        try
        {
            Process.Start(new ProcessStartInfo(tool.Url) { UseShellExecute = true });
            Status = string.Format(Ui["Msg.ToolOpened"], tool.Name, tool.Url);
        }
        catch (Exception ex)
        {
            Status = string.Format(Ui["Msg.LookupFailed"], ex.Message);
        }
    }

    private async Task ExportAsync(IReportExporter exporter)
    {
        if (_assessment is null || _snapshot is null) return;

        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "HardwareInspector");
            var safeName = string.Join("_", MachineTitle.Split(Path.GetInvalidFileNameChars()));
            var file = Path.Combine(folder,
                $"BienBanKiemTra_{safeName}_{DateTime.Now:yyyyMMdd_HHmm}{exporter.FileExtension}");

            var path = await exporter.ExportAsync(_assessment, _snapshot, file);
            Status = string.Format(Ui["Msg.Saved"], exporter.Format, path);

            if (exporter.FileExtension == ".html")
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Status = string.Format(Ui["Msg.ExportFailed"], ex.Message);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _pipeline.Dispose();
    }
}
