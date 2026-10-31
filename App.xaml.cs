using System.IO;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using WinForms = System.Windows.Forms;

namespace GlintWin;

public partial class App : Application
{
    private Mutex? _mutex;
    private WinForms.NotifyIcon? _tray;
    private CancellationTokenSource? _cts;
    private Thread? _watcherThread;
    private Thread? _mspThread;
    private CandidateWatcher? _watcher;
    private MspWatcher? _mspWatcher;
    private Coordinator? _coordinator;
    private bool _paused;
    private WinForms.ToolStripMenuItem? _pauseItem;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (s, ea) =>
        {
            Logger.Error("dispatcher: " + ea.Exception);
            ea.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (s, ea) =>
            Logger.Error("appdomain: " + ea.ExceptionObject);

        NativeMethods.TrySetPerMonitorV2();

        _mutex = new Mutex(true, "GlintWin_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("GlintWin 已在运行（请查看托盘图标）。");
            Shutdown();
            return;
        }

        Logger.Init();
        var cfg = Config.Load();
        var dict = CedictDictionary.Load(Path.Combine(AppContext.BaseDirectory, cfg.DictionaryPath));

        _coordinator = new Coordinator(cfg, dict);
        _coordinator.Start(Dispatcher);

        _cts = new CancellationTokenSource();
        PinyinTracker.Start();
        _watcher = new CandidateWatcher(cfg);
        _watcher.Event += ev => _coordinator.Post(ev);
        _watcherThread = new Thread(() => _watcher.Loop(_cts.Token))
        {
            IsBackground = true,
            Name = "glint-watcher",
        };
        _watcherThread.Start();

        // 微软拼音通道（光标锚定 OCR，搜狗候选窗可见时自动让位）
        _mspWatcher = new MspWatcher(cfg);
        _mspWatcher.Event += ev => _coordinator.Post(ev);
        _mspThread = new Thread(() => _mspWatcher.Loop(_cts.Token))
        {
            IsBackground = true,
            Name = "glint-msp",
        };
        _mspThread.Start();

        SetupTray();
    }

    private void SetupTray()
    {
        _tray = new WinForms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "GlintWin — 候选词英文释义",
            Visible = true,
        };

        var menu = new WinForms.ContextMenuStrip();
        _pauseItem = new WinForms.ToolStripMenuItem("暂停");
        _pauseItem.Click += (s, ev) =>
        {
            _paused = !_paused;
            _pauseItem.Text = _paused ? "恢复" : "暂停";
            _watcher?.SetPaused(_paused);
            _mspWatcher?.SetPaused(_paused);
            PinyinTracker.SetPaused(_paused);
            if (_paused) _coordinator?.Post(new WatchEvent(false, default));
        };
        var exit = new WinForms.ToolStripMenuItem("退出");
        exit.Click += (s, ev) => ExitApp();
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(exit);
        _tray.ContextMenuStrip = menu;

        _tray.ShowBalloonTip(3000, "GlintWin 已启动",
            "用搜狗打字时，候选栏下方会显示英文释义（本地离线处理，不读取正文）。",
            WinForms.ToolTipIcon.Info);
    }

    private void ExitApp()
    {
        try { _cts?.Cancel(); } catch { }
        try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } } catch { }
        Environment.Exit(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _tray?.Dispose(); } catch { }
        base.OnExit(e);
    }
}
