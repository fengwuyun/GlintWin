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
    [STAThread] static void Main(string[] args)
    {
        var app = new Application();
        var cfg = new Config { PillMode = "custom" };
        var window = new ConsoleWindow(cfg, () => null, () => {});
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
        window.Width = 980; window.Height = 720;
        window.Show();
        foreach (var page in new[]{"look", "settings", "status", "dict"})
        {
            Call("ShowPage", page); window.UpdateLayout();
            window.Dispatcher.Invoke(() => {}, System.Windows.Threading.DispatcherPriority.ContextIdle);
            var content = (FrameworkElement)window.Content;
            var bmp = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(content);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp));
            Directory.CreateDirectory(args[0]);
            using var stream = File.Create(Path.Combine(args[0], page + ".png")); png.Save(stream);
            Check(content.ActualWidth > 800, "render " + page);
        }
        window.Close();
        Console.WriteLine("ALL UI TESTS PASS");
    }
}
