using System.Text;
using HardwareInspector.Core.Models.Firmware;
using HardwareInspector.Firmware.Native;

namespace HardwareInspector.Firmware.Acpi;

/// <summary>
/// Liệt kê toàn bộ bảng ACPI mà firmware nạp cho hệ điều hành.
/// Đây là nơi phát hiện WPBT — cơ chế cho phép BIOS thả một file .exe vào Windows
/// mỗi lần khởi động. WPBT là công cụ hợp lệ của một số OEM nhưng cũng là
/// đường đi kinh điển của rootkit tầng firmware.
/// </summary>
public sealed class AcpiTableScanner
{
    public List<AcpiTableInfo> Tables { get; } = new();

    public bool Scan()
    {
        try
        {
            var size = FirmwareNativeMethods.EnumSystemFirmwareTables(
                FirmwareNativeMethods.ProviderAcpi, null, 0);
            if (size == 0) return false;

            var buffer = new byte[size];
            var written = FirmwareNativeMethods.EnumSystemFirmwareTables(
                FirmwareNativeMethods.ProviderAcpi, buffer, size);
            if (written == 0) return false;

            for (var i = 0; i + 4 <= buffer.Length; i += 4)
            {
                var signature = Encoding.ASCII.GetString(buffer, i, 4);
                if (string.IsNullOrWhiteSpace(signature)) continue;

                var tableId = BitConverter.ToUInt32(buffer, i);
                var (length, oemId, oemTableId) = ReadTableHeader(tableId);
                Tables.Add(new AcpiTableInfo(signature.Trim(), length, oemId, oemTableId));
            }

            return Tables.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static (int Length, string? OemId, string? OemTableId) ReadTableHeader(uint tableId)
    {
        try
        {
            var size = FirmwareNativeMethods.GetSystemFirmwareTable(
                FirmwareNativeMethods.ProviderAcpi, tableId, null, 0);
            if (size < 36) return ((int)size, null, null);

            var header = new byte[Math.Min(size, 64)];
            FirmwareNativeMethods.GetSystemFirmwareTable(
                FirmwareNativeMethods.ProviderAcpi, tableId, header, (uint)header.Length);

            // Header ACPI chuẩn: Signature(4) Length(4) Revision Checksum OEMID(6) OEMTableID(8)
            var oemId = Encoding.ASCII.GetString(header, 10, 6).Trim('\0', ' ');
            var oemTableId = Encoding.ASCII.GetString(header, 16, 8).Trim('\0', ' ');
            return ((int)size, oemId, oemTableId);
        }
        catch
        {
            return (0, null, null);
        }
    }

    public bool Has(string signature) =>
        Tables.Any(t => t.Signature.Equals(signature, StringComparison.OrdinalIgnoreCase));

    public AcpiTableInfo? Get(string signature) =>
        Tables.FirstOrDefault(t => t.Signature.Equals(signature, StringComparison.OrdinalIgnoreCase));
}
