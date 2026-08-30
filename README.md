# Hardware Inspector

Công cụ desktop (C# / .NET 8 / WPF) thẩm định phần cứng laptop và PC — thiết kế cho
tình huống cụ thể: **bạn đang cầm một cái máy đã qua sử dụng và cần biết nó có đáng tin không.**

Phần mềm không chỉ liệt kê thông số. Nó đọc các "đồng hồ" mà từng linh kiện tự ghi lại,
đối chiếu chúng với nhau để tìm mâu thuẫn, và soi tầng firmware xem có dấu hiệu bị can thiệp.

> Đọc [`docs/METHODOLOGY.md`](docs/METHODOLOGY.md) trước. Nó giải thích cách nghĩ
> đứng sau từng phép kiểm tra, và trung thực về những gì công cụ này *không* làm được.

---

## Nó làm gì

**Đánh giá từng linh kiện** theo bốn mức — Tốt / Khá / Trung bình / Yếu — kèm điểm 0-100,
thời gian đã hoạt động, phần trăm sức khoẻ còn lại, và nhiệt độ min/trung bình/max.

| Linh kiện | Dữ liệu khai thác |
|---|---|
| CPU | Nhân/luồng, xung thiết kế vs xung thực đạt, nhiệt độ, microcode, phát hiện mẫu ES/QS |
| GPU | VRAM thật (không bị tràn 4 GB như WMI), vBIOS, nhiệt nhân vs điểm nóng, quạt |
| RAM | Từng thanh: hãng, part number, tốc độ danh định vs thực chạy, cấu hình kênh, ECC |
| Ổ lưu trữ | S.M.A.R.T. đầy đủ: giờ chạy, sector hỏng, TBW, tuổi thọ NAND, cảm biến va đập |
| Pin | Chu kỳ sạc, độ chai so với thiết kế, ngày sản xuất, chất liệu |
| Màn hình | EDID thô: mã tấm nền thật, tuần/năm sản xuất, độ sâu màu, kích thước |
| Bo mạch | Serial hệ thống/bo/vỏ, cảm biến mở vỏ, kênh cấp phép Windows |

**Kiểm tra BIOS đã bị can thiệp chưa** — tám phép kiểm tra ở bốn tầng:

- Chuỗi khởi động Secure Boot: trạng thái bật/tắt, **Setup Mode** (khoá nền tảng bị xoá),
  sự hiện diện của khoá PK, độ mới của danh sách thu hồi dbx
- **Bảng ACPI WPBT** — cơ chế cho phép firmware chạy file thực thi trong Windows
  ở mỗi lần khởi động, sống sót qua cả format ổ cứng
- Bảng **MSDM/SLIC** — khoá Windows OEM nạp tại nhà máy; vắng mặt trên laptop
  thương hiệu là chỉ dấu mạnh cho việc bo mạch đã bị thay
- SMBIOS thô đọc trực tiếp từ firmware: trường định danh để mặc định, serial lệch nhau
- Chuỗi phiên bản BIOS chứa dấu hiệu bản mod, ngày phát hành bất thường, microcode bị hạ cấp
- Công tắc khởi động Windows: `testsigning`, `nointegritychecks`, `debug`
- Quét phân vùng EFI: liệt kê mọi file `.efi`, tính SHA-256, đánh dấu file ngoài đường dẫn chuẩn
- TPM, Measured Boot, VBS/HVCI, Kernel DMA Protection

**Đối chiếu chéo để phát hiện mâu thuẫn** — phần cốt lõi khi thẩm định máy cũ:

- Tuổi máy suy ra từ BIOS, giờ chạy ổ cứng, năm sản xuất tấm nền, ngày sản xuất pin
  có khớp nhau không
- Pin gần như mới trên máy có ổ chạy 20.000 giờ → pin vừa được thay để "làm đẹp" máy
- Ổ cứng chạy trên 60% toàn bộ thời gian kể từ ngày xuất xưởng → máy chạy 24/7,
  không phải máy cá nhân

**Bài kiểm tra thực hành:**

- Chạy tải nặng CPU/RAM/ổ đĩa rồi tự quét lại — vì mọi vấn đề tản nhiệt chỉ lộ khi máy nóng thật
- Kiểm tra màn hình toàn màn hình: 8 bài theo thứ tự có chủ đích (đen → trắng → RGB → xám →
  chuyển sắc), có lưới chia vùng để soi điểm chết

**Xuất biên bản** HTML một file (in được, đưa người bán xem tại chỗ) hoặc JSON đầy đủ dữ liệu thô.

---

## Cấu trúc thư mục

```
HardwareInspector/
├── HardwareInspector.sln
├── Directory.Build.props              # thiết lập chung cho mọi project
├── docs/
│   ├── METHODOLOGY.md                 # quy trình thẩm định — đọc trước
│   └── ARCHITECTURE.md                # sơ đồ phụ thuộc, cách mở rộng
└── src/
    ├── HardwareInspector.Core/        # model + interface, không phụ thuộc gì
    │   ├── Abstractions/              #   IInfoCollector, IComponentAnalyzer, ...
    │   ├── Models/
    │   │   ├── Components/            #   ProcessorInfo, StorageInfo, BatteryInfo, ...
    │   │   ├── Firmware/              #   SmbiosStructure, SecureBootState, TpmState
    │   │   ├── Sensors/               #   SensorReading, SensorStats (min/max/avg)
    │   │   └── Assessment/            #   ComponentAssessment, TrustReport
    │   └── Utils/                     #   RatingScale, FormatHelpers
    │
    ├── HardwareInspector.Collectors/  # đọc dữ liệu thô
    │   ├── Wmi/                       #   WmiQuery — lớp bọc an toàn
    │   ├── Components/                #   một collector cho mỗi nhóm linh kiện
    │   ├── Smart/                     #   AtaSmartReader, NvmeReliabilityReader
    │   ├── Edid/                      #   EdidParser — tự bóc 128 byte
    │   └── EventLogs/                 #   ReliabilityScanner (WHEA, disk, bugcheck)
    │
    ├── HardwareInspector.Sensors/     # LibreHardwareMonitor + thống kê min/max/avg
    │
    ├── HardwareInspector.Firmware/    # tầng nhạy cảm nhất
    │   ├── Native/                    #   P/Invoke GetSystemFirmwareTable, biến UEFI
    │   ├── Smbios/                    #   đọc + giải mã bảng SMBIOS thô
    │   ├── Acpi/                      #   liệt kê bảng ACPI, tìm WPBT/MSDM
    │   ├── Uefi/                      #   SecureBootInspector, EspScanner
    │   ├── Tpm/                       #   TPM + Device Guard
    │   └── Checks/                    #   8 phép kiểm tra, mỗi cái một câu hỏi
    │
    ├── HardwareInspector.Analysis/    # chấm điểm và kết luận
    │   ├── Scoring/                   #   ScoreCard
    │   ├── Analyzers/                 #   một analyzer cho mỗi linh kiện
    │   └── Trust/                     #   SecondHandTrustEngine — đối chiếu chéo
    │
    ├── HardwareInspector.Benchmark/   # tạo tải để bộc lộ vấn đề nhiệt
    ├── HardwareInspector.Reporting/   # xuất HTML + JSON
    └── HardwareInspector.App/         # WPF, MVVM
        ├── Services/                  #   InspectionPipeline
        ├── ViewModels/
        ├── Views/                     #   MainWindow, ScreenTestWindow
        └── Themes/
```

---

## Chạy thử

```bash
dotnet restore
dotnet build -c Release
dotnet run --project src/HardwareInspector.App
```

Yêu cầu: .NET 8 SDK, Windows 10 1809 trở lên.

**Bắt buộc chạy với quyền Administrator.** `app.manifest` đã khai báo `requireAdministrator`
nên Windows sẽ tự hỏi. Không có quyền này thì mất S.M.A.R.T., cảm biến nhiệt,
biến UEFI và phân vùng EFI — tức là mất phần lớn giá trị của công cụ.
Khi thiếu quyền, phần mềm báo trạng thái firmware là *chưa đủ dữ liệu* chứ không bao giờ
báo *sạch*.

### Thư viện ngoài

| Gói | Dùng để |
|---|---|
| `LibreHardwareMonitorLib` | Nhiệt độ, xung nhịp, quạt, điện áp, công suất |
| `System.Management` | Truy vấn WMI |
| `System.Diagnostics.EventLog` | Đọc nhật ký sự cố hệ thống |

Còn lại đều dùng P/Invoke trực tiếp tới API Windows — SMBIOS, ACPI, biến UEFI, EDID
đều tự đọc và tự parse.

---

## Quy trình sử dụng khuyến nghị

1. **Bắt đầu kiểm tra** — quét toàn bộ, mất khoảng 20-40 giây
2. Đọc tab **BIOS & bảo mật** trước. Nếu ở đây có vấn đề, mọi thứ khác thành thứ yếu
3. Đọc tab **Đối chiếu chéo** — đây là chỗ lộ ra sự không trung thực
4. **Chạy tải nặng** 15 phút, rồi quay lại tab Linh kiện xem nhiệt độ dưới tải
5. **Kiểm tra màn hình** trong phòng tối
6. Làm hết tab **Kiểm tra tay** — danh sách này thay đổi theo những gì vừa phát hiện
7. **Xuất biên bản** để lưu hoặc đưa người bán xem

---

## Giới hạn cần biết

Phần mềm chạy trong Windows không thể:

- Đọc nội dung chip BIOS để so byte-by-byte với bản gốc — cần programmer SPI và tháo máy
- Phát hiện RAM lỗi nhẹ — cần MemTest86 từ USB
- Thấy artifact GPU — cần chạy tải đồ hoạ thật và nhìn bằng mắt
- Đánh giá tụ phồng, bản lề nứt, ẩm mốc, chất lượng nguồn

Một firmware đã bị can thiệp hoàn toàn có thể nói dối về chính nó.
Vì vậy mọi kết luận đều kèm mức độ chắc chắn, và mọi báo cáo đều kết thúc
bằng checklist kiểm tra tay.
