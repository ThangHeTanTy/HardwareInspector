using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using HardwareInspector.App.Rendering;
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

/// <summary>
/// Một dòng trong bảng thông số linh kiện. Dòng mở đầu phần thông tin của ổ thứ hai,
/// màn thứ hai... mang theo mô hình 3D của chính thiết bị đó, đặt ngay cạnh dòng.
/// </summary>
public sealed record MetricRowView(MetricLine Metric, ComponentModelSpec? Spec, string? StateKey);

/// <summary>Một ô thông tin nhanh ở phần kết luận: tên linh kiện và vài thông số chính.</summary>
public sealed record QuickFact(string Label, string Value);

/// <summary>Một dòng chi phí ở tab "Mua hay không", số tiền đã định dạng sẵn.</summary>
public sealed record CostRow(string Title, string Reason, string Range);

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly InspectionPipeline _pipeline = new();
    private readonly MachineAssessmentService _assessmentService = new();
    private readonly BenchmarkRunner _benchmarkRunner = new();
    private readonly GpuFanOverrideStore _fanOverrides = new();
    private readonly DisplayPortOverrideStore _portOverrides = new();

    /// <summary>Kết quả thử bàn phím và thiết bị, giữ qua các lượt quét lại trong cùng phiên.</summary>
    private readonly Core.Models.Diagnostics.DeviceTestResult _deviceTests = new();
    private AssemblySpec? _overviewModel;
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
        DeviceTestCommand = new RelayCommand(OpenDeviceTest);
        ExportHtmlCommand = new RelayCommand(() => ExportAsync(new HtmlReportExporter()), () => HasResult && !IsBusy);
        ExportJsonCommand = new RelayCommand(() => ExportAsync(new JsonReportExporter()), () => HasResult && !IsBusy);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
        CopyComponentCommand = new RelayCommand(CopySelectedComponent, () => SelectedComponent is not null);

        _selectedLanguage = Loc.Options.First(o => o.Language == Loc.Current);
        _pipeline.BeforeAnalysis = snapshot =>
        {
            foreach (var d in snapshot.Displays) d.OverrideConnection(_portOverrides.Get(d));
            snapshot.DeviceTests = _deviceTests;
        };
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

            var results = BenchmarkResults.Select(r => r.Rebuild?.Invoke() ?? r).ToList();
            BenchmarkResults.Clear();
            foreach (var r in results) BenchmarkResults.Add(r);
            if (_snapshot is { } snapshot) RefreshPortChoices(snapshot);
        }
    }

    private void RebuildLocalizedContent()
    {
        if (_snapshot is null)
        {
            Status = UiStrings.Instance["Status.Ready"];
            return;
        }

        _pipeline.Relocalize(_snapshot);
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
    public ObservableCollection<DisplayPortChoice> DisplayPortChoices { get; } = new();

    /// <summary>Ô chọn cổng thật chỉ hiện khi đang xem phần màn hình và có màn rời.</summary>
    public bool ShowDisplayPortChoices =>
        SelectedComponent?.Kind == ComponentKind.Display && DisplayPortChoices.Count > 0;
    public ObservableCollection<WarrantyLookup> WarrantyLookups { get; } = new();

    public RelayCommand ScanCommand { get; }
    public RelayCommand StressCommand { get; }
    public RelayCommand ScreenTestCommand { get; }
    public RelayCommand DeviceTestCommand { get; }
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
            RefreshOverviewModel();
            OnPropertyChanged(nameof(HasResult));
            OnPropertyChanged(nameof(VerdictRowHeight));
            OnPropertyChanged(nameof(OverallScoreText));
            OnPropertyChanged(nameof(OverallRatingText));
            OnPropertyChanged(nameof(OverallColor));
            OnPropertyChanged(nameof(TrustLevelText));
            OnPropertyChanged(nameof(TrustVerdict));
            OnPropertyChanged(nameof(FirmwareStatusText));
            OnPropertyChanged(nameof(FirmwareColor));
            OnPropertyChanged(nameof(MachineTitle));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(QuickFacts));
            OnPropertyChanged(nameof(Advice));
            OnPropertyChanged(nameof(AdviceRepairs));
            OnPropertyChanged(nameof(AdviceUpgrades));
            OnPropertyChanged(nameof(AdviceRepairTotal));
            OnPropertyChanged(nameof(AdviceCostLine));
            OnPropertyChanged(nameof(CrossChecks));
            OnPropertyChanged(nameof(ManualChecklist));
            OnPropertyChanged(nameof(FirmwareFindings));
            OnPropertyChanged(nameof(SelectedModelSpec));
            OnPropertyChanged(nameof(SelectedMetricRows));
        }
    }

    /// <summary>Cảnh lắp ráp tổng thể ở phần kết luận. Dựng lại khi có kết quả mới hoặc đổi số quạt.</summary>
    public AssemblySpec? OverviewModel
    {
        get => _overviewModel;
        private set => Set(ref _overviewModel, value);
    }

    public HealthRating OverallRating => _assessment?.OverallRating ?? HealthRating.Unknown;

    private void RefreshOverviewModel()
    {
        OverviewModel = _snapshot is { } snapshot && _assessment is { } assessment
            ? ComponentModelSpecFactory.CreateAssembly(snapshot, assessment, _fanOverrides.Get(snapshot.PrimaryGpu?.Name))
            : null;
        OnPropertyChanged(nameof(OverallRating));
    }

    public ComponentAssessment? SelectedComponent
    {
        get => _selectedComponent;
        set
        {
            if (!Set(ref _selectedComponent, value)) return;
            CopyComponentCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(SelectedModelSpec));
            OnPropertyChanged(nameof(SelectedMetricRows));
            OnPropertyChanged(nameof(ShowDisplayPortChoices));
        }
    }

    /// <summary>
    /// Thông số của linh kiện đang chọn, mỗi dòng kèm mô hình 3D nếu đó là dòng mở đầu
    /// một thiết bị khác thiết bị đã vẽ ở góc trên (ổ thứ hai trở đi, màn phụ).
    /// </summary>
    public IReadOnlyList<MetricRowView> SelectedMetricRows
    {
        get
        {
            if (SelectedComponent is not { } c) return Array.Empty<MetricRowView>();
            var snapshot = _snapshot;
            var header = snapshot is null ? null : ComponentModelSpecFactory.HeaderAnchor(snapshot, c.Kind);

            return c.Metrics.Select(m =>
            {
                var spec = snapshot is not null && m.Anchor is { } anchor && anchor != header
                    ? ComponentModelSpecFactory.CreateForAnchor(snapshot, anchor)
                    : null;
                return new MetricRowView(m, spec, spec is null ? null : $"{c.Kind}#{m.Anchor}");
            }).ToList();
        }
    }

    /// <summary>
    /// Thông số mô hình 3D của linh kiện đang chọn, suy ra từ dữ liệu máy thật.
    /// Tính lại mỗi lần đọc — rẻ, và luôn khớp với snapshot cùng lựa chọn số quạt mới nhất.
    /// </summary>
    public ComponentModelSpec? SelectedModelSpec =>
        _snapshot is { } snapshot && SelectedComponent is { } component
            ? ComponentModelSpecFactory.Create(snapshot, component, _fanOverrides.Get(snapshot.PrimaryGpu?.Name))
            : null;

    /// <summary>Người dùng chỉnh số quạt card đồ hoạ trên mô hình. 0 là quay về tự nhận diện.</summary>
    public void SetGpuFanCount(int count)
    {
        _fanOverrides.Set(_snapshot?.PrimaryGpu?.Name, count);
        OnPropertyChanged(nameof(SelectedModelSpec));
        RefreshOverviewModel();
    }

    private void RefreshDisplays(SystemSnapshot snapshot)
    {
        var selected = SelectedDisplay;
        Displays.Clear();
        foreach (var d in snapshot.Displays) Displays.Add(d);
        SelectedDisplay = (selected is not null && Displays.Contains(selected) ? selected : null)
                          ?? Displays.FirstOrDefault(d => d.IsPrimary) ?? Displays.FirstOrDefault();
        OnPropertyChanged(nameof(HasMultipleDisplays));
    }

    private void RefreshPortChoices(SystemSnapshot snapshot)
    {
        DisplayPortChoices.Clear();
        for (var i = 0; i < snapshot.Displays.Count; i++)
        {
            var d = snapshot.Displays[i];
            if (d.DetectedConnection == DisplayConnection.Internal) continue;
            DisplayPortChoices.Add(new DisplayPortChoice(d, i, OnDisplayPortChosen));
        }
        OnPropertyChanged(nameof(ShowDisplayPortChoices));
    }

    /// <summary>Người dùng chọn cổng thật cho một màn: ghi nhớ, áp vào ảnh chụp và chấm điểm lại.</summary>
    private void OnDisplayPortChosen(DisplayPortChoice choice)
    {
        _portOverrides.Set(choice.Display, choice.Selected.Value);
        choice.Display.OverrideConnection(choice.Selected.Value);
        RebuildLocalizedContent();

        // Nhãn ô chọn màn hình có tên cổng, dựng lại danh sách để nó cập nhật.
        // Không dựng lại các ô chọn cổng: ô vừa đổi đang nằm trong danh sách đó.
        if (_snapshot is { } snapshot) RefreshDisplays(snapshot);
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

    /// <summary>Nhắc người dùng đang ở chế độ chạy từ USB: dữ liệu và biên bản ghi cạnh file .exe.</summary>
    public string? PortableNote => AppPaths.IsPortable ? Ui["Status.Portable"] : null;

    /// <summary>Thẻ kết luận chiếm 40% chiều cao vùng dưới thanh lệnh; chưa có kết quả thì co về 0.</summary>
    public System.Windows.GridLength VerdictRowHeight => HasResult
        ? new System.Windows.GridLength(2, System.Windows.GridUnitType.Star)
        : new System.Windows.GridLength(0);

    public PurchaseAdvice? Advice => _assessment?.Advice;

    public IReadOnlyList<CostRow> AdviceRepairs => CostRows(CostKind.Repair);
    public IReadOnlyList<CostRow> AdviceUpgrades => CostRows(CostKind.Upgrade);

    private IReadOnlyList<CostRow> CostRows(CostKind kind) => _assessment?.Advice.Costs
        .Where(c => c.Kind == kind)
        .Select(c => new CostRow(c.Title, c.Reason, FormatHelpers.VndRange(c.MinVnd, c.MaxVnd)))
        .ToList() ?? (IReadOnlyList<CostRow>)Array.Empty<CostRow>();

    public string AdviceRepairTotal => _assessment?.Advice is { HasRepairs: true } a
        ? Ui["Advice.Total"] + FormatHelpers.VndRange(a.RepairMinVnd, a.RepairMaxVnd)
        : Ui["Advice.Total"] + "0 ₫";

    /// <summary>Dòng ngắn dưới kết luận ở thẻ tổng: chi phí khắc phục nếu có.</summary>
    public string? AdviceCostLine => _assessment?.Advice is { HasRepairs: true } a
        ? Ui["Advice.Total"] + FormatHelpers.VndRange(a.RepairMinVnd, a.RepairMaxVnd)
        : null;

    /// <summary>
    /// Thông tin nhanh của máy ở phần kết luận: model bo mạch, CPU, RAM, từng ổ, card đồ hoạ,
    /// kích thước và tần số quét từng màn. Chi tiết đầy đủ vẫn nằm ở danh sách linh kiện.
    /// </summary>
    public IReadOnlyList<QuickFact> QuickFacts => _snapshot is { } s ? BuildQuickFacts(s) : Array.Empty<QuickFact>();

    private static IReadOnlyList<QuickFact> BuildQuickFacts(SystemSnapshot s)
    {
        static string Join(params string?[] parts) =>
            string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

        static string Lines(IEnumerable<string> lines)
        {
            var text = string.Join("\n", lines);
            return string.IsNullOrWhiteSpace(text) ? "—" : text;
        }

        var facts = new List<QuickFact>();

        var b = s.Baseboard;
        var board = Join(b.BoardManufacturer, b.BoardProduct);
        var system = Join(b.SystemManufacturer, b.SystemProductName);
        facts.Add(new QuickFact(Loc.S("Bo mạch chủ", "Motherboard"),
            s.IsLaptop && !FormatHelpers.IsPlaceholder(b.SystemProductName) && !string.IsNullOrWhiteSpace(system)
                ? $"{system}\n{board}".Trim()
                : string.IsNullOrWhiteSpace(board) ? "—" : board));

        facts.Add(new QuickFact("CPU", Lines(s.Processors.Select(c => Join(c.Name,
            c.PhysicalCores > 0 ? Loc.S("{0} nhân / {1} luồng", "{0} cores / {1} threads", c.PhysicalCores, c.LogicalCores) : null)))));

        var mem = s.Memory;
        var modules = mem.Modules;
        var speed = modules.Select(m => m.ConfiguredSpeedMtps > 0 ? m.ConfiguredSpeedMtps : m.SpeedMtps).DefaultIfEmpty(0).Max();
        facts.Add(new QuickFact("RAM", mem.TotalPhysicalBytes > 0 || modules.Count > 0
            ? Join(
                (modules.Count > 0 ? modules.Sum(m => m.CapacityBytes) : mem.TotalPhysicalBytes).ToSize(0),
                modules.Select(m => m.MemoryType).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t) && t.StartsWith("DDR", StringComparison.OrdinalIgnoreCase)),
                speed > 0 ? $"{speed} MT/s" : null,
                modules.Count > 0 ? Loc.S("{0} thanh", "{0} module(s)", modules.Count) : null)
            : "—"));

        facts.Add(new QuickFact(Loc.S("Ổ lưu trữ", "Storage"), Lines(s.StorageDevices.Select(d =>
            Join(d.Model, d.CapacityBytes > 0 ? d.CapacityBytes.ToSize(0) : null, Join(d.BusType, d.MediaType))))));

        facts.Add(new QuickFact(Loc.S("Card đồ hoạ (VGA)", "Graphics (GPU)"), Lines(s.GraphicsAdapters
            .OrderByDescending(g => g.IsDiscrete)
            .Select(g => Join(g.Name, g.VideoMemoryBytes > 0 ? g.VideoMemoryBytes.ToSize(0) : null)))));

        facts.Add(new QuickFact(Loc.S("Màn hình", "Displays"), Lines(s.Displays.Select((d, i) =>
        {
            var size = d.DiagonalInches is { } inch ? $"{inch}\"" : Loc.S("không rõ kích thước", "size unknown");
            var hz = d.RefreshHz is { } r
                ? d.MaxRefreshHz is { } max && max > r
                    ? Loc.S("{0} Hz (tối đa {1} Hz)", "{0} Hz (max {1} Hz)", r, max)
                    : $"{r} Hz"
                : d.MaxRefreshHz is { } m ? Loc.S("tối đa {0} Hz", "max {0} Hz", m) : null;
            return $"{Loc.S("Màn {0}", "Display {0}", i + 1)}: {Join(size, hz)}";
        }))));

        return facts;
    }
    public string OverallScoreText => _assessment is null ? "—" : _assessment.OverallScore.ToString();
    public string OverallRatingText => _assessment?.OverallRating.ToText() ?? Ui["Verdict.NotChecked"];
    public string OverallColor => _assessment?.OverallRating.ToHexColor() ?? "#8A8F98";

    public string TrustLevelText => _assessment is null
        ? "—"
        : $"{_assessment.Trust.Level.ToText()} ({_assessment.Trust.TrustScore}/100)";

    public string TrustVerdict => _assessment?.Trust.Verdict ?? string.Empty;

    public string FirmwareStatusText => _assessment?.Firmware.Status.ToText() ?? Ui["Verdict.NotChecked"];

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

            RefreshDisplays(snapshot);
            RefreshPortChoices(snapshot);

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

    /// <summary>
    /// Mở bài thử bàn phím, touchpad, loa, micro, webcam. Đóng cửa sổ thì kết quả Đạt / Không đạt
    /// được đưa vào kết luận và chi phí khắc phục ngay, không phải quét lại máy.
    /// </summary>
    private void OpenDeviceTest()
    {
        var window = new Views.DeviceTest.DeviceTestWindow(_deviceTests, _snapshot?.IsLaptop ?? true);
        window.Closed += (_, _) =>
        {
            if (_snapshot is null) return;
            _snapshot.DeviceTests = _deviceTests;
            RebuildLocalizedContent();
        };
        window.Show();
        window.Activate();
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
            var folder = AppPaths.ReportsDirectory;
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
