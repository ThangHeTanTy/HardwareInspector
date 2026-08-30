using System.Text;
using System.Windows;
using HardwareInspector.App.ViewModels;

namespace HardwareInspector.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // ViewModel được tạo ở đây thay vì trong XAML.
        // Nếu tạo bằng <Window.DataContext><vm:MainViewModel /></Window.DataContext>,
        // mọi ngoại lệ trong constructor đều bị XAML parser bọc thành XamlParseException
        // với thông điệp chỉ về một dòng XAML bất kỳ, che mất nguyên nhân thật.
        try
        {
            DataContext = new MainViewModel();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Không khởi tạo được ViewModel.\n\n" + Describe(ex),
                "Hardware Inspector", MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }

        Closed += (_, _) => (DataContext as MainViewModel)?.Dispose();
    }

    private void OnWarrantyClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: Core.Models.Assessment.WarrantyLookup lookup }
            && DataContext is MainViewModel vm)
            vm.OpenWarranty(lookup);
    }

    /// <summary>Trải phẳng chuỗi InnerException — nguyên nhân thật thường nằm ở tầng sâu nhất.</summary>
    private static string Describe(Exception ex)
    {
        var sb = new StringBuilder();
        var depth = 0;
        for (Exception? e = ex; e is not null; e = e.InnerException, depth++)
        {
            sb.Append(new string(' ', depth * 2))
              .Append(e.GetType().Name)
              .Append(": ")
              .AppendLine(e.Message);
        }
        return sb.ToString();
    }
}
