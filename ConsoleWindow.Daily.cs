using System.IO;
using System.Text;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Control = System.Windows.Controls.Control;
using Binding = System.Windows.Data.Binding;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using DataGrid = System.Windows.Controls.DataGrid;
using Orientation = System.Windows.Controls.Orientation;
using MessageBox = System.Windows.MessageBox;
using WinForms = System.Windows.Forms;
namespace GlintWin;
public sealed partial class ConsoleWindow
{
    private DataGrid? _dailyGrid;
    private DatePicker? _dailyDate;
    private ComboBox? _dailyFilter, _dailySort;
    private TextBox? _dailySearch, _dailyWord, _dailyPinyin, _dailyMeaning;
    private TextBlock? _dailySummary;
    private int _dailyPage, _dailyRequest;
    private UIElement BuildDailyPage()
    {
        var sp = VStack(PageTitle("每日词汇"), Description("把每天遇到的词留在本机，核对后补充释义，再加入个人词库。"));
        if (_daily == null) { sp.Children.Add(Card(Description("每日词汇数据库未能打开，请查看诊断日志。其他功能仍可使用。"))); return PageScroll(sp); }
        var stateText = new TextBlock { Text = _cfg.AutoCollectDailyInput ? "自动采集已开启" : "自动采集已关闭", Foreground = FgBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12,0,0,0) };
        var toggle = MakeSwitch(_cfg.AutoCollectDailyInput, on =>
        {
            _cfg.AutoCollectDailyInput = on; _daily.RefreshEnabled(); SaveCfg(); FlushConfig();
            stateText.Text = on ? "自动采集已开启" : "自动采集已关闭";
        });
        var excluded = DailyInput(string.Join("; ", _cfg.CollectionExcludedApps ?? new()));
        excluded.ToolTip = "进程名称，用分号隔开，例如：app.exe; another.exe";
        var saveExcluded = FlatButton("保存排除项", () =>
        {
            _cfg.CollectionExcludedApps = excluded.Text.Split(new[]{';','；',',','，','\n'}, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _daily.RefreshEnabled(); SaveCfg(); FlushConfig();
            if (_dailySummary != null) _dailySummary.Text = "应用排除列表已保存";
        });
        var exclusionRow = new Grid(); exclusionRow.ColumnDefinitions.Add(new ColumnDefinition()); exclusionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(saveExcluded,1); exclusionRow.Children.Add(excluded); exclusionRow.Children.Add(saveExcluded);
        sp.Children.Add(Card(VStack(new StackPanel { Orientation = Orientation.Horizontal, Children = { toggle, stateText } },
            new TextBlock { Text = "仅采集空格或可识别数字选中的候选，全部先标记待核对；鼠标选词、翻页后无法确认的候选会跳过。托盘“暂停”同时暂停采集。", Foreground = FgSubBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,10,0,12) },
            new TextBlock { Text = "不采集这些应用（进程名，用分号分隔）", Foreground = FgSubBrush, FontSize = 12, Margin = new Thickness(0,0,0,7) }, exclusionRow)));
        _dailyDate = new DatePicker { SelectedDate = DateTime.Today, Width = 136, Margin = new Thickness(0,0,8,0) };
        _dailyFilter = new ComboBox { Width = 108, Margin = new Thickness(0,0,8,0), ItemsSource = new[]{"全部","待核对","待翻译","已有释义","已忽略","已入库"}, SelectedIndex = 0 };
        _dailySearch = DailyInput(""); _dailySearch.Width = 155; _dailySearch.ToolTip = "搜索词条、拼音或释义";
        _dailySort = new ComboBox { Width = 100, Margin = new Thickness(0,0,8,0), ItemsSource = new[]{"最近出现","出现次数"}, SelectedIndex = 0 };
        var refresh = FlatButton("筛选 / 刷新", () => { _dailyPage = 0; _ = LoadDailyAsync(); });
        var filters = new WrapPanel { Margin = new Thickness(0,0,0,12), Children = { _dailyDate, _dailyFilter, _dailySearch, _dailySort, refresh } };
        _dailyGrid = new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false,
            SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column, Height = 220, Background = BgCardBrush,
            RowBackground = BgCardBrush, AlternatingRowBackground = BgWindowBrush, Foreground = FgBrush,
            BorderBrush = LineBrush, BorderThickness = new Thickness(1), GridLinesVisibility = DataGridGridLinesVisibility.None,
            EnableRowVirtualization = true, EnableColumnVirtualization = true,
        };
        var style = new Style(typeof(TextBlock)); style.Setters.Add(new Setter(TextBlock.ForegroundProperty,FgBrush)); style.Setters.Add(new Setter(TextBlock.MarginProperty,new Thickness(7,7,7,7)));
        var header = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        header.Setters.Add(new Setter(Control.BackgroundProperty,BgCardBrush)); header.Setters.Add(new Setter(Control.ForegroundProperty,FgSubBrush)); header.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(7,8,7,8)));
        _dailyGrid.ColumnHeaderStyle=header;
        void Col(string title,string property,double weight) => _dailyGrid.Columns.Add(new DataGridTextColumn { Header=title, Binding=new Binding(property), Width=new DataGridLength(weight,DataGridLengthUnitType.Star), MinWidth=48, ElementStyle=style });
        Col("词条",nameof(DailyWordEntry.Word),1.2); Col("次数",nameof(DailyWordEntry.Count),.5); Col("释义",nameof(DailyWordEntry.Meaning),2); Col("状态",nameof(DailyWordEntry.Status),1);
        _dailyGrid.Columns.Add(new DataGridCheckBoxColumn { Header="待核对",Binding=new Binding(nameof(DailyWordEntry.NeedsReview)),Width=65 });
        _dailyGrid.SelectionChanged += (_,_) =>
        {
            if (_dailyGrid?.SelectedItem is not DailyWordEntry entry) return;
            _dailyWord!.Text=entry.Word; _dailyPinyin!.Text=entry.Pinyin; _dailyMeaning!.Text=entry.Meaning;
        };
        _dailySummary = new TextBlock { Foreground=FgSubBrush,FontSize=12,Margin=new Thickness(0,10,0,10),TextWrapping=TextWrapping.Wrap };
        var previous=FlatButton("上一页",()=>{if(_dailyPage>0){_dailyPage--;_ = LoadDailyAsync();}});
        var next=FlatButton("下一页",()=>{_dailyPage++;_ = LoadDailyAsync();});
        var export=FlatButton("导出筛选结果",()=>_ = ExportDailyAsync());
        var delete=FlatButton("删除所选日期",()=>_ = DeleteDailyAsync());
        var nav=new WrapPanel{Children={previous,next,export,delete}};
        sp.Children.Add(Card(VStack(filters,_dailyGrid,_dailySummary,nav)));
        _dailyWord=DailyInput(""); _dailyPinyin=DailyInput(""); _dailyMeaning=DailyInput("");
        _dailyMeaning.AcceptsReturn=true; _dailyMeaning.Height=64; _dailyMeaning.TextWrapping=TextWrapping.Wrap;
        var editorRow=new Grid(); editorRow.ColumnDefinitions.Add(new ColumnDefinition());editorRow.ColumnDefinitions.Add(new ColumnDefinition());
        var wordGroup=VStack(CardTitle("词条"),_dailyWord);var pinyinGroup=VStack(CardTitle("拼音"),_dailyPinyin);Grid.SetColumn(pinyinGroup,1);editorRow.Children.Add(wordGroup);editorRow.Children.Add(pinyinGroup);
        sp.Children.Add(Card(VStack(CardTitle("核对与翻译"),editorRow,new TextBlock{Text="英文释义（多个释义可用分号分隔）",Foreground=FgSubBrush,Margin=new Thickness(0,12,0,6)},_dailyMeaning,
            new WrapPanel{Margin=new Thickness(0,12,0,0),Children={FlatButton("确认并保存",()=>_ = SaveDailyAsync(false,false)),FlatButton("忽略此词",()=>_ = SaveDailyAsync(true,false)),FlatButton("加入自定义词库",()=>_ = SaveDailyAsync(false,true))}})));
        var page=PageScroll(sp); _dailyPage=0;
        page.Loaded += (_,_) => { if(_activePage=="daily") _ = LoadDailyAsync(); };
        return page;
    }
    private static TextBox DailyInput(string text) => new() { Text=text,Foreground=FgBrush,Background=BgWindowBrush,BorderBrush=LineBrush,Padding=new Thickness(8,6,8,6),Margin=new Thickness(0,0,8,0),MinHeight=32 };
    private string DailyDate => (_dailyDate?.SelectedDate ?? DateTime.Today).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
    private string DailyFilter => _dailyFilter?.SelectedItem is string f && f!="全部" ? f : "";
    private async Task LoadDailyAsync()
    {
        if(_daily==null||_dailyGrid==null)return;
        int request=++_dailyRequest;string date=DailyDate,filter=DailyFilter,search=_dailySearch?.Text.Trim()??"";int page=_dailyPage; bool byFrequency = _dailySort?.SelectedIndex == 1;
        try
        {
            var result=await Task.Run(()=> { var count=_daily.Store.Count(date,filter,search); var actualPage=Math.Min(page,Math.Max(0,(count-1)/50));return (count,actualPage,rows:_daily.Store.Query(date,filter,search,actualPage,50,byFrequency)); });
            if(request!=_dailyRequest||_dailyGrid==null)return;
            _dailyPage=result.actualPage;_dailyGrid.ItemsSource=result.rows;
            _dailySummary!.Text=$"{date} · {result.count} 个词 · 第 {_dailyPage+1}/{Math.Max(1,(result.count+49)/50)} 页"+(_daily.Store.LastError==null?"":" · 写入异常："+_daily.Store.LastError);
        }
        catch(Exception ex){if(request==_dailyRequest&&_dailySummary!=null)_dailySummary.Text="读取失败："+ex.Message;}
    }
    private async Task SaveDailyAsync(bool ignore,bool addToDictionary)
    {
        if(_dailyGrid?.SelectedItem is not DailyWordEntry selected || _daily==null) { if(_dailySummary!=null)_dailySummary.Text="请先选择一个词条"; return; }
        string word=_dailyWord!.Text.Trim(),pinyin=_dailyPinyin!.Text.Trim(),meaning=_dailyMeaning!.Text.Trim();
        if(word.Length==0 || (addToDictionary && meaning.Length==0)){_dailySummary!.Text="请填写词条；加入词库前需要填写释义";return;}
        if(addToDictionary)
        {
            var existing=CustomStore.Load().FirstOrDefault(x=>x.Word==word);
            if(existing!=null&&MessageBox.Show(this,"自定义词库已有该词，是否替换其拼音和释义？","确认覆盖",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        }
        var entry=new DailyWordEntry{Id=selected.Id,Date=selected.Date,Word=word,Pinyin=pinyin,Meaning=meaning,NeedsReview=false,Status=ignore?"已忽略":addToDictionary?"已入库":meaning.Length==0?"待翻译":"已有释义"};
        try
        {
            if(addToDictionary)
            {
                var custom=CustomStore.Load();custom.RemoveAll(x=>x.Word==word);custom.Add(new CustomWord(word,pinyin,meaning));CustomStore.Save(custom);
                _customWords.Clear();foreach(var item in custom)_customWords.Add(item);
                _reloadDictionary();
            }
            await Task.Run(()=>_daily.Store.Update(entry)); await LoadDailyAsync();
        }
        catch(Exception ex){if(_dailySummary!=null)_dailySummary.Text="保存失败："+ex.Message;}
    }
    private async Task DeleteDailyAsync()
    {
        if(_daily==null)return;var date=DailyDate;
        if(MessageBox.Show(this,$"删除 {date} 的全部采集记录？这不会删除自定义词库中的词条。","删除当日记录",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        try { await Task.Run(()=>_daily.Store.DeleteDate(date));await LoadDailyAsync(); }
        catch(Exception ex){if(_dailySummary!=null)_dailySummary.Text="删除失败："+ex.Message;}
    }
    private async Task ExportDailyAsync()
    {
        if(_daily==null)return;string date=DailyDate,filter=DailyFilter,search=_dailySearch?.Text.Trim()??"";
        using var dialog=new WinForms.SaveFileDialog{Filter="CSV 文件|*.csv",FileName=$"GlintWin-{date}.csv"};
        if(dialog.ShowDialog()!=WinForms.DialogResult.OK)return;
        try
        {
            await Task.Run(()=>_daily.Store.ExportCsv(dialog.FileName,date,filter,search));
            if(_dailySummary!=null)_dailySummary.Text="筛选结果已导出";
        }
        catch(Exception ex){if(_dailySummary!=null)_dailySummary.Text="导出失败："+ex.Message;}
    }
}
