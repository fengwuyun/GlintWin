using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CheckBox = System.Windows.Controls.CheckBox;
using Button = System.Windows.Controls.Button;
namespace GlintWin;
public sealed partial class ConsoleWindow
{
    private readonly HashSet<string> _dailyStatuses=new();
    private readonly Dictionary<string,CheckBox> _statusChecks=new();
    private Button? _statusButton;
    private bool _settingStatuses;
    private UIElement BuildStatusFilter()
    {
        _dailyStatuses.Clear();_statusChecks.Clear();
        var list=new StackPanel();
        _statusButton=FlatButton("全部状态 ⌄",()=>{});_statusButton.Width=124;_statusButton.Margin=new Thickness(0,0,8,0);
        var popup=new Popup{PlacementTarget=_statusButton,Placement=PlacementMode.Bottom,StaysOpen=false,AllowsTransparency=true};
        popup.Child=new Border{Background=BgCardBrush,BorderBrush=LineBrush,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(7),Padding=new Thickness(12),Child=list};
        _statusButton.Click+=(_,_)=>popup.IsOpen=!popup.IsOpen;
        list.Children.Add(FlatButton("全部 / 清空",()=>SetDailyStatuses("")));
        foreach(var status in new[]{"待核对","待翻译","已有释义","已忽略","已入库"})
        {
            var check=new CheckBox{Content=status,Foreground=FgBrush,Margin=new Thickness(0,9,0,0),IsChecked=false};
            _statusChecks[status]=check;list.Children.Add(check);
            check.Checked+=(_,_)=>{if(!_settingStatuses)SetDailyStatuses(string.Join("|",_statusChecks.Where(p=>p.Value.IsChecked==true).Select(p=>p.Key)));};
            check.Unchecked+=(_,_)=>{if(!_settingStatuses)SetDailyStatuses(string.Join("|",_statusChecks.Where(p=>p.Value.IsChecked==true).Select(p=>p.Key)));};
        }
        var host=new Grid();host.Children.Add(_statusButton);host.Children.Add(popup);return host;
    }
    private void SetDailyStatuses(string value)
    {
        _settingStatuses=true;
        try{_dailyStatuses.Clear();foreach(var status in value.Split('|',StringSplitOptions.RemoveEmptyEntries))if(_statusChecks.ContainsKey(status))_dailyStatuses.Add(status);
            foreach(var pair in _statusChecks)pair.Value.IsChecked=_dailyStatuses.Contains(pair.Key);
            if(_statusButton!=null){_statusButton.Content=_dailyStatuses.Count==0?"全部状态 ⌄":_dailyStatuses.Count==1?_dailyStatuses.Single()+" ⌄":$"已选 {_dailyStatuses.Count} 项 ⌄";_statusButton.ToolTip=_dailyStatuses.Count==0?"全部状态":string.Join("、",_dailyStatuses);}
        }finally{_settingStatuses=false;}
        QueueDailyFilter(false);
    }
}
