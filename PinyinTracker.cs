using System.Runtime.InteropServices;
using System.Text;

namespace GlintWin;

/// <summary>
/// 低级键盘钩子拼音追踪器（WH_KEYBOARD_LL，专用线程 + 消息泵）。
/// 隐私边界：仅实时记录 A-Z 字母序列（退格可修正），空格/回车/ESC 立即清空，
/// Ctrl/Alt 组合键清空；不记录其他按键、不持久化、不上传。源码可审计。
/// 用途：①微软拼音通道的「正在组词」触发信号；②权威拼音，用于候选词交叉验证与纠错。
/// </summary>
public static class PinyinTracker
{
    private static readonly object Gate = new();
    private static readonly StringBuilder Buffer = new();
    private static long _lastKeyTicks;
    private static IntPtr _hook;
    private static bool _started;
    private static volatile bool _paused;

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private delegate IntPtr LowLevelHookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode; public uint scanCode; public uint flags; public uint time; public IntPtr extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam;
        public uint time; public int ptX; public int ptY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int idHook, LowLevelHookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")]
    private static extern int GetMessageW(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG msg);

    public static void Start()
    {
        if (_started) return;
        _started = true;
        var t = new Thread(() =>
        {
            _hook = SetWindowsHookExW(WH_KEYBOARD_LL, HookCallback, GetModuleHandleW(null), 0);
            if (_hook == IntPtr.Zero)
            {
                Logger.Warn("keyboard hook install failed — 微软拼音通道退化为不可用");
                return;
            }
            while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
        })
        { IsBackground = true, Name = "glint-keyhook" };
        t.Start();
    }

    public static void SetPaused(bool paused)
    {
        _paused = paused;
        if (paused) Clear();
    }

    /// <summary>1500ms 内有字母输入（组词进行中）。</summary>
    public static bool IsFresh
    {
        get { lock (Gate) return Buffer.Length > 0 && Environment.TickCount64 - _lastKeyTicks < 1500; }
    }

    /// <summary>当前拼音串（字母，小写）；过期返回 null。</summary>
    public static string? CurrentPinyin
    {
        get
        {
            lock (Gate)
                return Buffer.Length > 0 && Environment.TickCount64 - _lastKeyTicks < 1500
                    ? Buffer.ToString()
                    : null;
        }
    }

    private static void Clear()
    {
        lock (Gate)
        {
            Buffer.Clear();
            _lastKeyTicks = 0;
        }
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !_paused)
        {
            try
            {
                int msg = wParam.ToInt32();
                if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    uint vk = kb.vkCode;
                    bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0;
                    bool alt = (GetAsyncKeyState(0x12) & 0x8000) != 0;
                    if (ctrl || alt)
                    {
                        Clear();
                    }
                    else if (vk is >= 0x41 and <= 0x5A)
                    {
                        lock (Gate)
                        {
                            if (Buffer.Length < 24) Buffer.Append((char)('a' + (vk - 0x41)));
                            _lastKeyTicks = Environment.TickCount64;
                        }
                    }
                    else if (vk == 0x08) // 退格：撤掉最后一个字母
                    {
                        lock (Gate)
                        {
                            if (Buffer.Length > 0) Buffer.Length--;
                            _lastKeyTicks = Environment.TickCount64;
                        }
                    }
                    else if (vk is 0x20 or 0x0D or 0x1B) // 空格/回车/ESC：上屏或取消
                    {
                        Clear();
                    }
                }
            }
            catch
            {
                // 钩子回调绝不抛异常（>300ms 会被系统摘除）
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}
