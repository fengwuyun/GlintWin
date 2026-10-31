using System.Runtime.InteropServices;

namespace GlintWin;

/// <summary>
/// 微软拼音通道（P2）。
/// 尖峰结论（probe_msp4，Win11 25H2 实测）：微软拼音候选窗由「Windows 输入体验」以
/// DirectComposition 渲染 —— 无 Win32 子窗口、UIA 子树恒为 0，无法结构化读取。
/// 因此采用：前台线程光标坐标（GetGUIThreadInfo，合成期间稳定可用）锚定光标下方条带，
/// 像素变化检测 + 复用主 OCR 管线解析候选行。
/// 仲裁：搜狗候选窗可见时本通道自动让位。限定：应用需暴露 Win32 光标
/// （Win32/WinForms 及多数原生应用可用；Chromium 系自绘光标暂不支持）。
/// </summary>
public sealed class MspWatcher
{
    private readonly Config _cfg;
    private readonly int _selfPid;
    public event Action<WatchEvent>? Event;

    private volatile bool _paused;
    public void SetPaused(bool paused) => _paused = paused;

    public MspWatcher(Config cfg)
    {
        _cfg = cfg;
        _selfPid = Environment.ProcessId;
    }

    public void Loop(CancellationToken ct)
    {
        bool lastVisible = false;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                bool show = false;
                var band = new NativeMethods.RECT();
                if (!_paused && PinyinTracker.IsFresh && !CandidateWatcher.SogouCandidateVisible(_cfg))
                {
                    var caret = GetForegroundCaret();
                    if (caret.HasValue && ImeOpenForForeground())
                    {
                        band = BandFor(caret.Value);
                        show = true;
                    }
                }
                if (show)
                {
                    lastVisible = true;
                    Event?.Invoke(new WatchEvent(true, band, RequireNumbered: true));
                }
                else if (lastVisible)
                {
                    lastVisible = false;
                    Event?.Invoke(new WatchEvent(false, default));
                }
            }
            catch (Exception ex)
            {
                Logger.Error("msp: " + ex.Message);
            }
            Thread.Sleep(_cfg.MspPollIntervalMs);
        }
    }

    /// <summary>前台窗口的 IME 是否处于中文开启状态（WM_IME_CONTROL 查询，免抓屏；自己进程跳过）。</summary>
    private static bool ImeOpenForForeground()
    {
        var fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        uint fgPid;
        NativeMethods.GetWindowThreadProcessId(fg, out fgPid);
        if (fgPid == (uint)Environment.ProcessId) return false;

        var imeWnd = NativeMethods.ImmGetDefaultIMEWnd(fg);
        if (imeWnd == IntPtr.Zero) return false;
        var status = NativeMethods.SendMessage(imeWnd, NativeMethods.WM_IME_CONTROL,
            NativeMethods.IMC_GETOPENSTATUS, IntPtr.Zero);
        return status != IntPtr.Zero;
    }

    /// <summary>前台线程的插入符屏幕坐标；无光标（或异常）返回 null。</summary>
    private static NativeMethods.RECT? GetForegroundCaret()
    {
        var fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero) return null;
        uint pid;
        uint tid = NativeMethods.GetWindowThreadProcessId(fg, out pid);
        var ti = new NativeMethods.GUITHREADINFO { cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
        if (!NativeMethods.GetGUIThreadInfo(tid, ref ti) || ti.hwndCaret == IntPtr.Zero) return null;

        var pt = new NativeMethods.POINT { X = ti.rcCaret.Left, Y = ti.rcCaret.Top };
        if (!NativeMethods.ClientToScreen(ti.hwndCaret, ref pt)) return null;

        var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
        if (pt.X < vs.Left - 50 || pt.X > vs.Right || pt.Y < vs.Top - 50 || pt.Y > vs.Bottom) return null;
        return new NativeMethods.RECT { Left = pt.X, Top = pt.Y, Right = pt.X + 2, Bottom = pt.Y + 2 };
    }

    /// <summary>光标下方条带；贴近屏幕底部时改取上方（候选条此时渲染在光标上方）。</summary>
    private NativeMethods.RECT BandFor(NativeMethods.RECT caret)
    {
        var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
        int width = _cfg.MspBandWidthPx;
        int height = _cfg.MspBandHeightPx;
        int left = caret.Left - 8;
        int top = caret.Bottom + _cfg.MspBandOffsetY;
        if (top + height + 16 > vs.Bottom)
            top = caret.Top - _cfg.MspBandOffsetY - height;
        if (left + width > vs.Right) left = vs.Right - width;
        if (left < vs.Left) left = vs.Left;
        if (top < vs.Top) top = vs.Top;
        return new NativeMethods.RECT { Left = left, Top = top, Right = left + width, Bottom = top + height };
    }
}
