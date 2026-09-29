# Kiến trúc

## Sơ đồ phụ thuộc

```
                    ┌──────────────────┐
                    │       Core       │  models, enums, interfaces
                    └────────┬─────────┘  (không phụ thuộc gì)
          ┌──────────┬───────┼────────┬──────────┬──────────┐
          ▼          ▼       ▼        ▼          ▼          ▼
    Collectors   Sensors  Analysis  Benchmark  Reporting  Firmware
          │                                                  │
          └──────────────────────────────────────────────────┘
                              ▲
                              │  Firmware phụ thuộc Collectors (dùng WmiQuery)
                    ┌─────────┴─────────┐
                    │        App        │  WPF, MVVM
                    └───────────────────┘
```

Quy tắc: **mọi thứ phụ thuộc vào Core, Core không phụ thuộc vào gì cả.**
Không có phụ thuộc vòng. Lớp App là nơi duy nhất biết về tất cả các lớp khác.

Hệ quả thực tế của quy tắc này: mọi logic thuần tuý — bảng tra, phép so sánh,
quy đổi định dạng — đều phải nằm ở `Core`, kể cả khi nó "có vẻ" thuộc về nơi khác.
`CpuNameParser` và `MicroarchitectureCatalog` là ví dụ: chúng phục vụ `Collectors`
lúc thu thập và `Analysis` lúc đối chiếu, nên chỗ duy nhất đặt được là `Core`.
Đặt ở `Collectors` sẽ buộc `Analysis` phải tham chiếu ngược lên tầng thu thập,
phá vỡ ranh giới "Analysis không bao giờ chạm phần cứng".

Tương tự với chiều ngược lại: `SetWindowPos` để đặt cửa sổ kiểm tra màu lên đúng
màn hình là việc của tầng giao diện, nên nó sống ở `App/Services/MonitorPositioner.cs`
chứ không phải ở `Collectors/Native`.

## Vai trò từng project

| Project | Trách nhiệm | Không được làm |
|---|---|---|
| `Core` | Model dữ liệu, enum, interface, tiện ích format, bảng tra vi kiến trúc CPU | Gọi WMI, đọc registry, chạm UI |
| `Collectors` | Đọc dữ liệu thô: WMI, registry, S.M.A.R.T., EDID, Event Log | Đánh giá, chấm điểm |
| `Sensors` | Đo nhiệt độ, xung nhịp, quạt qua LibreHardwareMonitor | Kết luận về sức khoẻ |
| `Firmware` | SMBIOS thô, bảng ACPI, biến UEFI, TPM, quét ESP | Chấm điểm linh kiện khác |
| `Analysis` | Biến dữ liệu thô thành đánh giá, điểm số, cảnh báo, kết luận tin cậy | Đọc dữ liệu trực tiếp từ hệ thống |
| `Benchmark` | Tạo tải để bộc lộ vấn đề nhiệt; ghi lại dữ kiện thô của bài GPU (`GpuTestResult`) | Đánh giá kết quả |
| `Reporting` | Xuất HTML và JSON | Thu thập hay phân tích |
| `App` | Giao diện WPF, điều phối pipeline | Chứa logic nghiệp vụ |

Ranh giới quan trọng nhất: **Analysis không bao giờ gọi WMI.**
Nó chỉ đọc từ `SystemSnapshot`. Nhờ đó có thể viết unit test cho toàn bộ logic đánh giá
bằng cách dựng snapshot giả, không cần máy thật.

## Luồng chạy một lượt kiểm tra

```
InspectionPipeline.RunAsync()
   │
   ├─ 1. BaseboardCollector      → SMBIOS qua WMI, BIOS, OS, giấy phép
   ├─ 2. ProcessorCollector      → CPU, microcode từ registry
   ├─ 3. GraphicsCollector       → GPU, VRAM thật từ registry driver
   ├─ 4. MemoryCollector         → từng thanh RAM, suy ra cấu hình kênh
   ├─ 5. StorageCollector        → ổ đĩa + S.M.A.R.T. (ATA hoặc NVMe)
   ├─ 6. BatteryCollector        → ghép 3 nguồn WMI để có chu kỳ + độ chai
   ├─ 7. DisplayCollector        → EDID thô từ registry, tự parse
   ├─ 8. ReliabilityScanner      → WHEA, disk error, bugcheck 90 ngày
   ├─ 9. FirmwareIntegrityService→ chạy 8 phép kiểm tra firmware
   └─ 10. SensorSnapshotCollector→ chốt min/max/avg của phiên đo
   │
   ▼
SystemSnapshot  (dữ liệu thô, không có đánh giá)
   │
   ▼
MachineAssessmentService.Assess()
   │
   ├─ 8 analyzer chạy độc lập → ComponentAssessment mỗi cái
   ├─ Tính điểm tổng có trọng số
   └─ SecondHandTrustEngine    → đối chiếu chéo, kết luận tin cậy
   │
   ▼
MachineAssessment  (thứ giao diện và báo cáo tiêu thụ)
```

## Thêm một phép kiểm tra firmware mới

1. Tạo lớp trong `Firmware/Checks/` cài `IFirmwareCheck`.
2. Đăng ký vào danh sách trong `FirmwareIntegrityService`.
3. Đặt `RequiresAdmin = true` nếu cần quyền — service sẽ tự bỏ qua khi thiếu quyền
   và trạng thái tổng sẽ là `Unverified` thay vì `Clean`.

Đặt `Confidence` trung thực. Đây là điều quan trọng nhất: một phép kiểm tra
đặt confidence 1.0 cho một suy đoán sẽ làm hỏng độ tin cậy của cả công cụ.

## Thêm một analyzer linh kiện mới

1. Cài `IComponentAnalyzer` trong `Analysis/Analyzers/`.
2. Dùng `ScoreCard` để gom metric và finding — nó tự lo phần chấm điểm và quy đổi rating.
3. Đăng ký vào `MachineAssessmentService` và thêm trọng số vào bảng `Weights`.

## Yêu cầu chạy

- .NET 8 SDK, Windows 10 1809 trở lên
- Chạy quyền Administrator (đã khai báo trong `app.manifest`)
- `dotnet restore` cần mạng để tải `LibreHardwareMonitorLib`, `System.Management` và `Vortice.Direct3D11`
- Bài kiểm tra GPU cần card hỗ trợ Direct3D 11 (feature level 11_0); shader biên dịch lúc chạy bằng `d3dcompiler_47.dll` có sẵn trong Windows

```bash
dotnet restore
dotnet build -c Release
dotnet run --project src/HardwareInspector.App
```
