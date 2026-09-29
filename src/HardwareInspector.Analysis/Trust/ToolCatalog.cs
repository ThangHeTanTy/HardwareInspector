using HardwareInspector.Core.Models.Assessment;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Analysis.Trust;

/// <summary>
/// Danh mục công cụ chuyên dụng gắn vào checklist kiểm tra tay.
///
/// Tiêu chí chọn: được cộng đồng phần cứng dùng rộng rãi, có bản miễn phí dùng được
/// cho việc kiểm tra máy cũ (trừ khi ghi rõ), và đường dẫn trỏ thẳng tới trang của
/// nhà phát triển. Mỗi công cụ ghi rõ cần nhìn vào đâu, vì chạy mà không biết đọc
/// kết quả thì cũng như không chạy.
///
/// Câu chữ được dựng lại mỗi lần gọi để theo đúng ngôn ngữ đang chọn.
/// </summary>
public static class ToolCatalog
{
    private static string Free => S("Miễn phí", "Free");
    private static string FreePersonal => S("Miễn phí cho cá nhân", "Free for personal use");
    private static string Online => S("Chạy trên trình duyệt", "Runs in the browser");

    // --- GPU ---

    public static ToolLink Occt => new("OCCT", "https://www.ocbase.com/download",
        S("Bài VRAM tự phát hiện lỗi và bài 3D có kiểm lỗi — kết quả chỉ cần đọc số lỗi, không cần nhìn bằng mắt.",
          "Its VRAM test detects errors by itself and its 3D test checks for errors — you read an error count instead of eyeballing."),
        FreePersonal);

    public static ToolLink FurMark => new("FurMark 2", "https://geeks3d.com/furmark/",
        S("Tải nhiệt GPU cực nặng. Chạy 15 phút, quan sát sọc, chấm màu, nhấp nháy và nhiệt độ có ổn định không.",
          "Extreme GPU thermal load. Run for 15 minutes and watch for stripes, coloured dots, flicker, and whether temperatures level off."),
        Free);

    public static ToolLink Superposition => new("Unigine Superposition", "https://benchmark.unigine.com/superposition",
        S("Cảnh 3D nhiều chi tiết, lỗi hiển thị dễ thấy hơn FurMark. So điểm với card cùng loại trên mạng.",
          "A detailed 3D scene where rendering faults are easier to spot than in FurMark. Compare the score with the same card online."),
        S("Miễn phí (bản Basic)", "Free (Basic edition)"));

    public static ToolLink ThreeDMark => new("3DMark", "https://store.steampowered.com/app/223850/3DMark/",
        S("Time Spy Stress Test chạy 20 vòng: độ ổn định khung hình từ 97% trở lên là đạt. Bản demo miễn phí không có bài stress.",
          "Time Spy Stress Test runs 20 loops: frame-rate stability of 97% or more is a pass. The free demo lacks the stress test."),
        S("Trả phí (bản Advanced)", "Paid (Advanced edition)"));

    public static ToolLink GpuZ => new("GPU-Z", "https://www.techpowerup.com/gpuz/",
        S("Phát hiện card giả/đổi tên, xem vBIOS, và kiểm tra khe PCIe có chạy đủ x16 khi có tải không (nút \"?\" cạnh Bus Interface).",
          "Detects fake or rebadged cards, shows the vBIOS, and checks whether the PCIe slot runs at full x16 under load (the \"?\" beside Bus Interface)."),
        Free);

    // --- Cảm biến, CPU, RAM ---

    public static ToolLink HwInfo => new("HWiNFO", "https://www.hwinfo.com/download/",
        S("Mở chế độ Sensors-only trong lúc chạy tải: xem nhiệt độ, xung, công suất, và cột \"Thermal Throttling\" có chuyển sang Yes không.",
          "Open Sensors-only mode during a stress run: watch temperatures, clocks, power, and whether \"Thermal Throttling\" flips to Yes."),
        S("Miễn phí cho mục đích phi thương mại", "Free for non-commercial use"));

    public static ToolLink Cinebench => new("Cinebench 2024", "https://www.maxon.net/en/downloads/cinebench-2024-downloads",
        S("Chạy đa nhân chế độ 10 phút, so điểm với máy cùng CPU. Thấp hơn 15% trở lên là máy đang bị giới hạn nhiệt hoặc công suất.",
          "Run multi-core in 10-minute mode and compare with the same CPU. 15% or more below is thermal or power limiting."),
        Free);

    public static ToolLink Prime95 => new("Prime95", "https://www.mersenne.org/download/",
        S("Torture Test: tự báo lỗi khi CPU/RAM tính sai. Chỉ một lỗi \"FATAL ERROR\" cũng đủ để không mua.",
          "Torture Test: reports errors when the CPU/RAM miscalculates. A single \"FATAL ERROR\" is reason enough to walk away."),
        Free);

    public static ToolLink MemTest86 => new("MemTest86", "https://www.memtest86.com/download.htm",
        S("Ghi ra USB rồi khởi động từ USB, chạy đủ 4 lượt. Bất kỳ lỗi nào (màu đỏ) đều là RAM hỏng.",
          "Write it to a USB stick and boot from it, run all 4 passes. Any error (red) means bad RAM."),
        Free);

    public static ToolLink MemTest86Plus => new("Memtest86+", "https://www.memtest.org/",
        S("Bản mã nguồn mở, dùng khi máy không khởi động được MemTest86 (máy cũ không có UEFI).",
          "Open-source alternative for machines that cannot boot MemTest86 (older, non-UEFI systems)."),
        Free);

    // --- Ổ lưu trữ ---

    public static ToolLink CrystalDiskInfo => new("CrystalDiskInfo", "https://crystalmark.info/en/software/crystaldiskinfo/",
        S("Đối chiếu S.M.A.R.T. bằng một công cụ độc lập: số giờ chạy, số lần bật máy, tổng dữ liệu đã ghi phải khớp với ứng dụng này.",
          "Cross-check S.M.A.R.T. with an independent tool: power-on hours, power cycles and total writes should match this app."),
        Free);

    public static ToolLink CrystalDiskMark => new("CrystalDiskMark", "https://crystalmark.info/en/software/crystaldiskmark/",
        S("Đo tốc độ đọc/ghi thật. Thấp hơn nhiều so với thông số hãng là ổ đã xuống cấp, bị giả, hoặc cắm sai khe.",
          "Measures real read/write speed. Far below the rated spec means a worn or fake drive, or the wrong slot."),
        Free);

    // --- Màn hình và thiết bị ngoại vi ---

    public static ToolLink EizoMonitorTest => new("EIZO Monitor Test", "https://www.eizo.be/monitor-test/",
        S("13 bài kiểm tra: điểm chết, độ đồng đều, chuyển sắc, góc nhìn, thời gian đáp ứng.",
          "13 tests: dead pixels, uniformity, gradients, viewing angles, response time."),
        Online);

    public static ToolLink TestUfo => new("TestUFO", "https://testufo.com/",
        S("Kiểm tra bóng mờ, bỏ khung hình và tần số quét thật so với quảng cáo (ví dụ màn 144 Hz).",
          "Checks ghosting, frame skipping and whether the real refresh rate matches the advertised one (e.g. 144 Hz)."),
        Online);

    public static ToolLink KeyboardTest => new("Keyboard Test", "https://www.onlinemictest.com/keyboard-test/",
        S("Bấm lần lượt từng phím, phím nào không sáng là hỏng. Thử cả tổ hợp nhiều phím cùng lúc.",
          "Press every key in turn; any key that does not light up is dead. Try multi-key combinations too."),
        Online);

    public static ToolLink WebcamTest => new("Webcam Test", "https://www.onlinemictest.com/webcam-test/",
        S("Kiểm tra webcam có lên hình, độ nét và độ phân giải thật.",
          "Checks the webcam produces an image, its sharpness and real resolution."),
        Online);

    public static ToolLink MicTest => new("Mic Test", "https://www.onlinemictest.com/",
        S("Nói vào micro và xem sóng âm; tiếng rè hoặc không có tín hiệu là micro/cáp hỏng.",
          "Speak into the mic and watch the waveform; crackling or no signal means a bad mic or cable."),
        Online);

    public static ToolLink BatteryInfoView => new("BatteryInfoView", "https://www.nirsoft.net/utils/battery_information_view.html",
        S("Xem tốc độ xả (mW) khi rút sạc: xả nhanh bất thường hoặc điện áp tụt nhanh là pin sắp hỏng.",
          "Watch the discharge rate (mW) on battery: abnormally fast discharge or rapidly sagging voltage means a failing battery."),
        Free);

    public static ToolLink Speedtest => new("Speedtest", "https://www.speedtest.net/",
        S("Đo tốc độ Wi-Fi thật, so với máy khác cùng mạng ở cùng vị trí.",
          "Measures real Wi-Fi speed; compare with another device on the same network in the same spot."),
        Online);
}
