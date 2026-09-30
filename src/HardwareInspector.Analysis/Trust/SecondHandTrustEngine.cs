using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Assessment;
using HardwareInspector.Core.Utils;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Analysis.Trust;

/// <summary>
/// Đây là phần cốt lõi của một buổi thẩm định máy cũ.
///
/// Nguyên tắc làm việc: không tin bất kỳ con số đơn lẻ nào. Mỗi linh kiện đều mang
/// một loại "đồng hồ" riêng — số giờ chạy của ổ cứng, số chu kỳ của pin, năm sản xuất
/// tấm nền, ngày phát hành BIOS, ngày cài Windows. Trên một cái máy nguyên bản và
/// được mô tả trung thực, tất cả các đồng hồ đó kể cùng một câu chuyện.
/// Người bán có thể sửa một hoặc hai con số, nhưng rất khó làm cho tất cả khớp nhau.
/// Việc của engine này là tìm chỗ chúng mâu thuẫn.
/// </summary>
public sealed class SecondHandTrustEngine
{
    public TrustReport Evaluate(SystemSnapshot snapshot, IReadOnlyList<ComponentAssessment> components)
    {
        var report = new TrustReport();
        var deductions = 0;

        deductions += RunAgeTriangulation(snapshot, report);
        deductions += RunFirmwareTrust(snapshot, report);
        deductions += RunIdentityChecks(snapshot, report);
        deductions += RunWearChecks(snapshot, report);
        deductions += CollectCriticalFindings(components, report);

        report.TrustScore = Math.Clamp(100 - deductions, 0, 100);
        report.Level = DetermineLevel(snapshot, report);
        report.Verdict = BuildVerdict(report, snapshot);
        BuildManualChecklist(snapshot, report);

        return report;
    }

    // ---------------------------------------------------------------------
    // 1. Đối chiếu tuổi máy từ nhiều nguồn độc lập
    // ---------------------------------------------------------------------
    private int RunAgeTriangulation(SystemSnapshot snapshot, TrustReport report)
    {
        var penalty = 0;
        var clocks = new List<(string Source, double Years)>();

        if (snapshot.Bios.ReleaseDate is { } bios)
            clocks.Add(("Ngày phát hành BIOS", (DateTime.Now - bios).TotalDays / 365));

        var storageHours = snapshot.StorageDevices
            .Where(d => d.PowerOnHours is > 0)
            .Select(d => d.PowerOnHours!.Value)
            .DefaultIfEmpty(0)
            .Max();

        if (storageHours > 0)
        {
            // Máy văn phòng dùng trung bình khoảng 6-8 giờ/ngày.
            var impliedYears = storageHours / (8 * 250.0);
            clocks.Add(("Giờ chạy ổ cứng", impliedYears));
        }

        var panelYear = snapshot.Displays.Select(d => d.ManufactureYear).FirstOrDefault(y => y.HasValue);
        if (panelYear.HasValue)
            clocks.Add(("Năm sản xuất tấm nền", DateTime.Now.Year - panelYear.Value));

        if (snapshot.PrimaryBattery?.ManufactureDate is { } batteryDate)
            clocks.Add(("Ngày sản xuất pin", (DateTime.Now - batteryDate).TotalDays / 365));

        if (clocks.Count < 2)
        {
            report.CrossChecks.Add(new CrossCheckResult(
                "Đối chiếu tuổi máy",
                false,
                "Không thu thập đủ nguồn độc lập để đối chiếu tuổi máy. " +
                "Chạy ứng dụng với quyền Administrator sẽ mở thêm dữ liệu S.M.A.R.T. và pin.",
                Severity.Notice));
            return 5;
        }

        var min = clocks.Min(c => c.Years);
        var max = clocks.Max(c => c.Years);
        var spread = max - min;
        var detail = string.Join("; ", clocks.Select(c => $"{c.Source}: {c.Years:0.#} năm"));

        if (spread > 4)
        {
            penalty += 20;
            report.CrossChecks.Add(new CrossCheckResult(
                "Đối chiếu tuổi máy",
                false,
                $"Các nguồn cho ra tuổi máy lệch nhau tới {spread:0.#} năm. {detail}. " +
                "Chênh lệch lớn như vậy nghĩa là ít nhất một linh kiện chính đã được thay, " +
                "hoặc thông tin nào đó đã bị chỉnh sửa.",
                Severity.Warning));

            report.RedFlags.Add(new Finding
            {
                Code = "TRUST-AGE-001",
                Component = ComponentKind.Motherboard,
                Severity = Severity.Warning,
                Title = $"Tuổi máy theo các nguồn lệch nhau {spread:0.#} năm",
                Detail = detail,
                Recommendation = "Xác định linh kiện nào lệch, rồi hỏi người bán lịch sử thay thế của đúng linh kiện đó.",
                ScorePenalty = 20,
                Confidence = 0.7
            });
        }
        else
        {
            report.CrossChecks.Add(new CrossCheckResult(
                "Đối chiếu tuổi máy",
                true,
                $"Các nguồn nhất quán trong khoảng {spread:0.#} năm. {detail}."));
        }

        return penalty;
    }

    // ---------------------------------------------------------------------
    // 2. Tin cậy tầng firmware
    // ---------------------------------------------------------------------
    private int RunFirmwareTrust(SystemSnapshot snapshot, TrustReport report)
    {
        var fw = snapshot.Firmware;

        switch (fw.Status)
        {
            case IntegrityStatus.Compromised:
                report.CrossChecks.Add(new CrossCheckResult(
                    "Toàn vẹn firmware", false,
                    "Phát hiện bằng chứng firmware đã bị can thiệp. Đây là loại vấn đề mà cài lại Windows " +
                    "hoặc thay ổ cứng đều không giải quyết được.",
                    Severity.Critical));
                report.RedFlags.AddRange(fw.Findings.Where(f => f.Severity == Severity.Critical));
                return 45;

            case IntegrityStatus.Suspicious:
                report.CrossChecks.Add(new CrossCheckResult(
                    "Toàn vẹn firmware", false,
                    "Có bất thường ở tầng firmware nhưng chưa đủ để kết luận. Xem chi tiết ở mục BIOS & bảo mật.",
                    Severity.Warning));
                report.RedFlags.AddRange(fw.Findings.Where(f => f.Severity >= Severity.Warning));
                return 20;

            case IntegrityStatus.Unverified:
                report.CrossChecks.Add(new CrossCheckResult(
                    "Toàn vẹn firmware", false,
                    "Chưa chạy đủ phép kiểm tra do thiếu quyền Administrator. " +
                    "Không thể coi đây là kết quả sạch.",
                    Severity.Notice));
                return 8;

            default:
                report.CrossChecks.Add(new CrossCheckResult(
                    "Toàn vẹn firmware", true,
                    "Chuỗi khởi động và bảng firmware không cho thấy dấu hiệu can thiệp."));
                return 0;
        }
    }

    // ---------------------------------------------------------------------
    // 3. Định danh máy có nhất quán không
    // ---------------------------------------------------------------------
    private int RunIdentityChecks(SystemSnapshot snapshot, TrustReport report)
    {
        var penalty = 0;
        var b = snapshot.Baseboard;

        var missing = new List<string>();
        if (FormatHelpers.IsPlaceholder(b.SystemSerialNumber)) missing.Add("serial máy");
        if (FormatHelpers.IsPlaceholder(b.BoardSerialNumber)) missing.Add("serial bo mạch");
        if (FormatHelpers.IsPlaceholder(b.SystemProductName)) missing.Add("tên model");

        if (missing.Count >= 2)
        {
            penalty += 15;
            report.CrossChecks.Add(new CrossCheckResult(
                "Định danh máy", false,
                $"Thiếu hoặc để mặc định: {string.Join(", ", missing)}. " +
                "Máy nguyên bản của hãng lớn luôn có đầy đủ các trường này.",
                Severity.Warning));
        }
        else
        {
            report.CrossChecks.Add(new CrossCheckResult(
                "Định danh máy", true,
                $"Serial và model được nạp đầy đủ trong firmware ({b.SystemManufacturer} {b.SystemProductName})."));
        }

        // Kênh cấp phép Windows là chỉ dấu gián tiếp cho việc bo mạch có bị thay không.
        var channel = snapshot.OperatingSystem.LicenseChannel;
        if (!snapshot.OperatingSystem.IsActivated)
        {
            penalty += 5;
            report.CrossChecks.Add(new CrossCheckResult(
                "Bản quyền Windows", false,
                "Windows chưa được kích hoạt. Nếu người bán quảng cáo máy kèm bản quyền, đây là điểm cần làm rõ.",
                Severity.Notice));
        }
        else if (channel.Contains("Volume", StringComparison.OrdinalIgnoreCase))
        {
            penalty += 5;
            report.CrossChecks.Add(new CrossCheckResult(
                "Bản quyền Windows", false,
                "Windows kích hoạt bằng khoá Volume (dành cho doanh nghiệp). " +
                "Trên một máy bán lẻ, điều này thường có nghĩa máy là hàng thanh lý doanh nghiệp, " +
                "hoặc được kích hoạt bằng công cụ không chính thống.",
                Severity.Notice));
        }
        else
        {
            report.CrossChecks.Add(new CrossCheckResult(
                "Bản quyền Windows", true, $"Đã kích hoạt qua kênh {channel}."));
        }

        return penalty;
    }

    // ---------------------------------------------------------------------
    // 4. Mức hao mòn có tương xứng với lời mô tả không
    // ---------------------------------------------------------------------
    private int RunWearChecks(SystemSnapshot snapshot, TrustReport report)
    {
        var penalty = 0;

        var battery = snapshot.PrimaryBattery;
        var storageHours = snapshot.StorageDevices
            .Where(d => d.PowerOnHours is > 0)
            .Select(d => d.PowerOnHours!.Value)
            .DefaultIfEmpty(0).Max();

        // Pin gần như mới nhưng ổ cứng chạy hàng chục nghìn giờ: pin đã được thay để "làm đẹp" máy.
        if (battery is { IsPresent: true, CycleCount: > 0 } && storageHours > 15_000)
        {
            if (battery.CycleCount < 100)
            {
                penalty += 12;
                report.CrossChecks.Add(new CrossCheckResult(
                    "Tương quan pin ↔ ổ cứng", false,
                    $"Ổ cứng đã chạy {storageHours:N0} giờ nhưng pin mới qua {battery.CycleCount} chu kỳ. " +
                    "Nhiều khả năng pin vừa được thay trước khi bán. Điều này không xấu, " +
                    "nhưng nó cho thấy tuổi thật của máy nằm ở con số giờ chạy ổ cứng, không phải ở tình trạng pin.",
                    Severity.Notice));
            }
            else
            {
                report.CrossChecks.Add(new CrossCheckResult(
                    "Tương quan pin ↔ ổ cứng", true,
                    $"Mức hao mòn của pin ({battery.CycleCount} chu kỳ) tương xứng với giờ chạy ổ cứng ({storageHours:N0} giờ)."));
            }
        }

        // Ổ cứng chạy quá nhiều giờ so với tuổi máy tính theo BIOS: máy từng chạy liên tục 24/7.
        if (snapshot.Bios.ReleaseDate is { } biosDate && storageHours > 0)
        {
            var machineAgeHours = (DateTime.Now - biosDate).TotalDays * 24;
            if (machineAgeHours > 0)
            {
                var dutyCycle = storageHours / machineAgeHours;
                if (dutyCycle > 0.6)
                {
                    penalty += 15;
                    report.CrossChecks.Add(new CrossCheckResult(
                        "Cường độ sử dụng", false,
                        $"Ổ cứng đã chạy khoảng {dutyCycle:P0} toàn bộ thời gian kể từ khi máy xuất xưởng. " +
                        "Đây là kiểu vận hành của máy chủ, máy đào coin hoặc máy trạm chạy liên tục, " +
                        "không phải máy cá nhân dùng hàng ngày.",
                        Severity.Warning));

                    report.RedFlags.Add(new Finding
                    {
                        Code = "TRUST-DUTY-001",
                        Component = ComponentKind.Storage,
                        Severity = Severity.Warning,
                        Title = $"Máy chạy gần như liên tục ({dutyCycle:P0} thời gian)",
                        Detail = $"{storageHours:N0} giờ chạy trên tổng {machineAgeHours:N0} giờ kể từ ngày BIOS.",
                        Recommendation = "Hỏi thẳng máy trước đây dùng làm gì. Toàn bộ linh kiện đều chịu mức hao mòn tương ứng.",
                        ScorePenalty = 15,
                        Confidence = 0.75
                    });
                }
                else
                {
                    report.CrossChecks.Add(new CrossCheckResult(
                        "Cường độ sử dụng", true,
                        $"Ổ cứng chạy khoảng {dutyCycle:P0} thời gian kể từ ngày xuất xưởng — mức sử dụng cá nhân bình thường."));
                }
            }
        }

        return penalty;
    }

    // ---------------------------------------------------------------------
    // 5. Gom các phát hiện nghiêm trọng từ mọi linh kiện
    // ---------------------------------------------------------------------
    private int CollectCriticalFindings(IReadOnlyList<ComponentAssessment> components, TrustReport report)
    {
        var criticals = components
            .SelectMany(c => c.Findings)
            .Where(f => f.Severity == Severity.Critical)
            .DistinctBy(f => f.Code)
            .ToList();

        foreach (var f in criticals.Where(f => report.RedFlags.All(r => r.Code != f.Code)))
            report.RedFlags.Add(f);

        return Math.Min(30, criticals.Count * 10);
    }

    // ---------------------------------------------------------------------
    private TrustLevel DetermineLevel(SystemSnapshot snapshot, TrustReport report)
    {
        if (snapshot.Firmware.Status == IntegrityStatus.Compromised) return TrustLevel.DoNotBuy;
        if (report.RedFlags.Count(f => f.Severity == Severity.Critical) >= 3) return TrustLevel.DoNotBuy;

        return report.TrustScore switch
        {
            >= 82 => TrustLevel.Trustworthy,
            >= 62 => TrustLevel.Acceptable,
            >= 38 => TrustLevel.Suspicious,
            _ => TrustLevel.DoNotBuy
        };
    }

    private string BuildVerdict(TrustReport report, SystemSnapshot snapshot) => report.Level switch
    {
        TrustLevel.Trustworthy =>
            "Các nguồn dữ liệu độc lập kể cùng một câu chuyện và không có dấu hiệu can thiệp. " +
            "Máy phù hợp để giao dịch với mức giá đúng tình trạng. Vẫn nên chạy bài kiểm tra thủ công ở cuối báo cáo.",

        TrustLevel.Acceptable =>
            "Máy không có vấn đề nghiêm trọng, nhưng có một vài điểm hao mòn hoặc bất thường nhỏ. " +
            "Dùng các phát hiện bên dưới làm cơ sở thương lượng giá, và kiểm tra tay những mục được đánh dấu.",

        TrustLevel.Suspicious =>
            "Có những mâu thuẫn hoặc hao mòn đủ lớn để cần làm rõ trước khi trả tiền. " +
            "Đừng chốt giao dịch cho tới khi người bán giải thích thoả đáng từng điểm được liệt kê.",

        TrustLevel.DoNotBuy =>
            "Phát hiện vấn đề ở mức không nên chấp nhận: hoặc firmware có dấu hiệu bị can thiệp, " +
            "hoặc nhiều linh kiện chính đang ở tình trạng hỏng. Chi phí khắc phục thường vượt giá trị còn lại của máy.",

        _ => "Chưa thu thập đủ dữ liệu để đưa ra kết luận. Hãy chạy lại ứng dụng với quyền Administrator."
    };

    /// <summary>
    /// Những thứ phần mềm không bao giờ thay thế được. Danh sách này thay đổi
    /// theo loại máy vì kiểm laptop và kiểm PC là hai công việc khác nhau.
    ///
    /// Mục nào có công cụ chuyên dụng làm tốt hơn thì gắn kèm đường dẫn tải chính thức,
    /// để người dùng không phải tự tìm — và không lạc vào trang tải lậu gắn mã độc.
    /// </summary>
    private void BuildManualChecklist(SystemSnapshot snapshot, TrustReport report)
    {
        var list = report.ManualChecklist;
        void Add(string text, params ToolLink[] tools) => list.Add(new ManualCheckItem(text, tools));

        var gpu = snapshot.PrimaryGpu;
        var gpuTest = snapshot.GpuTest;

        Add("Đối chiếu serial in trên tem máy với serial phần mềm đọc được trong mục Bo mạch chủ.");
        Add("Tra serial trên trang bảo hành của hãng: model tra ra phải trùng với máy đang cầm, và xem còn hạn không.");
        Add("Chạy bài kiểm tra màn hình trong phòng tối để soi điểm chết, điểm sáng và hở sáng viền.",
            ToolCatalog.EizoMonitorTest, ToolCatalog.TestUfo);
        Add("Chạy bài tải nặng CPU/GPU ít nhất 15 phút, vừa chạy vừa theo dõi nhiệt độ và nghe tiếng quạt.",
            ToolCatalog.HwInfo, ToolCatalog.Cinebench, ToolCatalog.Prime95);

        if (gpu is { IsDiscrete: true })
        {
            var text = gpuTest is { HasHardErrors: true }
                ? S("Bài kiểm tra GPU trong ứng dụng đã phát hiện lỗi. Chạy lại bằng một công cụ độc lập để xác nhận " +
                    "trước khi mặc cả hoặc từ chối mua — hai công cụ cùng báo lỗi thì không còn gì để bàn.",
                    "The in-app GPU test found errors. Re-run with an independent tool to confirm " +
                    "before negotiating or walking away — two tools reporting errors leaves nothing to argue about.")
                : S("Chạy một bài tải đồ hoạ 3D 15 phút và nhìn trực tiếp màn hình: sọc, chấm màu, nhấp nháy hay " +
                    "treo hình là dấu hiệu card hỏng mà phần mềm không tự thấy được. Kiểm tra thêm khe PCIe có chạy đủ làn không.",
                    "Run a 3D graphics load for 15 minutes and watch the screen yourself: stripes, coloured dots, flicker or " +
                    "freezes are card faults software cannot see. Also check that the PCIe slot runs at full width.");
            Add(text, ToolCatalog.Occt, ToolCatalog.FurMark, ToolCatalog.Superposition, ToolCatalog.ThreeDMark, ToolCatalog.GpuZ);
        }

        Add(S("Đối chiếu S.M.A.R.T. và đo tốc độ ổ bằng công cụ độc lập — số giờ chạy và dữ liệu đã ghi phải khớp với ứng dụng này.",
              "Cross-check S.M.A.R.T. and measure drive speed with independent tools — power-on hours and total writes should match this app."),
            ToolCatalog.CrystalDiskInfo, ToolCatalog.CrystalDiskMark);
        Add("Cắm thử lần lượt từng cổng USB, HDMI, jack tai nghe, khe thẻ nhớ.");
        Add("Kiểm tra Wi-Fi và Bluetooth bằng cách kết nối thật, không chỉ nhìn Device Manager.",
            ToolCatalog.Speedtest);
        Add("Chạy MemTest86 từ USB nếu định giữ máy lâu — phần mềm trong Windows không phát hiện được RAM lỗi nhẹ.",
            ToolCatalog.MemTest86, ToolCatalog.MemTest86Plus);

        if (snapshot.IsLaptop)
        {
            Add("Gõ thử toàn bộ bàn phím, đặc biệt các phím ít dùng ở hàng số và cụm điều hướng.",
                ToolCatalog.KeyboardTest);
            Add("Kiểm tra bản lề: mở gập vài lần, nghe tiếng kêu và xem vỏ quanh bản lề có nứt hay phồng không.");
            Add("Rút sạc và dùng thử ít nhất 30 phút để xem máy có tự tắt do pin sụt áp không.",
                ToolCatalog.BatteryInfoView);
            Add("Kiểm tra webcam, micro và loa — mở ứng dụng ghi âm và quay thử.",
                ToolCatalog.WebcamTest, ToolCatalog.MicTest);
            Add("Soi ốc đáy máy: ốc toét đầu hoặc thiếu ốc nghĩa là máy đã được tháo nhiều lần.");
            Add("Sờ vỏ máy khi chạy tải nặng để phát hiện điểm nóng bất thường.");
        }
        else
        {
            Add("Mở thùng máy quan sát tụ điện trên bo mạch: tụ phồng đầu hoặc rỉ dịch là dấu hiệu bo sắp hỏng.");
            Add("Kiểm tra nguồn (PSU): xem nhãn công suất thật, nghe tiếng rít, ngửi mùi khét.");
            Add("Xem bụi bám và dấu hiệu ẩm mốc, gỉ sét trên bo mạch — chỉ dấu môi trường lưu trữ kém.");
            Add("Kiểm tra tản nhiệt CPU có bị lỏng chốt hoặc lắp lệch không.");
        }

        if (snapshot.Firmware.Status is IntegrityStatus.Suspicious or IntegrityStatus.Compromised)
        {
            Add("Nạp lại BIOS gốc tải từ trang chủ hãng theo đúng serial máy.");
            Add("Vào BIOS chọn Restore Factory Keys rồi bật Secure Boot, kiểm tra máy còn khởi động được không.");
            Add("Cài lại Windows sạch từ file ISO tải trực tiếp từ Microsoft, không dùng bản ghost của người bán.");
        }

        if (!snapshot.Firmware.RanWithAdminRights)
            list.Insert(0, new ManualCheckItem(
                "Chạy lại ứng dụng này với quyền Administrator để mở khoá các phép kiểm tra firmware và S.M.A.R.T."));
    }
}
