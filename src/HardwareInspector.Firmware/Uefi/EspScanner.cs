using System.Security.Cryptography;
using HardwareInspector.Core.Models.Firmware;

namespace HardwareInspector.Firmware.Uefi;

/// <summary>
/// Gắn tạm phân vùng EFI (ESP) và liệt kê mọi file .efi trong đó.
/// Bootkit hầu như luôn để lại một binary lạ ngoài các đường dẫn OEM chuẩn.
/// Cần quyền Administrator; nếu không gắn được thì trả về danh sách rỗng, không ném lỗi.
/// </summary>
public sealed class EspScanner
{
    private static readonly string[] KnownPaths =
    {
        @"\EFI\BOOT\BOOTX64.EFI",
        @"\EFI\BOOT\BOOTIA32.EFI",
        @"\EFI\MICROSOFT\BOOT\BOOTMGFW.EFI",
        @"\EFI\MICROSOFT\BOOT\BOOTMGR.EFI",
        @"\EFI\MICROSOFT\BOOT\MEMTEST.EFI",
        @"\EFI\MICROSOFT\RECOVERY\BOOTMGFW.EFI",
        @"\EFI\UBUNTU\SHIMX64.EFI",
        @"\EFI\UBUNTU\GRUBX64.EFI"
    };

    private const string MountLetter = "S:";

    public List<EspBinaryEntry> Scan(out bool succeeded)
    {
        var entries = new List<EspBinaryEntry>();
        succeeded = false;

        if (!TryMount()) return entries;

        try
        {
            var root = MountLetter + @"\";
            if (!Directory.Exists(root)) return entries;

            foreach (var file in Directory.EnumerateFiles(root, "*.efi",
                         new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                var info = new FileInfo(file);
                var relative = file[MountLetter.Length..].ToUpperInvariant();

                entries.Add(new EspBinaryEntry
                {
                    Path = relative,
                    SizeBytes = info.Length,
                    LastWriteUtc = info.LastWriteTimeUtc,
                    Sha256 = ComputeSha256(file),
                    IsKnownVendorPath = IsKnownPath(relative)
                });
            }

            succeeded = true;
        }
        catch { }
        finally { Unmount(); }

        return entries;
    }

    private static bool IsKnownPath(string relative)
    {
        if (KnownPaths.Contains(relative, StringComparer.OrdinalIgnoreCase)) return true;
        // Thư mục của các OEM lớn được coi là hợp lệ theo mặc định.
        return relative.StartsWith(@"\EFI\DELL\", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith(@"\EFI\HP\", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith(@"\EFI\LENOVO\", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith(@"\EFI\ASUS\", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith(@"\EFI\ACER\", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith(@"\EFI\MSI\", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith(@"\EFI\TOOLS\", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ComputeSha256(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch { return null; }
    }

    private static bool TryMount() => RunMountVol($"{MountLetter} /S");
    private static void Unmount() => RunMountVol($"{MountLetter} /D");

    private static bool RunMountVol(string arguments)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("mountvol", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(10_000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
