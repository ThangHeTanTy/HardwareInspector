using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Assessment;
using HardwareInspector.Core.Utils;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Analysis.Warranty;

/// <summary>
/// Dựng đầu mối tra bảo hành cho từng linh kiện có serial đọc được.
///
/// Ứng dụng không tự gọi API của hãng. Các cổng tra cứu đều yêu cầu xác thực
/// hoặc có lớp chống bot, và quan trọng hơn: gửi serial máy của người dùng
/// tới máy chủ bên thứ ba mà không hỏi là điều không nên làm. Thay vào đó,
/// phần mềm dựng sẵn đường dẫn kèm serial để người dùng bấm và tự đối chiếu.
/// </summary>
public sealed class WarrantyLookupService
{
    public IReadOnlyList<WarrantyLookup> Build(SystemSnapshot snapshot)
    {
        var results = new List<WarrantyLookup>();

        AddSystem(snapshot, results);
        AddStorage(snapshot, results);
        AddDisplays(snapshot, results);
        AddBattery(snapshot, results);
        AddGpu(snapshot, results);

        return results;
    }

    // ------------------------------------------------------------------
    private void AddSystem(SystemSnapshot snapshot, List<WarrantyLookup> results)
    {
        var b = snapshot.Baseboard;
        var serial = FormatHelpers.IsPlaceholder(b.SystemSerialNumber) ? null : b.SystemSerialNumber.Trim();
        var vendor = b.SystemManufacturer;

        var (url, note) = ResolveSystemVendor(vendor, serial);

        results.Add(new WarrantyLookup
        {
            Component = ComponentKind.Motherboard,
            ComponentName = $"{vendor} {b.SystemProductName}".Trim(),
            Vendor = string.IsNullOrWhiteSpace(vendor) ? S("Không xác định", "Unknown") : vendor,
            Serial = serial,
            Url = url,
            Note = note ?? (serial is null
                ? snapshot.IsLaptop
                    ? S("Không đọc được serial máy — hãy lấy serial từ tem đáy máy.",
                        "No system serial could be read — take the serial from the label under the machine.")
                    : S("Máy tự ráp thường không có serial hệ thống, chuyện này bình thường. Bảo hành bo mạch tra theo serial in trên tem bo hoặc hộp.",
                        "Self-built PCs usually have no system serial; that is normal. Board warranty goes by the serial on the board's sticker or box.")
                : S("Đối chiếu model mà trang bảo hành trả về với máy đang cầm. Lệch model nghĩa là serial không thuộc máy này.",
                    "Compare the model the warranty site returns with the machine in your hands. A different model means the serial is not this machine's."))
        });
    }

    private static (string? Url, string? Note) ResolveSystemVendor(string vendor, string? serial)
    {
        var v = vendor.ToLowerInvariant();

        if (v.Contains("dell"))
            return (serial is null
                    ? "https://www.dell.com/support/home/en-us"
                    : $"https://www.dell.com/support/home/en-us/product-support/servicetag/{Uri.EscapeDataString(serial)}/overview",
                S("Dell gọi serial là Service Tag, gồm 7 ký tự. Trang này cho biết ngày hết hạn bảo hành và cấu hình xuất xưởng — " +
                  "hãy so cấu hình đó với cấu hình phần mềm đọc được.",
                  "Dell calls the serial a Service Tag, 7 characters long. The page shows the warranty end date and factory configuration — " +
                  "compare that configuration with what this app reads."));

        if (v.Contains("lenovo") || v.Contains("thinkpad"))
            return (serial is null
                    ? "https://pcsupport.lenovo.com/warrantylookup"
                    : $"https://pcsupport.lenovo.com/products/{Uri.EscapeDataString(serial)}",
                S("Lenovo hiển thị cả ngày xuất xưởng. Ngày này là mốc chuẩn để đối chiếu với " +
                  "ngày sản xuất pin và năm sản xuất tấm nền.",
                  "Lenovo also shows the ship date. It is the reference point to compare with " +
                  "the battery's manufacture date and the panel's manufacture year."));

        if (v.Contains("hp") || v.Contains("hewlett") || v.Contains("compaq"))
            return ("https://support.hp.com/us-en/checkwarranty",
                S("HP yêu cầu nhập serial thủ công kèm mã captcha nên không điền sẵn được. " +
                  "Serial đã được sao vào clipboard khi bạn bấm mở.",
                  "HP requires typing the serial with a captcha, so it cannot be pre-filled. " +
                  "The serial is copied to the clipboard when you click to open."));

        if (v.Contains("asus"))
            return ("https://www.asus.com/support/warranty-status-inquiry/",
                S("ASUS cần cả serial và model. Trang cũng cho biết máy thuộc thị trường nào — " +
                  "máy xách tay từ thị trường khác thường không được bảo hành trong nước.",
                  "ASUS needs both serial and model. The page also shows which market the machine belongs to — " +
                  "grey imports from other markets usually have no local warranty."));

        if (v.Contains("acer"))
            return ("https://www.acer.com/support/product-registration",
                S("Acer tra theo SNID hoặc serial. SNID là dãy số ngắn hơn nằm cạnh serial trên tem.",
                  "Acer looks up by SNID or serial. The SNID is the shorter number next to the serial on the label."));

        if (v.Contains("msi") || v.Contains("micro-star"))
            return ("https://account.msi.com/login?redirect=warranty",
                S("MSI yêu cầu đăng nhập tài khoản để tra bảo hành.", "MSI requires signing in to check warranty."));

        if (v.Contains("apple"))
            return ("https://checkcoverage.apple.com/",
                S("Apple hiển thị cả tình trạng khoá kích hoạt. Với máy Mac cũ, đây là mục quan trọng nhất cần kiểm tra.",
                  "Apple also shows the activation lock status. On a used Mac this is the most important thing to check."));

        if (v.Contains("microsoft"))
            return ("https://mysupport.microsoft.com/devices",
                S("Surface tra theo serial in ở chân chống hoặc dưới bàn phím.", "Surface devices look up by the serial printed under the kickstand or keyboard."));

        if (v.Contains("gigabyte") || v.Contains("aorus"))
            return ("https://www.gigabyte.com/Support/Consumer/WarrantyList", null);

        return (null,
            S("Chưa có sẵn liên kết cho hãng này. Tra thủ công bằng cụm từ " +
              $"\"{vendor} warranty check serial\" trên trang chủ của hãng.",
              "No ready-made link for this manufacturer. Search manually for " +
              $"\"{vendor} warranty check serial\" on the manufacturer's site."));
    }

    // ------------------------------------------------------------------
    private void AddStorage(SystemSnapshot snapshot, List<WarrantyLookup> results)
    {
        foreach (var d in snapshot.StorageDevices)
        {
            var serial = FormatHelpers.IsPlaceholder(d.SerialNumber) ? null : d.SerialNumber.Trim();
            var (vendor, url) = ResolveStorageVendor(d.Model);

            results.Add(new WarrantyLookup
            {
                Component = ComponentKind.Storage,
                ComponentName = d.Model,
                Vendor = vendor,
                Serial = serial,
                Url = url,
                Note = d.PowerOnHours is { } hours
                    ? S($"Ổ đã chạy {hours:N0} giờ. Bảo hành SSD thường hết hiệu lực khi vượt hạn mức TBW " +
                        $"kể cả còn trong thời hạn{(d.TeraBytesWritten is { } tbw ? $" — ổ này đã ghi {tbw:0.#} TB" : string.Empty)}.",
                        $"The drive has run {hours:N0} hours. SSD warranties usually end once the TBW rating is exceeded, " +
                        $"even within the warranty period{(d.TeraBytesWritten is { } tbw2 ? $" — this drive has written {tbw2:0.#} TB" : string.Empty)}.")
                    : S("Đối chiếu ngày sản xuất mà trang bảo hành trả về với tuổi máy.", "Compare the manufacture date the warranty site returns with the machine's age.")
            });
        }
    }

    private static (string Vendor, string? Url) ResolveStorageVendor(string model)
    {
        var m = model.ToUpperInvariant();

        if (m.Contains("SAMSUNG")) return ("Samsung", "https://semiconductor.samsung.com/consumer-storage/support/warranty/");
        if (m.Contains("WDC") || m.Contains("WESTERN DIGITAL") || m.StartsWith("WD"))
            return ("Western Digital", "https://support-en.wd.com/app/warrantystatusweb");
        if (m.Contains("SEAGATE") || m.StartsWith("ST")) return ("Seagate", "https://www.seagate.com/support/warranty-and-replacements/");
        if (m.Contains("CRUCIAL") || m.Contains("MICRON")) return ("Crucial / Micron", "https://www.crucial.com/support/warranty");
        if (m.Contains("KINGSTON")) return ("Kingston", "https://www.kingston.com/en/support/technical/warranty");
        if (m.Contains("SANDISK")) return ("SanDisk", "https://support-en.sandisk.com/app/warrantystatus");
        if (m.Contains("TOSHIBA") || m.Contains("KIOXIA")) return ("Kioxia / Toshiba", "https://personal.kioxia.com/en-apac/support/warranty.html");
        if (m.Contains("INTEL") || m.Contains("SOLIDIGM")) return ("Solidigm / Intel", "https://www.solidigm.com/support-page/warranty.html");
        if (m.Contains("HITACHI") || m.Contains("HGST")) return ("HGST", "https://support-en.wd.com/app/warrantystatusweb");
        if (m.Contains("SK HYNIX") || m.Contains("HYNIX")) return ("SK hynix", "https://ssd.skhynix.com/support/");
        if (m.Contains("ADATA")) return ("ADATA", "https://www.adata.com/en/support/warranty/");
        if (m.Contains("LEXAR")) return ("Lexar", "https://www.lexar.com/support/");

        return (model.Split(' ').FirstOrDefault() ?? S("Không xác định", "Unknown"), null);
    }

    // ------------------------------------------------------------------
    private void AddDisplays(SystemSnapshot snapshot, List<WarrantyLookup> results)
    {
        foreach (var d in snapshot.Displays.Where(d => d.EdidAvailable && !d.IsInternal))
        {
            var serial = FormatHelpers.IsPlaceholder(d.SerialNumber) ? null : d.SerialNumber.Trim();
            var made = d.ManufactureYear is { } y
                ? S($"Tấm nền sản xuất {(d.ManufactureWeek is { } w ? $"tuần {w}/" : string.Empty)}{y}. ",
                    $"Panel made {(d.ManufactureWeek is { } w2 ? $"week {w2}/" : "in ")}{y}. ")
                : string.Empty;

            results.Add(new WarrantyLookup
            {
                Component = ComponentKind.Display,
                ComponentName = $"{d.ManufacturerName} {d.PanelModel}".Trim(),
                Vendor = d.ManufacturerName,
                Serial = serial,
                Url = null,
                Note = made + S("Bảo hành màn hình rời tính từ ngày mua chứ không phải ngày sản xuất, " +
                                "nhưng ngày sản xuất cho biết màn đã nằm kho bao lâu trước khi tới tay người dùng đầu tiên.",
                                "Monitor warranties run from the purchase date, not the manufacture date, " +
                                "but the manufacture date tells you how long it sat in stock before its first owner.")
            });
        }
    }

    private void AddBattery(SystemSnapshot snapshot, List<WarrantyLookup> results)
    {
        if (snapshot.PrimaryBattery is not { IsPresent: true } battery) return;

        results.Add(new WarrantyLookup
        {
            Component = ComponentKind.Battery,
            ComponentName = $"{battery.Manufacturer} {battery.Name}".Trim(),
            Vendor = string.IsNullOrWhiteSpace(battery.Manufacturer) ? S("Không xác định", "Unknown") : battery.Manufacturer,
            Serial = FormatHelpers.IsPlaceholder(battery.SerialNumber) ? null : battery.SerialNumber,
            Url = null,
            Note = battery.ManufactureDate is { } made
                ? S($"Pin sản xuất {made:dd/MM/yyyy}. Hầu hết hãng chỉ bảo hành pin 6-12 tháng, " +
                    "ngắn hơn nhiều so với bảo hành máy.",
                    $"Battery made {made:dd/MM/yyyy}. Most manufacturers only cover batteries for 6-12 months, " +
                    "much shorter than the machine's warranty.")
                : S("Pin thường có thời hạn bảo hành riêng, ngắn hơn bảo hành máy. Hỏi rõ điểm này khi mua.",
                    "Batteries usually have their own, shorter warranty than the machine. Ask about it when buying.")
        });
    }

    private void AddGpu(SystemSnapshot snapshot, List<WarrantyLookup> results)
    {
        var gpu = snapshot.GraphicsAdapters.FirstOrDefault(g => g.IsDiscrete);
        if (gpu is null) return;

        results.Add(new WarrantyLookup
        {
            Component = ComponentKind.Gpu,
            ComponentName = gpu.Name,
            Vendor = gpu.Vendor,
            Serial = null,
            Url = null,
            Note = S("Card rời không phơi bày serial qua phần mềm. Serial nằm trên tem dán ở thân card — " +
                     "tem bị bóc hoặc rách là dấu hiệu card đã mất bảo hành, thường do từng được tháo tản nhiệt.",
                     "Discrete cards do not expose their serial to software. It is on a sticker on the card — " +
                     "a peeled or torn sticker means the warranty is void, usually because the cooler was removed.")
        });
    }
}
