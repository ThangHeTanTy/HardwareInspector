using System.Runtime.InteropServices;

namespace HardwareInspector.App.Views.DeviceTest;

/// <summary>
/// Đo mức âm thanh micro mặc định bằng API waveIn của Windows, không cần thư viện ngoài.
///
/// Dùng chế độ không callback rồi tự hỏi bộ đệm đã đầy chưa trên luồng giao diện, nhờ đó
/// không có luồng nào của driver chạm vào đối tượng .NET. Hai bộ đệm 50 ms xoay vòng.
/// </summary>
public sealed class MicrophoneMeter : IDisposable
{
    private const int SampleRate = 44100;
    private const int BufferSamples = SampleRate / 20;
    private const uint WhdrDone = 0x01, WhdrPrepared = 0x02;
    private const uint WaveMapper = unchecked((uint)-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormatEx
    {
        public ushort FormatTag, Channels;
        public uint SamplesPerSec, AvgBytesPerSec;
        public ushort BlockAlign, BitsPerSample, Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHdr
    {
        public IntPtr Data;
        public uint BufferLength, BytesRecorded;
        public IntPtr User;
        public uint Flags, Loops;
        public IntPtr Next, Reserved;
    }

    [DllImport("winmm.dll")] private static extern int waveInGetNumDevs();
    [DllImport("winmm.dll")] private static extern int waveInOpen(out IntPtr handle, uint deviceId, ref WaveFormatEx format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern int waveInPrepareHeader(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveInUnprepareHeader(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveInAddBuffer(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveInStart(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int waveInReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int waveInClose(IntPtr handle);

    private static readonly int HeaderSize = Marshal.SizeOf<WaveHdr>();
    private readonly IntPtr[] _headers = new IntPtr[2];
    private readonly IntPtr[] _buffers = new IntPtr[2];
    private IntPtr _handle;

    public static int DeviceCount
    {
        get { try { return waveInGetNumDevs(); } catch { return 0; } }
    }

    public bool IsRunning => _handle != IntPtr.Zero;

    /// <summary>Mở micro mặc định. Trả lỗi dạng chữ nếu không mở được (không có micro, bị chặn quyền riêng tư).</summary>
    public string? Start()
    {
        if (IsRunning) return null;

        var format = new WaveFormatEx
        {
            FormatTag = 1, Channels = 1, SamplesPerSec = SampleRate, AvgBytesPerSec = SampleRate * 2,
            BlockAlign = 2, BitsPerSample = 16, Size = 0
        };

        var rc = waveInOpen(out _handle, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, 0);
        if (rc != 0)
        {
            _handle = IntPtr.Zero;
            return $"waveInOpen = {rc}";
        }

        for (var i = 0; i < 2; i++)
        {
            _buffers[i] = Marshal.AllocHGlobal(BufferSamples * 2);
            _headers[i] = Marshal.AllocHGlobal(HeaderSize);
            Marshal.StructureToPtr(new WaveHdr { Data = _buffers[i], BufferLength = BufferSamples * 2 }, _headers[i], false);
            waveInPrepareHeader(_handle, _headers[i], HeaderSize);
            waveInAddBuffer(_handle, _headers[i], HeaderSize);
        }

        waveInStart(_handle);
        return null;
    }

    /// <summary>Lấy mức đỉnh của các bộ đệm vừa đầy (0..1), rồi trả bộ đệm lại cho driver. Null nếu chưa có gì mới.</summary>
    public double? Poll()
    {
        if (!IsRunning) return null;
        double? peak = null;

        for (var i = 0; i < 2; i++)
        {
            var hdr = Marshal.PtrToStructure<WaveHdr>(_headers[i]);
            if ((hdr.Flags & WhdrDone) == 0) continue;

            var samples = (int)hdr.BytesRecorded / 2;
            var max = 0;
            for (var s = 0; s < samples; s++)
                max = Math.Max(max, Math.Abs((int)Marshal.ReadInt16(hdr.Data, s * 2)));
            peak = Math.Max(peak ?? 0, max / 32768.0);

            hdr.Flags = WhdrPrepared;
            hdr.BytesRecorded = 0;
            Marshal.StructureToPtr(hdr, _headers[i], false);
            waveInAddBuffer(_handle, _headers[i], HeaderSize);
        }

        return peak;
    }

    public void Stop()
    {
        if (!IsRunning) return;
        waveInReset(_handle);
        for (var i = 0; i < 2; i++)
        {
            waveInUnprepareHeader(_handle, _headers[i], HeaderSize);
            Marshal.FreeHGlobal(_headers[i]);
            Marshal.FreeHGlobal(_buffers[i]);
            _headers[i] = _buffers[i] = IntPtr.Zero;
        }
        waveInClose(_handle);
        _handle = IntPtr.Zero;
    }

    public void Dispose() => Stop();
}
