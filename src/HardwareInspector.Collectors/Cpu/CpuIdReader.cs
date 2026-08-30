using System.Runtime.Intrinsics.X86;
using System.Text;
using HardwareInspector.Core.Models.Components;

namespace HardwareInspector.Collectors.Cpu;

/// <summary>
/// Đọc chữ ký CPU bằng lệnh CPUID.
///
/// Đây là điểm mấu chốt để chống đánh lừa. Tên CPU mà người dùng thấy trong
/// Task Manager hay dxdiag đến từ hai nguồn: chuỗi brand trong CPUID (nằm trong
/// silicon, được nạp từ vi mã của chính con chip) và trường Processor Version trong
/// SMBIOS Type 4 (do BIOS cung cấp, sửa được bằng phần mềm).
///
/// Người bán muốn biến một con i3 thế hệ 5 thành i7 thế hệ 9 sẽ sửa được SMBIOS,
/// nhưng Family/Model trong CPUID leaf 1 thì gắn với chính con silicon.
/// Đối chiếu hai thứ này là cách phát hiện.
/// </summary>
public static class CpuIdReader
{
    public static CpuIdSignature Read()
    {
        var sig = new CpuIdSignature();
        if (!X86Base.IsSupported) return sig;

        try
        {
            // Leaf 0: chuỗi vendor nằm trong EBX, EDX, ECX theo đúng thứ tự đó.
            var (maxLeaf, ebx0, ecx0, edx0) = X86Base.CpuId(0, 0);
            sig.VendorId = string.Concat(FourChars(ebx0), FourChars(edx0), FourChars(ecx0));

            // Leaf 1: signature + cờ tính năng
            var (eax1, ebx1, ecx1, edx1) = X86Base.CpuId(1, 0);
            sig.Stepping = eax1 & 0xF;
            sig.Model = (eax1 >> 4) & 0xF;
            sig.Family = (eax1 >> 8) & 0xF;
            sig.ExtendedModel = (eax1 >> 16) & 0xF;
            sig.ExtendedFamily = (eax1 >> 20) & 0xFF;

            // Quy tắc ghép của Intel và AMD: Family 0xF thì cộng thêm ExtendedFamily;
            // Model mở rộng áp dụng khi Family là 6 hoặc 15.
            sig.DisplayFamily = sig.Family == 0x0F ? sig.Family + sig.ExtendedFamily : sig.Family;
            sig.DisplayModel = sig.Family is 0x06 or 0x0F
                ? (sig.ExtendedModel << 4) + sig.Model
                : sig.Model;

            sig.LogicalProcessorsFromCpuId = (ebx1 >> 16) & 0xFF;
            sig.IsHypervisorPresent = ((ecx1 >> 31) & 1) == 1;
            _ = edx1;

            if (sig.IsHypervisorPresent) sig.HypervisorVendor = ReadHypervisorVendor();

            // Leaf 0x16: tần số cơ bản và tối đa, chỉ Intel từ Skylake trở đi.
            if (maxLeaf >= 0x16)
            {
                var (baseMhz, maxMhz, _, _) = X86Base.CpuId(0x16, 0);
                if (baseMhz is > 0 and < 10000) sig.BaseFrequencyMhz = baseMhz;
                if (maxMhz is > 0 and < 10000) sig.MaxFrequencyMhz = maxMhz;
            }

            sig.BrandString = ReadBrandString();
            sig.L3CacheKbFromCpuId = ReadL3CacheKb(sig.VendorId);
        }
        catch
        {
            // Nền tảng ARM hoặc môi trường chặn CPUID: trả về chữ ký rỗng,
            // các phép đối chiếu bên trên sẽ tự bỏ qua.
        }

        return sig;
    }

    /// <summary>Chuỗi tên đầy đủ nằm ở ba leaf mở rộng 0x80000002 đến 0x80000004.</summary>
    private static string ReadBrandString()
    {
        var (maxExtended, _, _, _) = X86Base.CpuId(unchecked((int)0x80000000), 0);
        if ((uint)maxExtended < 0x80000004) return string.Empty;

        var sb = new StringBuilder(48);
        for (var leaf = 0x80000002; leaf <= 0x80000004; leaf++)
        {
            var (a, b, c, d) = X86Base.CpuId(unchecked((int)leaf), 0);
            sb.Append(FourChars(a)).Append(FourChars(b)).Append(FourChars(c)).Append(FourChars(d));
        }

        return sb.ToString().Replace("\0", string.Empty).Trim();
    }

    private static string ReadHypervisorVendor()
    {
        try
        {
            var (_, b, c, d) = X86Base.CpuId(0x40000000, 0);
            return string.Concat(FourChars(b), FourChars(c), FourChars(d))
                .Replace("\0", string.Empty).Trim();
        }
        catch { return string.Empty; }
    }

    private static int ReadL3CacheKb(string vendor)
    {
        try
        {
            if (vendor.StartsWith("GenuineIntel", StringComparison.Ordinal))
            {
                // Leaf 4: duyệt từng cấp cache cho tới khi gặp cấp không tồn tại.
                for (var index = 0; index < 8; index++)
                {
                    var (eax, ebx, ecx, _) = X86Base.CpuId(4, index);
                    var type = eax & 0x1F;
                    if (type == 0) break;

                    var level = (eax >> 5) & 0x7;
                    if (level != 3) continue;

                    var ways = ((ebx >> 22) & 0x3FF) + 1;
                    var partitions = ((ebx >> 12) & 0x3FF) + 1;
                    var lineSize = (ebx & 0xFFF) + 1;
                    var sets = ecx + 1;
                    return (int)((long)ways * partitions * lineSize * sets / 1024);
                }
            }
            else if (vendor.StartsWith("AuthenticAMD", StringComparison.Ordinal))
            {
                var (_, _, _, edx) = X86Base.CpuId(unchecked((int)0x80000006), 0);
                var l3 = (edx >> 18) & 0x3FFF;   // đơn vị 512 KB
                return l3 * 512;
            }
        }
        catch { }
        return 0;
    }

    private static string FourChars(int register) => string.Concat(
        (char)(register & 0xFF),
        (char)((register >> 8) & 0xFF),
        (char)((register >> 16) & 0xFF),
        (char)((register >> 24) & 0xFF));
}
