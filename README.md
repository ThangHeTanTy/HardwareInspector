# Hardware Inspector

Công cụ desktop (C# / .NET 8 / WPF) thẩm định phần cứng laptop và PC — thiết kế cho
tình huống cụ thể: **bạn đang cầm một cái máy đã qua sử dụng và cần biết nó có đáng tin không.**

Phần mềm không chỉ liệt kê thông số. Nó đọc các "đồng hồ" mà từng linh kiện tự ghi lại,
đối chiếu chúng với nhau để tìm mâu thuẫn, và kiểm tra linh kiện có bị đổi tên/làm giả không.

> Đọc [`docs/METHODOLOGY.md`](docs/METHODOLOGY.md) trước. Nó giải thích cách nghĩ
> đứng sau từng phép kiểm tra, và trung thực về những gì công cụ này *không* làm được.

---

## Nó làm gì

**Đánh giá từng linh kiện** theo bốn mức — Tốt / Khá / Trung bình / Yếu — kèm điểm 0-100,
thời gian đã hoạt động, phần trăm sức khoẻ còn lại, và nhiệt độ min/trung bình/max.
Mỗi linh kiện có mô hình 3D xoay được bằng chuột (mỗi linh kiện giữ góc xoay riêng),
dựng theo dữ liệu máy thật: kích thước và kiểu chân CPU theo socket (LGA1700, AM4, AM5,
BGA laptop), card đồ hoạ dài theo số quạt đọc từ cảm biến (chỉnh tay được), số chip VRAM
theo dung lượng, thanh RAM DIMM/SO-DIMM đúng vị trí khấc DDR4/DDR5, ổ M.2/2,5"/3,5",
số cell pin theo điện áp, màn rời hay nắp laptop đúng tỉ lệ, bo mạch đúng số khe RAM
và khe đang cắm. Bộ phận có vấn đề đổi màu cam/đỏ — chip VRAM lỗi, mặt đĩa có sector hỏng,
cell pin chai — và mô hình tự tách lớp khi chỗ hỏng nằm bên trong vỏ.
Bo mạch vẽ thêm cổng mặt sau/mặt bên (USB, LAN, HDMI/DP/VGA, jack âm thanh, PS/2) đọc từ
bảng SMBIOS Type 8, cùng cổng SATA, header quạt và header USB trên bo. Máy có nhiều ổ hoặc
nhiều màn thì mỗi thiết bị có mô hình riêng, đặt ngay cạnh dòng thông tin của thiết bị đó.
Phần kết luận có cảnh lắp ráp tổng thể: CPU trong socket, RAM trong khe, card đồ hoạ trong
khe PCIe, ổ M.2 trong khe M.2, ổ SATA nối cáp về bo, màn hình nối cáp về cổng xuất hình;
cột mốc trên mỗi linh kiện tô màu theo mức đánh giá của nó. Cạnh đó là thông tin nhanh của máy:
model bo mạch, CPU, RAM, từng ổ, card đồ hoạ, kích thước và tần số quét từng màn.

**Kết luận cho người mua** bằng lời thường: *Nên mua*, *Mua được nhưng cần thương lượng* hoặc
*Không nên mua*, kèm lý do, việc người bán phải làm trước khi trả tiền, chi phí khắc phục ước tính
bằng VNĐ (thay ổ, thay pin, thay keo tản nhiệt, thay bàn phím…) và gợi ý mức trả giá — xem tab
*Mua hay không*.

**Chống gian lận cấu hình:** ngoài đối chiếu chữ ký CPUID với tên CPU, bài kiểm tra GPU đo sức tính
FP32 và băng thông VRAM rồi so với thông số chính hãng của model card khai. Card đời thấp nạp vBIOS
đổi tên hụt rất xa ở cả hai con số.

**Khoá doanh nghiệp:** phát hiện laptop còn đăng ký Windows Autopilot hoặc MDM của một tổ chức (cài lại
Windows là bị khoá), tài khoản Entra ID, domain, Intel AMT, chống trộm Absolute/Computrace, BitLocker
và mật khẩu BIOS.

**Thử bàn phím & thiết bị:** vẽ bàn phím laptop, phím nào bấm thì sáng lên, bắt phím dội, phím kẹt và
bấm nhiều phím cùng lúc; thử touchpad phủ kín vùng, click và cuộn; loa trái/phải riêng từng bên và
bài quét âm nghe rè; đo mức thu micro; mở webcam. Kết quả Đạt / Không đạt được tính vào chi phí khắc phục.

**Kiểm tra từng bước:** nút *Kiểm tra từng bước* dẫn người mới đi qua đủ trình tự — quét máy, chạy tải,
bàn phím, touchpad, loa/micro/webcam, màn hình, cổng USB, Wi-Fi/Bluetooth, pin — bước nào xong được đánh dấu,
cuối cùng mở thẳng kết luận.

**Đo pin thật:** rút sạc 5–15 phút, ứng dụng đọc dung lượng và công suất xả của pin rồi quy ra số giờ dùng
thực tế. **Cổng USB:** cắm một USB lần lượt vào từng cổng, ứng dụng ghi nhận cổng nào hoạt động.

**So sánh nhiều máy:** nhập giá người bán (gõ "12,5tr", "12.500.000" hay "800k" đều được) rồi lưu; tab
*So sánh máy* tính tổng tiền thật phải bỏ ra (giá + chi phí sửa) và chỉ ra máy đáng tiền nhất.

**Chạy từ USB:** `tools\publish-portable.cmd` đóng gói bản tự chứa, không cần cài .NET. Chạy từ ổ USB (hoặc
có file `portable.txt` cạnh .exe) thì cài đặt, danh sách so sánh và biên bản đều ghi cạnh .exe.

Màn hình nối qua bộ chuyển đổi (ví dụ màn VGA cắm qua chip DisplayPort→VGA) có thể bị nhận sai cổng;
khi đó chọn tay "Cổng thật của màn hình" trong phần Màn hình, lựa chọn được ghi nhớ cho lần sau.

Giao diện, kết quả phân tích, nhật ký sự cố, kết quả chạy tải và biên bản HTML
đều có hai ngôn ngữ Tiếng Việt / English. Đổi ngôn ngữ thì toàn bộ câu chữ đổi theo ngay,
dựng lại từ dữ liệu đã thu nên không phải quét lại máy.

| Linh kiện | Dữ liệu khai thác |
|---|---|
| CPU | Nhân/luồng, xung thiết kế vs xung thực đạt, nhiệt độ, microcode, phát hiện mẫu ES/QS |
| GPU | VRAM thật (không bị tràn 4 GB như WMI), vBIOS, nhiệt nhân vs điểm nóng, quạt; bài tải có đối chiếu kết quả tính toán, kiểm tra VRAM, lịch sử driver bị reset (TDR) |
| RAM | Từng thanh: hãng, part number, tốc độ danh định vs thực chạy, cấu hình kênh, ECC |
| Ổ lưu trữ | S.M.A.R.T. đầy đủ: giờ chạy, sector hỏng, TBW, tuổi thọ NAND, cảm biến va đập |
| Pin | Chu kỳ sạc, độ chai so với thiết kế, ngày sản xuất, chất liệu |
| Màn hình | EDID thô: mã tấm nền thật, tuần/năm sản xuất, độ sâu màu, kích thước; cổng thật kể cả khi màn VGA đi qua bộ chuyển DP/HDMI→VGA |
| Bo mạch | Model bo, từng khe RAM / PCIe / M.2 (đang dùng hay trống), cổng ngoài và đầu cắm trên bo, RAM tối đa, trần tốc độ SSD, kênh cấp phép Windows |

**BIOS không nằm trong tiêu chí chấm điểm.** BIOS đi kèm bo mạch do hãng phát hành và cập nhật;
trên bo bán lẻ các trường nó khai (serial, tên hệ thống, ngày phát hành, Secure Boot) thường để mặc định
ngay từ khi mới mua, nên dùng chúng để chấm điểm chỉ sinh cảnh báo sai. Phần bo mạch chỉ xét model bo,
từng khe RAM / PCIe / M.2 đang dùng hay còn trống, cổng kết nối và khả năng nâng cấp.
Mật khẩu BIOS vẫn được báo trong mục *Khoá & quyền sở hữu* vì đó là thứ khoá máy thật sự.

**Đối chiếu chéo để phát hiện mâu thuẫn** — phần cốt lõi khi thẩm định máy cũ:

- Tuổi máy suy ra từ giờ chạy ổ cứng, năm sản xuất tấm nền, ngày sản xuất pin
  có khớp nhau không
- Pin gần như mới trên máy có ổ chạy 20.000 giờ → pin vừa được thay để "làm đẹp" máy
- Ổ cứng chạy trên 60% toàn bộ thời gian kể từ ngày xuất xưởng → máy chạy 24/7,
  không phải máy cá nhân

**Bài kiểm tra thực hành:**

- Chạy tải nặng CPU/RAM/ổ đĩa rồi tự quét lại — vì mọi vấn đề tản nhiệt chỉ lộ khi máy nóng thật
- Tải nặng GPU 5 phút bằng compute shader Direct3D 11, tự chọn card rời trên laptop hai GPU.
  Cứ vài trăm mili giây, kết quả tính lại được so với kết quả chuẩn đến từng bit —
  card khoẻ không bao giờ lệch. Đo luôn hiệu năng phút cuối so với phút đầu để phát hiện tự hạ xung
- Kiểm tra VRAM ngay khi card còn nóng: ghi mẫu dữ liệu lên ~80% VRAM riêng, đọc lại so sánh
  theo kiểu MemTest, nhiều lượt với mẫu gốc và mẫu đảo bit. Lỗi VRAM là bằng chứng cứng, không cần nhìn bằng mắt
- Kiểm tra màn hình toàn màn hình: 8 bài theo thứ tự có chủ đích (đen → trắng → RGB → xám →
  chuyển sắc), có lưới chia vùng để soi điểm chết

**Checklist kiểm tra tay kèm công cụ chuyên dụng** — mỗi mục phần mềm không làm thay được
đều gắn đường dẫn tới trang chính thức của công cụ phù hợp (OCCT, FurMark 2, Superposition,
3DMark, GPU-Z, HWiNFO, Cinebench, Prime95, MemTest86, CrystalDiskInfo/Mark, EIZO Monitor Test,
TestUFO...), ghi rõ miễn phí hay trả phí và cần nhìn vào đâu trong kết quả.

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
    │   │   ├── Firmware/              #   SmbiosStructure
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
    ├── HardwareInspector.Firmware/    # đọc bảng SMBIOS thô (khe RAM, khe mở rộng, cổng)
    │   ├── Native/                    #   P/Invoke GetSystemFirmwareTable
    │   ├── Smbios/                    #   SmbiosReader
    │   └── Upgrade/                   #   khả năng nâng cấp: khe cắm, cổng, RAM tối đa
    │
    ├── HardwareInspector.Analysis/    # chấm điểm và kết luận
    │   ├── Scoring/                   #   ScoreCard
    │   ├── Analyzers/                 #   một analyzer cho mỗi linh kiện
    │   └── Trust/                     #   SecondHandTrustEngine — đối chiếu chéo
    │
    ├── HardwareInspector.Benchmark/   # tạo tải để bộc lộ vấn đề nhiệt; Gpu/ = compute shader D3D11
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
nên Windows sẽ tự hỏi. Không có quyền này thì mất S.M.A.R.T. và cảm biến nhiệt
— tức là mất phần lớn giá trị của công cụ.

**Driver cảm biến.** LibreHardwareMonitor nạp driver kernel WinRing0 (dịch vụ `R0HardwareInspector`).
Ứng dụng đặt file driver trong `%TEMP%` chứ không để cạnh file .exe, nên kể cả khi tiến trình bị dừng
giữa chừng (bấm Stop trong Visual Studio, End task) thì thư mục project vẫn xoá được bình thường.
Bản build cũ còn kẹt file `HardwareInspector.sys` trong thư mục `bin` thì chạy
`tools\unload-driver.cmd` bằng quyền Administrator để gỡ driver mà không cần khởi động lại máy.

### Thư viện ngoài

| Gói | Dùng để |
|---|---|
| `LibreHardwareMonitorLib` | Nhiệt độ, xung nhịp, quạt, điện áp, công suất |
| `System.Management` | Truy vấn WMI |
| `System.Diagnostics.EventLog` | Đọc nhật ký sự cố hệ thống |
| `Vortice.Direct3D11`, `Vortice.D3DCompiler` | Compute shader Direct3D 11 cho bài tải GPU và kiểm tra VRAM |

Còn lại đều dùng P/Invoke trực tiếp tới API Windows — SMBIOS, EDID
đều tự đọc và tự parse.

---

## Quy trình sử dụng khuyến nghị

1. **Bắt đầu kiểm tra** — quét toàn bộ, mất khoảng 20-40 giây
2. Đọc tab **Mua hay không** và **Đối chiếu chéo** — đây là chỗ lộ ra sự không trung thực
3. **Chạy tải nặng** 15 phút, rồi quay lại tab Linh kiện xem nhiệt độ dưới tải
4. **Kiểm tra màn hình** trong phòng tối
5. Làm hết tab **Kiểm tra tay** — danh sách này thay đổi theo những gì vừa phát hiện
6. **Xuất biên bản** để lưu hoặc đưa người bán xem

---

## Giới hạn cần biết

Phần mềm chạy trong Windows không thể:

- Phát hiện RAM lỗi nhẹ — cần MemTest86 từ USB
- Thấy artifact GPU — cần chạy tải đồ hoạ thật và nhìn bằng mắt
- Đánh giá tụ phồng, bản lề nứt, ẩm mốc, chất lượng nguồn

Thông tin khe cắm do bo mạch tự khai nên có thể thiếu hoặc lệch so với thực tế.
Vì vậy mọi kết luận đều kèm mức độ chắc chắn, và mọi báo cáo đều kết thúc
bằng checklist kiểm tra tay.
