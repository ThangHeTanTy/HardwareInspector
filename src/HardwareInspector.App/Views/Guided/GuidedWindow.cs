using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HardwareInspector.App.ViewModels;
using HardwareInspector.App.Views.DeviceTest;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.App.Views.Guided;

/// <summary>
/// Chế độ kiểm tra từng bước cho người lần đầu đi xem máy cũ.
///
/// Không thêm bài kiểm tra mới nào: cửa sổ chỉ xếp các bài sẵn có thành một trình tự cố định,
/// cho biết bước nào xong, bước nào chưa, bước nào không đạt, và mở đúng bài khi bấm.
/// Thứ tự có chủ đích: quét trước khi máy nóng, chạy tải để lộ lỗi tản nhiệt, rồi tới các bài
/// cần tay và mắt, cuối cùng mới xem kết luận khi đã đủ dữ liệu.
/// </summary>
public sealed class GuidedWindow : Window
{
    private enum StepState { NotDone, Done, Passed, Failed, Partial }

    private sealed record Step(string Title, string Description, string ActionLabel, Func<StepState> State, Action Run, Func<bool>? Enabled = null);

    private static readonly Brush BrText = Hex("#E6E8EC");
    private static readonly Brush BrDim = Hex("#9AA0AA");
    private static readonly Brush BrLine = Hex("#262A33");
    private static readonly Brush BrPanel = Hex("#171A21");

    private readonly MainViewModel _vm;
    private readonly List<Step> _steps;
    private readonly StackPanel _list = new();
    private readonly TextBlock _progress = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };

    public GuidedWindow(MainViewModel vm)
    {
        _vm = vm;
        Title = S("Kiểm tra từng bước", "Step-by-step check");
        Width = 760;
        Height = 820;
        MinWidth = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = TryFindResource("BgBrush") as Brush ?? Hex("#0F1115");
        Foreground = BrText;

        var t = vm.DeviceTests;
        _steps = new List<Step>
        {
            new(S("Quét toàn bộ máy", "Scan the whole machine"),
                S("Đọc cấu hình, sức khoẻ ổ cứng, pin, BIOS và dấu hiệu máy bị khoá. Khoảng 1 phút.",
                  "Reads the configuration, drive health, battery, BIOS and lock signs. About 1 minute."),
                S("Quét", "Scan"),
                () => vm.HasResult ? StepState.Done : StepState.NotDone,
                () => Execute(vm.ScanCommand)),

            new(S("Chạy tải nặng", "Run the stress test"),
                S("Ép CPU, RAM, ổ đĩa và GPU chạy hết sức khoảng 9 phút để lộ lỗi tản nhiệt, VRAM và card bị đổi tên. Cắm sạc khi chạy.",
                  "Loads the CPU, RAM, drive and GPU for about 9 minutes to expose cooling faults, bad VRAM and renamed cards. Keep the charger plugged in."),
                S("Chạy tải", "Run"),
                () => vm.StressDone ? StepState.Done : StepState.NotDone,
                () => Execute(vm.StressCommand),
                () => vm.HasResult),

            new(S("Thử bàn phím", "Test the keyboard"),
                S("Bấm hết các phím, tìm phím chết, phím dội, phím kẹt.", "Press every key to find dead, chattering or stuck keys."),
                S("Mở", "Open"),
                () => Verdict(t.KeyboardOk),
                () => vm.OpenDeviceTest(DeviceTestTab.Keyboard)),

            new(S("Thử touchpad / chuột", "Test the touchpad / mouse"),
                S("Rê kín khung, thử click và cuộn hai ngón.", "Cover the whole area, try clicks and two-finger scroll."),
                S("Mở", "Open"),
                () => Verdict(t.TouchpadOk),
                () => vm.OpenDeviceTest(DeviceTestTab.Pointer)),

            new(S("Thử loa, micro, webcam", "Test speakers, microphone, webcam"),
                S("Loa trái/phải riêng từng bên, micro nhận tiếng, webcam lên hình rõ.",
                  "Left/right speakers separately, the microphone picks up sound, the webcam shows a clear picture."),
                S("Mở", "Open"),
                () => Combine(t.SpeakerLeftOk, t.SpeakerRightOk, t.MicOk, t.CameraOk),
                () => vm.OpenDeviceTest(DeviceTestTab.Speakers)),

            new(S("Soi màn hình", "Inspect the screen"),
                S("Chạy bài nền màu toàn màn hình trong phòng tối, tìm điểm chết, hở sáng, ám màu.",
                  "Run the full-screen colour test in a dark room to find dead pixels, bleed and tint."),
                S("Mở", "Open"),
                () => Verdict(t.ScreenOk),
                () => vm.OpenDeviceTest(DeviceTestTab.Screen)),

            new(S("Thử từng cổng USB", "Test every USB port"),
                S("Cắm một USB lần lượt vào từng cổng, kể cả USB-C.", "Plug a USB device into each port in turn, including USB-C."),
                S("Mở", "Open"),
                () => Verdict(t.UsbOk),
                () => vm.OpenDeviceTest(DeviceTestTab.Usb)),

            new(S("Thử Wi-Fi và Bluetooth", "Test Wi-Fi and Bluetooth"),
                S("Kết nối Wi-Fi thật, ghép thử điện thoại hoặc tai nghe Bluetooth.",
                  "Connect to real Wi-Fi and pair a phone or Bluetooth headphones."),
                S("Mở", "Open"),
                () => Combine(t.WifiOk, t.BluetoothOk),
                () => vm.OpenDeviceTest(DeviceTestTab.Wireless)),
        };

        if (vm.IsLaptop)
            _steps.Add(new(S("Đo pin thật", "Measure real battery life"),
                S("Rút sạc, dùng nhẹ 10 phút để biết pin thực tế dùng được bao lâu.",
                  "Unplug and use the machine lightly for 10 minutes to see how long the battery really lasts."),
                S("Mở", "Open"),
                () => t.BatteryRuntimeHours is { } h ? (h >= 1.5 ? StepState.Passed : StepState.Failed) : StepState.NotDone,
                () => vm.OpenDeviceTest(DeviceTestTab.Battery)));

        _steps.Add(new(S("Xem kết luận: có nên mua không", "See the verdict: buy or not"),
            S("Tổng hợp mọi bước ở trên thành kết luận, chi phí khắc phục và gợi ý trả giá.",
              "Turns every step above into a verdict, repair costs and a negotiation tip."),
            S("Xem kết luận", "See verdict"),
            () => StepState.NotDone,
            () => vm.ShowAdvice(),
            () => vm.HasResult));

        var root = new DockPanel { Margin = new Thickness(18) };
        var header = new StackPanel();
        header.Children.Add(new TextBlock
        {
            Text = S("Làm lần lượt từ trên xuống. Bước nào xong sẽ được đánh dấu; có thể quay lại làm lại bất cứ lúc nào.",
                     "Work from top to bottom. Finished steps are marked; you can go back and redo any of them."),
            Foreground = BrDim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        });
        header.Children.Add(_progress);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;

        Activated += (_, _) => Refresh();
        vm.PropertyChanged += OnVmChanged;
        Closed += (_, _) => vm.PropertyChanged -= OnVmChanged;
        Refresh();
    }

    private static readonly HashSet<string> RelevantProperties = new()
    {
        nameof(MainViewModel.HasResult), nameof(MainViewModel.StressDone), nameof(MainViewModel.Advice), nameof(MainViewModel.IsBusy)
    };

    // Thanh tiến độ đổi giá trị liên tục khi quét; chỉ vẽ lại khi trạng thái các bước có thể đã đổi.
    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is { } name && RelevantProperties.Contains(name))
            Dispatcher.BeginInvoke(Refresh);
    }

    private void Refresh()
    {
        _list.Children.Clear();
        var counted = _steps.Take(_steps.Count - 1).ToList();
        var finished = counted.Count(s => s.State() is StepState.Done or StepState.Passed or StepState.Failed);
        _progress.Text = S($"Đã xong {finished}/{counted.Count} bước", $"{finished}/{counted.Count} steps done");

        for (var i = 0; i < _steps.Count; i++)
            _list.Children.Add(Row(i + 1, _steps[i]));
    }

    private UIElement Row(int number, Step step)
    {
        var state = step.State();
        var (chip, color) = state switch
        {
            StepState.Done => (S("✓ Đã xong", "✓ Done"), "#2FBF71"),
            StepState.Passed => (S("✓ Đạt", "✓ Pass"), "#2FBF71"),
            StepState.Failed => (S("✕ Không đạt", "✕ Fail"), "#E5544B"),
            StepState.Partial => (S("◐ Làm dở", "◐ Partly done"), "#F4A259"),
            _ => (S("○ Chưa làm", "○ Not done"), "#8A8F98")
        };

        var enabled = step.Enabled?.Invoke() ?? true;
        var button = new Button { Content = step.ActionLabel, Padding = new Thickness(14, 5, 14, 5), MinWidth = 110, IsEnabled = enabled, VerticalAlignment = VerticalAlignment.Center };
        button.Click += (_, _) => step.Run();

        var text = new StackPanel { Margin = new Thickness(12, 0, 12, 0) };
        text.Children.Add(new TextBlock { Text = step.Title, Foreground = BrText, FontWeight = FontWeights.SemiBold, FontSize = 14 });
        text.Children.Add(new TextBlock { Text = step.Description, Foreground = BrDim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4) });
        text.Children.Add(new TextBlock { Text = chip, Foreground = Hex(color), FontWeight = FontWeights.SemiBold });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var badge = new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(17), BorderThickness = new Thickness(2),
            BorderBrush = Hex(color), VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = number.ToString(), Foreground = BrText, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(button, 2);
        grid.Children.Add(badge);
        grid.Children.Add(text);
        grid.Children.Add(button);

        return new Border
        {
            Background = BrPanel, BorderBrush = BrLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8), Child = grid
        };
    }

    private static StepState Verdict(bool? ok) => ok switch
    {
        true => StepState.Passed,
        false => StepState.Failed,
        _ => StepState.NotDone
    };

    /// <summary>Gộp nhiều bài nhỏ: có bài hỏng là không đạt, đủ cả là đạt, mới làm một phần là làm dở.</summary>
    private static StepState Combine(params bool?[] results)
    {
        if (results.Any(r => r == false)) return StepState.Failed;
        if (results.All(r => r == true)) return StepState.Passed;
        return results.Any(r => r == true) ? StepState.Partial : StepState.NotDone;
    }

    private static void Execute(System.Windows.Input.ICommand command)
    {
        if (command.CanExecute(null)) command.Execute(null);
    }

    private static Brush Hex(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
