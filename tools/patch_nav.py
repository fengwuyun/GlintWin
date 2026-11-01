# -*- coding: utf-8 -*-
# 补丁（重跑版）：导航可读性 + 候选行锚定回退
import io

p = 'ConsoleWindow.cs'
s = io.open(p, encoding='utf-8').read()

# 1) 窗口默认前景色
if 'Foreground = FgBrush;\n        FontSize = 13.5;' not in s:
    old = '''        Background = BgWindowBrush;
        FontSize = 13.5;'''
    new = '''        Background = BgWindowBrush;
        Foreground = FgBrush;
        FontSize = 13.5;'''
    assert old in s, 'fg anchor missing'
    s = s.replace(old, new)

# 2) NavItem 重构（幂等：已重构则跳过）
if 'private readonly List<(Border Item, Border Accent, TextBlock Label, TextBlock Glyph, string Key)> _navItems' not in s:
    old = '''    private Border NavItem(string key, string glyph, string label)
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
        var glyphTb = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 10, 0),
            Foreground = NavIdleBrush,
        };
        var labelTb = new TextBlock
        {
            Text = label,
            FontSize = 13.5,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = NavIdleBrush,
        };
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
        b.MouseEnter += (_, _) => { if (_activePage != key) b.Background = BgHoverBrush; };
        b.MouseLeave += (_, _) => { if (_activePage != key) b.Background = Brushes.Transparent; };
        _navItems.Add((b, accent, labelTb, glyphTb, key));
        return b;
    }'''
    assert old in s, 'navitem anchor missing'
    s = s.replace(old, new)

# 3) ShowPage 前景色同步（幂等）
if 'foreach (var (item, accent, labelTb, glyphTb, k) in _navItems)' not in s:
    old = '''        foreach (var (item, accent, k) in _navItems)
        {
            item.Background = k == key ? BgHoverBrush : Brushes.Transparent;
            accent.Visibility = k == key ? Visibility.Visible : Visibility.Collapsed;
        }'''
    new = '''        foreach (var (item, accent, labelTb, glyphTb, k) in _navItems)
        {
            bool active = k == key;
            item.Background = active ? BgHoverBrush : Brushes.Transparent;
            accent.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            labelTb.Foreground = active ? FgBrush : NavIdleBrush;
            glyphTb.Foreground = active ? AccentBrush : NavIdleBrush;
        }'''
    assert old in s, 'showpage anchor missing'
    s = s.replace(old, new)

# 4) 元组字段（幂等）
if 'private readonly List<(Border Item, Border Accent, TextBlock Label, TextBlock Glyph, string Key)> _navItems' not in s:
    old = '''    private readonly List<(Border Item, Border Accent, string Key)> _navItems = new();'''
    new = '''    private readonly List<(Border Item, Border Accent, TextBlock Label, TextBlock Glyph, string Key)> _navItems = new();
    private static readonly Brush NavIdleBrush = new SolidColorBrush(Color.FromRgb(0xC5, 0xC9, 0xD4));'''
    assert old in s, 'navitems field anchor missing'
    s = s.replace(old, new)

# 5) 分组标签提亮（幂等）
if 'FontSize = 11.5,\n        Foreground = new SolidColorBrush(Color.FromRgb(0xAE, 0xB3, 0xC2)),' not in s:
    old = '''    private static TextBlock NavLabel(string t) => new()
    {
        Text = t,
        FontSize = 11,
        Foreground = FgSubBrush,
        Margin = new Thickness(8, 10, 0, 6),
    };'''
    new = '''    private static TextBlock NavLabel(string t) => new()
    {
        Text = t,
        FontSize = 11.5,
        Foreground = new SolidColorBrush(Color.FromRgb(0xAE, 0xB3, 0xC2)),
        Margin = new Thickness(8, 10, 0, 6),
    };'''
    assert old in s, 'navlabel anchor missing'
    s = s.replace(old, new)

# 6) 位置：回到候选行锚定（幂等）
if '(anchor, anchorOffset) = (rect, 6);' in s:
    old = '''            // 产品规则：悬浮条恒在搜狗输入窗（拼音框+候选行）正下方 —— 与 OCR 质量解耦，永不跑到上方
            (anchor, anchorOffset) = (rect, 6);'''
    new = '''            // 产品规则：悬浮条贴在候选行正下方（用户框选位置）。
            // 锚点取可信帧的行框；不可信帧沿用上一帧位置，连续 3 帧不可信则整体隐藏（防冻结残留）。
            (anchor, anchorOffset) = _lastHasRow ? (_lastRowRect, 4) : (rect, 6);'''
    assert old in s, 'anchor revert missing'
    s = s.replace(old, new)

io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('patched')
