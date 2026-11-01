using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace GlintWin;

/// <summary>
/// 仿 Glint 的胶囊悬浮条：置顶、绝不抢焦点、鼠标穿透、不进 Alt-Tab。
/// 配色/字号跟随输入法皮肤（皮肤取色失败时回退深色主题）。
/// “隐藏”实现为移到屏幕外，避免反复 Show/Hide 产生闪烁与焦点扰动。
/// </summary>
public sealed class OverlayWindow : Window
{
    private readonly Border _border;
    private readonly TextBlock _text;
    private readonly double _defaultFontSize;
    private IntPtr _hwnd;
    private bool _shown;
    private NativeMethods.RECT _placedRect;
    private bool _placedValid;

    public OverlayWindow(Config cfg)
    {
        _defaultFontSize = cfg.FontSizeDip;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.WidthAndHeight;

        _border = new Border
        {
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromArgb(0xE8, 0x1E, 0x20, 0x25)),
            Padding = new Thickness(12, 5, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _text = new TextBlock
        {
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = _defaultFontSize,
            Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0xEE, 0xF4)),
            MaxWidth = 640,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _border.Child = _text;
        Content = _border;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        int ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        // NOACTIVATE+TOOLWINDOW：不抢焦点；TRANSPARENT：鼠标穿透
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE,
            ex | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT);
    }

    public void ShowOffscreen()
    {
        if (_shown) return;
        Left = -30000;
        Top = -30000;
        Show();
        _shown = true;
    }

    public void Update(string candidate, string? gloss, string? pinyin, SkinTheme? theme)
    {
        Color bg, bright, dim;
        double fontSize;
        if (theme != null)
        {
            // 半透明：皮肤候选栏本身透底，悬浮条用同色半透明才能与皮肤观感一致
            bg = Color.FromArgb(0xD8, theme.Bg.R, theme.Bg.G, theme.Bg.B);
            bright = theme.Fg;
            dim = Lerp(theme.Fg, theme.Bg, 0.45);
            double scale = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            fontSize = Math.Clamp(theme.RowTextHeightPx / scale * 0.80, 11, 16);
        }
        else
        {
            bg = Color.FromArgb(0xE8, 0x1E, 0x20, 0x25);
            bright = Color.FromRgb(0xF2, 0xF5, 0xFA);
            dim = Color.FromRgb(0x9A, 0xA3, 0xB2);
            fontSize = _defaultFontSize;
        }

        _border.Background = new SolidColorBrush(bg);
        _text.FontSize = fontSize;

        _text.Inlines.Clear();
        _text.Inlines.Add(new Run(candidate) { Foreground = new SolidColorBrush(dim) });
        _text.Inlines.Add(new Run("   "));
        if (!string.IsNullOrEmpty(gloss))
        {
            _text.Inlines.Add(new Run(gloss) { Foreground = new SolidColorBrush(bright), FontWeight = FontWeights.Medium });
        }
        else if (!string.IsNullOrEmpty(pinyin))
        {
            _text.Inlines.Add(new Run(pinyin + " —") { Foreground = new SolidColorBrush(dim) });
        }
        else
        {
            _text.Inlines.Add(new Run("—") { Foreground = new SolidColorBrush(dim) });
        }
        UpdateLayout();
    }

    /// <summary>
    /// 把悬浮条放到锚点矩形（物理像素坐标，通常为候选行）正下方。
    /// 产品规则：悬浮条永远在候选行下方，绝不翻到上方；
    /// 屏幕底部空间不足时最多把底边贴到屏幕底（此时会压住候选窗底边，但不越过行底之上）。
    /// </summary>
    public void Place(NativeMethods.RECT anchor, int offsetY)
    {
        double scale = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        int w = (int)Math.Ceiling(ActualWidth * scale);
        int h = (int)Math.Ceiling(ActualHeight * scale);
        int x = anchor.Left;
        int y = anchor.Bottom + offsetY;

        var mr = new NativeMethods.RECT
        {
            Left = anchor.Left, Top = anchor.Top, Right = anchor.Right, Bottom = anchor.Bottom
        };
        var hmon = NativeMethods.MonitorFromRect(ref mr, 2 /* MONITOR_DEFAULTTONEAREST */);
        var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (hmon != IntPtr.Zero && NativeMethods.GetMonitorInfo(hmon, ref mi))
        {
            // 横向：先按工作区收拢，再保证不出显示器
            if (x + w > mi.rcWork.Right) x = mi.rcWork.Right - w;
            if (x < mi.rcWork.Left) x = mi.rcWork.Left;
            if (x + w > mi.rcMonitor.Right) x = mi.rcMonitor.Right - w;
            if (x < mi.rcMonitor.Left) x = mi.rcMonitor.Left;

            // 纵向：只在屏幕底边内收拢，最低不得高于候选行底（即绝不翻到上方）
            int yMax = mi.rcMonitor.Bottom - h;
            if (y > yMax) y = Math.Max(yMax, anchor.Bottom);
        }

        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, x, y, w, h,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        _placedRect = new NativeMethods.RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
        _placedValid = true;
    }

    /// <summary>悬浮条当前占用的屏幕矩形（供 OCR 前抹除自身像素，切断反馈回路）。</summary>
    public bool TryGetPlacedRect(out NativeMethods.RECT rect)
    {
        rect = _placedRect;
        return _placedValid;
    }

    public void HidePill()
    {
        if (!_shown) return;
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, -30000, -30000, 0, 0,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSIZE);
        _placedValid = false;
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));
}
