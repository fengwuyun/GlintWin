using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;
namespace GlintWin;
public sealed partial class ConsoleWindow
{
    private readonly SolidColorBrush SidebarBrush = new();
    private readonly SolidColorBrush ButtonBrush = new();
    private sealed class ReviewLabelConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)=>value is true?"待核对":"已核对";
        public object ConvertBack(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)=>throw new NotSupportedException();
    }
    private sealed class PaletteColor : DependencyObject
    {
        public static readonly DependencyProperty ValueProperty=DependencyProperty.Register(nameof(Value),typeof(System.Windows.Media.Color),typeof(PaletteColor));
        public System.Windows.Media.Color Value { get=>(System.Windows.Media.Color)GetValue(ValueProperty); set=>SetValue(ValueProperty,value); }
    }
    private readonly Dictionary<SolidColorBrush,PaletteColor> _paletteBindings=new();
    private void SetPaletteColor(SolidColorBrush brush,System.Windows.Media.Color color)
    {
        if(!_paletteBindings.TryGetValue(brush,out var value))
        {
            value=new PaletteColor();_paletteBindings.Add(brush,value);
            System.Windows.Data.BindingOperations.SetBinding(brush,SolidColorBrush.ColorProperty,new System.Windows.Data.Binding(nameof(PaletteColor.Value)){Source=value});
        }
        value.Value=color;
    }
    private DispatcherTimer? _filterTimer;
    private void ApplyConsoleTheme()
    {
        bool light = _cfg.ConsoleTheme == "light";
        System.Windows.Media.Color C(string value) => (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value);
        SetPaletteColor(BgWindowBrush,C(light?"#F3F5F9":"#13151A"));
        SetPaletteColor(BgCardBrush,C(light?"#FFFFFF":"#1C1F27"));
        SetPaletteColor(SidebarBrush,C(light?"#E9EDF5":"#101217"));
        SetPaletteColor(ButtonBrush,C(light?"#E7EBF3":"#2B303B"));
        SetPaletteColor(FgBrush,C(light?"#172033":"#EFF0F4"));
        SetPaletteColor(FgSubBrush,C(light?"#58657A":"#9CA0AD"));
        SetPaletteColor(LineBrush,C(light?"#D9DFEA":"#2C303B"));
        SetPaletteColor(NavIdleBrush,C(light?"#46536A":"#C5C9D4"));
        foreach(var pair in new[]{("Surface",BgCardBrush),("Input",BgWindowBrush),("Text",FgBrush),("Muted",FgSubBrush),("Line",LineBrush),("Button",ButtonBrush),("Accent",AccentBrush)}) Resources[pair.Item1]=pair.Item2;
    }
    private void SetConsoleTheme(string theme)
    {
        _cfg.ConsoleTheme=theme=="light"?"light":"dark";
        ApplyConsoleTheme();SaveCfg();FlushConfig();
        var handle=new WindowInteropHelper(this).Handle;
        if(handle!=IntPtr.Zero){int dark=_cfg.ConsoleTheme=="light"?0:1;DwmSetWindowAttribute(handle,20,ref dark,sizeof(int));}
    }
    private void QueueDailyFilter(bool debounce)
    {
        _filterTimer?.Stop();_dailyRequest++;_dailyPage=0;
        if(!debounce){_ = LoadDailyAsync();return;}
        if(_filterTimer==null){_filterTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(220)};_filterTimer.Tick+=(_,_)=>{_filterTimer.Stop();if(_activePage=="daily")_ = LoadDailyAsync();};Closed+=(_,_)=>_filterTimer.Stop();}
        _filterTimer.Start();
    }
    private void InstallCollectionStyles()
    {
        const string xaml="""
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
 <Style TargetType="CheckBox"><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CheckBox"><StackPanel Orientation="Horizontal"><Border Name="box" Width="16" Height="16" CornerRadius="4" BorderThickness="1" BorderBrush="{DynamicResource Line}" Background="{DynamicResource Input}" Margin="0,0,8,0"><TextBlock Name="tick" Text="✓" Foreground="White" FontSize="12" HorizontalAlignment="Center" VerticalAlignment="Center" Visibility="Collapsed"/></Border><ContentPresenter VerticalAlignment="Center"/></StackPanel><ControlTemplate.Triggers><Trigger Property="IsChecked" Value="True"><Setter TargetName="box" Property="Background" Value="{DynamicResource Accent}"/><Setter TargetName="tick" Property="Visibility" Value="Visible"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="box" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType="ComboBox">
  <Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Background" Value="{DynamicResource Input}"/><Setter Property="BorderBrush" Value="{DynamicResource Line}"/><Setter Property="MinHeight" Value="36"/><Setter Property="VerticalContentAlignment" Value="Center"/>
  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox"><Grid>
   <ToggleButton Focusable="False" IsChecked="{Binding IsDropDownOpen,RelativeSource={RelativeSource TemplatedParent},Mode=TwoWay}" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"><ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="7"><TextBlock Text="⌄" Foreground="{DynamicResource Muted}" HorizontalAlignment="Right" Margin="0,0,10,0" VerticalAlignment="Center"/></Border></ControlTemplate></ToggleButton.Template></ToggleButton>
   <ContentPresenter Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" IsHitTestVisible="False" Margin="10,0,28,0" VerticalAlignment="Center"/>
   <Popup Name="PART_Popup" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}" AllowsTransparency="True" Focusable="False"><Border MinWidth="{Binding ActualWidth,RelativeSource={RelativeSource TemplatedParent}}" Background="{DynamicResource Surface}" BorderBrush="{DynamicResource Line}" BorderThickness="1" CornerRadius="7" Padding="4"><ScrollViewer MaxHeight="280"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer></Border></Popup>
  </Grid></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType="ComboBoxItem"><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Padding" Value="10,8"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem"><Border Name="b" Background="Transparent" CornerRadius="4" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="b" Property="Background" Value="{DynamicResource Button}"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter TargetName="b" Property="Background" Value="{DynamicResource Button}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType="TextBox"><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Background" Value="{DynamicResource Input}"/><Setter Property="BorderBrush" Value="{DynamicResource Line}"/><Setter Property="CaretBrush" Value="{DynamicResource Text}"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TextBox"><Border Name="b" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="7" Padding="{TemplateBinding Padding}"><ScrollViewer Name="PART_ContentHost"/></Border><ControlTemplate.Triggers><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType="DatePicker"><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="DatePicker"><Border Background="{DynamicResource Input}" BorderBrush="{DynamicResource Line}" BorderThickness="1" CornerRadius="7"><Grid><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="30"/></Grid.ColumnDefinitions><DatePickerTextBox Name="PART_TextBox" Foreground="{DynamicResource Text}" Background="{DynamicResource Input}"/><Button Name="PART_Button" Grid.Column="1" Content="▦" Foreground="{DynamicResource Muted}" Background="Transparent" BorderThickness="0" Padding="0" MinHeight="0" Focusable="False"/><Popup Name="PART_Popup" Placement="Bottom" AllowsTransparency="True" StaysOpen="False"/></Grid></Border></ControlTemplate></Setter.Value></Setter><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Background" Value="{DynamicResource Input}"/><Setter Property="BorderBrush" Value="{DynamicResource Line}"/><Setter Property="MinHeight" Value="36"/></Style>
 <Style TargetType="DatePickerTextBox"><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Background" Value="{DynamicResource Input}"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="DatePickerTextBox"><Border Background="{TemplateBinding Background}" Padding="8,6"><ScrollViewer Name="PART_ContentHost"/></Border></ControlTemplate></Setter.Value></Setter></Style>
 <Style x:Key="CalendarPanel" TargetType="CalendarItem"><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CalendarItem"><Border Background="{DynamicResource Surface}" BorderBrush="{DynamicResource Line}" BorderThickness="1" CornerRadius="8" Padding="10"><StackPanel><Grid><Grid.ColumnDefinitions><ColumnDefinition Width="32"/><ColumnDefinition/><ColumnDefinition Width="32"/></Grid.ColumnDefinitions><Button Name="PART_PreviousButton" Content="‹" Background="{DynamicResource Button}" Foreground="{DynamicResource Text}"/><Button Name="PART_HeaderButton" Grid.Column="1" Background="Transparent" Foreground="{DynamicResource Text}"/><Button Name="PART_NextButton" Grid.Column="2" Content="›" Background="{DynamicResource Button}" Foreground="{DynamicResource Text}"/></Grid><Grid Name="PART_Root"><UniformGrid Name="PART_MonthView" Columns="7" Rows="7"/><UniformGrid Name="PART_YearView" Columns="4" Rows="3" Visibility="Collapsed"/></Grid></StackPanel></Border><ControlTemplate.Triggers><DataTrigger Binding="{Binding DisplayMode,RelativeSource={RelativeSource AncestorType=Calendar}}" Value="Year"><Setter TargetName="PART_MonthView" Property="Visibility" Value="Collapsed"/><Setter TargetName="PART_YearView" Property="Visibility" Value="Visible"/></DataTrigger><DataTrigger Binding="{Binding DisplayMode,RelativeSource={RelativeSource AncestorType=Calendar}}" Value="Decade"><Setter TargetName="PART_MonthView" Property="Visibility" Value="Collapsed"/><Setter TargetName="PART_YearView" Property="Visibility" Value="Visible"/></DataTrigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType="CalendarDayButton"><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="MinWidth" Value="30"/><Setter Property="MinHeight" Value="30"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CalendarDayButton"><Border Name="d" Background="Transparent" CornerRadius="5" Padding="4"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsSelected" Value="True"><Setter TargetName="d" Property="Background" Value="{DynamicResource Accent}"/><Setter Property="Foreground" Value="White"/></Trigger><Trigger Property="IsInactive" Value="True"><Setter Property="Opacity" Value="0.45"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.3"/></Trigger><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="d" Property="Opacity" Value="0.65"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType="CalendarButton"><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Background" Value="{DynamicResource Button}"/></Style>
 <Style TargetType="Calendar"><Setter Property="CalendarItemStyle" Value="{StaticResource CalendarPanel}"/><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Background" Value="{DynamicResource Surface}"/><Setter Property="BorderBrush" Value="{DynamicResource Line}"/></Style>
 <Style TargetType="DataGridRow"><Setter Property="Foreground" Value="{DynamicResource Text}"/><Style.Triggers><Trigger Property="IsSelected" Value="True"><Setter Property="Background" Value="{DynamicResource Button}"/></Trigger></Style.Triggers></Style>
 <Style TargetType="DataGridCell"><Setter Property="BorderThickness" Value="0"/><Style.Triggers><Trigger Property="IsSelected" Value="True"><Setter Property="Background" Value="{DynamicResource Button}"/><Setter Property="Foreground" Value="{DynamicResource Text}"/></Trigger></Style.Triggers></Style>
</ResourceDictionary>
""";
        Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Parse(xaml));
    }
}
