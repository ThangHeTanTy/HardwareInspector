using System.Management;

namespace HardwareInspector.Collectors.Wmi;

/// <summary>
/// Lớp bọc mỏng quanh WMI. Mọi truy vấn đều được bọc try/catch vì nhiều namespace
/// (root\WMI, root\CIMV2\Security) chỉ trả dữ liệu khi chạy quyền Administrator.
/// </summary>
public static class WmiQuery
{
    public const string CimV2 = @"root\CIMV2";
    public const string WmiNamespace = @"root\WMI";
    public const string StorageNamespace = @"root\Microsoft\Windows\Storage";
    public const string SecurityCenter = @"root\Microsoft\Windows\DeviceGuard";
    public const string TpmNamespace = @"root\CIMV2\Security\MicrosoftTpm";

    public static IEnumerable<ManagementObject> Query(string query, string scope = CimV2)
    {
        ManagementObjectCollection? results = null;
        try
        {
            using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(query));
            results = searcher.Get();
        }
        catch
        {
            yield break;
        }

        foreach (ManagementObject mo in results)
            yield return mo;
    }

    public static ManagementObject? QuerySingle(string query, string scope = CimV2) =>
        Query(query, scope).FirstOrDefault();

    public static string Str(this ManagementBaseObject mo, string property)
    {
        try { return mo[property]?.ToString()?.Trim() ?? string.Empty; }
        catch { return string.Empty; }
    }

    public static int Int(this ManagementBaseObject mo, string property, int fallback = 0)
    {
        try { return mo[property] is null ? fallback : Convert.ToInt32(mo[property]); }
        catch { return fallback; }
    }

    public static long Long(this ManagementBaseObject mo, string property, long fallback = 0)
    {
        try { return mo[property] is null ? fallback : Convert.ToInt64(mo[property]); }
        catch { return fallback; }
    }

    public static bool Bool(this ManagementBaseObject mo, string property, bool fallback = false)
    {
        try { return mo[property] is null ? fallback : Convert.ToBoolean(mo[property]); }
        catch { return fallback; }
    }

    public static byte[]? Bytes(this ManagementBaseObject mo, string property)
    {
        try { return mo[property] as byte[]; }
        catch { return null; }
    }

    /// <summary>WMI trả ngày theo định dạng DMTF: yyyyMMddHHmmss.ffffff+UUU.</summary>
    public static DateTime? Date(this ManagementBaseObject mo, string property)
    {
        var raw = mo.Str(property);
        if (string.IsNullOrWhiteSpace(raw) || raw.Length < 14) return null;
        try { return ManagementDateTimeConverter.ToDateTime(raw); }
        catch { return null; }
    }
}
