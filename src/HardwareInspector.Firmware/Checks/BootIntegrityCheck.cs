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
    public string Title => S("Cấu hình khởi động Windows", "Windows boot configuration");
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
                Title = S("Chế độ Test Signing đang bật",
"Test Signing mode is on"),
                Detail = S("Khi bật Test Signing, Windows nạp cả driver ký bằng chứng chỉ tự tạo. " +
                         "Đây là điều kiện cần để chạy driver kernel không có chữ ký của Microsoft.",
"With Test Signing on, Windows also loads drivers signed with self-made certificates. " +
                         "It is a prerequisite for running kernel drivers not signed by Microsoft."),
                Recommendation = S("Chạy 'bcdedit /set testsigning off' rồi khởi động lại. " +
                                 "Trước đó hãy rà soát các driver đang nạp bằng autoruns hoặc driverquery.",
"Run 'bcdedit /set testsigning off' and restart. " +
                                 "Before that, review loaded drivers with autoruns or driverquery."),
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
                Title = S("Kiểm tra toàn vẹn driver đã bị tắt",
"Driver integrity checks are disabled"),
                Detail = S("Cờ nointegritychecks vô hiệu hoá hoàn toàn việc xác thực chữ ký driver kernel.",
"The nointegritychecks flag completely disables kernel driver signature verification."),
                Recommendation = S("Chạy 'bcdedit /set nointegritychecks off' và cài lại Windows nếu không rõ nguồn gốc máy.",
"Run 'bcdedit /set nointegritychecks off' and reinstall Windows if the machine's history is unknown."),
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
                Title = S("Kernel debugger đang được bật",
"The kernel debugger is enabled"),
                Detail = S("Máy cho phép gắn trình gỡ lỗi vào nhân hệ điều hành, đọc và sửa được bộ nhớ kernel.",
"The machine allows a debugger to attach to the OS kernel and read and modify kernel memory."),
                Recommendation = S("Chạy 'bcdedit /debug off' nếu bạn không chủ động phát triển driver.",
"Run 'bcdedit /debug off' unless you are actively developing drivers."),
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
                Title = S("Không có bảo vệ DMA ở mức nhân",
"No kernel-level DMA protection"),
                Detail = S("Thiếu Kernel DMA Protection nghĩa là thiết bị cắm qua Thunderbolt/USB4 có thể " +
                         "đọc thẳng bộ nhớ hệ thống. Nhiều máy đời cũ vốn không hỗ trợ tính năng này.",
"Without Kernel DMA Protection, devices plugged into Thunderbolt/USB4 can " +
                         "read system memory directly. Many older machines simply do not support it."),
                Recommendation = S("Bật VT-d/AMD-Vi trong BIOS nếu máy có hỗ trợ.",
"Enable VT-d/AMD-Vi in the BIOS if the machine supports it."),
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
                Title = S("Có TPM nhưng không tìm thấy nhật ký Measured Boot",
"TPM present but no Measured Boot log found"),
                Detail = S("Measured Boot ghi lại băm của từng thành phần khởi động vào TPM. " +
                         "Không có nhật ký thì không thể chứng minh chuỗi khởi động chưa bị thay đổi.",
"Measured Boot records a hash of each boot component in the TPM. " +
                         "Without the log there is no way to prove the boot chain is unchanged."),
                Recommendation = S("Bật BitLocker hoặc Device Health Attestation để kích hoạt Measured Boot.",
"Enable BitLocker or Device Health Attestation to activate Measured Boot."),
                Source = EvidenceSource.FileSystem,
                ScorePenalty = 5,
                Confidence = 0.6
            };
    }
}
