using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HardwareInspector.App.Views.DeviceTest;

/// <summary>Một lần nhấn hoặc nhả phím, kèm mốc thời gian của chính bàn phím (ms).</summary>
public readonly record struct KeyEvent(int KeyId, bool Down, uint TimeMs);

/// <summary>
/// Hook bàn phím cấp thấp (WH_KEYBOARD_LL) dùng riêng cho bài thử bàn phím.
///
/// Không dùng sự kiện KeyDown của WPF vì: phím Windows mở Start, Print Screen chỉ báo khi nhả,
/// Alt và F10 bị nuốt làm phím menu, và WPF không phân biệt Enter chính với Enter bàn phím số.
/// Hook nhận mọi phím trước khi Windows xử lý, nên vừa đếm được đủ, vừa chặn được để
/// người dùng bấm phím Windows hay Alt+Tab mà không bị văng khỏi bài thử.
/// Ctrl+Alt+Del thì Windows không cho ứng dụng nào chặn — đó là chủ ý của Windows.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;
    private const uint LlkhfExtended = 0x01;

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr hMod, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    // Giữ tham chiếu tới delegate: nếu để GC thu hồi, Windows sẽ gọi vào vùng nhớ đã giải phóng.
    private readonly HookProc _proc;
    private IntPtr _hook;

    public event Action<KeyEvent>? Key;

    public KeyboardHook() => _proc = Callback;

    public bool IsActive => _hook != IntPtr.Zero;

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        using var module = Process.GetCurrentProcess().MainModule;
        _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(module?.ModuleName), 0);
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0) return CallNextHookEx(_hook, code, wParam, lParam);

        var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
        var message = (int)wParam;
        var down = message is WmKeyDown or WmSysKeyDown;
        var up = message is WmKeyUp or WmSysKeyUp;

        if (down || up)
        {
            try { Key?.Invoke(new KeyEvent(KeyIds.From(data.VkCode, data.ScanCode, (data.Flags & LlkhfExtended) != 0), down, data.Time)); }
            catch { /* lỗi vẽ giao diện không được làm kẹt bàn phím của cả máy */ }
        }

        // Chặn phím: bài thử nhận đủ, còn Windows không mở Start hay chuyển cửa sổ.
        return (IntPtr)1;
    }

    public void Dispose() => Stop();
}

/// <summary>
/// Mã định danh phím = mã phím ảo, cộng 0x100 cho những phím mà cờ "extended" mới phân biệt được:
/// cụm điều hướng với phím số khi tắt NumLock, Enter chính với Enter bàn phím số.
/// </summary>
public static class KeyIds
{
    public const int Ext = 0x100;

    private static readonly HashSet<uint> ExtendedMatters = new()
    {
        0x0D, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E
    };

    public static int From(uint vk, uint scanCode, bool extended)
    {
        // Phòng khi driver báo mã chung thay vì mã trái/phải.
        vk = vk switch
        {
            0x10 => scanCode == 0x36 ? 0xA1u : 0xA0u,   // Shift
            0x11 => extended ? 0xA3u : 0xA2u,           // Ctrl
            0x12 => extended ? 0xA5u : 0xA4u,           // Alt
            _ => vk
        };

        return ExtendedMatters.Contains(vk) && extended ? (int)vk | Ext : (int)vk;
    }
}
