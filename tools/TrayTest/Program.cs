using System.Reflection;
using System.Windows;
using GlintWin;
using Forms = System.Windows.Forms;
class TestApp : App { protected override void OnStartup(StartupEventArgs e) { } }
class Program
{
    static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);}
    [STAThread] static void Main(string[] args)
    {
        try { Run(args[0]); } catch(Exception ex) { Console.Error.WriteLine(ex); Environment.Exit(1); }
    }
    static void Run(string directory)
    {
        var app=new TestApp { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(app.Dispatcher));
        AppPaths.Initialize(System.IO.Path.Combine(directory,Guid.NewGuid().ToString("N")),System.IO.Path.Combine(directory,"no-legacy"));
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        object? Field(string name)=>typeof(App).GetField(name,flags)!.GetValue(app);
        void Call(string name)=>typeof(App).GetMethod(name,flags)!.Invoke(app,null);
        typeof(App).GetField("_config",flags)!.SetValue(app,new Config());
        Call("SetupTray");
        using var tray=(Forms.NotifyIcon)Field("_tray")!;
        // Test the real NotifyIcon event path, without injecting mouse input into the user's desktop.
        void Click(Forms.MouseButtons button)=>typeof(Forms.NotifyIcon).GetMethod("OnMouseClick",flags)!.Invoke(tray,new object[]{new Forms.MouseEventArgs(button,1,0,0,0)});
        void Pump() { var frame=new System.Windows.Threading.DispatcherFrame(); app.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(()=>frame.Continue=false)); System.Windows.Threading.Dispatcher.PushFrame(frame); }
        Click(Forms.MouseButtons.Right);Pump();
        Check(Field("_mainWin")==null,"right click does not open console");
        Click(Forms.MouseButtons.Left);Pump();
        var window=(ConsoleWindow?)Field("_mainWin");
        Check(window!=null && window.IsVisible,"left tray click opens console");
        window!.WindowState=WindowState.Minimized;
        Click(Forms.MouseButtons.Left);Pump();
        Check(window.IsVisible && window.WindowState!=WindowState.Minimized,"left click restores minimized console");
        window.Hide();Click(Forms.MouseButtons.Left);Pump();
        Check(window.IsVisible,"left click shows hidden console");
        Click(Forms.MouseButtons.Left);Pump();
        Check(ReferenceEquals(window,Field("_mainWin")),"repeated clicks reuse console");
        window.WindowState=WindowState.Maximized;Click(Forms.MouseButtons.Left);Pump();
        Check(window.WindowState==WindowState.Maximized,"maximized console remains maximized");
        window.Close();Pump();Check(Field("_mainWin")==null,"closed console is released");
        tray.ContextMenuStrip!.Items[0].PerformClick();Pump();
        var reopened=(ConsoleWindow?)Field("_mainWin");
        Check(reopened!=null && reopened.IsVisible && !ReferenceEquals(window,reopened),"context menu recreates closed console");
        reopened!.Close();tray.Visible=false;app.Shutdown();
        Console.WriteLine("ALL TRAY TESTS PASS");
    }
}
