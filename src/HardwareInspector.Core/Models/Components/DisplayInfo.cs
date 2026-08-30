namespace HardwareInspector.Core.Models.Components;

/// <summary>Cổng vật lý mà màn hình đang cắm vào.</summary>
public enum DisplayConnection
{
    Unknown = 0,
    Internal,       // tấm nền gắn liền máy (eDP/LVDS)
    Vga,
    Dvi,
    Hdmi,
    DisplayPort,
    UsbC,
    Wireless,
    Other
}

public sealed class DisplayInfo
{
    // --- Định danh trong hệ thống ---
    public string AdapterDeviceName { get; set; } = string.Empty;   // \\.\DISPLAY1
    public string AdapterDescription { get; set; } = string.Empty;  // tên card xuất hình
    public string MonitorDeviceId { get; set; } = string.Empty;     // DISPLAY\GSM5B09\5&...
    public string FriendlyName { get; set; } = string.Empty;        // tên Windows hiển thị

    // --- Dữ liệu bóc từ EDID ---
    public string ManufacturerCode { get; set; } = string.Empty;
    public string ManufacturerName { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string PanelModel { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public int? ManufactureYear { get; set; }
    public int? ManufactureWeek { get; set; }
    public int? DiagonalInches { get; set; }
    public int? BitsPerColor { get; set; }
    public int? NativeWidthPx { get; set; }
    public int? NativeHeightPx { get; set; }
    public int? MaxRefreshHz { get; set; }
    public bool EdidAvailable { get; set; }

    // --- Chế độ đang chạy ---
    public int WidthPx { get; set; }
    public int HeightPx { get; set; }
    public int? RefreshHz { get; set; }
    public int? BitsPerPixel { get; set; }

    // --- Vị trí trên desktop ảo, đơn vị pixel vật lý ---
    public int PositionX { get; set; }
    public int PositionY { get; set; }

    public DisplayConnection Connection { get; set; } = DisplayConnection.Unknown;
    public bool IsInternal => Connection == DisplayConnection.Internal;
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; }

    public string Resolution => WidthPx > 0 ? $"{WidthPx}x{HeightPx}" : "—";

    public string NativeResolution =>
        NativeWidthPx is > 0 ? $"{NativeWidthPx}x{NativeHeightPx}" : "—";

    /// <summary>Đang chạy đúng độ phân giải gốc của tấm nền hay không.</summary>
    public bool? RunningAtNativeResolution =>
        NativeWidthPx is > 0 && WidthPx > 0
            ? NativeWidthPx == WidthPx && NativeHeightPx == HeightPx
            : null;

    public string ConnectionText => Connection switch
    {
        DisplayConnection.Internal => "Tấm nền tích hợp (eDP/LVDS)",
        DisplayConnection.Vga => "VGA (D-Sub)",
        DisplayConnection.Dvi => "DVI",
        DisplayConnection.Hdmi => "HDMI",
        DisplayConnection.DisplayPort => "DisplayPort",
        DisplayConnection.UsbC => "USB-C / Thunderbolt",
        DisplayConnection.Wireless => "Không dây (Miracast)",
        DisplayConnection.Other => "Cổng khác",
        _ => "Không xác định"
    };

    /// <summary>Tên cổng ở dạng ngắn, dùng cho ô chọn màn hình.</summary>
    public string ConnectionShort => Connection switch
    {
        DisplayConnection.Internal => "tích hợp",
        DisplayConnection.Vga => "VGA",
        DisplayConnection.Dvi => "DVI",
        DisplayConnection.Hdmi => "HDMI",
        DisplayConnection.DisplayPort => "DisplayPort",
        DisplayConnection.UsbC => "USB-C",
        DisplayConnection.Wireless => "không dây",
        _ => "cổng khác"
    };

    /// <summary>
    /// Nhãn cho ô chọn màn hình. Giữ ngắn có chủ đích: ô chọn nằm trên thanh lệnh
    /// nên bề rộng hạn chế, và thứ người dùng cần để phân biệt hai màn chỉ là
    /// tên tấm nền, độ phân giải và cổng cắm.
    /// </summary>
    public string DisplayLabel
    {
        get
        {
            var name = !string.IsNullOrWhiteSpace(PanelModel) ? PanelModel
                     : !string.IsNullOrWhiteSpace(FriendlyName) ? FriendlyName
                     : !string.IsNullOrWhiteSpace(ManufacturerName) ? ManufacturerName
                     : AdapterDeviceName.Replace(@"\\.\", string.Empty);

            var tag = IsPrimary ? " · chính" : string.Empty;
            return $"{name} · {Resolution} · {ConnectionShort}{tag}";
        }
    }
}
