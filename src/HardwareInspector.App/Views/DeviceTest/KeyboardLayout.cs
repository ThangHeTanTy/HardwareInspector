namespace HardwareInspector.App.Views.DeviceTest;

/// <summary>Một phím trên hình bàn phím: nhãn, bề rộng (đơn vị 1 phím) và các mã có thể sinh ra nó.</summary>
public sealed record KeyDef(string Label, double Width, params int[] Ids)
{
    /// <summary>Ô trống để căn lề, không phải phím.</summary>
    public bool IsGap => Ids.Length == 0;

    public static KeyDef Gap(double width) => new(string.Empty, width);
}

/// <summary>
/// Bố cục bàn phím laptop phổ biến (không có cụm phím số), thêm cụm phím số tuỳ chọn cho máy 15–17 inch.
/// Laptop mỗi hãng xếp cụm điều hướng một kiểu, nên phím nào máy không có thì người dùng
/// bấm chuột phải để đánh dấu "không có", phím đó không bị tính là chưa thử.
/// </summary>
public static class KeyboardLayout
{
    private const int E = KeyIds.Ext;

    private static KeyDef K(string label, int vk, double width = 1) => new(label, width, vk);
    private static KeyDef L(char c) => new(c.ToString(), 1, c);

    public static IReadOnlyList<IReadOnlyList<KeyDef>> Main { get; } = new List<IReadOnlyList<KeyDef>>
    {
        new[] { K("Esc", 0x1B), KeyDef.Gap(0.25),
                K("F1", 0x70), K("F2", 0x71), K("F3", 0x72), K("F4", 0x73), KeyDef.Gap(0.25),
                K("F5", 0x74), K("F6", 0x75), K("F7", 0x76), K("F8", 0x77), KeyDef.Gap(0.25),
                K("F9", 0x78), K("F10", 0x79), K("F11", 0x7A), K("F12", 0x7B), KeyDef.Gap(0.25),
                K("PrtSc", 0x2C), K("Del", 0x2E | E) },
        new[] { K("`", 0xC0), L('1'), L('2'), L('3'), L('4'), L('5'), L('6'), L('7'), L('8'), L('9'), L('0'),
                K("-", 0xBD), K("=", 0xBB), K("⌫ Backspace", 0x08, 2) },
        new[] { K("Tab", 0x09, 1.5), L('Q'), L('W'), L('E'), L('R'), L('T'), L('Y'), L('U'), L('I'), L('O'), L('P'),
                K("[", 0xDB), K("]", 0xDD), K("\\", 0xDC, 1.5) },
        new[] { K("Caps", 0x14, 1.75), L('A'), L('S'), L('D'), L('F'), L('G'), L('H'), L('J'), L('K'), L('L'),
                K(";", 0xBA), K("'", 0xDE), K("Enter", 0x0D, 2.25) },
        new[] { K("Shift", 0xA0, 2.25), L('Z'), L('X'), L('C'), L('V'), L('B'), L('N'), L('M'),
                K(",", 0xBC), K(".", 0xBE), K("/", 0xBF), K("Shift", 0xA1, 1.75), K("↑", 0x26 | E) },
        new[] { K("Ctrl", 0xA2, 1.25), K("Win", 0x5B, 1.25), K("Alt", 0xA4, 1.25), K("Space", 0x20, 5.5),
                K("Alt", 0xA5, 1.25), K("Menu", 0x5D), K("Ctrl", 0xA3, 1.25),
                K("←", 0x25 | E), K("↓", 0x28 | E), K("→", 0x27 | E) },
    };

    /// <summary>Cụm điều hướng, đặt cạnh khối chính.</summary>
    public static IReadOnlyList<IReadOnlyList<KeyDef>> Navigation { get; } = new List<IReadOnlyList<KeyDef>>
    {
        new[] { K("Ins", 0x2D | E), K("Home", 0x24 | E), K("PgUp", 0x21 | E) },
        new[] { K("End", 0x23 | E), K("PgDn", 0x22 | E), K("Pause", 0x13) },
    };

    /// <summary>Phím số: mỗi phím nhận cả mã khi bật NumLock lẫn mã khi tắt NumLock.</summary>
    public static IReadOnlyList<IReadOnlyList<KeyDef>> Numpad { get; } = new List<IReadOnlyList<KeyDef>>
    {
        new[] { K("Num", 0x90), K("/", 0x6F), K("*", 0x6A), K("-", 0x6D) },
        new[] { new KeyDef("7", 1, 0x67, 0x24), new KeyDef("8", 1, 0x68, 0x26), new KeyDef("9", 1, 0x69, 0x21), K("+", 0x6B) },
        new[] { new KeyDef("4", 1, 0x64, 0x25), new KeyDef("5", 1, 0x65, 0x0C), new KeyDef("6", 1, 0x66, 0x27), K("Enter", 0x0D | E) },
        new[] { new KeyDef("1", 1, 0x61, 0x23), new KeyDef("2", 1, 0x62, 0x28), new KeyDef("3", 1, 0x63, 0x22) },
        new[] { new KeyDef("0", 2, 0x60, 0x2D), new KeyDef(".", 1, 0x6E, 0x2E) },
    };
}
