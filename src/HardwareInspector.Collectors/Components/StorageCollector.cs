using HardwareInspector.Collectors.Smart;
using HardwareInspector.Collectors.Wmi;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Components;

namespace HardwareInspector.Collectors.Components;

public sealed class StorageCollector : IInfoCollector
{
    public string Name => "Ổ lưu trữ";
    public int Order => 40;

    // Mỗi ổ phải mở thiết bị ở mức khối và chờ phản hồi lệnh S.M.A.R.T.
    public int Weight => 4;

    public Task CollectAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        var physicalDisks = WmiQuery
            .Query("SELECT DeviceId, MediaType, BusType, Model, SerialNumber, FirmwareVersion, " +
                   "Size, SpindleSpeed FROM MSFT_PhysicalDisk", WmiQuery.StorageNamespace)
            .ToList();

        foreach (var mo in WmiQuery.Query(
            "SELECT Index, Model, SerialNumber, FirmwareRevision, Size, InterfaceType FROM Win32_DiskDrive"))
        {
            ct.ThrowIfCancellationRequested();

            var device = new StorageDeviceInfo
            {
                DeviceIndex = mo.Int("Index"),
                Model = mo.Str("Model"),
                SerialNumber = CleanSerial(mo.Str("SerialNumber")),
                FirmwareRevision = mo.Str("FirmwareRevision"),
                CapacityBytes = mo.Long("Size"),
                BusType = mo.Str("InterfaceType")
            };

            var match = physicalDisks.FirstOrDefault(p =>
                p.Str("DeviceId") == device.DeviceIndex.ToString() ||
                CleanSerial(p.Str("SerialNumber")).Equals(device.SerialNumber, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                device.MediaType = TranslateMediaType(match.Int("MediaType"));
                device.BusType = TranslateBusType(match.Int("BusType"), device.BusType);
                var rpm = match.Int("SpindleSpeed");
                if (rpm is > 0 and < 30000) device.SpindleSpeedRpm = rpm;
                if (string.IsNullOrWhiteSpace(device.Model)) device.Model = match.Str("Model");
            }

            ReadHealth(device);
            snapshot.StorageDevices.Add(device);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Thử lần lượt các đường đọc sức khoẻ, từ chính xác nhất tới dự phòng.
    /// Ổ NVMe và ổ SATA dùng hai tập lệnh hoàn toàn khác nhau, nhưng loại bus mà
    /// Windows báo không phải lúc nào cũng đúng (ổ NVMe sau cầu RAID hay bị báo là SCSI),
    /// nên nếu đường ưu tiên thất bại thì vẫn thử đường còn lại.
    /// </summary>
    private static void ReadHealth(StorageDeviceInfo device)
    {
        var preferNvme = device.BusType.Contains("NVMe", StringComparison.OrdinalIgnoreCase);

        var succeeded = preferNvme
            ? NvmeHealthReader.TryPopulate(device) || AtaSmartReader.TryPopulate(device)
            : AtaSmartReader.TryPopulate(device) || NvmeHealthReader.TryPopulate(device);

        if (succeeded) return;

        // Không đọc được thì phải nói rõ vì sao, để người dùng biết cần làm gì tiếp.
        device.SmartFailureReason = device.BusType.Contains("USB", StringComparison.OrdinalIgnoreCase)
            ? "Ổ gắn qua cầu USB. Phần lớn cầu USB không chuyển tiếp lệnh S.M.A.R.T. " +
              "Hãy tháo ổ ra và cắm trực tiếp vào cổng SATA hoặc khe M.2 để đọc được."
            : !BaseboardCollector.IsElevated()
                ? "Phiên chạy không có quyền Administrator nên không mở được thiết bị ở mức khối. " +
                  "Hãy chạy lại ứng dụng bằng Run as administrator."
                : "Driver của controller không chấp nhận lệnh S.M.A.R.T. " +
                  "Thường gặp khi ổ nằm sau RAID (Intel RST / AMD RAID). " +
                  "Chuyển chế độ SATA trong BIOS sang AHCI sẽ đọc được.";
    }

    /// <summary>WMI hay trả serial dạng hex đã mã hoá hoặc có khoảng trắng thừa.</summary>
    private static string CleanSerial(string raw)
    {
        var s = raw.Trim();
        if (string.IsNullOrEmpty(s)) return string.Empty;

        // Một số driver trả serial dưới dạng chuỗi hex của các ký tự ASCII, đảo từng cặp byte.
        if (s.Length is 40 or 20 && s.All(Uri.IsHexDigit))
        {
            try
            {
                var bytes = Convert.FromHexString(s);
                for (var i = 0; i + 1 < bytes.Length; i += 2)
                    (bytes[i], bytes[i + 1]) = (bytes[i + 1], bytes[i]);
                var decoded = System.Text.Encoding.ASCII.GetString(bytes).Trim();
                if (decoded.All(c => c is >= ' ' and <= '~')) return decoded;
            }
            catch { }
        }

        return s;
    }

    private static string TranslateMediaType(int code) => code switch
    {
        3 => "HDD",
        4 => "SSD",
        5 => "SCM",
        _ => "Không xác định"
    };

    private static string TranslateBusType(int code, string fallback) => code switch
    {
        1 => "SCSI",
        3 => "ATA",
        7 => "USB",
        8 => "RAID",
        9 => "iSCSI",
        10 => "SAS",
        11 => "SATA",
        17 => "NVMe",
        _ => string.IsNullOrWhiteSpace(fallback) ? "Không xác định" : fallback
    };
}
