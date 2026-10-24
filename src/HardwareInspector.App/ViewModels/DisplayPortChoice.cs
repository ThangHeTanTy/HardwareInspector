using HardwareInspector.App.Localization;
using HardwareInspector.Core.Models.Components;

namespace HardwareInspector.App.ViewModels;

public sealed record PortOption(DisplayConnection? Value, string Label);

/// <summary>
/// Ô chọn cổng thật của một màn hình. Dùng khi bộ chuyển đổi che mất kiểu cổng
/// (ví dụ màn VGA qua bộ chuyển DisplayPort→VGA thay EDID của màn) — người dùng
/// nhìn được dây cắm nên chọn tay, kết quả chấm điểm được tính lại ngay.
/// </summary>
public sealed class DisplayPortChoice : ObservableObject
{
    private static readonly DisplayConnection[] Ports =
    {
        DisplayConnection.Vga, DisplayConnection.Dvi, DisplayConnection.Hdmi,
        DisplayConnection.DisplayPort, DisplayConnection.UsbC
    };

    private readonly Action<DisplayPortChoice> _changed;
    private PortOption _selected;

    public DisplayPortChoice(DisplayInfo display, int index, Action<DisplayPortChoice> changed)
    {
        Display = display;
        _changed = changed;
        Title = $"{string.Format(UiStrings.Instance["Display.Port.Screen"], index + 1)} · {display.DisplayLabel}";

        Options = new[]
            {
                new PortOption(null, string.Format(UiStrings.Instance["Display.Port.Auto"],
                    DisplayInfo.LongName(display.DetectedConnection)))
            }
            .Concat(Ports.Select(p => new PortOption(p, DisplayInfo.LongName(p))))
            .ToList();

        _selected = display.ConnectionSetByUser
            ? Options.FirstOrDefault(o => o.Value == display.Connection) ?? Options[0]
            : Options[0];
    }

    public DisplayInfo Display { get; }
    public string Title { get; }
    public IReadOnlyList<PortOption> Options { get; }

    public PortOption Selected
    {
        get => _selected;
        set
        {
            if (value is null || !Set(ref _selected, value)) return;
            _changed(this);
        }
    }
}
