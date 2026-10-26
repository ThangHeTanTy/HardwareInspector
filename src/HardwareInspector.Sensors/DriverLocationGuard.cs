using LibreHardwareMonitor.Hardware;

namespace HardwareInspector.Sensors;

/// <summary>
/// Đẩy file driver WinRing0 ra thư mục tạm thay vì để cạnh file .exe.
///
/// LibreHardwareMonitor giải nén driver kernel ra "&lt;tên exe&gt;.sys" ngay trong thư mục
/// chạy, rồi cài nó thành dịch vụ R0&lt;tên exe&gt;. Khi tiến trình bị giết giữa chừng — bấm
/// Stop trong Visual Studio, tắt Visual Studio lúc đang debug, End task — thư viện không kịp
/// gỡ dịch vụ, nên Windows giữ khoá file .sys tới tận lần khởi động lại. Thư mục bin của
/// project vì thế không xoá được.
///
/// Thư viện chỉ dùng thư mục chạy khi tạo được file ở đó, nếu không sẽ lùi về file tạm
/// trong %TEMP%. Ta tự giữ khoá độc quyền hai đường dẫn nó thử trước trong lúc Open(),
/// để driver luôn nằm ở %TEMP%: có bị kẹt thì cũng không chặn việc xoá thư mục project.
/// </summary>
internal sealed class DriverLocationGuard : IDisposable
{
    private readonly List<FileStream> _locks = new();

    private DriverLocationGuard() { }

    public static DriverLocationGuard Acquire()
    {
        var guard = new DriverLocationGuard();
        var candidates = new[]
        {
            Environment.ProcessPath,
            typeof(Computer).Assembly.Location
        };

        foreach (var path in candidates
                     .Where(p => !string.IsNullOrEmpty(p))
                     .Select(p => Path.ChangeExtension(p!, ".sys"))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                // File còn sót từ lần chạy trước mà không ai giữ thì dọn luôn.
                if (File.Exists(path)) File.Delete(path);
                guard._locks.Add(new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite,
                    FileShare.None, 1, FileOptions.DeleteOnClose));
            }
            catch
            {
                // Đang bị driver cũ giữ: thư viện cũng sẽ không tạo được file ở đây, đúng ý ta.
            }
        }

        return guard;
    }

    public void Dispose()
    {
        foreach (var l in _locks)
        {
            try { l.Dispose(); } catch { }
        }
        _locks.Clear();
    }
}
