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
            clocks.Add((S("Ngày phát hành BIOS", "BIOS release date"), (DateTime.Now - bios).TotalDays / 365));

        var storageHours = snapshot.StorageDevices
            .Where(d => d.PowerOnHours is > 0)
            .Select(d => d.PowerOnHours!.Value)
            .DefaultIfEmpty(0)
            .Max();

        if (storageHours > 0)
        {
            // Máy văn phòng dùng trung bình khoảng 6-8 giờ/ngày.
            var impliedYears = storageHours / (8 * 250.0);
            clocks.Add((S("Giờ chạy ổ cứng", "Drive power-on hours"), impliedYears));
        }

        var panelYear = snapshot.Displays.Select(d => d.ManufactureYear).FirstOrDefault(y => y.HasValue);
        if (panelYear.HasValue)
            clocks.Add((S("Năm sản xuất tấm nền", "Panel manufacture year"), DateTime.Now.Year - panelYear.Value));

        if (snapshot.PrimaryBattery?.ManufactureDate is { } batteryDate)
            clocks.Add((S("Ngày sản xuất pin", "Battery manufacture date"), (DateTime.Now - batteryDate).TotalDays / 365));

        if (clocks.Count < 2)
        {
            report.CrossChecks.Add(new CrossCheckResult(
                S("Đối chiếu tuổi máy", "Machine age cross-check"),
                false,
                S("Không thu thập đủ nguồn độc lập để đối chiếu tuổi máy. " +
                  "Chạy ứng dụng với quyền Administrator sẽ mở thêm dữ liệu S.M.A.R.T. và pin.",
                  "Not enough independent sources to cross-check the machine's age. " +
                  "Running the app as Administrator unlocks S.M.A.R.T. and battery data."),
                Severity.Notice));
            return 5;
        }

        var min = clocks.Min(c => c.Years);
        var max = clocks.Max(c => c.Years);
        var spread = max - min;
        var detail = string.Join("; ", clocks.Select(c => S($"{c.Source}: {c.Years:0.#} năm", $"{c.Source}: {c.Years:0.#} years")));

        if (spread > 4)
        {
            penalty += 20;
            report.CrossChecks.Add(new CrossCheckResult(
                S("Đối chiếu tuổi máy", "Machine age cross-check"),
                false,
                S($"Các nguồn cho ra tuổi máy lệch nhau tới {spread:0.#} năm. {detail}. " +
                  "Chênh lệch lớn như vậy nghĩa là ít nhất một linh kiện chính đã được thay, " +
                  "hoặc thông tin nào đó đã bị chỉnh sửa.",
                  $"The sources disagree on the machine's age by up to {spread:0.#} years. {detail}. " +
                  "A gap this large means at least one major component was replaced, " +
                  "or some information has been altered."),
                Severity.Warning));

            report.RedFlags.Add(new Finding
            {
                Code = "TRUST-AGE-001",
                Component = ComponentKind.Motherboard,
                Severity = Severity.Warning,
                Title = S($"Tuổi máy theo các nguồn lệch nhau {spread:0.#} năm", $"Age sources disagree by {spread:0.#} years"),
                Detail = detail,
                Recommendation = S("Xác định linh kiện nào lệch, rồi hỏi người bán lịch sử thay thế của đúng linh kiện đó.",
                                   "Find which component is out of line, then ask the seller about that part's replacement history."),
                ScorePenalty = 20,
                Confidence = 0.7
            });
        }
        else
        {
            report.CrossChecks.Add(new CrossCheckResult(
                S("Đối chiếu tuổi máy", "Machine age cross-check"),
                true,
                S($"Các nguồn nhất quán trong khoảng {spread:0.#} năm. {detail}.", $"The sources agree within {spread:0.#} years. {detail}.")));
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
                    S("Toàn vẹn firmware", "Firmware integrity"), false,
                    S("Phát hiện bằng chứng firmware đã bị can thiệp. Đây là loại vấn đề mà cài lại Windows " +
                      "hoặc thay ổ cứng đều không giải quyết được.",
                      "Evidence of firmware tampering was found. Reinstalling Windows " +
                      "or replacing the drive will not fix this kind of problem."),
                    Severity.Critical));
                report.RedFlags.AddRange(fw.Findings.Where(f => f.Severity == Severity.Critical));
                return 45;

            case IntegrityStatus.Suspicious:
                report.CrossChecks.Add(new CrossCheckResult(
                    S("Toàn vẹn firmware", "Firmware integrity"), false,
                    S("Có bất thường ở tầng firmware nhưng chưa đủ để kết luận. Xem chi tiết ở mục BIOS & bảo mật.",
                      "There are firmware-level anomalies, but not enough to conclude. See BIOS & security for details."),
                    Severity.Warning));
                report.RedFlags.AddRange(fw.Findings.Where(f => f.Severity >= Severity.Warning));
                return 20;

            case IntegrityStatus.Unverified:
                report.CrossChecks.Add(new CrossCheckResult(
                    S("Toàn vẹn firmware", "Firmware integrity"), false,
                    S("Chưa chạy đủ phép kiểm tra do thiếu quyền Administrator. " +
                      "Không thể coi đây là kết quả sạch.",
                      "Not all checks ran because Administrator rights were missing. " +
                      "This cannot be treated as a clean result."),
                    Severity.Notice));
                return 8;

            default:
                report.CrossChecks.Add(new CrossCheckResult(
                    S("Toàn vẹn firmware", "Firmware integrity"), true,
                    S("Chuỗi khởi động và bảng firmware không cho thấy dấu hiệu can thiệp.",
                      "The boot chain and firmware tables show no signs of tampering.")));
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
        if (FormatHelpers.IsPlaceholder(b.SystemSerialNumber)) missing.Add(S("serial máy", "system serial"));
        if (FormatHelpers.IsPlaceholder(b.BoardSerialNumber)) missing.Add(S("serial bo mạch", "board serial"));
        if (FormatHelpers.IsPlaceholder(b.SystemProductName)) missing.Add(S("tên model", "model name"));

        if (missing.Count >= 2)
        {
            penalty += 15;
            report.CrossChecks.Add(new CrossCheckResult(
                S("Định danh máy", "Machine identity"), false,
                S($"Thiếu hoặc để mặc định: {string.Join(", ", missing)}. " +
                  "Máy nguyên bản của hãng lớn luôn có đầy đủ các trường này.",
                  $"Missing or left at defaults: {string.Join(", ", missing)}. " +
                  "Original machines from major brands always have these fields filled in."),
                Severity.Warning));
        }
        else
        {
            report.CrossChecks.Add(new CrossCheckResult(
                S("Định danh máy", "Machine identity"), true,
                S($"Serial và model được nạp đầy đủ trong firmware ({b.SystemManufacturer} {b.SystemProductName}).",
                  $"Serial and model are fully programmed in the firmware ({b.SystemManufacturer} {b.SystemProductName}).")));
        }

        // Kênh cấp phép Windows là chỉ dấu gián tiếp cho việc bo mạch có bị thay không.
        var channel = snapshot.OperatingSystem.LicenseChannel;
        if (!snapshot.OperatingSystem.IsActivated)
        {
            penalty += 5;
            report.CrossChecks.Add(new CrossCheckResult(
                S("Bản quyền Windows", "Windows license"), false,
                S("Windows chưa được kích hoạt. Nếu người bán quảng cáo máy kèm bản quyền, đây là điểm cần làm rõ.",
                  "Windows is not activated. If the seller advertised a licensed copy, this needs clarifying."),
                Severity.Notice));
        }
        else if (channel.Contains("Volume", StringComparison.OrdinalIgnoreCase))
        {
            penalty += 5;
            report.CrossChecks.Add(new CrossCheckResult(
                S("Bản quyền Windows", "Windows license"), false,
                S("Windows kích hoạt bằng khoá Volume (dành cho doanh nghiệp). " +
                  "Trên một máy bán lẻ, điều này thường có nghĩa máy là hàng thanh lý doanh nghiệp, " +
                  "hoặc được kích hoạt bằng công cụ không chính thống.",
                  "Windows is activated with a Volume (enterprise) key. " +
                  "On a retail machine this usually means an ex-corporate unit, " +
                  "or activation with an unofficial tool."),
                Severity.Notice));
        }
        else
        {
            report.CrossChecks.Add(new CrossCheckResult(
                S("Bản quyền Windows", "Windows license"), true, S($"Đã kích hoạt qua kênh {channel.OrDash()}.", $"Activated through the {channel.OrDash()} channel.")));
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
                    S("Tương quan pin ↔ ổ cứng", "Battery ↔ drive correlation"), false,
                    S($"Ổ cứng đã chạy {storageHours:N0} giờ nhưng pin mới qua {battery.CycleCount} chu kỳ. " +
                      "Nhiều khả năng pin vừa được thay trước khi bán. Điều này không xấu, " +
                      "nhưng nó cho thấy tuổi thật của máy nằm ở con số giờ chạy ổ cứng, không phải ở tình trạng pin.",
                      $"The drive has run {storageHours:N0} hours but the battery shows only {battery.CycleCount} cycles. " +
                      "The battery was most likely replaced before sale. That is not bad in itself, " +
                      "but it means the machine's real age is in the drive hours, not the battery condition."),
                    Severity.Notice));
            }
            else
            {
                report.CrossChecks.Add(new CrossCheckResult(
                    S("Tương quan pin ↔ ổ cứng", "Battery ↔ drive correlation"), true,
                    S($"Mức hao mòn của pin ({battery.CycleCount} chu kỳ) tương xứng với giờ chạy ổ cứng ({storageHours:N0} giờ).",
                      $"Battery wear ({battery.CycleCount} cycles) is consistent with the drive hours ({storageHours:N0} h).")));
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
                        S("Cường độ sử dụng", "Usage intensity"), false,
                        S($"Ổ cứng đã chạy khoảng {dutyCycle:P0} toàn bộ thời gian kể từ khi máy xuất xưởng. " +
                          "Đây là kiểu vận hành của máy chủ, máy đào coin hoặc máy trạm chạy liên tục, " +
                          "không phải máy cá nhân dùng hàng ngày.",
                          $"The drive has been running about {dutyCycle:P0} of the time since the machine shipped. " +
                          "That is how servers, mining rigs or always-on workstations run, " +
                          "not a personal machine used day to day."),
                        Severity.Warning));

                    report.RedFlags.Add(new Finding
                    {
                        Code = "TRUST-DUTY-001",
                        Component = ComponentKind.Storage,
                        Severity = Severity.Warning,
                        Title = S($"Máy chạy gần như liên tục ({dutyCycle:P0} thời gian)", $"The machine ran almost continuously ({dutyCycle:P0} of the time)"),
                        Detail = S($"{storageHours:N0} giờ chạy trên tổng {machineAgeHours:N0} giờ kể từ ngày BIOS.",
                                   $"{storageHours:N0} hours of running out of {machineAgeHours:N0} hours since the BIOS date."),
                        Recommendation = S("Hỏi thẳng máy trước đây dùng làm gì. Toàn bộ linh kiện đều chịu mức hao mòn tương ứng.",
                                           "Ask directly what the machine was used for. Every component carries matching wear."),
                        ScorePenalty = 15,
                        Confidence = 0.75
                    });
                }
                else
                {
                    report.CrossChecks.Add(new CrossCheckResult(
                        S("Cường độ sử dụng", "Usage intensity"), true,
                        S($"Ổ cứng chạy khoảng {dutyCycle:P0} thời gian kể từ ngày xuất xưởng — mức sử dụng cá nhân bình thường.",
                          $"The drive has run about {dutyCycle:P0} of the time since shipping — normal personal use.")));
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
        TrustLevel.Trustworthy => S(
            "Các nguồn dữ liệu độc lập kể cùng một câu chuyện và không có dấu hiệu can thiệp. " +
            "Máy phù hợp để giao dịch với mức giá đúng tình trạng. Vẫn nên chạy bài kiểm tra thủ công ở cuối báo cáo.",
            "Independent data sources tell the same story and there is no sign of tampering. " +
            "The machine is fine to buy at a price that reflects its condition. Still run the manual checks at the end of the report."),

        TrustLevel.Acceptable => S(
            "Máy không có vấn đề nghiêm trọng, nhưng có một vài điểm hao mòn hoặc bất thường nhỏ. " +
            "Dùng các phát hiện bên dưới làm cơ sở thương lượng giá, và kiểm tra tay những mục được đánh dấu.",
            "No serious problems, but a few points of wear or minor anomalies. " +
            "Use the findings below to negotiate the price, and check the flagged items by hand."),

        TrustLevel.Suspicious => S(
            "Có những mâu thuẫn hoặc hao mòn đủ lớn để cần làm rõ trước khi trả tiền. " +
            "Đừng chốt giao dịch cho tới khi người bán giải thích thoả đáng từng điểm được liệt kê.",
            "There are contradictions or wear large enough to clarify before paying. " +
            "Do not close the deal until the seller satisfactorily explains every point listed."),

        TrustLevel.DoNotBuy => S(
            "Phát hiện vấn đề ở mức không nên chấp nhận: hoặc firmware có dấu hiệu bị can thiệp, " +
            "hoặc nhiều linh kiện chính đang ở tình trạng hỏng. Chi phí khắc phục thường vượt giá trị còn lại của máy.",
            "Problems at a level you should not accept: either the firmware shows signs of tampering, " +
            "or several major components are failing. Fixing them usually costs more than the machine is worth."),

        _ => S("Chưa thu thập đủ dữ liệu để đưa ra kết luận. Hãy chạy lại ứng dụng với quyền Administrator.",
               "Not enough data was collected to reach a conclusion. Run the app again as Administrator.")
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

        Add(S("Đối chiếu serial in trên tem máy với serial phần mềm đọc được trong mục Bo mạch chủ.",
              "Compare the serial on the machine's label with the serial shown under Motherboard."));
        Add(S("Tra serial trên trang bảo hành của hãng: model tra ra phải trùng với máy đang cầm, và xem còn hạn không.",
              "Look the serial up on the manufacturer's warranty site: the model must match the machine in your hands, and check whether it is still covered."));
        Add(S("Chạy bài kiểm tra màn hình trong phòng tối để soi điểm chết, điểm sáng và hở sáng viền.",
              "Run the screen test in a dark room to spot dead pixels, stuck pixels and backlight bleed."),
            ToolCatalog.EizoMonitorTest, ToolCatalog.TestUfo);
        Add(S("Chạy bài tải nặng CPU/GPU ít nhất 15 phút, vừa chạy vừa theo dõi nhiệt độ và nghe tiếng quạt.",
              "Run a heavy CPU/GPU load for at least 15 minutes, watching temperatures and listening to the fans."),
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
        Add(S("Cắm thử lần lượt từng cổng USB, HDMI, jack tai nghe, khe thẻ nhớ.",
              "Try every USB port, HDMI port, headphone jack and card slot in turn."));
        Add(S("Kiểm tra Wi-Fi và Bluetooth bằng cách kết nối thật, không chỉ nhìn Device Manager.",
              "Test Wi-Fi and Bluetooth with real connections, not just by looking at Device Manager."),
            ToolCatalog.Speedtest);
        Add(S("Chạy MemTest86 từ USB nếu định giữ máy lâu — phần mềm trong Windows không phát hiện được RAM lỗi nhẹ.",
              "Run MemTest86 from USB if you plan to keep the machine — software inside Windows cannot catch slightly faulty RAM."),
            ToolCatalog.MemTest86, ToolCatalog.MemTest86Plus);

        if (snapshot.IsLaptop)
        {
            Add(S("Gõ thử toàn bộ bàn phím, đặc biệt các phím ít dùng ở hàng số và cụm điều hướng.",
                  "Type on every key, especially rarely used ones in the number row and navigation cluster."),
                ToolCatalog.KeyboardTest);
            Add(S("Kiểm tra bản lề: mở gập vài lần, nghe tiếng kêu và xem vỏ quanh bản lề có nứt hay phồng không.",
                  "Check the hinges: open and close a few times, listen for creaks and look for cracks or bulges around them."));
            Add(S("Rút sạc và dùng thử ít nhất 30 phút để xem máy có tự tắt do pin sụt áp không.",
                  "Unplug the charger and use it for at least 30 minutes to see whether it shuts down from battery sag."),
                ToolCatalog.BatteryInfoView);
            Add(S("Kiểm tra webcam, micro và loa — mở ứng dụng ghi âm và quay thử.",
                  "Test the webcam, microphone and speakers — record some audio and video."),
                ToolCatalog.WebcamTest, ToolCatalog.MicTest);
            Add(S("Soi ốc đáy máy: ốc toét đầu hoặc thiếu ốc nghĩa là máy đã được tháo nhiều lần.",
                  "Inspect the bottom screws: stripped or missing screws mean it has been opened many times."));
            Add(S("Sờ vỏ máy khi chạy tải nặng để phát hiện điểm nóng bất thường.",
                  "Feel the case under heavy load to find unusual hot spots."));
        }
        else
        {
            Add(S("Mở thùng máy quan sát tụ điện trên bo mạch: tụ phồng đầu hoặc rỉ dịch là dấu hiệu bo sắp hỏng.",
                  "Open the case and look at the board's capacitors: bulging tops or leaks mean the board is failing."));
            Add(S("Kiểm tra nguồn (PSU): xem nhãn công suất thật, nghe tiếng rít, ngửi mùi khét.",
                  "Check the PSU: read the real wattage label, listen for whine, smell for burning."));
            Add(S("Xem bụi bám và dấu hiệu ẩm mốc, gỉ sét trên bo mạch — chỉ dấu môi trường lưu trữ kém.",
                  "Look for dust, mould or rust on the board — signs of poor storage conditions."));
            Add(S("Kiểm tra tản nhiệt CPU có bị lỏng chốt hoặc lắp lệch không.",
                  "Check that the CPU cooler is not loose or mounted crooked."));
        }

        if (snapshot.Firmware.Status is IntegrityStatus.Suspicious or IntegrityStatus.Compromised)
        {
            Add(S("Nạp lại BIOS gốc tải từ trang chủ hãng theo đúng serial máy.",
                  "Reflash the original BIOS downloaded from the manufacturer's site for this serial."));
            Add(S("Vào BIOS chọn Restore Factory Keys rồi bật Secure Boot, kiểm tra máy còn khởi động được không.",
                  "In the BIOS choose Restore Factory Keys, enable Secure Boot and check that the machine still boots."));
            Add(S("Cài lại Windows sạch từ file ISO tải trực tiếp từ Microsoft, không dùng bản ghost của người bán.",
                  "Clean-install Windows from an ISO downloaded directly from Microsoft, not the seller's disk image."));
        }

        if (!snapshot.Firmware.RanWithAdminRights)
            list.Insert(0, new ManualCheckItem(
                S("Chạy lại ứng dụng này với quyền Administrator để mở khoá các phép kiểm tra firmware và S.M.A.R.T.",
                  "Run this app again as Administrator to unlock the firmware and S.M.A.R.T. checks.")));
    }
}
