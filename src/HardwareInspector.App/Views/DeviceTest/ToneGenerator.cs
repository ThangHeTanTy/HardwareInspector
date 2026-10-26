using System.IO;
using System.Media;

namespace HardwareInspector.App.Views.DeviceTest;

/// <summary>
/// Tạo và phát âm thử ngay trong bộ nhớ, không cần file âm thanh đi kèm.
/// Âm 1 kHz phát riêng từng kênh để nghe loa trái/phải; tiếng quét 80 Hz → 1,2 kHz
/// để nghe loa có rè hay lẹt xẹt ở dải trầm không — chỗ loa laptop hay hỏng nhất.
/// </summary>
public static class ToneGenerator
{
    public enum Channel { Left, Right, Both }

    private const int SampleRate = 44100;
    private static SoundPlayer? _player;   // giữ lại để GC không thu hồi giữa chừng lúc đang phát

    public static void PlayTone(Channel channel, double frequency = 1000, double seconds = 1.2) =>
        Play(Build(seconds, t => Math.Sin(2 * Math.PI * frequency * t), channel));

    public static void PlaySweep(double seconds = 3.5)
    {
        const double f0 = 80, f1 = 1200;
        // Quét theo hàm mũ để mỗi quãng tám chiếm thời gian như nhau, tai nghe đều hơn.
        var k = Math.Log(f1 / f0) / seconds;
        Play(Build(seconds, t => Math.Sin(2 * Math.PI * f0 * (Math.Exp(k * t) - 1) / k), Channel.Both));
    }

    public static void Stop()
    {
        try { _player?.Stop(); } catch { }
    }

    private static byte[] Build(double seconds, Func<double, double> wave, Channel channel)
    {
        var frames = (int)(SampleRate * seconds);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        var dataBytes = frames * 4;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + dataBytes); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)2);
        w.Write(SampleRate); w.Write(SampleRate * 4); w.Write((short)4); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(dataBytes);

        var fade = (int)(SampleRate * 0.02);
        for (var i = 0; i < frames; i++)
        {
            // Vào/ra nhỏ dần 20 ms để không có tiếng "bụp" ở đầu và cuối.
            var envelope = Math.Min(1.0, Math.Min(i, frames - 1 - i) / (double)fade);
            var v = (short)(wave(i / (double)SampleRate) * envelope * 0.6 * short.MaxValue);
            w.Write(channel == Channel.Right ? (short)0 : v);
            w.Write(channel == Channel.Left ? (short)0 : v);
        }

        w.Flush();
        return ms.ToArray();
    }

    private static void Play(byte[] wav)
    {
        Stop();
        _player = new SoundPlayer(new MemoryStream(wav));
        _player.Play();
    }
}
