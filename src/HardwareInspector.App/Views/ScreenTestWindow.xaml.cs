using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using HardwareInspector.App.Localization;
using HardwareInspector.App.Services;
using HardwareInspector.Core.Models.Components;
using HardwareInspector.Core.Models.Diagnostics;
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
///
/// Phần mềm không tự thấy được điểm chết: card đồ hoạ vẫn gửi đúng màu, lỗi nằm ở tấm nền.
/// Vì vậy người dùng chuột phải vào chỗ lỗi để đánh dấu; loại lỗi suy ra từ nền đang hiện
/// (chấm sáng trên nền đen, chấm đen trên nền trắng...) rồi đối chiếu với ISO 9241-307.
/// </summary>
public partial class ScreenTestWindow : Window
{
    private sealed record TestStep(string Title, string Hint, Brush Fill, PixelDefectKind MarkKind, bool BrightBackground);

    /// <summary>Bán kính (DIP) để chuột phải vào vòng đã có thì xoá thay vì thêm.</summary>
    private const double HitRadius = 16;

    private readonly List<TestStep> _steps;
    private readonly DisplayInfo? _target;
    private readonly DeviceTestResult _results;
    private readonly string _displayKey;
    private readonly DispatcherTimer _cursorTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _flashTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private Rectangle? _flashPatch;
    private int _flashIndex;
    private bool _marksVisible = true;
    private Point _lastMouse;
    private int _index;

    private static readonly Brush[] FlashColors =
        { Brushes.Red, Brushes.Lime, Brushes.Blue, Brushes.White, Brushes.Black };

    public ScreenTestWindow(DisplayInfo? target = null, DeviceTestResult? results = null)
    {
        InitializeComponent();
        _target = target;
        _results = results ?? new DeviceTestResult();
        _displayKey = PixelDefectPolicy.KeyOf(target);

        _steps = BuildSteps();
        BuildGridOverlay();
        Apply();

        _cursorTimer.Tick += (_, _) => { _cursorTimer.Stop(); Cursor = Cursors.None; };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); if (!_flashTimer.IsEnabled) Toast.Visibility = Visibility.Collapsed; };
        _flashTimer.Tick += (_, _) => FlashTick();
        SizeChanged += (_, _) => RedrawMarks();
        Closed += (_, _) => { _flashTimer.Stop(); _cursorTimer.Stop(); _toastTimer.Stop(); };

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
            Brushes.Black, PixelDefectKind.Bright, false),

        new(S("Nền trắng — tìm điểm chết và bụi", "White — look for dead pixels and dust"),
            S("Chấm đen li ti là điểm ảnh chết. Vệt xám mờ thường là bụi lọt vào giữa các lớp tấm nền. " +
              "Nếu thấy vùng ố vàng hoặc bóng mờ hình chữ nhật, đó có thể là dấu hiệu lưu ảnh do màn hình " +
              "từng hiển thị một khung hình tĩnh trong thời gian rất dài.",
              "Tiny black dots are dead pixels. Faint grey smudges are usually dust between the panel layers. " +
              "Yellowish patches or a rectangular ghost may be image retention from a screen " +
              "that showed a static picture for a very long time."),
            Brushes.White, PixelDefectKind.Dark, true),

        new(S("Nền đỏ — kiểm tra kênh đỏ", "Red — check the red channel"),
            S("Điểm ảnh chỉ hỏng một kênh phụ sẽ vô hình trên nền trắng nhưng hiện rõ ở đây. " +
              "Tìm những chấm không cùng sắc đỏ với vùng xung quanh.",
              "A pixel with only one failed subpixel is invisible on white but shows clearly here. " +
              "Look for dots that are not the same red as their surroundings."),
            Brushes.Red, PixelDefectKind.Subpixel, false),

        new(S("Nền lục — kiểm tra kênh lục", "Green — check the green channel"),
            S("Kênh lục chiếm phần lớn độ sáng cảm nhận được, nên lỗi ở đây dễ thấy nhất khi dùng hằng ngày.",
              "Green carries most of the perceived brightness, so faults here are the most visible in daily use."),
            Brushes.Lime, PixelDefectKind.Subpixel, true),

        new(S("Nền lam — kiểm tra kênh lam", "Blue — check the blue channel"),
            S("Nền lam cũng giúp thấy rõ hiện tượng ám màu không đều giữa các vùng của tấm nền.",
              "Blue also reveals uneven colour tint between areas of the panel."),
            Brushes.Blue, PixelDefectKind.Subpixel, false),

        new(S("Xám 50% — kiểm tra độ đồng đều", "50% grey — check uniformity"),
            S("Nền xám trung tính là bài khó nhất với một tấm nền cũ. Vùng nào ngả vàng, ngả xanh " +
              "hoặc tối hơn rõ rệt so với phần còn lại là dấu hiệu tấm nền đã xuống cấp hoặc từng bị ép nhiệt.",
              "Neutral grey is the toughest test for an old panel. Areas that look yellowish, bluish " +
              "or clearly darker than the rest mean the panel has degraded or was once heat-pressed."),
            new SolidColorBrush(Color.FromRgb(128, 128, 128)), PixelDefectKind.Blemish, true),

        new(S("Xám tối 20% — kiểm tra vết mây và loang sáng", "Dark grey 20% — check clouding and glow"),
            S("Trên nền xám tối, tấm nền tốt trông đều màu. Vùng sáng hơn dạng đám mây (mura), quầng sáng ở góc " +
              "hoặc vệt dọc/ngang là dấu hiệu tấm nền từng bị đè ép, cong vênh hoặc đèn nền không đều. " +
              "Chuột phải vào chỗ đó để ghi lại.",
              "On dark grey a good panel looks even. Brighter cloud-like areas (mura), glowing corners " +
              "or vertical/horizontal streaks mean the panel was pressed, warped or has an uneven backlight. " +
              "Right-click the spot to record it."),
            new SolidColorBrush(Color.FromRgb(51, 51, 51)), PixelDefectKind.Blemish, false),

        new(S("Chuyển sắc — kiểm tra phân dải màu", "Gradient — check for banding"),
            S("Dải chuyển từ đen sang trắng phải mượt. Nếu thấy các bậc thang rõ rệt, tấm nền có thể chỉ là " +
              "loại 6 bit giả lập 8 bit, hoặc driver đang đặt sai độ sâu màu.",
              "The black-to-white ramp should be smooth. Clear steps mean the panel may be " +
              "a 6-bit panel faking 8-bit, or the driver has the wrong colour depth set."),
            CreateGradient(Colors.Black, Colors.White), PixelDefectKind.Blemish, false),

        new(S("Chuyển sắc màu — kiểm tra dải màu", "Colour gradient — check colour transitions"),
            S("Quan sát chỗ chuyển giữa các màu. Vệt hoặc đốm lạ ở đây thường liên quan tới cáp màn hình " +
              "lỏng hoặc card đồ hoạ có vấn đề, chứ không phải lỗi tấm nền.",
              "Watch the transitions between colours. Odd streaks or blotches here usually point to a loose " +
              "display cable or a graphics card problem, not the panel."),
            CreateRainbow(), PixelDefectKind.Blemish, true)
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
        RedrawMarks();
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

    // ------------------------------------------------------------------
    // Đánh dấu điểm lỗi
    // ------------------------------------------------------------------

    private IEnumerable<PixelDefectMark> OwnMarks =>
        _results.PixelDefects.Where(m => string.Equals(m.DisplayKey, _displayKey, StringComparison.OrdinalIgnoreCase));

    /// <summary>Chuột phải: thêm điểm lỗi tại vị trí chuột, hoặc xoá điểm đã đánh dấu ngay đó.</summary>
    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        if (HelpPanel.Visibility == Visibility.Visible && HelpPanel.IsMouseOver) return;
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        var pos = e.GetPosition(this);
        var existing = OwnMarks.FirstOrDefault(m =>
            Math.Abs(m.X * ActualWidth - pos.X) <= HitRadius && Math.Abs(m.Y * ActualHeight - pos.Y) <= HitRadius);

        if (existing is not null)
        {
            _results.PixelDefects.Remove(existing);
            ShowToast(string.Format(UiStrings.Instance["Screen.Removed"], existing.KindText));
        }
        else
        {
            var mark = new PixelDefectMark
            {
                DisplayKey = _displayKey,
                X = pos.X / ActualWidth,
                Y = pos.Y / ActualHeight,
                Kind = _steps[_index].MarkKind
            };
            _results.PixelDefects.Add(mark);
            ShowToast(string.Format(UiStrings.Instance["Screen.Marked"], mark.KindText,
                mark.InCenter ? UiStrings.Instance["Screen.CenterZone"] : UiStrings.Instance["Screen.EdgeZone"]));
        }

        RedrawMarks();
        e.Handled = true;
    }

    /// <summary>
    /// Vẽ lại vòng đánh dấu. Vòng có lỗ ở giữa để không che chính điểm lỗi, màu đổi theo nền
    /// (tím trên nền sáng, xanh lơ trên nền tối) để luôn nhìn thấy được.
    /// </summary>
    private void RedrawMarks()
    {
        MarkLayer.Children.Clear();
        if (_flashPatch is not null) MarkLayer.Children.Add(_flashPatch);

        var marks = OwnMarks.ToList();
        UpdateSummary(marks);
        if (!_marksVisible || ActualWidth <= 0) return;

        var stroke = _steps[_index].BrightBackground ? Brushes.Magenta : Brushes.Cyan;
        var n = 0;
        foreach (var m in marks)
        {
            n++;
            var x = m.X * ActualWidth;
            var y = m.Y * ActualHeight;
            var ring = new Ellipse { Width = 26, Height = 26, Stroke = stroke, StrokeThickness = 2 };
            Canvas.SetLeft(ring, x - 13);
            Canvas.SetTop(ring, y - 13);
            MarkLayer.Children.Add(ring);

            var label = new TextBlock { Text = n.ToString(), Foreground = stroke, FontSize = 12, FontWeight = FontWeights.Bold };
            Canvas.SetLeft(label, x + 14);
            Canvas.SetTop(label, y - 22);
            MarkLayer.Children.Add(label);
        }
    }

    private void UpdateSummary(List<PixelDefectMark> marks)
    {
        if (marks.Count == 0)
        {
            MarkSummary.Text = string.Empty;
            return;
        }

        var sum = PixelDefectPolicy.Summarize(marks,
            _target?.NativeWidthPx ?? _target?.WidthPx ?? 0, _target?.NativeHeightPx ?? _target?.HeightPx ?? 0);
        MarkSummary.Text = string.Format(UiStrings.Instance["Screen.Summary"], sum.CountsText) +
                           (sum.ExceedsIso ? UiStrings.Instance["Screen.OverIso"] : string.Empty);
    }

    private void ShowToast(string text)
    {
        ToastText.Text = text;
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // ------------------------------------------------------------------
    // Nháy màu để chữa điểm kẹt
    // ------------------------------------------------------------------

    /// <summary>
    /// Nháy nhanh đỏ/lục/lam/trắng/đen trên một ô nhỏ quanh con trỏ. Điểm kẹt màu (đứng một màu)
    /// đôi khi hoạt động lại sau vài phút; điểm chết hẳn (luôn đen) thì không cứu được.
    /// Ô nhỏ để giảm ánh nháy, kèm cảnh báo cho người nhạy cảm với ánh sáng nhấp nháy.
    /// </summary>
    private void ToggleFlash()
    {
        if (_flashTimer.IsEnabled)
        {
            StopFlash();
            return;
        }

        _flashPatch = new Rectangle { Width = 48, Height = 48 };
        Canvas.SetLeft(_flashPatch, _lastMouse.X - 24);
        Canvas.SetTop(_flashPatch, _lastMouse.Y - 24);
        RedrawMarks();
        _flashTimer.Start();

        ToastText.Text = UiStrings.Instance["Screen.Flashing"];
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
    }

    private void StopFlash()
    {
        _flashTimer.Stop();
        _flashPatch = null;
        Toast.Visibility = Visibility.Collapsed;
        RedrawMarks();
    }

    private void FlashTick()
    {
        if (_flashPatch is null) return;
        _flashIndex = (_flashIndex + 1) % FlashColors.Length;
        _flashPatch.Fill = FlashColors[_flashIndex];
    }

    /// <summary>Con trỏ ẩn để không lẫn với điểm lỗi; chỉ hiện dạng chữ thập vài giây khi di chuột.</summary>
    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        if ((pos - _lastMouse).Length < 1) return;
        _lastMouse = pos;
        Cursor = Cursors.Cross;
        _cursorTimer.Stop();
        _cursorTimer.Start();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape when _flashTimer.IsEnabled:
                StopFlash();
                break;
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
            case Key.M:
                _marksVisible = !_marksVisible;
                RedrawMarks();
                break;
            case Key.F:
                ToggleFlash();
                break;
        }
    }
}
