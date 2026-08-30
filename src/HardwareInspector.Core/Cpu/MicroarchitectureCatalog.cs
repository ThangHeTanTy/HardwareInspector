namespace HardwareInspector.Core.Cpu;

/// <summary>
/// Bảng tra Family/Model của CPUID sang vi kiến trúc và thế hệ thương mại.
///
/// Bảng này là thước đo để đối chiếu với cái tên mà máy tự nhận. Một con chip
/// báo Family 6 Model 0x3D là Broadwell — tức thế hệ 5 — bất kể chuỗi tên
/// trong BIOS có ghi là i7-9700K hay không.
/// </summary>
public static class MicroarchitectureCatalog
{
    public sealed record Entry(string Name, int? Generation, int Year);

    /// <summary>Intel Family 6, tra theo DisplayModel.</summary>
    private static readonly Dictionary<int, Entry> Intel = new()
    {
        [0x1A] = new("Nehalem", 1, 2008),
        [0x1E] = new("Nehalem", 1, 2009),
        [0x25] = new("Westmere", 1, 2010),
        [0x2C] = new("Westmere", 1, 2010),
        [0x2A] = new("Sandy Bridge", 2, 2011),
        [0x2D] = new("Sandy Bridge-E", 2, 2011),
        [0x3A] = new("Ivy Bridge", 3, 2012),
        [0x3E] = new("Ivy Bridge-E", 3, 2013),
        [0x3C] = new("Haswell", 4, 2013),
        [0x45] = new("Haswell-U", 4, 2013),
        [0x46] = new("Haswell-H", 4, 2013),
        [0x3F] = new("Haswell-E", 4, 2014),
        [0x3D] = new("Broadwell", 5, 2014),
        [0x47] = new("Broadwell-H", 5, 2015),
        [0x4F] = new("Broadwell-E", 5, 2016),
        [0x56] = new("Broadwell-DE", 5, 2015),
        [0x4E] = new("Skylake-U/Y", 6, 2015),
        [0x5E] = new("Skylake-S/H", 6, 2015),
        [0x55] = new("Skylake-SP / Cascade Lake", null, 2017),
        [0x8E] = new("Kaby Lake / Coffee Lake / Whiskey Lake / Comet Lake (di động)", null, 2016),
        [0x9E] = new("Kaby Lake / Coffee Lake (để bàn)", null, 2017),
        [0xA5] = new("Comet Lake-S", 10, 2020),
        [0xA6] = new("Comet Lake-U", 10, 2020),
        [0x66] = new("Cannon Lake", 8, 2018),
        [0x7D] = new("Ice Lake", 10, 2019),
        [0x7E] = new("Ice Lake-U/Y", 10, 2019),
        [0x6A] = new("Ice Lake-SP", null, 2021),
        [0x8C] = new("Tiger Lake", 11, 2020),
        [0x8D] = new("Tiger Lake-H", 11, 2021),
        [0xA7] = new("Rocket Lake", 11, 2021),
        [0x97] = new("Alder Lake-S", 12, 2021),
        [0x9A] = new("Alder Lake-P/H", 12, 2022),
        [0xBE] = new("Alder Lake-N", 12, 2023),
        [0xB7] = new("Raptor Lake-S", 13, 2022),
        [0xBA] = new("Raptor Lake-P", 13, 2023),
        [0xBF] = new("Raptor Lake refresh", 14, 2023),
        [0xAA] = new("Meteor Lake-U/H", null, 2023),
        [0xAC] = new("Meteor Lake", null, 2023),
        [0xC6] = new("Arrow Lake", null, 2024),
        [0xBD] = new("Lunar Lake", null, 2024),
        // Dòng tiết kiệm điện
        [0x37] = new("Bay Trail (Atom)", null, 2013),
        [0x5C] = new("Apollo Lake (Atom)", null, 2016),
        [0x7A] = new("Gemini Lake (Atom)", null, 2017),
        [0x9C] = new("Jasper Lake (Atom)", null, 2021)
    };

    /// <summary>AMD tra theo DisplayFamily rồi tới khoảng DisplayModel.</summary>
    private static readonly Dictionary<int, string> AmdFamilies = new()
    {
        [0x15] = "Bulldozer / Piledriver / Excavator",
        [0x16] = "Jaguar / Puma",
        [0x17] = "Zen / Zen+ / Zen 2",
        [0x19] = "Zen 3 / Zen 4",
        [0x1A] = "Zen 5"
    };

    public static Entry? Lookup(string vendorId, int displayFamily, int displayModel)
    {
        if (vendorId.StartsWith("GenuineIntel", StringComparison.Ordinal))
        {
            if (displayFamily != 0x06) return null;
            return Intel.GetValueOrDefault(displayModel);
        }

        if (vendorId.StartsWith("AuthenticAMD", StringComparison.Ordinal))
            return AmdFamilies.TryGetValue(displayFamily, out var name)
                ? new Entry(name, null, 0)
                : null;

        return null;
    }

    /// <summary>
    /// Với hai model dùng chung cho nhiều thế hệ (0x8E và 0x9E trải từ thế hệ 7 tới 10),
    /// phải dựa thêm vào stepping để thu hẹp.
    /// </summary>
    public static int? RefineIntelGeneration(int displayModel, int stepping) => displayModel switch
    {
        0x9E => stepping switch
        {
            9 => 7,              // Kaby Lake-S
            10 or 11 => 8,       // Coffee Lake
            12 or 13 => 9,       // Coffee Lake refresh
            _ => null
        },
        0x8E => stepping switch
        {
            9 => 7,              // Kaby Lake-U
            10 => 8,             // Coffee Lake-U / Whiskey Lake
            11 => 8,             // Whiskey Lake
            12 => 10,            // Comet Lake-U
            _ => null
        },
        _ => null
    };
}
