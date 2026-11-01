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
    private Config? _config;
    private ConsoleWindow? _mainWin;
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
            // 已有实例在运行：静默退出（阻塞式弹窗会变成无人理会的僵尸进程）
            Logger.Info("another instance is running, exit");
            Shutdown();
            return;
        }

        Logger.Init();
        var cfg = Config.Load();
        _config = cfg;
        var dict = CedictDictionary.Load(Path.Combine(AppContext.BaseDirectory, cfg.DictionaryPath));
        ConsoleWindow.LastThemeProvider = () => _coordinator?.LastTheme;
        ConsoleWindow.DictionaryInfoText = dict != null
            ? $"{dict.Count:N0} 条 · CC-CEDICT（含拼音）"
            : "未加载 — 请先运行 tools\\convert_cedict.py 生成 Data\\cedict.json";

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

        // 首次运行（无配置文件）自动打开主界面
        if (e.Args.Contains("--ui") || !File.Exists(Path.Combine(AppContext.BaseDirectory, "config.json")))
        {
            Dispatcher.InvokeAsync(OpenMain);
        }
    }

    private void SetupTray()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        _tray = new WinForms.NotifyIcon
        {
            Icon = File.Exists(iconPath)
                ? new System.Drawing.Icon(iconPath, 32, 32)
                : System.Drawing.SystemIcons.Application,
            Text = "GlintWin — 候选词英文释义",
            Visible = true,
        };

        var menu = new WinForms.ContextMenuStrip();
        var mainItem = new WinForms.ToolStripMenuItem("打开主界面");
        mainItem.Click += (s, ev) => OpenMain();
        menu.Items.Add(mainItem);
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

    private void OpenMain()
    {
        if (_config == null) return;
        if (_mainWin != null)
        {
            _mainWin.Activate();
            return;
        }
        _mainWin = new ConsoleWindow(_config);
        _mainWin.Closed += (_, _) => _mainWin = null;
        _mainWin.Show();
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
