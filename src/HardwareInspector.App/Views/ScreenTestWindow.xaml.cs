using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using HardwareInspector.App.Localization;
using HardwareInspector.App.Services;
using HardwareInspector.Core.Models.Components;
using static HardwareInspector.Core.Localization.Loc;

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
            TargetLabel.Text = string.Format(UiStrings.Instance["Screen.Target"], _target.DisplayLabel);
            SourceInitialized += (_, _) => MoveToTargetMonitor();
        }
        else
        {
            TargetLabel.Text = UiStrings.Instance["Screen.TargetPrimary"];
        }
    }

    private void MoveToTargetMonitor()
    {
        if (_target is not null) MonitorPositioner.MoveTo(this, _target);
    }

    private static List<TestStep> BuildSteps() => new()
    {
        new(S("Nền đen — tìm điểm sáng và hở sáng", "Black — look for stuck pixels and backlight bleed"),
            S("Trong phòng tối, quét mắt khắp mặt kính. Chấm sáng cố định là điểm ảnh kẹt. " +
              "Vùng sáng loang ở bốn góc hoặc dọc cạnh dưới là hở sáng viền — mức độ nhẹ là bình thường " +
              "trên màn IPS, nhưng loang mạnh làm hỏng trải nghiệm xem phim và chỉnh ảnh.",
              "In a dark room, scan the whole screen. A fixed bright dot is a stuck pixel. " +
              "Light leaking at the four corners or along the bottom edge is backlight bleed — a little is normal " +
              "on IPS panels, but heavy bleed spoils films and photo editing."),
            Brushes.Black),

        new(S("Nền trắng — tìm điểm chết và bụi", "White — look for dead pixels and dust"),
            S("Chấm đen li ti là điểm ảnh chết. Vệt xám mờ thường là bụi lọt vào giữa các lớp tấm nền. " +
              "Nếu thấy vùng ố vàng hoặc bóng mờ hình chữ nhật, đó có thể là dấu hiệu lưu ảnh do màn hình " +
              "từng hiển thị một khung hình tĩnh trong thời gian rất dài.",
              "Tiny black dots are dead pixels. Faint grey smudges are usually dust between the panel layers. " +
              "Yellowish patches or a rectangular ghost may be image retention from a screen " +
              "that showed a static picture for a very long time."),
            Brushes.White),

        new(S("Nền đỏ — kiểm tra kênh đỏ", "Red — check the red channel"),
            S("Điểm ảnh chỉ hỏng một kênh phụ sẽ vô hình trên nền trắng nhưng hiện rõ ở đây. " +
              "Tìm những chấm không cùng sắc đỏ với vùng xung quanh.",
              "A pixel with only one failed subpixel is invisible on white but shows clearly here. " +
              "Look for dots that are not the same red as their surroundings."),
            Brushes.Red),

        new(S("Nền lục — kiểm tra kênh lục", "Green — check the green channel"),
            S("Kênh lục chiếm phần lớn độ sáng cảm nhận được, nên lỗi ở đây dễ thấy nhất khi dùng hằng ngày.",
              "Green carries most of the perceived brightness, so faults here are the most visible in daily use."),
            Brushes.Lime),

        new(S("Nền lam — kiểm tra kênh lam", "Blue — check the blue channel"),
            S("Nền lam cũng giúp thấy rõ hiện tượng ám màu không đều giữa các vùng của tấm nền.",
              "Blue also reveals uneven colour tint between areas of the panel."),
            Brushes.Blue),

        new(S("Xám 50% — kiểm tra độ đồng đều", "50% grey — check uniformity"),
            S("Nền xám trung tính là bài khó nhất với một tấm nền cũ. Vùng nào ngả vàng, ngả xanh " +
              "hoặc tối hơn rõ rệt so với phần còn lại là dấu hiệu tấm nền đã xuống cấp hoặc từng bị ép nhiệt.",
              "Neutral grey is the toughest test for an old panel. Areas that look yellowish, bluish " +
              "or clearly darker than the rest mean the panel has degraded or was once heat-pressed."),
            new SolidColorBrush(Color.FromRgb(128, 128, 128))),

        new(S("Chuyển sắc — kiểm tra phân dải màu", "Gradient — check for banding"),
            S("Dải chuyển từ đen sang trắng phải mượt. Nếu thấy các bậc thang rõ rệt, tấm nền có thể chỉ là " +
              "loại 6 bit giả lập 8 bit, hoặc driver đang đặt sai độ sâu màu.",
              "The black-to-white ramp should be smooth. Clear steps mean the panel may be " +
              "a 6-bit panel faking 8-bit, or the driver has the wrong colour depth set."),
            CreateGradient(Colors.Black, Colors.White)),

        new(S("Chuyển sắc màu — kiểm tra dải màu", "Colour gradient — check colour transitions"),
            S("Quan sát chỗ chuyển giữa các màu. Vệt hoặc đốm lạ ở đây thường liên quan tới cáp màn hình " +
              "lỏng hoặc card đồ hoạ có vấn đề, chứ không phải lỗi tấm nền.",
              "Watch the transitions between colours. Odd streaks or blotches here usually point to a loose " +
              "display cable or a graphics card problem, not the panel."),
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
        StepCounter.Text = string.Format(UiStrings.Instance["Screen.Counter"], _index + 1, _steps.Count);
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
