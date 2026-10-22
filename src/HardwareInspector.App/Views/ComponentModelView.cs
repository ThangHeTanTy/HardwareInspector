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
    private static readonly Dictionary<(ComponentKind, HealthRating), Model3DGroup> Cache = new();

    private const double AutoSpinDegreesPerSecond = 18;

    /// <summary>Góc nhìn dọc mong muốn. WPF chỉ nhận góc ngang nên phải quy đổi theo tỉ lệ khung.</summary>
    private const double VerticalFieldOfView = 40;

    private readonly PerspectiveCamera _camera;

    private readonly ModelVisual3D _content = new();
    private readonly AxisAngleRotation3D _yaw = new(new Vector3D(0, 1, 0), 0);
    private readonly AxisAngleRotation3D _pitch = new(new Vector3D(1, 0, 0), 0);
    private readonly ScaleTransform3D _scale = new(1, 1, 1);
    private readonly TextBlock _hint;

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

        var root = new Grid();
        root.Children.Add(viewport);
        root.Children.Add(_hint);
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

    private void OnLanguageChanged(object? sender, EventArgs e) => UpdateHint();

    private void UpdateHint() =>
        _hint.Text = Loc.S("Kéo để xoay · cuộn để phóng to · nhấp đúp để đặt lại",
                           "Drag to rotate · scroll to zoom · double-click to reset");

    private void Rebuild()
    {
        if (!IsLoaded) return;

        _content.Content = GetModel(Kind, Rating);
        ApplyState();

        if (State.Touched) StopSpin();
        else StartSpin();
    }

    private static Model3DGroup GetModel(ComponentKind kind, HealthRating rating)
    {
        if (Cache.TryGetValue((kind, rating), out var cached)) return cached;

        var group = new Model3DGroup();
        foreach (var surface in ComponentModels.Build(kind))
        {
            var mesh = new MeshGeometry3D();
            foreach (var p in surface.Positions) mesh.Positions.Add(new Point3D(p.X, p.Y, p.Z));
            foreach (var n in surface.Normals) mesh.Normals.Add(new Vector3D(n.X, n.Y, n.Z));
            foreach (var i in surface.Indices) mesh.TriangleIndices.Add(i);
            mesh.Freeze();

            var spec = ComponentModels.Materials[surface.Material];
            var hex = surface.Material == ComponentModels.RatingMaterial ? rating.ToHexColor() : spec.Hex;
            var model = new GeometryModel3D(mesh, CreateMaterial(hex, spec));
            model.Freeze();
            group.Children.Add(model);
        }

        group.Freeze();
        Cache[(kind, rating)] = group;
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
