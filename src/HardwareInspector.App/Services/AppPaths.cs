using System.IO;

namespace HardwareInspector.App.Services;

/// <summary>
/// Nơi ứng dụng ghi dữ liệu.
///
/// Chạy bình thường: cài đặt nhỏ ở %LOCALAPPDATA%, biên bản ở Tài liệu.
/// Chạy từ USB (file .exe nằm trên ổ di động, hoặc có file portable.txt cạnh .exe):
/// mọi thứ ghi ngay cạnh .exe, để mang USB đi xem máy rồi rút ra là không để lại
/// dữ liệu trên máy người bán, và biên bản của mọi máy đã xem nằm sẵn trên USB.
/// </summary>
public static class AppPaths
{
    private static readonly string ExeDirectory =
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    public static bool IsPortable { get; } = DetectPortable();

    /// <summary>Cài đặt và dữ liệu nhỏ: số quạt đã chỉnh, cổng màn hình đã chọn, danh sách so sánh máy.</summary>
    public static string DataDirectory => IsPortable
        ? Path.Combine(ExeDirectory, "HardwareInspectorData")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HardwareInspector");

    /// <summary>Biên bản HTML/JSON và nhật ký lỗi.</summary>
    public static string ReportsDirectory => IsPortable
        ? Path.Combine(ExeDirectory, "BienBan")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HardwareInspector");

    public static string DataFile(string name) => Path.Combine(DataDirectory, name);

    private static bool DetectPortable()
    {
        try
        {
            if (File.Exists(Path.Combine(ExeDirectory, "portable.txt"))) return true;
            var root = Path.GetPathRoot(ExeDirectory);
            return !string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Removable;
        }
        catch
        {
            return false;
        }
    }
}
