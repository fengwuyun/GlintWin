# -*- coding: utf-8 -*-
# 外观页重构：合并颜色卡 + BuildPicker(out) + 开关/目标状态同步
import io

p = 'ConsoleWindow.cs'
s = io.open(p, encoding='utf-8').read()

start = s.index('    private UIElement BuildLookPage()')
end = s.index('    private static readonly (string Name, string Bg, string Fg, int Opacity)[] Palettes =')

NEW_LOOK = '''    private UIElement BuildLookPage()
    {
        var sp = VStack(PageTitle("悬浮条外观"));

        // 跟随皮肤开关（关 = 自定义颜色）
        var autoRow = new StackPanel { Orientation = Orientation.Horizontal };
        _lblAutoState = new TextBlock
        {
            Text = "跟随皮肤自动取色",
            Foreground = FgBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        _autoSwitch = MakeSwitch(!string.Equals(_cfg.PillMode, "custom", StringComparison.OrdinalIgnoreCase), v =>
        {
            _cfg.PillMode = v ? "auto" : "custom";
            SaveCfg();
            if (_lblAutoState != null) _lblAutoState.Text = v ? "跟随皮肤自动取色" : "自定义颜色";
            RefreshLook();
        });
        autoRow.Children.Add(_autoSwitch);
        autoRow.Children.Add(_lblAutoState);
        sp.Children.Add(Card(VStack(CardTitle("取色模式"), autoRow,
            new TextBlock { Text = "开启后悬浮条颜色实时跟随输入法皮肤；关闭后使用下方自定义颜色。", Foreground = FgSubBrush, FontSize = 11.5 })));

        var tgtBg = FlatButton("底色", () => SetEditTarget("bg"));
        var tgtFg = FlatButton("文字色", () => SetEditTarget("fg"));
        _tgtBgBtn = tgtBg;
        _tgtFgBtn = tgtFg;

        var paletteWrap = new WrapPanel();
        foreach (var p in Palettes)
        {
            var sw = new Border
            {
                Width = 76,
                Height = 48,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 8, 8),
                Background = new SolidColorBrush(ParseColor(p.Bg, Colors.Gray)),
                Cursor = Cursors.Hand,
                ToolTip = p.Name,
                Child = new TextBlock
                {
                    Text = "学习",
                    Foreground = new SolidColorBrush(ParseColor(p.Fg, Colors.White)),
                    FontSize = 12.5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var bg = p.Bg;
            var fg = p.Fg;
            var op = p.Opacity;
            sw.MouseLeftButtonUp += (_, _) =>
            {
                _cfg.PillMode = "custom";
                _cfg.PillBgColor = bg;
                _cfg.PillFgColor = fg;
                _cfg.PillOpacityPercent = op;
                LoadLookFromCfg();
                SaveCfg();
            };
            paletteWrap.Children.Add(sw);
        }

        BuildPicker(out var svBoxEl, out var hueGridEl);

        _slider = new Slider { Minimum = 40, Maximum = 100, Width = 320, VerticalAlignment = VerticalAlignment.Center };
        _lblOpacity = new TextBlock { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = FgBrush };
        _slider.ValueChanged += (_, _) =>
        {
            _cfg.PillOpacityPercent = (int)_slider.Value;
            if (_lblOpacity != null) _lblOpacity.Text = ((int)_slider.Value) + "%";
            RefreshLook();
        };
        var sliderRow = new StackPanel { Orientation = Orientation.Horizontal };
        sliderRow.Children.Add(_slider);
        sliderRow.Children.Add(_lblOpacity);

        var colorCard = Card(VStack(
            new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12), Children = { tgtBg, tgtFg } },
            new TextBlock { Text = "调色盘", Foreground = FgSubBrush, FontSize = 11.5, Margin = new Thickness(0, 0, 0, 6) },
            paletteWrap,
            new TextBlock { Text = "精细取色", Foreground = FgSubBrush, FontSize = 11.5, Margin = new Thickness(0, 12, 0, 6) },
            svBoxEl,
            new StackPanel { Margin = new Thickness(0, 12, 0, 0), Children = { hueGridEl } },
            _lblBg,
            _lblFg,
            new TextBlock { Text = "不透明度", Foreground = FgSubBrush, FontSize = 11.5, Margin = new Thickness(0, 12, 0, 6) },
            sliderRow));
        sp.Children.Add(colorCard);

        _previewText = new TextBlock
        {
            Text = "学习   learn · study",
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 15,
        };
        _previewBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 8, 16, 9),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = _previewText,
        };
        var checker = new Border
        {
            Background = CheckerBrush(),
            Padding = new Thickness(2),
            MinHeight = 72,
            VerticalAlignment = VerticalAlignment.Center,
            Child = _previewBorder,
        };
        sp.Children.Add(Card(VStack(CardTitle("实时预览（棋盘格代表应用背景）"), checker)));

        var page = PageScroll(sp);
        LoadLookFromCfg();
        return page;
    }

    private void SetEditTarget(string t)
    {
        _editTarget = t;
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
        _svBox.MouseLeftButtonDown += (_, e) => { _svBox.CaptureMouse(); PickSv(e.GetPosition(_svBox)); };
        _svBox.MouseMove += (_, e) => { if (_svBox.IsMouseCaptured) PickSv(e.GetPosition(_svBox)); };
        _svBox.MouseLeftButtonUp += (_, _) => _svBox.ReleaseMouseCapture();

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
        _hueBar.MouseLeftButtonDown += (_, e) => { _hueBar.CaptureMouse(); PickHue(e.GetPosition(_hueBar)); };
        _hueBar.MouseMove += (_, e) => { if (_hueBar.IsMouseCaptured) PickHue(e.GetPosition(_hueBar)); };
        _hueBar.MouseLeftButtonUp += (_, _) => _hueBar.ReleaseMouseCapture();
        svBoxEl = _svBox;
        hueGridEl = hueGrid;
    }

'''

s = s[:start] + NEW_LOOK + s[end:]

# 字段补充
old = '''    private string _editTarget = "bg";'''
new = '''    private string _editTarget = "bg";
    private Button? _tgtBgBtn;
    private Button? _tgtFgBtn;
    private Border? _autoSwitch;
    private TextBlock? _lblAutoState;'''
assert old in s, 'field anchor missing'
s = s.replace(old, new)

# LoadLookFromCfg 重写
old = '''    private void LoadLookFromCfg()
    {
        bool custom = string.Equals(_cfg.PillMode, "custom", StringComparison.OrdinalIgnoreCase);
        if (_rbAuto != null) _rbAuto.IsChecked = !custom;
        if (_rbCustom != null) _rbCustom.IsChecked = custom;
        if (_colorRow != null) _colorRow.IsEnabled = custom;
        if (_slider != null) _slider.Value = Math.Clamp(_cfg.PillOpacityPercent, 40, 100);
        RefreshLook();
    }'''
new = '''    private void LoadLookFromCfg()
    {
        bool custom = string.Equals(_cfg.PillMode, "custom", StringComparison.OrdinalIgnoreCase);
        if (_autoSwitch != null) SetSwitchState(_autoSwitch, !custom);
        if (_lblAutoState != null) _lblAutoState.Text = custom ? "自定义颜色" : "跟随皮肤自动取色";
        if (_slider != null) _slider.Value = Math.Clamp(_cfg.PillOpacityPercent, 40, 100);
        SetEditTarget(_editTarget);
        RefreshLook();
    }'''
assert old in s, 'loadfromcfg anchor missing'
s = s.replace(old, new)

# ApplyPicker：同步开关
old = '''        _cfg.PillMode = "custom";
        if (_rbCustom != null) _rbCustom.IsChecked = true;
        SaveCfg();
        RefreshLook();'''
new = '''        _cfg.PillMode = "custom";
        if (_autoSwitch != null) SetSwitchState(_autoSwitch, false);
        if (_lblAutoState != null) _lblAutoState.Text = "自定义颜色";
        SaveCfg();
        RefreshLook();'''
assert old in s, 'applypicker anchor missing'
s = s.replace(old, new)

# SetSwitchState 小件
old = '''    private Border MakeSwitch(bool isOn, Action<bool> onChange)'''
new = '''    private static void SetSwitchState(Border track, bool on)
    {
        track.Tag = on;
        track.Background = on ? AccentBrush : new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x60));
        if (track.Child is Ellipse e) e.Margin = new Thickness(on ? 23 : 3, 0, on ? 3 : 23, 0);
    }

    private Border MakeSwitch(bool isOn, Action<bool> onChange)'''
assert old in s, 'setswitch anchor missing'
s = s.replace(old, new)

io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('look page patched')
