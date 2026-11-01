# -*- coding: utf-8 -*-
# ConsoleWindow UI 重构补丁：合并颜色卡 / 导航高亮 / 字体层级 / 开关同步
import io

p = 'ConsoleWindow.cs'
s = io.open(p, encoding='utf-8').read()

# 1) 字段：去掉 rb/colorRow，加目标按钮与自动开关引用
old = '''    private RadioButton? _rbAuto;
    private RadioButton? _rbCustom;
    private StackPanel? _colorRow;'''
new = '''    private Button? _tgtBgBtn;
    private Button? _tgtFgBtn;
    private Border? _autoSwitch;'''
assert old in s, 'fields anchor missing'
s = s.replace(old, new)

# 2) 窗口默认字体
old = '''        Background = BgWindowBrush;
        TrySetIcon();'''
new = '''        Background = BgWindowBrush;
        FontSize = 13.5;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        TrySetIcon();'''
assert old in s, 'font anchor missing'
s = s.replace(old, new)

# 3) NavItem：带高亮竖条
old = '''    private Border NavItem(string key, string glyph, string label)
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 3),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        });
        sp.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13.5,
            VerticalAlignment = VerticalAlignment.Center,
        });
        b.Child = sp;
        b.MouseLeftButtonUp += (_, _) => ShowPage(key);
        b.MouseEnter += (_, _) => { if (_activePage != key) b.Background = BgHoverBrush; };
        b.MouseLeave += (_, _) => { if (_activePage != key) b.Background = Brushes.Transparent; };
        _navItems.Add((b, key));
        return b;
    }'''
new = '''    private Border NavItem(string key, string glyph, string label)
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 3),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
        };
        var accent = new Border
        {
            Width = 3,
            Height = 18,
            CornerRadius = new CornerRadius(2),
            Background = AccentBrush,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 10, 0),
        });
        sp.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13.5,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(accent, 0);
        Grid.SetColumn(sp, 1);
        grid.Children.Add(accent);
        grid.Children.Add(sp);
        b.Child = grid;
        b.Tag = accent;
        b.MouseLeftButtonUp += (_, _) => ShowPage(key);
        b.MouseEnter += (_, _) => { if (_activePage != key) b.Background = BgHoverBrush; };
        b.MouseLeave += (_, _) => { if (_activePage != key) b.Background = Brushes.Transparent; };
        _navItems.Add((b, accent, key));
        return b;
    }'''
assert old in s, 'navitem anchor missing'
s = s.replace(old, new)

# 4) ShowPage：同步高亮竖条
old = '''    private void ShowPage(string key)
    {
        _activePage = key;
        foreach (var (item, k) in _navItems)
            item.Background = k == key ? BgHoverBrush : Brushes.Transparent;'''
new = '''    private void ShowPage(string key)
    {
        _activePage = key;
        foreach (var (item, accent, k) in _navItems)
        {
            item.Background = k == key ? BgHoverBrush : Brushes.Transparent;
            accent.Visibility = k == key ? Visibility.Visible : Visibility.Collapsed;
        }'''
assert old in s, 'showpage anchor missing'
s = s.replace(old, new)
old = '''    private readonly List<(Border Item, string Key)> _navItems = new();'''
new = '''    private readonly List<(Border Item, Border Accent, string Key)> _navItems = new();'''
assert old in s, 'navitems field anchor missing'
s = s.replace(old, new)

# 5) Card 间距
old = '''        Padding = new Thickness(16),
        Margin = new Thickness(0, 0, 0, 14),'''
new = '''        Padding = new Thickness(18, 14, 18, 16),
        Margin = new Thickness(0, 0, 0, 12),'''
assert old in s, 'card margin anchor missing'
s = s.replace(old, new)

io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ui part1 ok')

# 6) BuildLookPage 整体重构 + BuildPicker 拆分
start = s.index('    private UIElement BuildLookPage()')
end = s.index('    private static readonly (string Name, string Bg, string Fg, int Opacity)[] Palettes =')
s = s[:start] + NEW_LOOK + s[end:]
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('look page ok')
