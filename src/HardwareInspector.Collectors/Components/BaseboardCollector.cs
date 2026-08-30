using System.Security.Principal;
using HardwareInspector.Collectors.Wmi;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Components;

namespace HardwareInspector.Collectors.Components;

public sealed class BaseboardCollector : IInfoCollector
{
    public string Name => "Bo mạch, BIOS và hệ điều hành";
    public int Order => 5;

    public Task CollectAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        CollectSystem(snapshot.Baseboard);
        CollectBoard(snapshot.Baseboard);
        CollectChassis(snapshot.Baseboard);
        CollectBios(snapshot.Bios);
        CollectOs(snapshot.OperatingSystem);
        return Task.CompletedTask;
    }

    private static void CollectSystem(BaseboardInfo b)
    {
        var cs = WmiQuery.QuerySingle(
            "SELECT Manufacturer, Model, SystemFamily, SystemSKUNumber FROM Win32_ComputerSystem");
        if (cs is not null)
        {
            b.SystemManufacturer = cs.Str("Manufacturer");
            b.SystemProductName = cs.Str("Model");
            b.SystemFamily = cs.Str("SystemFamily");
            b.SystemSku = cs.Str("SystemSKUNumber");
        }

        var csp = WmiQuery.QuerySingle(
            "SELECT IdentifyingNumber, UUID, Name, Vendor FROM Win32_ComputerSystemProduct");
        if (csp is not null)
        {
            b.SystemSerialNumber = csp.Str("IdentifyingNumber");
            b.SystemUuid = csp.Str("UUID");
        }
    }

    private static void CollectBoard(BaseboardInfo b)
    {
        var mo = WmiQuery.QuerySingle(
            "SELECT Manufacturer, Product, SerialNumber, Version FROM Win32_BaseBoard");
        if (mo is null) return;
        b.BoardManufacturer = mo.Str("Manufacturer");
        b.BoardProduct = mo.Str("Product");
        b.BoardSerialNumber = mo.Str("SerialNumber");
        b.BoardVersion = mo.Str("Version");
    }

    private static void CollectChassis(BaseboardInfo b)
    {
        var mo = WmiQuery.QuerySingle(
            "SELECT ChassisTypes, SerialNumber, SMBIOSAssetTag, SecurityStatus FROM Win32_SystemEnclosure");
        if (mo is null) return;

        b.ChassisSerialNumber = mo.Str("SerialNumber");
        b.ChassisAssetTag = mo.Str("SMBIOSAssetTag");
        b.ChassisSecurityStatus = TranslateSecurityStatus(mo.Int("SecurityStatus", -1));

        try
        {
            if (mo["ChassisTypes"] is ushort[] types && types.Length > 0)
                b.ChassisType = TranslateChassis(types[0]);
        }
        catch { }
    }

    private static void CollectBios(BiosInfo bios)
    {
        var mo = WmiQuery.QuerySingle(
            "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate, SMBIOSMajorVersion, " +
            "SMBIOSMinorVersion, EmbeddedControllerMajorVersion, EmbeddedControllerMinorVersion, " +
            "BiosCharacteristics FROM Win32_BIOS");
        if (mo is null) return;

        bios.Vendor = mo.Str("Manufacturer");
        bios.Version = mo.Str("SMBIOSBIOSVersion");
        bios.ReleaseDate = mo.Date("ReleaseDate");
        bios.SmbiosVersion = $"{mo.Int("SMBIOSMajorVersion")}.{mo.Int("SMBIOSMinorVersion")}";

        var ecMajor = mo.Int("EmbeddedControllerMajorVersion", -1);
        var ecMinor = mo.Int("EmbeddedControllerMinorVersion", -1);
        if (ecMajor >= 0) bios.EcFirmwareVersion = $"{ecMajor}.{ecMinor}";

        // BIOS đang chạy chế độ UEFI hay Legacy — quyết định Secure Boot có nghĩa hay không.
        bios.IsUefi = Environment.GetEnvironmentVariable("firmware_type")
                          ?.Equals("UEFI", StringComparison.OrdinalIgnoreCase) == true
                      || Directory.Exists(@"C:\Windows\Panther") && IsUefiFromRegistry();
    }

    private static bool IsUefiFromRegistry()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            return key is not null;
        }
        catch { return false; }
    }

    private static void CollectOs(OperatingSystemInfo os)
    {
        var mo = WmiQuery.QuerySingle(
            "SELECT Caption, Version, BuildNumber, InstallDate, LastBootUpTime, OSArchitecture " +
            "FROM Win32_OperatingSystem");
        if (mo is not null)
        {
            os.Caption = mo.Str("Caption");
            os.Version = mo.Str("Version");
            os.BuildNumber = mo.Str("BuildNumber");
            os.InstallDate = mo.Date("InstallDate");
            os.LastBootUpTime = mo.Date("LastBootUpTime");
            os.Architecture = mo.Str("OSArchitecture");
        }

        var lic = WmiQuery.QuerySingle(
            "SELECT LicenseStatus, ProductKeyChannel, Description FROM SoftwareLicensingProduct " +
            "WHERE PartialProductKey IS NOT NULL AND Name LIKE 'Windows%'");
        if (lic is not null)
        {
            os.IsActivated = lic.Int("LicenseStatus") == 1;
            var channel = lic.Str("ProductKeyChannel");
            os.LicenseChannel = string.IsNullOrWhiteSpace(channel) ? lic.Str("Description") : channel;
        }

        os.IsAdminSession = IsElevated();
    }

    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static string TranslateSecurityStatus(int code) => code switch
    {
        1 => "Khác",
        2 => "Không xác định",
        3 => "An toàn (chưa ghi nhận mở vỏ)",
        4 => "Có khoá ngoài",
        5 => "Đã bị mở vỏ / khoá bị vô hiệu",
        _ => "Không xác định"
    };

    private static string TranslateChassis(ushort code) => code switch
    {
        3 => "Desktop",
        4 => "Low Profile Desktop",
        6 => "Mini Tower",
        7 => "Tower",
        8 => "Portable",
        9 => "Laptop",
        10 => "Notebook",
        11 => "Hand Held",
        13 => "All in One",
        14 => "Sub Notebook",
        23 => "Rack Mount Chassis",
        30 => "Tablet",
        31 => "Convertible",
        32 => "Detachable",
        _ => $"Loại {code}"
    };
}
