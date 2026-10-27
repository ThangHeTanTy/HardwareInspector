namespace HardwareInspector.Core.Models.Firmware;

/// <summary>Một cấu trúc SMBIOS thô đã được tách header + chuỗi.</summary>
public sealed class SmbiosStructure
{
    public byte Type { get; init; }
    public byte Length { get; init; }
    public ushort Handle { get; init; }
    public byte[] Data { get; init; } = Array.Empty<byte>();
    public List<string> Strings { get; } = new();

    public string GetString(int index) =>
        index > 0 && index <= Strings.Count ? Strings[index - 1] : string.Empty;
}
