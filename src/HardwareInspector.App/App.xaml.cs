using System.Windows;
using System.Windows.Threading;

namespace HardwareInspector.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Công cụ chẩn đoán chạm vào nhiều API cấp thấp; một ngoại lệ lẻ
        // không nên làm sập cả phiên kiểm tra đang dở.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) LogFatal(ex);
        };

        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogFatal(e.Exception);
        MessageBox.Show(
            $"Đã xảy ra lỗi ngoài dự kiến:\n\n{e.Exception.Message}\n\n" +
            "Ứng dụng vẫn tiếp tục chạy, nhưng kết quả của bước vừa rồi có thể không đầy đủ.",
            "Hardware Inspector", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    private static void LogFatal(Exception ex)
    {
        try
        {
            var folder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HardwareInspector");
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(folder, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { }
    }
}
