using GlintWin;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
class Program
{
    static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); }
    [STAThread] static void Main(string[] args) { try { Run(args); } catch (Exception ex) { Console.Error.WriteLine(ex); Environment.Exit(1); } }
    static void Run(string[] args)
    {
        var app = new Application();
        System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(app.Dispatcher));
        AppPaths.Initialize(Path.Combine(args[0], "test-state-" + Guid.NewGuid().ToString("N")), Path.Combine(args[0], "no-legacy"));
        var cfg = new Config { PillMode = "custom" };
        using var daily = new DailyCollectionService(cfg, () => false);
        daily.Cancel(); daily.RefreshEnabled();
        daily.Observe("跳过", "tiaoguo", null, 1, 100);
        Check(!File.Exists(Path.Combine(AppPaths.Root, "daily-words.db")), "disabled collection leaves database unopened");
        daily.Store.Record("学习", "xue xi", "learn; study", true);
        daily.Store.Record("待译词", "dai yi ci", null, true);
        var previewDictionary = CedictDictionary.LoadAll(cfg);
        var window = new ConsoleWindow(cfg, () => previewDictionary, () => {}, daily);
        void Await(Task task)
        {
            if (!task.IsCompleted)
            {
                var frame = new System.Windows.Threading.DispatcherFrame();
                task.ContinueWith(_ => window.Dispatcher.BeginInvoke(() => frame.Continue = false));
                System.Windows.Threading.Dispatcher.PushFrame(frame);
            }
            task.GetAwaiter().GetResult();
        }
        object? Field(string name) => typeof(ConsoleWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window);
        object? Call(string name, params object[] values) => typeof(ConsoleWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, values);
        void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Call("ShowPage", "look");
        Click((Button)Field("_tgtFgBtn")!);
        Check((string)Field("_editTarget")! == "fg", "text color button works");
        var originalBackground = cfg.PillBgColor;
        Click((Button)Call("ColorButton", "test", "#FF6366F1")!);
        Check(cfg.PillFgColor == "#FF6366F1" && cfg.PillBgColor == originalBackground, "swatch changes selected target only");
        Check(cfg.RecentColors[0] == "#FF6366F1", "swatch added to history");
        Click((Button)Field("_tgtBgBtn")!);
        Click((Button)Call("ColorButton", "test", "#FF18181B")!);
        Check(cfg.PillBgColor == "#FF18181B", "background swatch works");
        ((Slider)Field("_slider")!).Value = 73;
        Call("FlushConfig");
        var saved = Config.Load();
        Check(saved.PillOpacityPercent == 73 && saved.RecentColors.SequenceEqual(cfg.RecentColors), "opacity and history persist to disk");
        Call("ShowPage", "settings"); Call("ShowPage", "look");
        Check(cfg.PillOpacityPercent == 73, "page navigation preserves opacity");
        Call("ShowPage", "daily");
        Await((Task)Call("LoadDailyAsync")!);
        var dailyGrid = (DataGrid)Field("_dailyGrid")!;
        Check(dailyGrid.Items.Count == 2, "daily inbox shows stored records");
        dailyGrid.SelectedItem = dailyGrid.Items.Cast<DailyWordEntry>().Single(x => x.Word == "待译词");
        ((TextBox)Field("_dailyMeaning")!).Text = "pending word";
        Await((Task)Call("SaveDailyAsync", false, false)!);
        var reviewed = daily.Store.Query(DateTime.Today.ToString("yyyy-MM-dd"), "", "待译词").Single();
        Check(!reviewed.NeedsReview && reviewed.Status == "已有释义" && reviewed.Meaning == "pending word", "review saves translation");
        dailyGrid.SelectedItem = dailyGrid.Items.Cast<DailyWordEntry>().Single(x => x.Word == "待译词");
        Await((Task)Call("SaveDailyAsync", false, true)!);
        Check(CustomStore.Load().Any(x => x.Word == "待译词" && x.Meaning == "pending word"), "reviewed word enters custom dictionary");
        Check(!cfg.AutoCollectDailyInput, "automatic collection remains opt-in");
        window.Width = 980; window.Height = 720;
        window.Show();
        Directory.CreateDirectory(Path.Combine(AppPaths.Root, "Data", "Packs"));
        File.WriteAllText(Path.Combine(AppPaths.Root, "Data", "Packs", "test_extra.json"), "{\"测试词包\":{\"p\":\"ce shi ci bao\",\"s\":[\"test pack\"]}}");
        IEnumerable<string> Texts(DependencyObject root)
        {
            if (root is TextBlock text) yield return text.Text;
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
                foreach (var value in Texts(child)) yield return value;
        }
        Check(Texts(window).Contains("词汇采集"), "collection page renamed in navigation");
        Check(Texts((DependencyObject)Call("BuildDictPage")!).Contains("test_extra"), "new dictionary packs appear without code changes");
        File.Delete(Path.Combine(AppPaths.Root, "Data", "Packs", "test_extra.json"));
        var imported=new List<CustomWord>{new("重复","chong fu","old")};
        CustomStore.MergeInto(imported,new[]{new CustomWord("重复","new pinyin","new, meaning; second; third"),new CustomWord("新增","xin zeng","added")});
        Check(imported.Count==2 && imported[0].Meaning=="new, meaning; second; third","CSV merge replaces duplicate definitions");
        var csv=Path.Combine(AppPaths.Root,"roundtrip.csv");CustomStore.ExportCsv(csv,imported);
        var reimported=CustomStore.ImportCsv(csv);
        Check(reimported.Count==2&&reimported[0].Meaning==imported[0].Meaning,"CSV quoted commas and three senses round trip");
        Call("ShowPage","dict");((TextBox)Field("_customSearch")!).Text="pending";
        Check(((DataGrid)Field("_customGrid")!).Items.Count==1,"custom dictionary search matches translation");
        ((TextBox)Field("_customSearch")!).Text="no-such-word";
        Check(((DataGrid)Field("_customGrid")!).Items.Count==0,"custom dictionary search excludes other entries");
        Call("ShowPage", "daily"); Await((Task)Call("LoadDailyAsync")!);
        void WaitFor(Func<bool> condition) { for(int i=0;i<100&&!condition();i++) Await(Task.Delay(20)); Check(condition(), "live filter updates results"); }
        var dateControl=(DatePicker)Field("_dailyDate")!;
        dateControl.SelectedDate=DateTime.Today.AddDays(-1);
        WaitFor(()=>((DataGrid)Field("_dailyGrid")!).Items.Count==0);
        dateControl.SelectedDate=DateTime.Today;
        WaitFor(()=>((DataGrid)Field("_dailyGrid")!).Items.Count==2);
        ((TextBox)Field("_dailySearch")!).Text="学习";
        WaitFor(()=>((DataGrid)Field("_dailyGrid")!).Items.Count==1);
        Call("SetDailyStatuses","已入库");
        WaitFor(()=>((DataGrid)Field("_dailyGrid")!).Items.Count==0);
        ((TextBox)Field("_dailySearch")!).Text="";
        WaitFor(()=>((DataGrid)Field("_dailyGrid")!).Items.Count==1);
        Call("SetDailyStatuses","");
        WaitFor(()=>((DataGrid)Field("_dailyGrid")!).Items.Count==2);
        Call("SetDailyStatuses","已入库|待翻译");
        WaitFor(()=>((DataGrid)Field("_dailyGrid")!).Items.Count==1);
        Call("SetDailyStatuses","已入库|已有释义");
        WaitFor(()=>((DataGrid)Field("_dailyGrid")!).Items.Count==2);
        foreach(var theme in new[]{"light","dark"})
        {
        Call("SetConsoleTheme",theme);
        Check(cfg.ConsoleTheme==theme && Config.Load().ConsoleTheme==theme,"console theme persists: "+theme);
        Check(cfg.PillOpacityPercent==73,"console theme preserves overlay settings");
        foreach (var page in new[]{"look", "settings", "status", "dict", "daily"})
        {
            Call("ShowPage", page);
            if (page == "daily") Await((Task)Call("LoadDailyAsync")!);
            window.UpdateLayout();
            if(page=="daily")
            {
                var date=(DatePicker)Field("_dailyDate")!;
                date.IsDropDownOpen=true;Await(Task.Delay(60));
                Check(date.IsDropDownOpen,"themed calendar popup opens: "+theme);
                date.IsDropDownOpen=false;
            }
            window.Dispatcher.Invoke(() => {}, System.Windows.Threading.DispatcherPriority.ContextIdle);
            var content = (FrameworkElement)window.Content;
            var bmp = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(content);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp));
            Directory.CreateDirectory(args[0]);
            using var stream = File.Create(Path.Combine(args[0], theme+"-"+page + ".png")); png.Save(stream);
            Check(content.ActualWidth > 800, "render " + page);
        }
        }
        window.Close();
        Console.WriteLine("ALL UI TESTS PASS");
    }
}
