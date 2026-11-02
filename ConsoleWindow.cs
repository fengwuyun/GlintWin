using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Control = System.Windows.Controls.Control;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using DataGrid = System.Windows.Controls.DataGrid;
using DataGridGridLinesVisibility = System.Windows.Controls.DataGridGridLinesVisibility;
using DataGridHeadersVisibility = System.Windows.Controls.DataGridHeadersVisibility;
using DataGridLength = System.Windows.Controls.DataGridLength;
using DataGridLengthUnitType = System.Windows.Controls.DataGridLengthUnitType;
using DataGridTextColumn = System.Windows.Controls.DataGridTextColumn;
using Ellipse = System.Windows.Shapes.Ellipse;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;
using RadioButton = System.Windows.Controls.RadioButton;
using Rectangle = System.Windows.Shapes.Rectangle;
using Slider = System.Windows.Controls.Slider;
using WinForms = System.Windows.Forms;

namespace GlintWin;

/// <summary>
/// 可视化控制台（深色 Fluent 风格）：左侧导航 + 四页内容。
/// 状态总览 / 悬浮条外观（调色盘 + HSV 取色器 + 实时预览）/ 词库管理（主题词包 + 自定义词库）/ 通用设置。
/// </summary>
public sealed partial class ConsoleWindow : Window
{
    /// <summary>App 注入：取最近一次皮肤取色主题，供「自动」模式预览。</summary>
    public static Func<SkinTheme?>? LastThemeProvider { get; set; }

    /// <summary>词典规模描述（App 注入）。</summary>
    public static string? DictionaryInfoText { get; set; }

    private readonly Config _cfg;
    private readonly DailyCollectionService? _daily;
    public static string RuntimeStatusText { get; set; } = "离线 OCR 状态待检查";
    private readonly Func<CedictDictionary?> _dictProvider;
    private readonly Action _reloadDictionary;
    private readonly ContentControl _pageHost = new();
    private readonly List<(Border Item, Border Accent, TextBlock Label, TextBlock Glyph, string Key)> _navItems = new();
    private static readonly Brush NavIdleBrush = new SolidColorBrush(Color.FromRgb(0xC5, 0xC9, 0xD4));
    private string _activePage = "status";
    private readonly ObservableCollection<CustomWord> _customWords;
    private WrapPanel? _recentColors;
    private readonly System.Windows.Threading.DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private bool _configDirty;
    private bool _loadingLook;

    // 外观页控件引用（跨页保持）
    private TextBlock? _lblBg;
    private TextBlock? _lblFg;
    private Slider? _slider;
    private TextBlock? _lblOpacity;
    private Border? _previewBorder;
    private TextBlock? _previewText;
    private Border? _svBox;
    private Ellipse? _svThumb;
    private Border? _hueBar;
    private Ellipse? _hueThumb;
    private LinearGradientBrush? _svFill;
    private double _hue = 210, _sat = 0.75, _val = 0.18;
    private string _editTarget = "bg";
    private Button? _tgtBgBtn;
    private Button? _tgtFgBtn;
    private Border? _autoSwitch;
    private TextBlock? _lblAutoState;

    // 主题色
    private static readonly Color Accent = Color.FromRgb(0x6B, 0x8A, 0xFB);
    private static readonly Brush AccentBrush = new SolidColorBrush(Accent);
    private static readonly Brush BgWindowBrush = new SolidColorBrush(Color.FromRgb(0x13, 0x15, 0x1A));
    private static readonly Brush BgCardBrush = new SolidColorBrush(Color.FromRgb(0x1C, 0x1F, 0x27));
    private static readonly Brush BgHoverBrush = new SolidColorBrush(Color.FromArgb(60, 0x6B, 0x8A, 0xFB));
    private static readonly Brush FgBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0xF0, 0xF4));
    private static readonly Brush FgSubBrush = new SolidColorBrush(Color.FromRgb(0x9C, 0xA0, 0xAD));
    private static readonly Brush LineBrush = new SolidColorBrush(Color.FromRgb(0x2C, 0x30, 0x3B));

    public ConsoleWindow(Config cfg, Func<CedictDictionary?> dictProvider, Action reloadDictionary, DailyCollectionService? daily = null)
    {
        _daily = daily;
        _cfg = cfg;
        _dictProvider = dictProvider;
        _reloadDictionary = reloadDictionary;
        _customWords = new ObservableCollection<CustomWord>(CustomStore.Load());

        Title = "GlintWin 控制台";
        Width = 980;
        Height = Math.Min(720, SystemParameters.WorkArea.Height * 0.9);
        MinWidth = 860;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = BgWindowBrush;
        Foreground = FgBrush;
        FontSize = 13.5;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        TrySetIcon();
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        InstallControlStyles();
        _saveTimer.Tick += (_, _) => FlushConfig();
        Closed += (_, _) => { FlushConfig(); _saveTimer.Stop(); };
        SourceInitialized += (_, _) =>
        {
            int dark = 1;
            DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
        };

        var sidebar = new Border
        {
            Width = 200,
            Background = new SolidColorBrush(Color.FromRgb(0x10, 0x12, 0x17)),
            Child = BuildSidebar(),
        };

        var root = new Grid { Background = BgWindowBrush };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(sidebar, 0);
        Grid.SetColumn(_pageHost, 1);
        root.Children.Add(sidebar);
        root.Children.Add(_pageHost);

        Content = root;
        ShowPage("status");
    }

    private UIElement BuildSidebar()
    {
        var sp = new StackPanel { Margin = new Thickness(14, 18, 14, 14) };

        var logoRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 26) };
        var logo = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(9),
            Background = new LinearGradientBrush(Color.FromRgb(0x5B, 0x7C, 0xFA), Color.FromRgb(0x9B, 0x5B, 0xFA), 45),
            Child = new TextBlock
            {
                Text = char.ConvertFromUtf32(0x8BD1),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 17,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        logoRow.Children.Add(logo);
        logoRow.Children.Add(new TextBlock
        {
            Text = "GlintWin",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = FgBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        });
        sp.Children.Add(logoRow);

        sp.Children.Add(NavLabel("总览"));
        sp.Children.Add(NavItem("status", "\uE80F", "状态总览"));
        sp.Children.Add(NavItem("look", "\uE790", "悬浮条外观"));
        sp.Children.Add(NavLabel("词库"));
        sp.Children.Add(NavItem("dict", "\uE8A5", "词库管理"));
        sp.Children.Add(NavItem("daily", "\uE787", "每日词汇"));
        sp.Children.Add(NavLabel("系统"));
        sp.Children.Add(NavItem("settings", "\uE713", "通用设置"));

        sp.Children.Add(new Border { Height = 1, Background = LineBrush, Margin = new Thickness(8, 14, 8, 10), Opacity = 0.5 });
        sp.Children.Add(new TextBlock
        {
            Text = "v0.4.3 · 全离线处理\n每日采集可选 · 不上传",
            FontSize = 11,
            Foreground = FgSubBrush,
            Margin = new Thickness(8, 0, 0, 0),
            LineHeight = 17,
        });
        return sp;
    }

    private static TextBlock NavLabel(string t) => new()
    {
        Text = t,
        FontSize = 12,
        Foreground = new SolidColorBrush(Color.FromRgb(0xB4, 0xB9, 0xC6)),
        Margin = new Thickness(10, 16, 0, 8),
    };

    private Border NavItem(string key, string glyph, string label)
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(6, 0, 6, 0),
            Height = 44,
            Focusable = true,
            Margin = new Thickness(0, 0, 0, 5),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
        };
        var accent = new Border
        {
            Width = 3,
            Height = 20,
            CornerRadius = new CornerRadius(2),
            Background = AccentBrush,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        var glyphTb = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 12, 0),
            Foreground = NavIdleBrush,
        };
        var labelTb = new TextBlock
        {
            Text = label,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = NavIdleBrush,
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(glyphTb);
        sp.Children.Add(labelTb);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(accent, 0);
        Grid.SetColumn(sp, 1);
        grid.Children.Add(accent);
        grid.Children.Add(sp);
        b.Child = grid;
        b.Tag = (accent, labelTb, glyphTb);
        b.MouseLeftButtonUp += (_, _) => ShowPage(key);
        b.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { ShowPage(key); e.Handled = true; } };
        b.MouseEnter += (_, _) => { if (_activePage != key) b.Background = BgHoverBrush; };
        b.MouseLeave += (_, _) => { if (_activePage != key) b.Background = Brushes.Transparent; };
        _navItems.Add((b, accent, labelTb, glyphTb, key));
        return b;
    }

    private void ShowPage(string key)
    {
        if (_activePage == "look" && key != "look")
        {
            _lblBg = _lblFg = _lblOpacity = _previewText = _lblAutoState = null;
            _slider = null; _previewBorder = _svBox = _hueBar = _autoSwitch = null;
            _svThumb = _hueThumb = null; _svFill = null;
            _tgtBgBtn = _tgtFgBtn = null; _recentColors = null;
        }
        if (key != "daily") { _dailyRequest++; _dailyGrid = null; }
        _activePage = key;
        foreach (var (item, accent, labelTb, glyphTb, k) in _navItems)
        {
            bool active = k == key;
            item.Background = active ? BgHoverBrush : Brushes.Transparent;
            accent.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            labelTb.Foreground = active ? FgBrush : NavIdleBrush;
            glyphTb.Foreground = active ? AccentBrush : NavIdleBrush;
        }
        _pageHost.Content = key switch
        {
            "look" => BuildLookPage(),
            "dict" => BuildDictPage(),
            "daily" => BuildDailyPage(),
            "settings" => BuildSettingsPage(),
            _ => BuildStatusPage(),
        };
    }

    /// <summary>刷新状态页（词典重载后由 App 调用）。</summary>
    public void RefreshStatus()
    {
        if (_activePage == "status") ShowPage("status");
    }

    private static ScrollViewer PageScroll(UIElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Padding = new Thickness(28, 24, 28, 24),
    };

    private static TextBlock Description(string text) => new()
    {
        Text = text, Foreground = FgSubBrush, FontSize = 12.5, TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, -4, 0, 20),
    };

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void InstallControlStyles()
    {
        const string xaml = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style TargetType="Button">
            <Setter Property="FontSize" Value="13"/>
            <Setter Property="MinHeight" Value="34"/>
            <Setter Property="Template">
              <Setter.Value><ControlTemplate TargetType="Button">
                <Border x:Name="Surface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="7" Padding="{TemplateBinding Padding}">
                  <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" RecognizesAccessKey="True"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="Opacity" Value="0.8"/></Trigger>
                  <Trigger Property="IsPressed" Value="True"><Setter TargetName="Surface" Property="Opacity" Value="0.6"/></Trigger>
                  <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="#A5B4FC"/><Setter TargetName="Surface" Property="BorderThickness" Value="2"/></Trigger>
                  <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Surface" Property="Opacity" Value="0.4"/></Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate></Setter.Value>
            </Setter>
          </Style>
          <Style TargetType="ScrollBar">
            <Setter Property="Width" Value="10"/><Setter Property="Background" Value="Transparent"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar">
              <Grid Background="Transparent"><Track x:Name="PART_Track" IsDirectionReversed="True" Orientation="{TemplateBinding Orientation}">
                <Track.DecreaseRepeatButton><RepeatButton Command="ScrollBar.PageUpCommand" Opacity="0"/></Track.DecreaseRepeatButton>
                <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="#454B59" CornerRadius="3" Margin="2" MinHeight="24"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
                <Track.IncreaseRepeatButton><RepeatButton Command="ScrollBar.PageDownCommand" Opacity="0"/></Track.IncreaseRepeatButton>
              </Track></Grid>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="ToolTip"><Setter Property="Background" Value="#252A35"/><Setter Property="Foreground" Value="#F1F5F9"/><Setter Property="BorderBrush" Value="#3B4252"/><Setter Property="Padding" Value="10,6"/></Style>
        </ResourceDictionary>
        """;
        Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Parse(xaml));
    }

    private static TextBlock PageTitle(string t) => new()
    {
        Text = t,
        FontSize = 25,
        FontWeight = FontWeights.SemiBold,
        Foreground = FgBrush,
        Margin = new Thickness(0, 0, 0, 14),
    };

    private static TextBlock CardTitle(string t) => new()
    {
        Text = t,
        FontSize = 13.5,
        FontWeight = FontWeights.SemiBold,
        Foreground = FgBrush,
        Margin = new Thickness(0, 0, 0, 10),
    };

    private static Border Card(UIElement content) => new()
    {
        Background = BgCardBrush,
        BorderBrush = LineBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(20, 18, 20, 18),
        Margin = new Thickness(0, 0, 0, 12),
        Child = content,
    };

    private static StackPanel VStack(params UIElement[] items)
    {
        var sp = new StackPanel();
        foreach (var i in items) sp.Children.Add(i);
        return sp;
    }

    // ==================== 状态总览 ====================
    private UIElement BuildStatusPage()
    {
        var dict = _dictProvider();
        var packsOn = _cfg.EnabledPacks.Count > 0 ? string.Join("、", _cfg.EnabledPacks) : "（无）";

        var statusCard = Card(VStack(
            new TextBlock { Text = "● 监控中", Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xC3, 0x6F)), FontWeight = FontWeights.SemiBold },
            new TextBlock { Text = RuntimeStatusText, Foreground = FgSubBrush, Margin = new Thickness(0, 6, 0, 0) },
            new TextBlock { Text = "悬浮条跟随输入法皮肤取色，释义全离线查询，按键仅识别拼音不上传", Foreground = FgSubBrush, Margin = new Thickness(0, 2, 0, 0) }));

        var dictCard = Card(VStack(
            CardTitle("词典"),
            new TextBlock { Text = ConsoleWindow.DictionaryInfoText ?? "—", Foreground = FgBrush, Margin = new Thickness(0, 0, 0, 6) },
            new TextBlock
            {
                Text = dict == null ? "（未加载）" : $"基础词条 {dict.BaseCount:N0} ｜ 主题词包 +{dict.PackCounts.Values.Sum():N0} ｜ 自定义 +{dict.CustomCount:N0}",
                Foreground = FgSubBrush,
            },
            new TextBlock { Text = "启用的词包：" + packsOn, Foreground = FgSubBrush, Margin = new Thickness(0, 6, 0, 0) }));

        var tipCard = Card(VStack(
            CardTitle("小提示"),
            new TextBlock
            {
                Text = "· 悬浮条永远在候选行下方，鼠标穿透不打断输入\n· 外观页可一键套用调色盘或自定义任意颜色\n· 词库管理页可启停主题词包、维护自定义词库（支持 CSV 模板导入）",
                Foreground = FgSubBrush,
                LineHeight = 20,
            }));

        return PageScroll(VStack(PageTitle("状态总览"), statusCard, dictCard, tipCard));
    }

    // ==================== 悬浮条外观 ====================
    private UIElement BuildLookPage()
    {
        var sp = VStack(PageTitle("悬浮条外观"), Description("配色随心，专注输入。所有调整即时预览并自动保存。"));
        _previewText = new TextBlock { Text = "学习   learn · study", FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 13 };
        _previewBorder = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 9, 16, 9), HorizontalAlignment = HorizontalAlignment.Center, Child = _previewText };
        var preview = new Border { Background = CheckerBrush(), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 20, 10, 20), Child = _previewBorder };
        _lblAutoState = new TextBlock { Foreground = FgBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        _autoSwitch = MakeSwitch(_cfg.PillMode != "custom", on =>
        {
            _cfg.PillMode = on ? "auto" : "custom";
            LoadLookFromCfg(); SaveCfg();
        });
        var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0), Children = { _autoSwitch, _lblAutoState } };
        var rightColumn = new StackPanel();
        rightColumn.Children.Add(Card(VStack(CardTitle("实时预览"), preview, modeRow)));
        var leftColumn = new StackPanel();
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        leftColumn.Margin = new Thickness(0, 0, 14, 0);
        Grid.SetColumn(rightColumn, 1);
        columns.Children.Add(leftColumn); columns.Children.Add(rightColumn);
        sp.Children.Add(columns);

        _tgtBgBtn = FlatButton("背景色", () => SetEditTarget("bg"));
        _tgtFgBtn = FlatButton("文字色", () => SetEditTarget("fg"));
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16), Children = { _tgtBgBtn, _tgtFgBtn } };
        var quick = new WrapPanel();
        foreach (var (name, hex) in QuickColors) quick.Children.Add(ColorButton(name, hex));
        _recentColors = new WrapPanel();
        RefreshHistory();
        _lblBg = new TextBlock { Foreground = FgSubBrush, FontSize = 12, Margin = new Thickness(0, 12, 0, 0) };
        _lblFg = new TextBlock { Foreground = FgSubBrush, FontSize = 12, Margin = new Thickness(0, 3, 0, 0) };
        BuildPicker(out var sv, out var hue);
        var picker = new Expander
        {
            Header = "精细取色", Foreground = FgBrush, Margin = new Thickness(0, 14, 0, 0),
            Content = new StackPanel { Margin = new Thickness(0, 12, 0, 0), Children = { sv, new Border { Margin = new Thickness(0, 12, 0, 0), Child = hue } } }
        };
        leftColumn.Children.Add(Card(VStack(tabs, CardTitle("精选颜色"), quick,
            new TextBlock { Text = "最近使用 · 最多 8 色", Foreground = FgSubBrush, FontSize = 12, Margin = new Thickness(0, 10, 0, 10) },
            _recentColors, _lblBg, _lblFg, picker)));

        var palettes = new WrapPanel();
        foreach (var p in Palettes)
        {
            var button = FlatButton(p.Name, () =>
            {
                _cfg.PillMode = "custom"; _cfg.PillBgColor = p.Bg; _cfg.PillFgColor = p.Fg; _cfg.PillOpacityPercent = p.Opacity;
                _cfg.RememberColor(p.Fg); _cfg.RememberColor(p.Bg);
                LoadLookFromCfg(); RefreshHistory(); SaveCfg();
            });
            button.Background = new SolidColorBrush(ParseColor(p.Bg, Colors.Gray));
            button.Foreground = new SolidColorBrush(ParseColor(p.Fg, Colors.White));
            button.MinWidth = 88; button.Padding = new Thickness(8, 8, 8, 8); button.Margin = new Thickness(0, 0, 8, 8);
            button.ToolTip = p.Bg + " / " + p.Fg;
            palettes.Children.Add(button);
        }
        _slider = new Slider { Minimum = 40, Maximum = 100, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 150, VerticalAlignment = VerticalAlignment.Center };
        _lblOpacity = new TextBlock { Margin = new Thickness(14, 0, 0, 0), Foreground = FgBrush, VerticalAlignment = VerticalAlignment.Center };
        _slider.ValueChanged += (_, _) =>
        {
            if (_loadingLook) return;
            _cfg.PillOpacityPercent = (int)_slider.Value;
            _lblOpacity.Text = _cfg.PillOpacityPercent + "%";
            RefreshLook(); SaveCfg();
        };
        var sliderRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { _slider, _lblOpacity } };
        rightColumn.Children.Add(Card(VStack(CardTitle("配色方案"), palettes,
            new TextBlock { Text = "不透明度", Foreground = FgSubBrush, Margin = new Thickness(0, 8, 0, 8) }, sliderRow)));
        LoadLookFromCfg();
        return PageScroll(sp);
    }

    private static readonly (string Name, string Hex)[] QuickColors =
    {
        ("石墨", "#FF18181B"), ("深岩", "#FF27272A"), ("雾灰", "#FF64748B"), ("云白", "#FFF8FAFC"),
        ("靛蓝", "#FF6366F1"), ("紫罗兰", "#FF8B5CF6"), ("天蓝", "#FF3B82F6"), ("青碧", "#FF06B6D4"),
        ("翡翠", "#FF10B981"), ("琥珀", "#FFF59E0B"), ("珊瑚", "#FFF43F5E"), ("暖砂", "#FFFBF7F0"),
    };

    private Button ColorButton(string name, string hex)
    {
        var b = FlatButton("", () =>
        {
            if (_editTarget == "bg") _cfg.PillBgColor = hex; else _cfg.PillFgColor = hex;
            _cfg.PillMode = "custom"; _cfg.RememberColor(hex);
            LoadLookFromCfg(); RefreshHistory(); SaveCfg();
        });
        b.Width = 36; b.Height = 36; b.MinWidth = 0;
        b.Padding = new Thickness(0); b.Margin = new Thickness(0, 0, 9, 6);
        b.Background = new SolidColorBrush(ParseColor(hex, Colors.Gray));
        b.ToolTip = name + " · " + hex;
        System.Windows.Automation.AutomationProperties.SetName(b, name + " " + hex);
        return b;
    }

    private void RefreshHistory()
    {
        if (_recentColors == null) return;
        _recentColors.Children.Clear();
        foreach (var hex in (_cfg.RecentColors ?? new()).Take(8).ToArray())
            _recentColors.Children.Add(ColorButton("最近使用", hex));
        if (_recentColors.Children.Count == 0)
            _recentColors.Children.Add(new TextBlock { Text = "选择颜色后会自动记录在这里", Foreground = FgSubBrush, FontSize = 12 });
    }

    private void CommitPicker()
    {
        _cfg.RememberColor(_editTarget == "bg" ? _cfg.PillBgColor : _cfg.PillFgColor);
        RefreshHistory(); SaveCfg();
    }

    private void SetEditTarget(string t)
    {
        _editTarget = t;
        var current = ParseColor(t == "bg" ? _cfg.PillBgColor : _cfg.PillFgColor, Colors.Gray);
        double r = current.R / 255d, g = current.G / 255d, b = current.B / 255d;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
        _val = max; _sat = max == 0 ? 0 : delta / max;
        _hue = delta == 0 ? 0 : max == r ? 60 * (((g - b) / delta + 6) % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        UpdatePickerThumbs();
        if (_tgtBgBtn != null)
            _tgtBgBtn.Background = new SolidColorBrush(t == "bg" ? Accent : Color.FromRgb(0x35, 0x35, 0x3D));
        if (_tgtFgBtn != null)
            _tgtFgBtn.Background = new SolidColorBrush(t == "fg" ? Accent : Color.FromRgb(0x35, 0x35, 0x3D));
    }

    private void BuildPicker(out UIElement svBoxEl, out UIElement hueGridEl)
    {
        _svBox = new Border
        {
            Width = 240,
            Height = 160,
            CornerRadius = new CornerRadius(6),
            Cursor = Cursors.Cross,
        };
        var svGrid = new Grid();
        _svFill = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(Colors.White, 1) },
        };
        svGrid.Children.Add(new System.Windows.Shapes.Rectangle { Fill = _svFill });
        svGrid.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 0, 0, 0), 0),
                    new GradientStop(Colors.Black, 1),
                },
            },
        });
        _svThumb = new Ellipse
        {
            Width = 12,
            Height = 12,
            Stroke = Brushes.White,
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(-6, -6, 0, 0),
        };
        var svCanvas = new Canvas { ClipToBounds = true, Children = { _svThumb } };
        svGrid.Children.Add(svCanvas);
        _svBox.Child = svGrid;
        _svBox.SizeChanged += (_, _) => UpdatePickerThumbs();
        _svBox.MouseLeftButtonDown += (_, e) => { _svBox.CaptureMouse(); PickSv(e.GetPosition(_svBox)); };
        _svBox.MouseMove += (_, e) => { if (_svBox.IsMouseCaptured) PickSv(e.GetPosition(_svBox)); };
        _svBox.MouseLeftButtonUp += (_, _) => _svBox.ReleaseMouseCapture();
        _svBox.LostMouseCapture += (_, _) => CommitPicker();

        _hueBar = new Border
        {
            Width = 240,
            Height = 14,
            CornerRadius = new CornerRadius(7),
            Cursor = Cursors.Hand,
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0xFF, 0x00, 0x00), 0),
                    new GradientStop(Color.FromRgb(0xFF, 0xFF, 0x00), 0.17),
                    new GradientStop(Color.FromRgb(0x00, 0xFF, 0x00), 0.33),
                    new GradientStop(Color.FromRgb(0x00, 0xFF, 0xFF), 0.5),
                    new GradientStop(Color.FromRgb(0x00, 0x00, 0xFF), 0.67),
                    new GradientStop(Color.FromRgb(0xFF, 0x00, 0xFF), 0.83),
                    new GradientStop(Color.FromRgb(0xFF, 0x00, 0x00), 1),
                },
            },
        };
        _hueThumb = new Ellipse
        {
            Width = 12,
            Height = 12,
            Stroke = Brushes.White,
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(-6, 0, 0, 0),
        };
        var hueGrid = new Grid { Width = 240 };
        hueGrid.Children.Add(_hueBar);
        hueGrid.Children.Add(_hueThumb);
        _hueBar.SizeChanged += (_, _) => UpdatePickerThumbs();
        _hueBar.MouseLeftButtonDown += (_, e) => { _hueBar.CaptureMouse(); PickHue(e.GetPosition(_hueBar)); };
        _hueBar.MouseMove += (_, e) => { if (_hueBar.IsMouseCaptured) PickHue(e.GetPosition(_hueBar)); };
        _hueBar.MouseLeftButtonUp += (_, _) => _hueBar.ReleaseMouseCapture();
        _hueBar.LostMouseCapture += (_, _) => CommitPicker();
        svBoxEl = _svBox;
        hueGridEl = hueGrid;
    }

    private static readonly (string Name, string Bg, string Fg, int Opacity)[] Palettes =
    {
        ("极夜黑", "#FF1E1E22", "#FFEAEAEA", 92),
        ("商务蓝", "#FF14213D", "#FFE5E5E5", 95),
        ("樱花粉", "#FFFFE3EC", "#FF8A4B60", 88),
        ("海盐蓝", "#FFE3F0FF", "#FF2B5C8A", 88),
        ("护眼豆沙", "#FFC7EDCC", "#FF3A5A40", 90),
        ("暖阳橙", "#FFFFE8CC", "#FF8A4B2A", 88),
        ("暗夜紫", "#FF2A1B3D", "#FFE0D0FF", 92),
        ("森林绿", "#FF102D26", "#FFA7F3D0", 92),
        ("柔和靛蓝", "#FF262640", "#FFC7D2FE", 94),
        ("石板灰", "#FF1E293B", "#FFE2E8F0", 94),
        ("雾白", "#FFF8FAFC", "#FF334155", 96),
        ("奶油白", "#FFFBF7F0", "#FF6B5B4E", 90),
    };

    private static Button FlatButton(string text, Action onClick)
    {
        var button = new Button
        {
            Content = text, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x30, 0x3B)), Foreground = FgBrush,
            BorderBrush = LineBrush, BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private void PickSv(Point pt)
    {
        if (_svBox == null) return;
        _sat = Math.Clamp(pt.X / Math.Max(1, _svBox.ActualWidth), 0, 1);
        _val = Math.Clamp(1 - pt.Y / Math.Max(1, _svBox.ActualHeight), 0, 1);
        UpdatePickerThumbs();
        ApplyPicker();
    }

    private void PickHue(Point pt)
    {
        if (_hueBar == null) return;
        _hue = Math.Clamp(pt.X / Math.Max(1, _hueBar.ActualWidth), 0, 0.999) * 360;
        UpdatePickerThumbs();
        ApplyPicker();
    }

    private void UpdatePickerThumbs()
    {
        if (_svBox == null || _svThumb == null || _hueThumb == null || _hueBar == null || _svFill == null) return;
        _svFill.GradientStops[1].Color = HsvToRgb(_hue, 1, 1);
        _svThumb.Margin = new Thickness(_sat * _svBox.ActualWidth - 6, (1 - _val) * _svBox.ActualHeight - 6, 0, 0);
        _hueThumb.Margin = new Thickness(_hue / 360 * _hueBar.ActualWidth - 6, 0, 0, 0);
    }

    private void ApplyPicker()
    {
        var c = HsvToRgb(_hue, _sat, _val);
        var hex = Hex(c);
        if (_editTarget == "bg")
        {
            _cfg.PillBgColor = hex;
            if (_lblBg != null) _lblBg.Text = "底色 " + hex;
        }
        else
        {
            _cfg.PillFgColor = hex;
            if (_lblFg != null) _lblFg.Text = "文字 " + hex;
        }
        _cfg.PillMode = "custom";
        if (_autoSwitch != null) SetSwitchState(_autoSwitch, false);
        if (_lblAutoState != null) _lblAutoState.Text = "自定义颜色";
        SaveCfg();
        RefreshLook();
    }

    // ==================== 词库管理 ====================
    private UIElement BuildDictPage()
    {
        var sp = VStack(PageTitle("词库管理"));

        var packList = new StackPanel();
        var builtin = new (string Id, string Name)[]
        {
            ("internet", "互联网热词"),
            ("ai", "AI 术语"),
            ("shortdrama", "短剧黑话"),
        };
        var packDirectory = Path.Combine(AppPaths.Root, "Data", "Packs");
        Directory.CreateDirectory(packDirectory);
        var names = builtin.ToDictionary(p => p.Id, p => p.Name, StringComparer.OrdinalIgnoreCase);
        names["modern_daily"] = "日常口语与网络表达";
        names["modern_tech"] = "AI、编程与办公术语";
        var discovered = Directory.GetFiles(packDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension).OfType<string>()
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase);
        foreach (var id in discovered)
        {
            var name = names.TryGetValue(id, out var displayName) ? displayName : id;
            var file = Path.Combine(packDirectory, id + ".json");
            int count = 0;
            try { if (File.Exists(file)) count = CountJsonEntries(file); } catch { }
            var on = _cfg.EnabledPacks.Contains(id, StringComparer.OrdinalIgnoreCase);

            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var nameStack = new StackPanel { Orientation = Orientation.Horizontal };
            nameStack.Children.Add(new TextBlock { Text = name, Foreground = FgBrush, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center });
            nameStack.Children.Add(new TextBlock { Text = "  " + count + " 词", Foreground = FgSubBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            row.Children.Add(nameStack);
            var sw = MakeSwitch(on, v =>
            {
                if (v && !_cfg.EnabledPacks.Contains(id, StringComparer.OrdinalIgnoreCase)) _cfg.EnabledPacks.Add(id);
                if (!v) _cfg.EnabledPacks.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
                SaveCfg();
                _reloadDictionary();
            });
            Grid.SetColumn(sw, 1);
            row.Children.Add(sw);
            packList.Children.Add(row);
        }
        sp.Children.Add(Card(VStack(CardTitle("主题词包"), packList,
            FlatButton("打开词包目录", () => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{packDirectory}\"") { UseShellExecute = true })),
            new TextBlock { Text = "启停即时生效。新 JSON 词包放入目录后，重新进入此页即可显示；个人词条可在下方导入 CSV。", TextWrapping = TextWrapping.Wrap, Foreground = FgSubBrush, FontSize = 11.5 })));

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            Height = 230,
            Background = Brushes.Transparent,
            RowBackground = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x28)),
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x30)),
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(1),
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x3D)),
            ItemsSource = _customWords,
        };
        var fgStyle = new Style(typeof(System.Windows.Controls.TextBlock));
        fgStyle.Setters.Add(new Setter(System.Windows.Controls.TextBlock.ForegroundProperty, FgBrush));
        fgStyle.Setters.Add(new Setter(System.Windows.Controls.TextBlock.MarginProperty, new Thickness(8, 6, 8, 6)));
        var headerStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, BgCardBrush));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, FgSubBrush));
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 10, 8, 10)));
        headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, LineBrush));
        headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        grid.ColumnHeaderStyle = headerStyle;
        grid.EnableRowVirtualization = true;
        grid.EnableColumnVirtualization = true;
        AddTextCol(grid, "词条", "Word", new DataGridLength(1, DataGridLengthUnitType.Star), fgStyle);
        AddTextCol(grid, "拼音", "Pinyin", new DataGridLength(1, DataGridLengthUnitType.Star), fgStyle);
        AddTextCol(grid, "释义（多个用 ; 分隔）", "Meaning", new DataGridLength(2, DataGridLengthUnitType.Star), fgStyle);

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        toolbar.Children.Add(FlatButton("＋ 新增", () => _customWords.Add(new CustomWord("新词条", "", ""))));
        toolbar.Children.Add(FlatButton("－ 删除选中", () =>
        {
            for (int i = grid.SelectedItems.Count - 1; i >= 0; i--)
                if (grid.SelectedItems[i] is CustomWord cw) _customWords.Remove(cw);
        }));
        toolbar.Children.Add(FlatButton("导入 CSV", () =>
        {
            var dlg = new WinForms.OpenFileDialog { Filter = "CSV 文件 (*.csv)|*.csv", Title = "导入自定义词库" };
            if (dlg.ShowDialog() == WinForms.DialogResult.OK)
            {
                foreach (var w in CustomStore.ImportCsv(dlg.FileName))
                    if (_customWords.All(x => !string.Equals(x.Word, w.Word, StringComparison.Ordinal)))
                        _customWords.Add(w);
                MessageBox.Show(this, $"导入完成，当前 {_customWords.Count} 条。点「保存并重载」生效。", "GlintWin");
            }
        }));
        toolbar.Children.Add(FlatButton("导出 CSV", () =>
        {
            var dlg = new WinForms.SaveFileDialog { Filter = "CSV 文件 (*.csv)|*.csv", FileName = "glintwin_custom.csv" };
            if (dlg.ShowDialog() == WinForms.DialogResult.OK)
            {
                CustomStore.ExportCsv(dlg.FileName, _customWords);
                MessageBox.Show(this, "导出完成。", "GlintWin");
            }
        }));
        toolbar.Children.Add(FlatButton("查看模板", () =>
        {
            CustomStore.Save(_customWords);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{CustomStore.TemplatePath}\"") { UseShellExecute = true });
        }));

        var hint = new TextBlock
        {
            Text = "模板：Data\\custom_template.csv（列：词条,拼音,释义1[,释义2,释义3]，UTF-8 带 BOM）。自定义词条优先于主词典与主题词包。",
            Foreground = FgSubBrush,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        };

        var saveBtn = FlatButton("保存并重载词典", () =>
        {
            CustomStore.Save(_customWords);
            _reloadDictionary();
            MessageBox.Show(this, "自定义词库已保存并重载。", "GlintWin");
        });

        sp.Children.Add(Card(VStack(CardTitle("自定义词库"), toolbar, grid, hint,
            new StackPanel { Margin = new Thickness(0, 10, 0, 0), Children = { saveBtn } })));

        return PageScroll(sp);
    }

    private static void AddTextCol(DataGrid grid, string header, string path, DataGridLength width, Style elStyle)
    {
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new System.Windows.Data.Binding(path),
            Width = width,
            MinWidth = path == "Meaning" ? 220 : 110,
            ElementStyle = elStyle,
        });
    }

    private static int CountJsonEntries(string path)
    {
        try
        {
            var dto = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(File.ReadAllText(path));
            return dto?.Count ?? 0;
        }
        catch { return 0; }
    }

    // ==================== 通用设置 ====================
    private UIElement BuildSettingsPage()
    {
        var sp = VStack(PageTitle("通用设置"));

        var startup = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 10),
            Children =
            {
                MakeSwitch(StartupEnabled(), SetStartup),
                new TextBlock { Text = "开机自动启动", Foreground = FgBrush, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center },
            }
        };
        sp.Children.Add(Card(VStack(
            CardTitle("启动与后台"),
            startup,
            new TextBlock { Text = "登录 Windows 后自动运行，安静驻留在系统托盘。", Foreground = FgSubBrush, FontSize = 11.5 })));

        sp.Children.Add(Card(VStack(
            CardTitle("关于"),
            new TextBlock { Text = "GlintWin v0.4.3 — 输入中文，顺手看见英文。", Foreground = FgBrush, Margin = new Thickness(0, 0, 0, 4) },
            new TextBlock { Text = "全离线运行，不上传输入。每日词汇默认关闭，开启后仅本地保存待核对词条。", Foreground = FgSubBrush, Margin = new Thickness(0, 0, 0, 4) },
            new TextBlock { Text = "词典：CC-CEDICT（CC-BY-SA 4.0）+ 主题词包 + 自定义词库。", Foreground = FgSubBrush })));

        var btnLogs = FlatButton("打开日志文件夹", () =>
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GlintWin", "logs");
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        });
        var btnExit = FlatButton("退出 GlintWin", () => Application.Current.Shutdown());
        btnExit.Margin = new Thickness(0);
        sp.Children.Add(Card(VStack(CardTitle("支持与诊断"), new StackPanel { Orientation = Orientation.Horizontal, Children = { btnLogs, btnExit } })));

        return PageScroll(sp);
    }

    private static void SetSwitchState(Border track, bool on)
    {
        track.Tag = on;
        track.Background = on ? AccentBrush : new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x60));
        if (track.Child is Ellipse e) e.Margin = new Thickness(on ? 23 : 3, 0, on ? 3 : 23, 0);
    }

    private Border MakeSwitch(bool isOn, Action<bool> onChange)
    {
        var thumb = new Ellipse
        {
            Width = 16,
            Height = 16,
            Fill = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(isOn ? 23 : 3, 0, isOn ? 3 : 23, 0),
        };
        var track = new Border
        {
            Width = 42,
            Height = 21,
            CornerRadius = new CornerRadius(11),
            Background = isOn ? AccentBrush : new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x60)),
            Cursor = Cursors.Hand,
            Child = thumb,
            Tag = isOn,
        };
        track.MouseLeftButtonUp += (_, _) =>
        {
            bool now = track.Tag is not true;
            track.Tag = now;
            track.Background = now ? AccentBrush : new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x60));
            thumb.Margin = new Thickness(now ? 23 : 3, 0, now ? 3 : 23, 0);
            onChange(now);
        };
        return track;
    }

    private static Brush CheckerBrush()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing
        {
            Brush = new SolidColorBrush(Color.FromRgb(0x23, 0x27, 0x30)),
            Geometry = new RectangleGeometry(new Rect(0, 0, 20, 20)),
        });
        var gray = new SolidColorBrush(Color.FromRgb(0x2D, 0x32, 0x3D));
        group.Children.Add(new GeometryDrawing { Brush = gray, Geometry = new RectangleGeometry(new Rect(0, 0, 10, 10)) });
        group.Children.Add(new GeometryDrawing { Brush = gray, Geometry = new RectangleGeometry(new Rect(10, 10, 10, 10)) });
        return new DrawingBrush
        {
            Drawing = group,
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 20, 20),
            Viewbox = new Rect(0, 0, 20, 20),
        };
    }

    // ---- 外观页状态 ----
    private void LoadLookFromCfg()
    {
        _loadingLook = true;
        bool custom = string.Equals(_cfg.PillMode, "custom", StringComparison.OrdinalIgnoreCase);
        if (_autoSwitch != null) SetSwitchState(_autoSwitch, !custom);
        if (_lblAutoState != null) _lblAutoState.Text = custom ? "自定义颜色" : "跟随皮肤自动取色";
        if (_slider != null) _slider.Value = Math.Clamp(_cfg.PillOpacityPercent, 40, 100);
        if (_lblOpacity != null) _lblOpacity.Text = _cfg.PillOpacityPercent + "%";
        _loadingLook = false;
        SetEditTarget(_editTarget);
        RefreshLook();
    }

    private void RefreshLook()
    {
        bool custom = string.Equals(_cfg.PillMode, "custom", StringComparison.OrdinalIgnoreCase);
        byte alpha = (byte)Math.Clamp(_cfg.PillOpacityPercent * 255 / 100, 40, 255);
        Color bg, fg;
        if (custom)
        {
            bg = ParseColor(_cfg.PillBgColor, Color.FromRgb(0x2A, 0x2C, 0x30));
            fg = ParseColor(_cfg.PillFgColor, Color.FromRgb(0xF2, 0xF5, 0xFA));
        }
        else
        {
            var t = LastThemeProvider?.Invoke();
            bg = t?.Bg ?? Color.FromRgb(0x2A, 0x2C, 0x30);
            fg = t?.Fg ?? Color.FromRgb(0xF2, 0xF5, 0xFA);
        }
        if (_previewBorder != null)
            _previewBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, bg.R, bg.G, bg.B));
        if (_previewText != null)
            _previewText.Foreground = new SolidColorBrush(fg);
        if (_lblBg != null) _lblBg.Text = "底色 " + _cfg.PillBgColor;
        if (_lblFg != null) _lblFg.Text = "文字 " + _cfg.PillFgColor;
    }

    private void SaveCfg()
    {
        _configDirty = true;
        _saveTimer.Stop(); _saveTimer.Start();
    }

    private void FlushConfig()
    {
        _saveTimer.Stop();
        if (!_configDirty) return;
        _cfg.Save(); _configDirty = false;
    }

    private void SetStartup(bool on)
    {
        try
        {
            if (on)
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic lnk = shell.CreateShortcut(StartupLnk);
                lnk.TargetPath = Environment.ProcessPath!;
                lnk.WorkingDirectory = AppContext.BaseDirectory;
                lnk.Arguments = "-tray";
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
        }
    }

    private static string StartupLnk => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), "GlintWin.lnk");

    private static bool StartupEnabled() => File.Exists(StartupLnk);

    private void TrySetIcon()
    {
        try
        {
            var p = Path.Combine(AppPaths.Root, "app.ico");
            if (File.Exists(p))
                Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(p));
        }
        catch
        {
            // 图标缺失不影响功能
        }
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        try { return (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex.Trim()); }
        catch { return fallback; }
    }

    private static string Hex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = v - c;
        (double r, double g, double b) = (h % 360) switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
