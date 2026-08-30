using HardwareInspector.Core.Models;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Firmware.Checks;

/// <summary>
/// Kiểm tra các công tắc mà Windows cung cấp để nới lỏng kiểm tra chữ ký driver.
/// Một máy dùng bình thường không cần bật bất kỳ công tắc nào trong số này.
/// </summary>
public sealed class BootIntegrityCheck : IFirmwareCheck
{
    public string Id => "FW-BOOT";
    public string Title => "Cấu hình khởi động Windows";
    public bool RequiresAdmin => true;

    public IEnumerable<Finding> Run(FirmwareContext ctx)
    {
        var boot = ctx.BootConfig;

        if (boot.TestSigningEnabled)
            yield return new Finding
            {
                Code = "FW-BOOT-001",
                Component = ComponentKind.Firmware,
                Severity = Severity.Critical,
                Title = "Chế độ Test Signing đang bật",
                Detail = "Khi bật Test Signing, Windows nạp cả driver ký bằng chứng chỉ tự tạo. " +
                         "Đây là điều kiện cần để chạy driver kernel không có chữ ký của Microsoft.",
                Recommendation = "Chạy 'bcdedit /set testsigning off' rồi khởi động lại. " +
                                 "Trước đó hãy rà soát các driver đang nạp bằng autoruns hoặc driverquery.",
                Source = EvidenceSource.Registry,
                ScorePenalty = 30,
                Confidence = 0.95
            };

        if (boot.IntegrityChecksDisabled)
            yield return new Finding
            {
                Code = "FW-BOOT-002",
                Component = ComponentKind.Firmware,
                Severity = Severity.Critical,
                Title = "Kiểm tra toàn vẹn driver đã bị tắt",
                Detail = "Cờ nointegritychecks vô hiệu hoá hoàn toàn việc xác thực chữ ký driver kernel.",
                Recommendation = "Chạy 'bcdedit /set nointegritychecks off' và cài lại Windows nếu không rõ nguồn gốc máy.",
                Source = EvidenceSource.Registry,
                ScorePenalty = 35,
                Confidence = 0.95
            };

        if (boot.DebuggerEnabled)
            yield return new Finding
            {
                Code = "FW-BOOT-003",
                Component = ComponentKind.Firmware,
                Severity = Severity.Warning,
                Title = "Kernel debugger đang được bật",
                Detail = "Máy cho phép gắn trình gỡ lỗi vào nhân hệ điều hành, đọc và sửa được bộ nhớ kernel.",
                Recommendation = "Chạy 'bcdedit /debug off' nếu bạn không chủ động phát triển driver.",
                Source = EvidenceSource.Registry,
                ScorePenalty = 20,
                Confidence = 0.9
            };

        if (!ctx.DeviceGuard.KernelDmaProtection && ctx.Snapshot.IsLaptop)
            yield return new Finding
            {
                Code = "FW-BOOT-004",
                Component = ComponentKind.Firmware,
                Severity = Severity.Notice,
                Title = "Không có bảo vệ DMA ở mức nhân",
                Detail = "Thiếu Kernel DMA Protection nghĩa là thiết bị cắm qua Thunderbolt/USB4 có thể " +
                         "đọc thẳng bộ nhớ hệ thống. Nhiều máy đời cũ vốn không hỗ trợ tính năng này.",
                Recommendation = "Bật VT-d/AMD-Vi trong BIOS nếu máy có hỗ trợ.",
                Source = EvidenceSource.Wmi,
                ScorePenalty = 4,
                Confidence = 0.7
            };

        if (ctx.Tpm.IsPresent && !ctx.Tpm.MeasuredBootLogPresent)
            yield return new Finding
            {
                Code = "FW-BOOT-005",
                Component = ComponentKind.Firmware,
                Severity = Severity.Notice,
                Title = "Có TPM nhưng không tìm thấy nhật ký Measured Boot",
                Detail = "Measured Boot ghi lại băm của từng thành phần khởi động vào TPM. " +
                         "Không có nhật ký thì không thể chứng minh chuỗi khởi động chưa bị thay đổi.",
                Recommendation = "Bật BitLocker hoặc Device Health Attestation để kích hoạt Measured Boot.",
                Source = EvidenceSource.FileSystem,
                ScorePenalty = 5,
                Confidence = 0.6
            };
    }
}
