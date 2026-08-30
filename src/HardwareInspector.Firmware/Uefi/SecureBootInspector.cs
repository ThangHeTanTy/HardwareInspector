using HardwareInspector.Core.Models.Firmware;
using HardwareInspector.Firmware.Native;
using Microsoft.Win32;

namespace HardwareInspector.Firmware.Uefi;

/// <summary>
/// Xác định trạng thái chuỗi khởi động tin cậy.
/// Bốn câu hỏi cần trả lời: Secure Boot có bật không, máy có đang ở Setup Mode
/// (nghĩa là chưa nạp khoá nền tảng nên ai cũng ký được bootloader), khoá PK có tồn tại,
/// và danh sách thu hồi dbx đã cập nhật tới đâu.
/// </summary>
public sealed class SecureBootInspector
{
    private const string GlobalVariableGuid = "{8be4df61-93ca-11d2-aa0d-00e098032b8c}";

    public SecureBootState Inspect()
    {
        FirmwareNativeMethods.EnableSystemEnvironmentPrivilege();

        var state = new SecureBootState
        {
            BootFromUefi = IsUefiBoot()
        };

        state.Enabled = ReadBoolVariable("SecureBoot") ?? ReadSecureBootFromRegistry();
        state.SetupMode = ReadBoolVariable("SetupMode");
        state.DeployedMode = ReadBoolVariable("DeployedMode");
        state.PlatformKeyPresent = ReadVariable("PK") is { Length: > 0 };
        state.VendorKeysState = ReadVendorKeysFromRegistry();
        ReadDbxInfo(state);

        return state;
    }

    private static bool IsUefiBoot()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            if (key is not null) return true;
        }
        catch { }

        return string.Equals(Environment.GetEnvironmentVariable("firmware_type"), "UEFI",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool? ReadSecureBootFromRegistry()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            var value = key?.GetValue("UEFISecureBootEnabled");
            return value is int i ? i == 1 : null;
        }
        catch { return null; }
    }

    private static string? ReadVendorKeysFromRegistry()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            return key?.GetValue("VendorKeys")?.ToString();
        }
        catch { return null; }
    }

    private static void ReadDbxInfo(SecureBootState state)
    {
        var dbx = ReadVariable("dbx");
        if (dbx is null || dbx.Length == 0) return;

        // Mỗi EFI_SIGNATURE_LIST tối thiểu 28 byte header. Số lượng chữ ký thu hồi
        // tăng theo từng bản cập nhật Microsoft phát hành, dùng làm chỉ dấu "BIOS có được cập nhật không".
        state.DbxUpdateCount = dbx.Length / 76;
    }

    private static bool? ReadBoolVariable(string name)
    {
        var data = ReadVariable(name);
        return data is { Length: > 0 } ? data[0] == 1 : null;
    }

    private static byte[]? ReadVariable(string name)
    {
        try
        {
            var buffer = new byte[64 * 1024];
            var size = FirmwareNativeMethods.GetFirmwareEnvironmentVariableExW(
                name, GlobalVariableGuid, buffer, (uint)buffer.Length, out _);
            if (size == 0) return null;
            return buffer.Take((int)size).ToArray();
        }
        catch
        {
            return null;
        }
    }
}
