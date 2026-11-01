using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using RadioButton = System.Windows.Controls.RadioButton;
using Slider = System.Windows.Controls.Slider;
using WinForms = System.Windows.Forms;

namespace GlintWin;

/// <summary>
/// 设置窗口：悬浮条底色模式（自动取色/自定义）、文字色、不透明度，
/// 带棋盘格实时预览（棋盘格代表应用背景，可直观看到透底效果）。
/// 修改即时生效（共享 Config 实例），「保存并关闭」写入 config.json。
/// </summary>
public sealed class SettingsWindow : Window
{
    /// <summary>App 注入：取最近一次皮肤取色主题，供「自动」模式预览。</summary>
    public static Func<SkinTheme?>? LastThemeProvider { get; set; }

    private readonly Config _cfg;
    private readonly RadioButton _rbAuto;
    private readonly RadioButton _rbCustom;
    private readonly StackPanel _colorRow;
    private readonly Button _btnBg;
    private readonly Button _btnFg;
    private readonly TextBlock _lblBg;
    private readonly TextBlock _lblFg;
    private readonly Slider _slider;
    private readonly TextBlock _lblOpacity;
    private readonly Border _previewBorder;
    private readonly TextBlock _previewText;

    public SettingsWindow(Config cfg)
    {
        _cfg = cfg;
        Title = "GlintWin 设置 — 悬浮条外观";
        Width = 500;
        Height = 470;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var panel = new StackPanel { Margin = new Thickness(18) };

        panel.Children.Add(new TextBlock { Text = "悬浮条底色", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
        _rbAuto = new RadioButton { GroupName = "mode", Content = "自动（跟随输入法皮肤取色）" };
        _rbCustom = new RadioButton { GroupName = "mode", Content = "自定义颜色", Margin = new Thickness(0, 4, 0, 8) };
        _rbAuto.Checked += (_, _) => { _cfg.PillMode = "auto"; _colorRow.IsEnabled = false; Refresh(); };
        _rbCustom.Checked += (_, _) => { _cfg.PillMode = "custom"; _colorRow.IsEnabled = true; Refresh(); };
        panel.Children.Add(_rbAuto);
        panel.Children.Add(_rbCustom);

        _colorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 2, 0, 10) };
        _btnBg = ColorButton("底色…", () => _cfg.PillBgColor, c => { _cfg.PillBgColor = Hex(c); Refresh(); });
        _btnFg = ColorButton("文字色…", () => _cfg.PillFgColor, c => { _cfg.PillFgColor = Hex(c); Refresh(); });
        _lblBg = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 14, 0) };
        _lblFg = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        _colorRow.Children.Add(_btnBg);
        _colorRow.Children.Add(_lblBg);
        _colorRow.Children.Add(_btnFg);
        _colorRow.Children.Add(_lblFg);
        panel.Children.Add(_colorRow);

        panel.Children.Add(new TextBlock { Text = "不透明度", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 4) });
        var sliderRow = new StackPanel { Orientation = Orientation.Horizontal };
        _slider = new Slider { Minimum = 40, Maximum = 100, Width = 280, VerticalAlignment = VerticalAlignment.Center };
        _lblOpacity = new TextBlock { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _slider.ValueChanged += (_, _) =>
        {
            _cfg.PillOpacityPercent = (int)_slider.Value;
            _lblOpacity.Text = ((int)_slider.Value) + "%";
            Refresh();
        };
        sliderRow.Children.Add(_slider);
        sliderRow.Children.Add(_lblOpacity);
        panel.Children.Add(sliderRow);

        panel.Children.Add(new TextBlock { Text = "预览（棋盘格代表应用背景，可直观看到透底效果）", Margin = new Thickness(0, 16, 0, 6) });
        _previewText = new TextBlock
        {
            Text = "学习   learn · study",
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 14,
        };
        _previewBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 7, 14, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = _previewText,
        };
        var checker = new Border { Background = CheckerBrush(), Padding = new Thickness(2), Child = _previewBorder };
        panel.Children.Add(checker);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var btnReset = new Button { Content = "恢复自动", Width = 88, Margin = new Thickness(0, 0, 10, 0) };
        var btnSave = new Button { Content = "保存并关闭", Width = 96 };
        btnReset.Click += (_, _) =>
        {
            _cfg.PillMode = "auto";
            _cfg.PillOpacityPercent = 85;
            _cfg.PillBgColor = "#FF2A2C30";
            _cfg.PillFgColor = "#FFF2F5FA";
            LoadFromCfg();
            _cfg.Save();
        };
        btnSave.Click += (_, _) => { _cfg.Save(); Close(); };
        btnRow.Children.Add(btnReset);
        btnRow.Children.Add(btnSave);
        panel.Children.Add(btnRow);

        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += (_, _) => LoadFromCfg();
    }

    private void LoadFromCfg()
    {
        bool custom = string.Equals(_cfg.PillMode, "custom", StringComparison.OrdinalIgnoreCase);
        _rbAuto.IsChecked = !custom;
        _rbCustom.IsChecked = custom;
        _colorRow.IsEnabled = custom;
        _slider.Value = Math.Clamp(_cfg.PillOpacityPercent, 40, 100);
        Refresh();
    }

    private static Button ColorButton(string text, Func<string> getHex, Action<Color> set)
    {
        var b = new Button { Content = text, Width = 84 };
        b.Click += (_, _) =>
        {
            var cur = Parse(getHex(), Color.FromRgb(0x80, 0x80, 0x80));
            using var dlg = new WinForms.ColorDialog { FullOpen = true, Color = WinFormsColor(cur) };
            if (dlg.ShowDialog() == WinForms.DialogResult.OK)
            {
                set(Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B));
            }
        };
        return b;
    }

    private void Refresh()
    {
        bool custom = string.Equals(_cfg.PillMode, "custom", StringComparison.OrdinalIgnoreCase);
        byte alpha = (byte)Math.Clamp(_cfg.PillOpacityPercent * 255 / 100, 40, 255);
        Color bg, fg;
        if (custom)
        {
            bg = Parse(_cfg.PillBgColor, Color.FromRgb(0x2A, 0x2C, 0x30));
            fg = Parse(_cfg.PillFgColor, Color.FromRgb(0xF2, 0xF5, 0xFA));
        }
        else
        {
            var t = LastThemeProvider?.Invoke();
            bg = t?.Bg ?? Color.FromRgb(0x2A, 0x2C, 0x30);
            fg = t?.Fg ?? Color.FromRgb(0xF2, 0xF5, 0xFA);
        }
        _previewBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, bg.R, bg.G, bg.B));
        _previewText.Foreground = new SolidColorBrush(fg);
        _lblBg.Text = _cfg.PillBgColor;
        _lblFg.Text = _cfg.PillFgColor;
    }

    /// <summary>棋盘格画刷：代表任意应用背景，展示半透明透底效果。</summary>
    private static Brush CheckerBrush()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing
        {
            Brush = Brushes.White,
            Geometry = new RectangleGeometry(new Rect(0, 0, 20, 20)),
        });
        var gray = new SolidColorBrush(Color.FromRgb(0xC9, 0xCD, 0xD2));
        group.Children.Add(new GeometryDrawing
        {
            Brush = gray,
            Geometry = new RectangleGeometry(new Rect(0, 0, 10, 10)),
        });
        group.Children.Add(new GeometryDrawing
        {
            Brush = gray,
            Geometry = new RectangleGeometry(new Rect(10, 10, 10, 10)),
        });
        return new DrawingBrush
        {
            Drawing = group,
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 20, 20),
            Viewbox = new Rect(0, 0, 20, 20),
        };
    }

    private static Color Parse(string hex, Color fallback)
    {
        try { return (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex.Trim()); }
        catch { return fallback; }
    }

    private static string Hex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private static System.Drawing.Color WinFormsColor(Color c) => System.Drawing.Color.FromArgb(255, c.R, c.G, c.B);
}
