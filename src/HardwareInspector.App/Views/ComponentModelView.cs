using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HardwareInspector.App.Rendering;
using HardwareInspector.Core.Localization;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Utils;

namespace HardwareInspector.App.Views;

/// <summary>
/// Khung xem 3D của một linh kiện, xoay được bằng chuột.
///
/// Mỗi loại linh kiện giữ góc xoay và mức phóng to riêng: xoay CPU sang một góc rồi
/// chuyển sang xem GPU, lúc quay lại CPU vẫn đứng đúng góc cũ. Linh kiện chưa bị
/// chạm tới thì tự quay chậm để người xem thấy được mọi mặt.
///
/// Hình dáng lấy từ <see cref="Spec"/> — thông số suy ra từ máy thật (socket CPU, loại RAM,
/// chuẩn ổ, số quạt card đồ hoạ...). Chưa có thông số thì vẽ mô hình mặc định theo
/// <see cref="Kind"/>.
///
/// Với card đồ hoạ, góc trên phải có bộ chọn số quạt: cảm biến không phải lúc nào
/// cũng đếm đúng (card ba quạt hay ghép hai quạt chung một bộ điều khiển), nên người
/// dùng nhìn card thật rồi chỉnh lại được.
///
/// Dựng hoàn toàn bằng code thay vì XAML để chỉ có một chỗ khai báo trong MainWindow.
/// </summary>
public sealed class ComponentModelView : Border
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(ComponentKind), typeof(ComponentModelView),
        new PropertyMetadata(ComponentKind.Cpu, (d, _) => ((ComponentModelView)d).Rebuild()));

    public static readonly DependencyProperty RatingProperty = DependencyProperty.Register(
        nameof(Rating), typeof(HealthRating), typeof(ComponentModelView),
        new PropertyMetadata(HealthRating.Unknown, (d, _) => ((ComponentModelView)d).Rebuild()));

    public static readonly DependencyProperty SpecProperty = DependencyProperty.Register(
        nameof(Spec), typeof(ComponentModelSpec), typeof(ComponentModelView),
        new PropertyMetadata(null, (d, _) => ((ComponentModelView)d).Rebuild()));

    /// <summary>Thông số dựng mô hình. Bị bỏ qua nếu không cùng loại với <see cref="Kind"/>.</summary>
    public ComponentModelSpec? Spec
    {
        get => (ComponentModelSpec?)GetValue(SpecProperty);
        set => SetValue(SpecProperty, value);
    }

    /// <summary>Người dùng chọn số quạt GPU. 0 nghĩa là quay về giá trị tự nhận diện.</summary>
    public event EventHandler<int>? FanCountChosen;

    public ComponentKind Kind
    {
        get => (ComponentKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public HealthRating Rating
    {
        get => (HealthRating)GetValue(RatingProperty);
        set => SetValue(RatingProperty, value);
    }

    /// <summary>Trạng thái xoay của từng loại linh kiện, giữ suốt phiên làm việc.</summary>
    private sealed class ViewState
    {
        public double Yaw = 35;
        public double Pitch = 10;
        public double Zoom = 1;
        public bool Touched;
    }

    private static readonly Dictionary<ComponentKind, ViewState> States = new();
    private static readonly Dictionary<(ComponentModelSpec, HealthRating), Model3DGroup> Cache = new();

    private const double AutoSpinDegreesPerSecond = 18;

    /// <summary>Góc nhìn dọc mong muốn. WPF chỉ nhận góc ngang nên phải quy đổi theo tỉ lệ khung.</summary>
    private const double VerticalFieldOfView = 40;

    private readonly PerspectiveCamera _camera;

    private readonly ModelVisual3D _content = new();
    private readonly AxisAngleRotation3D _yaw = new(new Vector3D(0, 1, 0), 0);
    private readonly AxisAngleRotation3D _pitch = new(new Vector3D(1, 0, 0), 0);
    private readonly ScaleTransform3D _scale = new(1, 1, 1);
    private readonly TextBlock _hint;
    private readonly StackPanel _fanBar;
    private readonly TextBlock _fanLabel;

    private Point _lastMouse;
    private bool _dragging;
    private TimeSpan _lastFrame;
    private bool _spinning;

    public ComponentModelView()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x14, 0x17, 0x1D));
        CornerRadius = new CornerRadius(8);
        ClipToBounds = true;
        Cursor = Cursors.SizeAll;
        Focusable = false;

        var transform = new Transform3DGroup();
        transform.Children.Add(new RotateTransform3D(_yaw));
        transform.Children.Add(new RotateTransform3D(_pitch));
        transform.Children.Add(_scale);
        _content.Transform = transform;

        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(0x50, 0x54, 0x5C)));
        lights.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-1, -1, -1)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(0x60, 0x66, 0x70), new Vector3D(1, -0.5, 1)));

        _camera = new PerspectiveCamera(new Point3D(0, 3.4, 8.8), new Vector3D(0, -3.4, -8.8),
            new Vector3D(0, 1, 0), VerticalFieldOfView);

        var viewport = new Viewport3D { Camera = _camera, IsHitTestVisible = false };
        viewport.Children.Add(new ModelVisual3D { Content = lights });
        viewport.Children.Add(_content);

        _hint = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8F, 0x98)),
            FontSize = 11,
            Margin = new Thickness(10, 0, 10, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false
        };

        _fanLabel = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xCE, 0xD6)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        _fanBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 10, 0),
            Visibility = Visibility.Collapsed
        };
        _fanBar.Children.Add(_fanLabel);
        foreach (var count in new[] { 0, 1, 2, 3 })
        {
            var button = new Button
            {
                Tag = count,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(2, 0, 0, 0),
                FontSize = 11,
                Cursor = Cursors.Hand
            };
            button.Click += (_, _) => FanCountChosen?.Invoke(this, (int)button.Tag);
            _fanBar.Children.Add(button);
        }

        var root = new Grid();
        root.Children.Add(viewport);
        root.Children.Add(_hint);
        root.Children.Add(_fanBar);
        Child = root;

        UpdateHint();
        SizeChanged += (_, e) => FitCamera(e.NewSize);
        Loaded += (_, _) => { Loc.Changed += OnLanguageChanged; Rebuild(); };
        Unloaded += (_, _) => { Loc.Changed -= OnLanguageChanged; StopSpin(); };
    }

    /// <summary>
    /// PerspectiveCamera.FieldOfView là góc ngang. Khung xem trong trang chi tiết rộng
    /// gấp mấy lần chiều cao, nên để nguyên góc ngang thì mô hình bị cắt mất trên dưới.
    /// Quy đổi để góc dọc luôn cố định — mô hình vừa khít chiều cao dù khung rộng bao nhiêu.
    /// </summary>
    private void FitCamera(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0) return;
        var halfVertical = VerticalFieldOfView / 2 * Math.PI / 180;
        var horizontal = 2 * Math.Atan(Math.Tan(halfVertical) * size.Width / size.Height);
        _camera.FieldOfView = horizontal * 180 / Math.PI;
    }

    private ViewState State => States.TryGetValue(Kind, out var s) ? s : States[Kind] = new ViewState();

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        UpdateHint();
        UpdateFanBar(EffectiveSpec);
    }

    /// <summary>Thông số dùng để vẽ: lấy Spec nếu khớp loại linh kiện, không thì mặc định.</summary>
    private ComponentModelSpec EffectiveSpec =>
        Spec is { } spec && spec.Kind == Kind ? spec : new ComponentModelSpec(Kind);

    private void UpdateFanBar(ComponentModelSpec spec)
    {
        var show = spec.Kind == ComponentKind.Gpu && spec.GpuForm != GpuForm.Integrated;
        _fanBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;

        var source = spec.FanSource switch
        {
            FanCountSource.Sensor => Loc.S("theo cảm biến", "from sensors"),
            FanCountSource.User => Loc.S("bạn đã chọn", "set by you"),
            _ => Loc.S("chưa đọc được, đang để mặc định", "not detected, using default")
        };
        _fanLabel.Text = Loc.S("Số quạt: {0} ({1})", "Fans: {0} ({1})", spec.FanCount, source);
        _fanLabel.ToolTip = Loc.S(
            "Card ba quạt thường chỉ báo hai cảm biến vì hai quạt dùng chung một bộ điều khiển. " +
            "Nhìn card thật rồi bấm đúng số quạt; ứng dụng sẽ nhớ cho card này.",
            "Three-fan cards often report only two sensors because two fans share one controller. " +
            "Look at the actual card and pick the right count; the app remembers it for this card.");

        var max = spec.GpuForm == GpuForm.LaptopDiscrete ? 2 : 3;
        foreach (var button in _fanBar.Children.OfType<Button>())
        {
            var count = (int)button.Tag;
            button.Content = count == 0 ? Loc.S("Tự động", "Auto") : count.ToString();
            button.Visibility = count <= max ? Visibility.Visible : Visibility.Collapsed;
            button.FontWeight = (count == 0 && spec.FanSource != FanCountSource.User) ||
                                (count == spec.FanCount && spec.FanSource == FanCountSource.User)
                ? FontWeights.Bold : FontWeights.Normal;
        }
    }

    private void UpdateHint() =>
        _hint.Text = Loc.S("Kéo để xoay · cuộn để phóng to · nhấp đúp để đặt lại",
                           "Drag to rotate · scroll to zoom · double-click to reset");

    private void Rebuild()
    {
        if (!IsLoaded) return;

        var spec = EffectiveSpec;
        _content.Content = GetModel(spec, Rating);
        UpdateFanBar(spec);
        ApplyState();

        if (State.Touched) StopSpin();
        else StartSpin();
    }

    private static Model3DGroup GetModel(ComponentModelSpec spec, HealthRating rating)
    {
        if (Cache.TryGetValue((spec, rating), out var cached)) return cached;

        var group = new Model3DGroup();
        foreach (var surface in ComponentModels.Build(spec))
        {
            var mesh = new MeshGeometry3D();
            foreach (var p in surface.Positions) mesh.Positions.Add(new Point3D(p.X, p.Y, p.Z));
            foreach (var n in surface.Normals) mesh.Normals.Add(new Vector3D(n.X, n.Y, n.Z));
            foreach (var i in surface.Indices) mesh.TriangleIndices.Add(i);
            mesh.Freeze();

            var material = ComponentModels.Materials[surface.Material];
            var hex = surface.Material == ComponentModels.RatingMaterial ? rating.ToHexColor() : material.Hex;
            var model = new GeometryModel3D(mesh, CreateMaterial(hex, material));
            model.Freeze();
            group.Children.Add(model);
        }

        group.Freeze();
        Cache[(spec, rating)] = group;
        return group;
    }

    private static Material CreateMaterial(string hex, MaterialSpec spec)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(color);
        brush.Freeze();

        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(brush));
        if (spec.Emissive)
            material.Children.Add(new EmissiveMaterial(brush));
        if (spec.SpecularPower > 0)
            material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(0x55, 0x58, 0x5E)),
                spec.SpecularPower));
        material.Freeze();
        return material;
    }

    private void ApplyState()
    {
        var s = State;
        _yaw.Angle = s.Yaw;
        _pitch.Angle = s.Pitch;
        _scale.ScaleX = _scale.ScaleY = _scale.ScaleZ = s.Zoom;
    }

    // ------------------------------------------------------------------
    // Tương tác chuột
    // ------------------------------------------------------------------

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (e.ClickCount == 2)
        {
            States[Kind] = new ViewState();
            ApplyState();
            StartSpin();
            e.Handled = true;
            return;
        }

        _dragging = true;
        _lastMouse = e.GetPosition(this);
        State.Touched = true;
        StopSpin();
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;

        var pos = e.GetPosition(this);
        var delta = pos - _lastMouse;
        _lastMouse = pos;

        var s = State;
        s.Yaw += delta.X * 0.6;
        s.Pitch = Math.Clamp(s.Pitch + delta.Y * 0.6, -85, 85);
        ApplyState();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragging = false;
        ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var s = State;
        s.Zoom = Math.Clamp(s.Zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), 0.6, 2.2);
        ApplyState();

        // Cuộn trong khung 3D là để phóng to, không để cuộn cả trang chi tiết.
        e.Handled = true;
    }

    // ------------------------------------------------------------------
    // Tự quay khi chưa có tương tác
    // ------------------------------------------------------------------

    private void StartSpin()
    {
        if (_spinning) return;
        _spinning = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopSpin()
    {
        if (!_spinning) return;
        _spinning = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs args) return;
        if (_lastFrame != TimeSpan.Zero)
        {
            var seconds = (args.RenderingTime - _lastFrame).TotalSeconds;
            State.Yaw += AutoSpinDegreesPerSecond * Math.Min(seconds, 0.1);
            _yaw.Angle = State.Yaw;
        }
        _lastFrame = args.RenderingTime;
    }
}
