using System.Text;
using HardwareInspector.Core.Models.Firmware;
using HardwareInspector.Firmware.Native;

namespace HardwareInspector.Firmware.Smbios;

/// <summary>
/// Đọc và phân giải bảng SMBIOS thô từ firmware.
/// WMI chỉ phơi bày một tập con đã được Windows làm sạch; bảng thô cho thấy
/// cả những trường mà công cụ sửa BIOS hay để lại dấu vết.
/// </summary>
public sealed class SmbiosReader
{
    public IReadOnlyList<SmbiosStructure> Structures { get; private set; } = Array.Empty<SmbiosStructure>();
    public string? SmbiosVersion { get; private set; }
    public bool IsAvailable => Structures.Count > 0;

    public bool Read()
    {
        try
        {
            var size = FirmwareNativeMethods.GetSystemFirmwareTable(
                FirmwareNativeMethods.ProviderRsmb, 0, null, 0);
            if (size == 0) return false;

            var buffer = new byte[size];
            var written = FirmwareNativeMethods.GetSystemFirmwareTable(
                FirmwareNativeMethods.ProviderRsmb, 0, buffer, size);
            if (written == 0 || written > size) return false;

            // Header RawSMBIOSData: Used20CallingMethod, Major, Minor, DmiRevision, Length(4), Data[]
            if (buffer.Length < 8) return false;
            SmbiosVersion = $"{buffer[1]}.{buffer[2]}";

            var tableLength = BitConverter.ToInt32(buffer, 4);
            var table = new byte[Math.Min(tableLength, buffer.Length - 8)];
            Array.Copy(buffer, 8, table, 0, table.Length);

            Structures = Parse(table);
            return Structures.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static List<SmbiosStructure> Parse(byte[] table)
    {
        var list = new List<SmbiosStructure>();
        var offset = 0;

        while (offset + 4 <= table.Length)
        {
            var type = table[offset];
            var length = table[offset + 1];
            if (length < 4) break;

            var handle = BitConverter.ToUInt16(table, offset + 2);
            if (offset + length > table.Length) break;

            var data = new byte[length];
            Array.Copy(table, offset, data, 0, length);

            var s = new SmbiosStructure { Type = type, Length = length, Handle = handle, Data = data };

            // Vùng chuỗi nằm ngay sau phần dữ liệu và luôn kết thúc bằng hai byte 0x00.
            //
            // Trường hợp phải xử lý riêng: cấu trúc KHÔNG có chuỗi nào thì vùng này
            // chỉ gồm đúng "00 00". Nếu chỉ nuốt một byte rồi thoát, con trỏ sẽ lệch
            // đúng một byte và mọi cấu trúc phía sau đều đọc sai. Type 16 (Physical
            // Memory Array) không có trường chuỗi nào và luôn nằm ngay trước Type 17,
            // nên lệch ở đây đồng nghĩa mất sạch thông tin khe RAM.
            var p = offset + length;

            if (p + 1 < table.Length && table[p] == 0 && table[p + 1] == 0)
            {
                p += 2;
            }
            else
            {
                while (p < table.Length)
                {
                    var start = p;
                    while (p < table.Length && table[p] != 0) p++;

                    if (p > start)
                        s.Strings.Add(Encoding.ASCII.GetString(table, start, p - start));

                    p++; // bỏ qua byte kết thúc chuỗi

                    // Byte 0x00 tiếp theo ngay sau đó là dấu kết thúc vùng chuỗi.
                    if (p < table.Length && table[p] == 0)
                    {
                        p++;
                        break;
                    }
                }
            }

            list.Add(s);
            offset = p;

            if (type == 127) break; // End-of-table
        }

        return list;
    }

    public SmbiosStructure? First(byte type) => Structures.FirstOrDefault(s => s.Type == type);
    public IEnumerable<SmbiosStructure> All(byte type) => Structures.Where(s => s.Type == type);
}
