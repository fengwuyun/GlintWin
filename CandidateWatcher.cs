using System.Text;

namespace GlintWin;

public readonly record struct WatchEvent(bool Visible, NativeMethods.RECT Rect, bool RequireNumbered = false);

/// <summary>
/// 后台轮询线程：发现输入法候选窗口（如搜狗 SoPY_Comp）的显示/移动/隐藏，
/// 并把事件推给 Coordinator。
/// </summary>
public sealed class CandidateWatcher
{
    private readonly Config _cfg;
    private readonly NativeMethods.EnumWindowsProc _proc;
    private readonly List<IntPtr> _hits = new();

    public event Action<WatchEvent>? Event;

    public CandidateWatcher(Config cfg)
    {
        _cfg = cfg;
        _proc = OnEnumWindow;   // 持有委托引用，防止 P/Invoke 期间被 GC 回收
    }

    private volatile bool _paused;
    public void SetPaused(bool paused) => _paused = paused;

    public void Loop(CancellationToken ct)
    {
        var lastVisible = false;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_paused)
                {
                    if (lastVisible)
                    {
                        lastVisible = false;
                        Event?.Invoke(new WatchEvent(false, default));
                    }
                }
                else
                {
                    var rect = FindVisibleWindow();
                    if (rect.HasValue)
                    {
                        lastVisible = true;
                        Event?.Invoke(new WatchEvent(true, rect.Value));
                    }
                    else if (lastVisible)
                    {
                        lastVisible = false;
                        Event?.Invoke(new WatchEvent(false, default));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("watcher: " + ex.Message);
            }
            Thread.Sleep(_cfg.PollIntervalMs);
        }
    }

    private NativeMethods.RECT? FindVisibleWindow()
    {
        _hits.Clear();
        NativeMethods.EnumWindows(_proc, IntPtr.Zero);

        NativeMethods.RECT best = default;
        long bestArea = 0;
        foreach (var h in _hits)
        {
            if (!NativeMethods.GetWindowRect(h, out var r)) continue;
            long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
            if (area > bestArea)
            {
                bestArea = area;
                best = r;
            }
        }
        return bestArea > 0 ? best : null;
    }

    private bool OnEnumWindow(IntPtr h, IntPtr l)
    {
        if (!NativeMethods.IsWindowVisible(h)) return true;
        var sb = new StringBuilder(64);
        NativeMethods.GetClassName(h, sb, 64);
        if (!string.Equals(sb.ToString(), _cfg.CandidateWindowClass, StringComparison.Ordinal))
            return true;
        _hits.Add(h);
        return true;
    }

    // ---- 静态工具：供微软拼音通道仲裁（搜狗候选窗可见时让位） ----
    private static readonly List<IntPtr> s_staticHits = new();
    private static string? s_staticFilter;
    private static NativeMethods.EnumWindowsProc? s_staticProc;

    private static bool OnEnumStatic(IntPtr h, IntPtr l)
    {
        if (!NativeMethods.IsWindowVisible(h)) return true;
        var sb = new StringBuilder(64);
        NativeMethods.GetClassName(h, sb, 64);
        if (!string.Equals(sb.ToString(), s_staticFilter, StringComparison.Ordinal)) return true;
        s_staticHits.Add(h);
        return true;
    }

    /// <summary>搜狗候选窗当前是否可见（用于双通道仲裁）。</summary>
    public static bool SogouCandidateVisible(Config cfg)
    {
        s_staticFilter = cfg.CandidateWindowClass;
        s_staticProc ??= OnEnumStatic;
        s_staticHits.Clear();
        NativeMethods.EnumWindows(s_staticProc, IntPtr.Zero);
        foreach (var h in s_staticHits)
        {
            if (!NativeMethods.GetWindowRect(h, out var r)) continue;
            if (r.Right - r.Left >= 100 && r.Bottom - r.Top >= 24) return true;
        }
        return false;
    }
}
