using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
using RadioButton = System.Windows.Controls.RadioButton;
using Slider = System.Windows.Controls.Slider;
using WinForms = System.Windows.Forms;

namespace GlintWin;

/// <summary>
/// 主界面（可视化控制台）：运行状态 / 悬浮条外观（自动取色或自定义，带棋盘格实时预览）/
/// 开机自启 / 诊断入口。修改即时生效，「保存配置」写入 config.json。
/// 类名带 _ns 后缀：避免与 Application.MainWindow 实例属性同名冲突。
/// </summary>
public sealed class ConsoleWindow : Window
{
    /// <summary>App 注入：取最近一次皮肤取色主题，供「自动」模式预览。</summary>
    public static Func<SkinTheme?>? LastThemeProvider { get; set; }

    private readonly Config _cfg;
    private readonly RadioButton _rbAuto;
    private readonly RadioButton _rbCustom;
    private readonly StackPanel _colorRow;
    private readonly TextBlock _lblBg;
    private readonly TextBlock _lblFg;
    private readonly Slider _slider;
    private readonly TextBlock _lblOpacity;
    private readonly Border _previewBorder;
    private readonly TextBlock _previewText;
    private readonly CheckBox _chkStartup;

    public ConsoleWindow(Config cfg)
    {
        _cfg = cfg;
        Title = "GlintWin 控制台";
        Width = 520;
        MinHeight = 560;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        TrySetIcon();

        var panel = new StackPanel { Margin = new Thickness(18) };

        // ---- 运行状态 ----
        panel.Children.Add(Section("运行状态"));
        panel.Children.Add(new TextBlock
        {
            Text = "● 监控中 — 搜狗 + 微软拼音双通道，全离线处理",
            Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x8E, 0x3E)),
            Margin = new Thickness(0, 0, 0, 3),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "词典：" + (DictionaryInfoText ?? "CC-CEDICT"),
            Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x64, 0x6C)),
            Margin = new Thickness(0, 0, 0, 2),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "配置：" + Path.Combine(AppContext.BaseDirectory, "config.json"),
            Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x64, 0x6C)),
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 4),
        });

        // ---- 悬浮条外观 ----
        panel.Children.Add(Section("悬浮条外观"));
        _rbAuto = new RadioButton { GroupName = "mode", Content = "自动（跟随输入法皮肤取色）" };
        _rbCustom = new RadioButton { GroupName = "mode", Content = "自定义颜色", Margin = new Thickness(0, 4, 0, 8) };
        _rbAuto.Checked += (_, _) => { _cfg.PillMode = "auto"; _colorRow.IsEnabled = false; Refresh(); };
        _rbCustom.Checked += (_, _) => { _cfg.PillMode = "custom"; _colorRow.IsEnabled = true; Refresh(); };
        panel.Children.Add(_rbAuto);
        panel.Children.Add(_rbCustom);

        _colorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 2, 0, 10) };
        var btnBg = ColorButton("底色…", () => _cfg.PillBgColor, c => { _cfg.PillBgColor = Hex(c); Refresh(); });
        var btnFg = ColorButton("文字色…", () => _cfg.PillFgColor, c => { _cfg.PillFgColor = Hex(c); Refresh(); });
        _lblBg = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 14, 0) };
        _lblFg = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        _colorRow.Children.Add(btnBg);
        _colorRow.Children.Add(_lblBg);
        _colorRow.Children.Add(btnFg);
        _colorRow.Children.Add(_lblFg);
        panel.Children.Add(_colorRow);

        panel.Children.Add(new TextBlock { Text = "不透明度", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 4) });
        var sliderRow = new StackPanel { Orientation = Orientation.Horizontal };
        _slider = new Slider { Minimum = 40, Maximum = 100, Width = 300, VerticalAlignment = VerticalAlignment.Center };
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

        panel.Children.Add(new TextBlock { Text = "预览（棋盘格代表应用背景，可直观看到透底效果）", Margin = new Thickness(0, 14, 0, 6) });
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

        // ---- 行为 ----
        panel.Children.Add(Section("行为"));
        _chkStartup = new CheckBox { Content = "开机自动启动", Margin = new Thickness(0, 2, 0, 4) };
        _chkStartup.Checked += (_, _) => SetStartup(true);
        _chkStartup.Unchecked += (_, _) => SetStartup(false);
        panel.Children.Add(_chkStartup);

        // ---- 诊断 ----
        panel.Children.Add(Section("诊断"));
        var diagRow = new StackPanel { Orientation = Orientation.Horizontal };
        var btnLogs = new Button { Content = "打开日志文件夹", Width = 120, Margin = new Thickness(0, 0, 10, 0) };
        btnLogs.Click += (_, _) =>
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GlintWin", "logs");
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        };
        var btnExit = new Button { Content = "退出 GlintWin", Width = 110 };
        btnExit.Click += (_, _) => Application.Current.Shutdown();
        diagRow.Children.Add(btnLogs);
        diagRow.Children.Add(btnExit);
        panel.Children.Add(diagRow);

        Content = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Loaded += (_, _) => LoadFromCfg();
    }

    /// <summary>词典信息文本（App 注入）。</summary>
    public static string? DictionaryInfoText { get; set; }

    private static TextBlock Section(string t) => new()
    {
        Text = t,
        FontWeight = FontWeights.Bold,
        FontSize = 14,
        Margin = new Thickness(0, 14, 0, 8),
    };

    private void LoadFromCfg()
    {
        bool custom = string.Equals(_cfg.PillMode, "custom", StringComparison.OrdinalIgnoreCase);
        _rbAuto.IsChecked = !custom;
        _rbCustom.IsChecked = custom;
        _colorRow.IsEnabled = custom;
        _slider.Value = Math.Clamp(_cfg.PillOpacityPercent, 40, 100);
        _chkStartup.IsChecked = StartupEnabled();
        Refresh();
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

    private static System.Drawing.Color WinFormsColor(Color c) => System.Drawing.Color.FromArgb(255, c.R, c.G, c.B);

    private static Color Parse(string hex, Color fallback)
    {
        try { return (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex.Trim()); }
        catch { return fallback; }
    }

    private static string Hex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

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

    // ---- 开机自启 ----
    private static string StartupLnk => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), "GlintWin.lnk");

    private static bool StartupEnabled() => File.Exists(StartupLnk);

    private void SetStartup(bool on)
    {
        try
        {
            if (on)
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic lnk = shell.CreateShortcut(StartupLnk);
                lnk.TargetPath = Environment.ProcessPath!;
                lnk.WorkingDirectory = AppContext.BaseDirectory;
                lnk.Description = "GlintWin - 候选词英文释义悬浮条";
                lnk.Save();
            }
            else if (File.Exists(StartupLnk))
            {
                File.Delete(StartupLnk);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("startup toggle failed: " + ex.Message);
            MessageBox.Show(this, "设置开机自启失败：" + ex.Message);
            _chkStartup.IsChecked = StartupEnabled();
        }
    }

    private void TrySetIcon()
    {
        try
        {
            var p = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(p))
                Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(p));
        }
        catch
        {
            // 图标缺失不影响功能
        }
    }
}
