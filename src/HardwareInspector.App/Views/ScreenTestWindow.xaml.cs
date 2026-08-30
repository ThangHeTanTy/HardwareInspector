using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using HardwareInspector.App.Services;
using HardwareInspector.Core.Models.Components;

namespace HardwareInspector.App.Views;

/// <summary>
/// Bài kiểm tra màn hình toàn màn hình, chạy trên đúng màn hình được chọn.
///
/// Thứ tự các bước không phải ngẫu nhiên. Nền đen trước để bắt điểm sáng và hở sáng viền,
/// vì đó là lúc mắt nhạy nhất với ánh sáng lọt ra. Nền trắng ngay sau đó để bắt điểm chết
/// và bụi lọt trong tấm nền. Ba màu cơ bản để kiểm tra từng kênh phụ: một điểm ảnh chỉ
/// hỏng kênh đỏ sẽ vô hình trên nền trắng nhưng lộ rõ trên nền đỏ. Cuối cùng là các dải
/// chuyển màu để phát hiện phân dải và ám màu không đều.
/// </summary>
public partial class ScreenTestWindow : Window
{
    private sealed record TestStep(string Title, string Hint, Brush Fill);

    private readonly List<TestStep> _steps;
    private readonly DisplayInfo? _target;
    private int _index;

    public ScreenTestWindow(DisplayInfo? target = null)
    {
        InitializeComponent();
        _target = target;

        _steps = BuildSteps();
        BuildGridOverlay();
        Apply();

        if (_target is not null)
        {
            TargetLabel.Text = $"Đang kiểm tra: {_target.DisplayLabel}";
            SourceInitialized += (_, _) => MoveToTargetMonitor();
        }
        else
        {
            TargetLabel.Text = "Đang kiểm tra màn hình chính";
        }
    }

    private void MoveToTargetMonitor()
    {
        if (_target is not null) MonitorPositioner.MoveTo(this, _target);
    }

    private static List<TestStep> BuildSteps() => new()
    {
        new("Nền đen — tìm điểm sáng và hở sáng",
            "Trong phòng tối, quét mắt khắp mặt kính. Chấm sáng cố định là điểm ảnh kẹt. " +
            "Vùng sáng loang ở bốn góc hoặc dọc cạnh dưới là hở sáng viền — mức độ nhẹ là bình thường " +
            "trên màn IPS, nhưng loang mạnh làm hỏng trải nghiệm xem phim và chỉnh ảnh.",
            Brushes.Black),

        new("Nền trắng — tìm điểm chết và bụi",
            "Chấm đen li ti là điểm ảnh chết. Vệt xám mờ thường là bụi lọt vào giữa các lớp tấm nền. " +
            "Nếu thấy vùng ố vàng hoặc bóng mờ hình chữ nhật, đó có thể là dấu hiệu lưu ảnh do màn hình " +
            "từng hiển thị một khung hình tĩnh trong thời gian rất dài.",
            Brushes.White),

        new("Nền đỏ — kiểm tra kênh đỏ",
            "Điểm ảnh chỉ hỏng một kênh phụ sẽ vô hình trên nền trắng nhưng hiện rõ ở đây. " +
            "Tìm những chấm không cùng sắc đỏ với vùng xung quanh.",
            Brushes.Red),

        new("Nền lục — kiểm tra kênh lục",
            "Kênh lục chiếm phần lớn độ sáng cảm nhận được, nên lỗi ở đây dễ thấy nhất khi dùng hằng ngày.",
            Brushes.Lime),

        new("Nền lam — kiểm tra kênh lam",
            "Nền lam cũng giúp thấy rõ hiện tượng ám màu không đều giữa các vùng của tấm nền.",
            Brushes.Blue),

        new("Xám 50% — kiểm tra độ đồng đều",
            "Nền xám trung tính là bài khó nhất với một tấm nền cũ. Vùng nào ngả vàng, ngả xanh " +
            "hoặc tối hơn rõ rệt so với phần còn lại là dấu hiệu tấm nền đã xuống cấp hoặc từng bị ép nhiệt.",
            new SolidColorBrush(Color.FromRgb(128, 128, 128))),

        new("Chuyển sắc — kiểm tra phân dải màu",
            "Dải chuyển từ đen sang trắng phải mượt. Nếu thấy các bậc thang rõ rệt, tấm nền có thể chỉ là " +
            "loại 6 bit giả lập 8 bit, hoặc driver đang đặt sai độ sâu màu.",
            CreateGradient(Colors.Black, Colors.White)),

        new("Chuyển sắc màu — kiểm tra dải màu",
            "Quan sát chỗ chuyển giữa các màu. Vệt hoặc đốm lạ ở đây thường liên quan tới cáp màn hình " +
            "lỏng hoặc card đồ hoạ có vấn đề, chứ không phải lỗi tấm nền.",
            CreateRainbow())
    };

    private void BuildGridOverlay()
    {
        var cells = GridOverlay.Rows * GridOverlay.Columns;
        for (var i = 0; i < cells; i++)
            GridOverlay.Children.Add(new System.Windows.Controls.Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(0.5)
            });
    }

    private static Brush CreateGradient(Color from, Color to) =>
        new LinearGradientBrush(from, to, new Point(0, 0), new Point(1, 0));

    private static Brush CreateRainbow()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        brush.GradientStops.Add(new GradientStop(Colors.Red, 0.00));
        brush.GradientStops.Add(new GradientStop(Colors.Yellow, 0.17));
        brush.GradientStops.Add(new GradientStop(Colors.Lime, 0.33));
        brush.GradientStops.Add(new GradientStop(Colors.Cyan, 0.50));
        brush.GradientStops.Add(new GradientStop(Colors.Blue, 0.67));
        brush.GradientStops.Add(new GradientStop(Colors.Magenta, 0.83));
        brush.GradientStops.Add(new GradientStop(Colors.Red, 1.00));
        return brush;
    }

    private void Apply()
    {
        var step = _steps[_index];
        Backdrop.Fill = step.Fill;
        StepTitle.Text = step.Title;
        StepHint.Text = step.Hint;
        StepCounter.Text = $"Bài {_index + 1}/{_steps.Count}";
    }

    private void Next()
    {
        _index = (_index + 1) % _steps.Count;
        Apply();
    }

    private void Previous()
    {
        _index = (_index - 1 + _steps.Count) % _steps.Count;
        Apply();
    }

    private void OnClick(object sender, MouseButtonEventArgs e) => Next();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Space or Key.Right or Key.Down or Key.PageDown:
                Next();
                break;
            case Key.Back or Key.Left or Key.Up or Key.PageUp:
                Previous();
                break;
            case Key.G:
                GridOverlay.Visibility = GridOverlay.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                break;
            case Key.H:
                HelpPanel.Visibility = HelpPanel.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                break;
        }
    }
}
