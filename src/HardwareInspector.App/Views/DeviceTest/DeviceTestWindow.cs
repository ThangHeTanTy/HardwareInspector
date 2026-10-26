using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HardwareInspector.Collectors.Wmi;
using HardwareInspector.Core.Models.Diagnostics;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.App.Views.DeviceTest;

/// <summary>
/// Thử các thiết bị vào ra mà người mua hay bỏ sót: bàn phím, touchpad/chuột, loa, micro, webcam.
///
/// Dựng hoàn toàn bằng code thay vì XAML vì phần lớn nội dung sinh theo dữ liệu
/// (hàng trăm phím, lưới touchpad). Phần mềm đo những gì đo được — phím nào đã nhận,
/// phím nào dội, micro thu được bao nhiêu — còn nghe và nhìn thì người dùng tự chấm Đạt / Không đạt.
/// </summary>
public sealed class DeviceTestWindow : Window
{
    private static readonly Brush BrKeyIdle = Hex("#2A2F38");
    private static readonly Brush BrKeyDone = Hex("#1F7A4C");
    private static readonly Brush BrKeyDown = Hex("#4C8DFF");
    private static readonly Brush BrKeyBad = Hex("#C0392B");
    private static readonly Brush BrKeyHeld = Hex("#D9822B");
    private static readonly Brush BrKeyExcluded = Hex("#15181D");
    private static readonly Brush BrText = Hex("#E6E8EC");
    private static readonly Brush BrDim = Hex("#9AA0AA");
    private static readonly Brush BrPanel = Hex("#171A21");
    private static readonly Brush BrLine = Hex("#262A33");

    private readonly DeviceTestResult _result;
    private readonly bool _isLaptop;
    private readonly TabControl _tabs = new();
    private readonly KeyboardHook _hook = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };

    public DeviceTestWindow(DeviceTestResult result, bool isLaptop)
    {
        _result = result;
        _isLaptop = isLaptop;

        Title = S("Thử bàn phím và thiết bị", "Keyboard and device test");
        Width = 1180;
        Height = 760;
        MinWidth = 900;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = TryFindResource("BgBrush") as Brush ?? Hex("#0F1115");
        Foreground = BrText;

        _tabs.Background = Brushes.Transparent;
        _tabs.BorderThickness = new Thickness(0);
        _tabs.Items.Add(Tab(S("Bàn phím", "Keyboard"), BuildKeyboard()));
        _tabs.Items.Add(Tab(S("Touchpad / chuột", "Touchpad / mouse"), BuildPointer()));
        _tabs.Items.Add(Tab(S("Loa", "Speakers"), BuildSpeakers()));
        _tabs.Items.Add(Tab("Micro", BuildMicrophone()));
        _tabs.Items.Add(Tab("Webcam", BuildCamera()));
        _tabs.SelectionChanged += (_, e) => { if (e.Source == _tabs) UpdateHook(); };

        var close = new Button { Content = S("Xong — lưu kết quả", "Done — save results"), Padding = new Thickness(16, 6, 16, 6) };
        close.Click += (_, _) => Close();
        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(close, Dock.Right);
        bottom.Children.Add(close);
        bottom.Children.Add(new TextBlock
        {
            Text = S("Kết quả Đạt / Không đạt được đưa vào kết luận và chi phí khắc phục ở màn hình chính.",
                     "Pass / Fail results feed into the verdict and repair costs on the main screen."),
            Foreground = BrDim, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap
        });

        var root = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(_tabs);
        Content = root;

        Activated += (_, _) => UpdateHook();
        Deactivated += (_, _) => UpdateHook();
        Closed += (_, _) => Finish();

        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
    }

    // ==================================================================
    // Bàn phím
    // ==================================================================

    private sealed class KeyVisual
    {
        public required KeyDef Def { get; init; }
        public required Border Box { get; init; }
        public required bool InNumpad { get; init; }
        public bool Tested, Down, Chatter, Held, Excluded;
        public uint DownAt, UpAt;
        public DateTime DownSince;
    }

    private readonly List<KeyVisual> _keys = new();
    private readonly Dictionary<int, List<KeyVisual>> _keysById = new();
    private TextBlock _keyboardStatus = null!, _keyboardSummary = null!;
    private Canvas _numpadCanvas = null!;
    private bool _numpadVisible;
    private int _maxSimultaneous;

    private UIElement BuildKeyboard()
    {
        _keyboardStatus = new TextBlock { Foreground = Hex("#4C8DFF"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        _keyboardSummary = new TextBlock { Foreground = BrText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };

        const double unit = 46;
        var main = DrawKeys(KeyboardLayout.Main, unit, false, out var mainWidth, out var mainHeight);
        var nav = DrawKeys(KeyboardLayout.Navigation, unit, false, out var navWidth, out _);
        _numpadCanvas = DrawKeys(KeyboardLayout.Numpad, unit, true, out var padWidth, out _);
        _numpadCanvas.Visibility = Visibility.Collapsed;

        var board = new StackPanel { Orientation = Orientation.Horizontal };
        board.Children.Add(main);
        var side = new StackPanel { Margin = new Thickness(unit * 0.4, 0, 0, 0) };
        side.Children.Add(nav);
        side.Children.Add(_numpadCanvas);
        _numpadCanvas.Margin = new Thickness(0, unit * 0.3, 0, 0);
        board.Children.Add(side);

        var numpadToggle = new CheckBox { Content = S("Máy có bàn phím số", "Has a numeric keypad"), Foreground = BrText, Margin = new Thickness(0, 0, 16, 0) };
        numpadToggle.Checked += (_, _) => { _numpadVisible = true; _numpadCanvas.Visibility = Visibility.Visible; RefreshKeyboardSummary(); };
        numpadToggle.Unchecked += (_, _) => { _numpadVisible = false; _numpadCanvas.Visibility = Visibility.Collapsed; RefreshKeyboardSummary(); };

        var reset = new Button { Content = S("Làm lại", "Start over"), Padding = new Thickness(12, 4, 12, 4) };
        reset.Click += (_, _) => ResetKeyboard();

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        tools.Children.Add(numpadToggle);
        tools.Children.Add(reset);

        var panel = new DockPanel();
        var top = new StackPanel();
        top.Children.Add(Hint(S(
            "Bấm lần lượt từng phím, phím đã nhận chuyển sang màu xanh. Phím đỏ là phím dội (bấm một lần ra hai lần), " +
            "phím cam là phím bị giữ quá 3 giây mà không nhả — nghi kẹt. Bấm nhiều phím cùng lúc (ví dụ W + A + S + D + Space) " +
            "để thử bàn phím có nuốt phím không. Phím máy không có thì bấm chuột phải lên phím đó để bỏ qua. " +
            "Phím Fn và các phím chỉnh độ sáng/âm lượng do phần cứng của máy xử lý, Windows không thấy — hãy thử chúng bằng mắt.",
            "Press every key in turn; keys turn green once received. Red means chatter (one press registers twice), " +
            "orange means held for over 3 seconds without release — possibly stuck. Press several keys together (e.g. W + A + S + D + Space) " +
            "to check for ghosting. Right-click a key the machine does not have to skip it. " +
            "Fn and brightness/volume keys are handled by the machine's hardware and are invisible to Windows — check them by eye.")));
        top.Children.Add(tools);
        top.Children.Add(_keyboardStatus);
        top.Children.Add(_keyboardSummary);
        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);

        var verdict = VerdictRow(S("Bàn phím", "Keyboard"), _result.KeyboardOk, v => _result.KeyboardOk = v);
        DockPanel.SetDock(verdict, Dock.Bottom);
        panel.Children.Add(verdict);

        panel.Children.Add(new Viewbox { Child = board, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, Margin = new Thickness(0, 4, 0, 8) });

        RefreshKeyboardSummary();
        return Card(panel);
    }

    private Canvas DrawKeys(IReadOnlyList<IReadOnlyList<KeyDef>> rows, double unit, bool numpad, out double width, out double height)
    {
        var canvas = new Canvas();
        width = 0;
        for (var r = 0; r < rows.Count; r++)
        {
            double x = 0;
            var y = r * unit + (r > 0 && !numpad && rows.Count > 2 ? unit * 0.25 : 0);
            foreach (var def in rows[r])
            {
                var w = def.Width * unit;
                if (!def.IsGap)
                {
                    // Phím "0" và Enter bàn phím số cao/rộng khác thường; vẽ đúng bề rộng là đủ nhận ra.
                    var box = new Border
                    {
                        Width = w - 4, Height = unit - 4, CornerRadius = new CornerRadius(5),
                        Background = BrKeyIdle, BorderBrush = BrLine, BorderThickness = new Thickness(1),
                        Child = new TextBlock
                        {
                            Text = def.Label, Foreground = BrText, FontSize = def.Label.Length > 3 ? 11 : 13,
                            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                        },
                        Cursor = Cursors.Hand
                    };
                    var visual = new KeyVisual { Def = def, Box = box, InNumpad = numpad };
                    box.MouseRightButtonUp += (_, _) => { visual.Excluded = !visual.Excluded; Paint(visual); RefreshKeyboardSummary(); };
                    box.ToolTip = S("Chuột phải: máy không có phím này", "Right-click: the machine has no such key");
                    Canvas.SetLeft(box, x + 2);
                    Canvas.SetTop(box, y + 2);
                    canvas.Children.Add(box);

                    _keys.Add(visual);
                    foreach (var id in def.Ids)
                    {
                        if (!_keysById.TryGetValue(id, out var list)) _keysById[id] = list = new List<KeyVisual>();
                        list.Add(visual);
                    }
                }
                x += w;
            }
            width = Math.Max(width, x);
        }

        height = rows.Count * unit + (numpad ? 0 : unit * 0.25);
        canvas.Width = width;
        canvas.Height = height;
        return canvas;
    }

    private void OnKey(KeyEvent e)
    {
        if (!_keysById.TryGetValue(e.KeyId, out var candidates)) return;

        // Một mã có thể thuộc cả cụm điều hướng lẫn bàn phím số (khi tắt NumLock): ưu tiên phím đang hiện.
        var key = candidates.FirstOrDefault(k => !k.InNumpad || _numpadVisible) ?? candidates[0];

        if (e.Down)
        {
            if (key.Down) return;   // tự lặp khi giữ phím, không phải lần bấm mới

            // Nhả rồi nhấn lại trong chưa tới 30 ms thì ngón tay không làm nổi — đó là tiếp điểm dội.
            if (key.Tested && key.UpAt != 0 && e.TimeMs - key.UpAt < 30) key.Chatter = true;
            key.Down = true;
            key.Tested = true;
            key.DownAt = e.TimeMs;
            key.DownSince = DateTime.UtcNow;
        }
        else
        {
            key.Down = false;
            key.UpAt = e.TimeMs;
        }

        _maxSimultaneous = Math.Max(_maxSimultaneous, _keys.Count(k => k.Down));
        Paint(key);
        RefreshKeyboardSummary();
    }

    private static void Paint(KeyVisual k)
    {
        k.Box.Background = k.Excluded ? BrKeyExcluded
            : k.Down ? BrKeyDown
            : k.Chatter ? BrKeyBad
            : k.Held ? BrKeyHeld
            : k.Tested ? BrKeyDone
            : BrKeyIdle;
        k.Box.Opacity = k.Excluded ? 0.45 : 1;
    }

    private IEnumerable<KeyVisual> CountedKeys => _keys.Where(k => !k.Excluded && (!k.InNumpad || _numpadVisible));

    private void RefreshKeyboardSummary()
    {
        var counted = CountedKeys.ToList();
        var tested = counted.Count(k => k.Tested);
        var chatter = counted.Where(k => k.Chatter).Select(k => k.Def.Label).ToList();
        var held = counted.Where(k => k.Held).Select(k => k.Def.Label).ToList();

        var text = S($"Đã nhận {tested}/{counted.Count} phím · bấm cùng lúc tối đa {_maxSimultaneous} phím",
                     $"Received {tested}/{counted.Count} keys · up to {_maxSimultaneous} keys at once");
        if (chatter.Count > 0) text += S($"\nPhím dội: {string.Join(", ", chatter)}", $"\nChattering keys: {string.Join(", ", chatter)}");
        if (held.Count > 0) text += S($"\nPhím nghi kẹt: {string.Join(", ", held)}", $"\nPossibly stuck keys: {string.Join(", ", held)}");
        _keyboardSummary.Text = text;
    }

    private void ResetKeyboard()
    {
        foreach (var k in _keys)
        {
            k.Tested = k.Down = k.Chatter = k.Held = false;
            k.DownAt = k.UpAt = 0;
            Paint(k);
        }
        _maxSimultaneous = 0;
        RefreshKeyboardSummary();
    }

    private void UpdateHook()
    {
        var wanted = IsActive && _tabs.SelectedIndex == 0;
        if (wanted && !_hook.IsActive)
        {
            _hook.Key += OnKey;
            _hook.Start();
        }
        else if (!wanted && _hook.IsActive)
        {
            _hook.Stop();
            _hook.Key -= OnKey;
            foreach (var k in _keys.Where(k => k.Down)) { k.Down = false; Paint(k); }
        }

        if (_keyboardStatus is not null)
            _keyboardStatus.Text = _hook.IsActive
                ? S("● Đang nhận phím — phím Windows, Alt+Tab tạm thời bị chặn. Bấm nút \"Xong\" bằng chuột để thoát.",
                    "● Capturing keys — Windows key and Alt+Tab are blocked for now. Click \"Done\" with the mouse to exit.")
                : S("○ Bấm vào cửa sổ này để tiếp tục nhận phím.", "○ Click this window to resume capturing keys.");
    }

    // ==================================================================
    // Touchpad / chuột
    // ==================================================================

    private const int GridColumns = 32, GridRows = 18;
    private readonly bool[] _visited = new bool[GridColumns * GridRows];
    private readonly System.Windows.Shapes.Rectangle[] _cells = new System.Windows.Shapes.Rectangle[GridColumns * GridRows];
    private TextBlock _pointerSummary = null!, _left = null!, _right = null!, _middle = null!, _scroll = null!;

    private UIElement BuildPointer()
    {
        var grid = new UniformGrid { Rows = GridRows, Columns = GridColumns, Background = Hex("#101318") };
        for (var i = 0; i < _cells.Length; i++)
        {
            _cells[i] = new System.Windows.Shapes.Rectangle { Fill = Brushes.Transparent, Stroke = BrLine, StrokeThickness = 0.5 };
            grid.Children.Add(_cells[i]);
        }

        var area = new Border { Child = grid, BorderBrush = BrLine, BorderThickness = new Thickness(1), Cursor = Cursors.Cross };
        area.MouseMove += (_, e) =>
        {
            var p = e.GetPosition(grid);
            if (grid.ActualWidth <= 0 || grid.ActualHeight <= 0) return;
            var c = (int)(p.X / grid.ActualWidth * GridColumns);
            var r = (int)(p.Y / grid.ActualHeight * GridRows);
            if (c < 0 || r < 0 || c >= GridColumns || r >= GridRows) return;
            var i = r * GridColumns + c;
            if (_visited[i]) return;
            _visited[i] = true;
            _cells[i].Fill = BrKeyDone;
            RefreshPointer();
        };
        area.MouseDown += (_, e) =>
        {
            switch (e.ChangedButton)
            {
                case MouseButton.Left: _result.LeftClickSeen = true; break;
                case MouseButton.Right: _result.RightClickSeen = true; break;
                case MouseButton.Middle: _middle.Tag = true; break;
            }
            RefreshPointer();
        };
        area.MouseWheel += (_, _) => { _result.ScrollSeen = true; RefreshPointer(); };

        _left = Chip(S("Click trái", "Left click"));
        _right = Chip(S("Click phải", "Right click"));
        _middle = Chip(S("Click giữa / ba ngón", "Middle / three-finger click"));
        _scroll = Chip(S("Cuộn hai ngón / con lăn", "Two-finger scroll / wheel"));
        _pointerSummary = new TextBlock { Foreground = BrText, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };

        var reset = new Button { Content = S("Làm lại", "Start over"), Padding = new Thickness(12, 4, 12, 4) };
        reset.Click += (_, _) =>
        {
            Array.Clear(_visited);
            foreach (var cell in _cells) cell.Fill = Brushes.Transparent;
            _result.LeftClickSeen = _result.RightClickSeen = _result.ScrollSeen = false;
            _middle.Tag = null;
            RefreshPointer();
        };

        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        chips.Children.Add(_pointerSummary);
        foreach (var chip in new[] { _left, _right, _middle, _scroll }) chips.Children.Add(chip);
        chips.Children.Add(reset);

        var panel = new DockPanel();
        var top = new StackPanel();
        top.Children.Add(Hint(S(
            "Rê ngón tay trên touchpad (hoặc rê chuột) cho con trỏ đi kín khung bên dưới. Con trỏ nhảy, khựng, hoặc không kéo " +
            "được tới một góc nào là dấu hiệu touchpad có vùng chết. Sau đó thử click trái, click phải và cuộn hai ngón trong khung.",
            "Move your finger on the touchpad (or the mouse) until the pointer has covered the whole area below. A pointer that jumps, " +
            "stutters, or cannot reach a corner means the touchpad has dead zones. Then try left click, right click and two-finger scroll inside the area.")));
        top.Children.Add(chips);
        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);

        var verdict = VerdictRow(S("Touchpad / chuột", "Touchpad / mouse"), _result.TouchpadOk, v => _result.TouchpadOk = v);
        DockPanel.SetDock(verdict, Dock.Bottom);
        panel.Children.Add(verdict);
        area.Margin = new Thickness(0, 0, 0, 8);
        panel.Children.Add(area);

        RefreshPointer();
        return Card(panel);
    }

    private void RefreshPointer()
    {
        _result.TouchpadCoveragePercent = Math.Round(_visited.Count(v => v) * 100.0 / _visited.Length, 1);
        _pointerSummary.Text = S($"Đã phủ {_result.TouchpadCoveragePercent:0}% khung", $"Covered {_result.TouchpadCoveragePercent:0}% of the area");
        Mark(_left, _result.LeftClickSeen);
        Mark(_right, _result.RightClickSeen);
        Mark(_middle, _middle.Tag is true);
        Mark(_scroll, _result.ScrollSeen);
    }

    // ==================================================================
    // Loa
    // ==================================================================

    private UIElement BuildSpeakers()
    {
        var buttons = new WrapPanel { Margin = new Thickness(0, 4, 0, 16) };
        void Add(string text, Action play)
        {
            var b = new Button { Content = text, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 10, 10) };
            b.Click += (_, _) => { try { play(); } catch { } };
            buttons.Children.Add(b);
        }
        Add(S("▶ Loa trái", "▶ Left speaker"), () => ToneGenerator.PlayTone(ToneGenerator.Channel.Left));
        Add(S("▶ Loa phải", "▶ Right speaker"), () => ToneGenerator.PlayTone(ToneGenerator.Channel.Right));
        Add(S("▶ Cả hai loa", "▶ Both speakers"), () => ToneGenerator.PlayTone(ToneGenerator.Channel.Both, 660));
        Add(S("▶ Quét âm trầm → cao (nghe rè)", "▶ Low → high sweep (listen for rattle)"), () => ToneGenerator.PlaySweep());

        var panel = new StackPanel();
        panel.Children.Add(Hint(S(
            "Bật âm lượng khoảng 70%, rút tai nghe ra. Bấm \"Loa trái\" thì chỉ loa bên trái được kêu, \"Loa phải\" thì chỉ bên phải. " +
            "Nghe cả hai bên đều phát, hoặc một bên im, là loa hoặc đường tín hiệu có vấn đề. Bài quét âm giúp nghe tiếng rè, lẹt xẹt " +
            "ở dải trầm — chỗ loa laptop hay rách màng.",
            "Set the volume to about 70% and unplug headphones. \"Left speaker\" should play only on the left, \"Right speaker\" only on the right. " +
            "Hearing both sides, or one side silent, means a speaker or signal path problem. The sweep helps you hear rattle or buzzing " +
            "in the bass range — where laptop speaker cones usually tear.")));
        panel.Children.Add(buttons);
        panel.Children.Add(VerdictRow(S("Loa trái", "Left speaker"), _result.SpeakerLeftOk, v => _result.SpeakerLeftOk = v));
        panel.Children.Add(VerdictRow(S("Loa phải", "Right speaker"), _result.SpeakerRightOk, v => _result.SpeakerRightOk = v));
        return Card(panel);
    }

    // ==================================================================
    // Micro
    // ==================================================================

    private readonly MicrophoneMeter _mic = new();
    private Border _micBar = null!;
    private TextBlock _micText = null!;
    private Button _micButton = null!;
    private double _micLevel;

    private UIElement BuildMicrophone()
    {
        _micBar = new Border { Background = Hex("#2FBF71"), Width = 0, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(4) };
        var track = new Border { Height = 26, Background = Hex("#101318"), BorderBrush = BrLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = _micBar, Margin = new Thickness(0, 6, 0, 6) };
        _micText = new TextBlock { Foreground = BrText, Margin = new Thickness(0, 0, 0, 12) };
        _micButton = new Button { Content = S("● Bắt đầu thu", "● Start listening"), Padding = new Thickness(14, 6, 14, 6), HorizontalAlignment = HorizontalAlignment.Left };
        _micButton.Click += (_, _) => ToggleMic();

        var panel = new StackPanel();
        panel.Children.Add(Hint(S(
            "Bấm \"Bắt đầu thu\" rồi nói bình thường cách máy khoảng 40 cm. Thanh mức phải nhảy lên theo tiếng nói; " +
            "im lặng thì gần như đứng yên. Không nhúc nhích là micro hỏng, bị tắt trong BIOS, hoặc bị chặn ở Cài đặt → Quyền riêng tư → Micro.",
            "Click \"Start listening\" and speak normally about 40 cm from the machine. The level bar should jump with your voice " +
            "and stay almost still in silence. No movement means a dead microphone, one disabled in the BIOS, or blocked in Settings → Privacy → Microphone.")));
        panel.Children.Add(new TextBlock
        {
            Text = S($"Số thiết bị thu âm Windows thấy: {MicrophoneMeter.DeviceCount}", $"Recording devices Windows sees: {MicrophoneMeter.DeviceCount}"),
            Foreground = BrDim, Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(_micButton);
        panel.Children.Add(track);
        panel.Children.Add(_micText);
        panel.Children.Add(VerdictRow("Micro", _result.MicOk, v => _result.MicOk = v));
        return Card(panel);
    }

    private void ToggleMic()
    {
        if (_mic.IsRunning)
        {
            _mic.Stop();
            _micButton.Content = S("● Bắt đầu thu", "● Start listening");
            return;
        }

        var error = _mic.Start();
        if (error is not null)
        {
            _micText.Text = S($"Không mở được micro ({error}). Kiểm tra máy có micro và quyền truy cập micro trong Cài đặt.",
                              $"Could not open the microphone ({error}). Check the machine has one and microphone access is allowed in Settings.");
            return;
        }
        _micButton.Content = S("■ Dừng thu", "■ Stop listening");
    }

    // ==================================================================
    // Webcam
    // ==================================================================

    private UIElement BuildCamera()
    {
        var list = new TextBlock { Foreground = BrText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), Text = S("Đang tìm camera…", "Looking for cameras…") };

        _ = Task.Run(() => WmiQuery.Query(
                    "SELECT Name, PNPClass FROM Win32_PnPEntity WHERE PNPClass = 'Camera' OR PNPClass = 'Image'")
                .Select(m => m.Str("Name")).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList())
            .ContinueWith(t =>
            {
                var cameras = t.IsCompletedSuccessfully ? t.Result : new List<string>();
                _result.Cameras.Clear();
                _result.Cameras.AddRange(cameras);
                list.Text = cameras.Count == 0
                    ? S("Windows không thấy camera nào. Có thể camera bị tắt bằng phím/công tắc vật lý, tắt trong BIOS, hoặc đã hỏng.",
                        "Windows sees no camera. It may be disabled by a key/physical shutter switch, turned off in the BIOS, or broken.")
                    : S("Camera Windows thấy: ", "Cameras Windows sees: ") + string.Join(", ", cameras);
            }, TaskScheduler.FromCurrentSynchronizationContext());

        var open = new Button { Content = S("Mở ứng dụng Camera của Windows", "Open the Windows Camera app"), Padding = new Thickness(14, 6, 14, 6), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 16) };
        open.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("microsoft.windows.camera:") { UseShellExecute = true }); }
            catch { }
        };

        var panel = new StackPanel();
        panel.Children.Add(Hint(S(
            "Mở ứng dụng Camera và nhìn hình: phải rõ, không sọc, không đốm, không tối đen. Nhiều laptop doanh nghiệp có nắp che " +
            "camera hoặc phím tắt camera — hãy gạt mở trước. Đèn báo cạnh camera nên sáng khi đang quay.",
            "Open the Camera app and check the picture: clear, no stripes, no spots, not black. Many business laptops have a privacy shutter " +
            "or a camera key — open it first. The indicator light next to the camera should turn on while recording.")));
        panel.Children.Add(list);
        panel.Children.Add(open);
        panel.Children.Add(VerdictRow("Webcam", _result.CameraOk, v => _result.CameraOk = v));
        return Card(panel);
    }

    // ==================================================================
    // Chung
    // ==================================================================

    private void OnTick()
    {
        // Phím giữ quá 3 giây: người thật hiếm khi giữ lâu vậy trong bài thử, phím kẹt thì giữ mãi.
        var now = DateTime.UtcNow;
        foreach (var k in _keys.Where(k => k.Down && !k.Held && (now - k.DownSince).TotalSeconds > 3))
        {
            k.Held = true;
            RefreshKeyboardSummary();
        }

        if (_mic.IsRunning && _mic.Poll() is { } peak)
        {
            _micLevel = Math.Max(peak, _micLevel * 0.85);
            var db = peak > 0 ? 20 * Math.Log10(peak) : -96;
            _result.MicPeakDb = Math.Max(_result.MicPeakDb ?? -96, db);
            if (_micBar.Parent is Border track && track.ActualWidth > 0)
                _micBar.Width = Math.Clamp(_micLevel, 0, 1) * (track.ActualWidth - 2);
            _micText.Text = S($"Mức hiện tại {db:0} dB · to nhất đã thu {_result.MicPeakDb:0} dB",
                              $"Current level {db:0} dB · loudest so far {_result.MicPeakDb:0} dB");
        }
    }

    private void Finish()
    {
        _timer.Stop();
        _hook.Dispose();
        _mic.Dispose();
        ToneGenerator.Stop();

        var counted = CountedKeys.ToList();
        if (counted.Any(k => k.Tested))
        {
            _result.KeysTotal = counted.Count;
            _result.KeysPressed = counted.Count(k => k.Tested);
            _result.UntestedKeys.Clear();
            _result.UntestedKeys.AddRange(counted.Where(k => !k.Tested).Select(k => k.Def.Label));
            _result.ChatterKeys.Clear();
            _result.ChatterKeys.AddRange(counted.Where(k => k.Chatter).Select(k => k.Def.Label));
            _result.StuckKeys.Clear();
            _result.StuckKeys.AddRange(counted.Where(k => k.Held).Select(k => k.Def.Label));
            _result.MaxSimultaneousKeys = _maxSimultaneous;
        }
    }

    private UIElement VerdictRow(string label, bool? current, Action<bool?> set)
    {
        var group = Guid.NewGuid().ToString("N");
        var pass = new RadioButton { Content = S("Đạt", "Pass"), GroupName = group, Foreground = BrText, Margin = new Thickness(0, 0, 16, 0), IsChecked = current == true };
        var fail = new RadioButton { Content = S("Không đạt", "Fail"), GroupName = group, Foreground = Hex("#F4A259"), Margin = new Thickness(0, 0, 16, 0), IsChecked = current == false };
        var skip = new RadioButton { Content = S("Chưa thử", "Not tested"), GroupName = group, Foreground = BrDim, IsChecked = current is null };
        pass.Checked += (_, _) => set(true);
        fail.Checked += (_, _) => set(false);
        skip.Checked += (_, _) => set(null);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 4) };
        row.Children.Add(new TextBlock { Text = label + ":", Foreground = BrText, FontWeight = FontWeights.SemiBold, Width = 150, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(pass);
        row.Children.Add(fail);
        row.Children.Add(skip);
        return row;
    }

    private static TabItem Tab(string header, UIElement content) => new() { Header = header, Content = content };

    private UIElement Card(UIElement child) => new Border
    {
        Background = BrPanel, BorderBrush = BrLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
        Padding = new Thickness(18), Margin = new Thickness(0, 12, 0, 0), Child = child
    };

    private static TextBlock Hint(string text) => new()
    {
        Text = text, Foreground = BrDim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), FontSize = 13
    };

    private static TextBlock Chip(string text) => new()
    {
        Text = "○ " + text, Foreground = BrDim, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center
    };

    private static void Mark(TextBlock chip, bool seen)
    {
        var text = chip.Text[2..];
        chip.Text = (seen ? "● " : "○ ") + text;
        chip.Foreground = seen ? Hex("#2FBF71") : BrDim;
    }

    private static Brush Hex(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
