using static HardwareInspector.Core.Localization.Loc;

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

    /// <summary>
    /// Kiểu tín hiệu vào mà màn hình tự khai trong EDID (byte 20, bit 7).
    /// False nghĩa là màn nhận tín hiệu analog — tức là màn VGA, bất kể card
    /// xuất hình báo cổng gì.
    /// </summary>
    public bool? EdidDigitalInput { get; set; }

    /// <summary>
    /// Cổng mà Windows báo, khi nó khác với cổng thật của màn hình.
    ///
    /// Windows chỉ biết đầu ra của card đồ hoạ. Màn VGA cắm qua bộ chuyển DisplayPort/HDMI→VGA
    /// — kể cả cổng VGA trên bo mạch đời mới, vốn là một chip chuyển DP→VGA gắn sẵn —
    /// sẽ được báo là DisplayPort hoặc HDMI. Null nghĩa là hai nguồn khớp nhau.
    /// </summary>
    public DisplayConnection? ReportedConnection { get; set; }

    public bool ViaConverter => ReportedConnection is not null && ReportedConnection != Connection;

    // Kết quả tự nhận diện, giữ lại khi người dùng chọn tay để bỏ chọn thì trả về như cũ.
    private (DisplayConnection Connection, DisplayConnection? Reported)? _detected;

    /// <summary>Cổng do người dùng chọn tay, không phải do phần mềm nhận diện.</summary>
    public bool ConnectionSetByUser => _detected is not null;

    /// <summary>Cổng phần mềm tự nhận diện, kể cả khi người dùng đã chọn tay cổng khác.</summary>
    public DisplayConnection DetectedConnection => _detected?.Connection ?? Connection;

    /// <summary>
    /// Đặt cổng thật theo lựa chọn của người dùng. Có những bộ chuyển đổi tự thay EDID
    /// của màn bằng EDID tín hiệu số, khi đó phần mềm không còn cách nào biết màn là VGA;
    /// người dùng nhìn được dây cắm thì chọn tay. Truyền null để quay về tự nhận diện.
    /// Cổng Windows báo vẫn được giữ để hiển thị dạng "qua bộ chuyển".
    /// </summary>
    public void OverrideConnection(DisplayConnection? port)
    {
        if (_detected is { } d)
        {
            (Connection, ReportedConnection) = d;
            _detected = null;
        }

        if (port is not { } p || p == Connection) return;

        _detected = (Connection, ReportedConnection);
        var reported = ReportedConnection ?? Connection;
        ReportedConnection = reported is DisplayConnection.Unknown or DisplayConnection.Internal || reported == p
            ? null
            : reported;
        Connection = p;
    }

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

    public string ConnectionText => ViaConverter
        ? S($"{NameOf(Connection)} — qua bộ chuyển {ShortOf(ReportedConnection!.Value)}→{ShortOf(Connection)}",
            $"{NameOf(Connection)} — via a {ShortOf(ReportedConnection!.Value)}→{ShortOf(Connection)} converter")
        : NameOf(Connection);

    /// <summary>Tên cổng ở dạng ngắn, dùng cho ô chọn màn hình.</summary>
    public string ConnectionShort => ViaConverter
        ? S($"{ShortOf(Connection)} (qua {ShortOf(ReportedConnection!.Value)})", $"{ShortOf(Connection)} (via {ShortOf(ReportedConnection!.Value)})")
        : ShortOf(Connection);

    private static string NameOf(DisplayConnection connection) => connection switch
    {
        DisplayConnection.Internal => S("Tấm nền tích hợp (eDP/LVDS)", "Built-in panel (eDP/LVDS)"),
        DisplayConnection.Vga => "VGA (D-Sub)",
        DisplayConnection.Dvi => "DVI",
        DisplayConnection.Hdmi => "HDMI",
        // USB-C/Thunderbolt chạy chế độ DisplayPort Alt Mode nên Windows cũng báo là DisplayPort.
        DisplayConnection.DisplayPort => S("DisplayPort (hoặc USB-C/Thunderbolt)", "DisplayPort (or USB-C/Thunderbolt)"),
        DisplayConnection.UsbC => "USB-C / Thunderbolt",
        DisplayConnection.Wireless => S("Không dây (Miracast)", "Wireless (Miracast)"),
        DisplayConnection.Other => S("Cổng khác", "Other port"),
        _ => S("Không xác định", "Unknown")
    };

    public static string LongName(DisplayConnection connection) => NameOf(connection);

    private static string ShortOf(DisplayConnection connection) => connection switch
    {
        DisplayConnection.Internal => S("tích hợp", "built-in"),
        DisplayConnection.Vga => "VGA",
        DisplayConnection.Dvi => "DVI",
        DisplayConnection.Hdmi => "HDMI",
        DisplayConnection.DisplayPort => "DisplayPort",
        DisplayConnection.UsbC => "USB-C",
        DisplayConnection.Wireless => S("không dây", "wireless"),
        _ => S("cổng khác", "other")
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

            var tag = IsPrimary ? S(" · chính", " · primary") : string.Empty;
            return $"{name} · {Resolution} · {ConnectionShort}{tag}";
        }
    }
}
