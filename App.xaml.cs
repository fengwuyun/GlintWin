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
    private EventWaitHandle? _activateEvent;
    private const string ActivateEventName = "GlintWin_ActivateUI";
    private ConsoleWindow? _mainWin;
    private bool _paused;
    private DailyCollectionService? _daily;
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
        if (e.Args.Length == 2 && e.Args[0] == "--verify-package")
        {
            try
            {
                AppPaths.Initialize(e.Args[1], AppContext.BaseDirectory);
                var checkDict = CedictDictionary.LoadAll(Config.Load());
                var checkOcr = new OcrService("zh-Hans-CN");
                File.WriteAllText(Path.Combine(AppPaths.Root, "package-check.json"), System.Text.Json.JsonSerializer.Serialize(new
                {
                    Entries = checkDict.Count, BaseEntries = checkDict.BaseCount, OcrAvailable = checkOcr.Available,
                    DefaultCollectionEnabled = new Config().AutoCollectDailyInput,
                    Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    Resources = typeof(AppPaths).Assembly.GetManifestResourceNames().Length
                }));
                Shutdown(checkDict.BaseCount > 100000 ? 0 : 2);
            }
            catch { Shutdown(3); }
            return;
        }

        _mutex = new Mutex(true, "GlintWin_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            // 已有实例：通知它打开主界面（双击图标必有响应），然后退出
            try
            {
                using var evt = EventWaitHandle.OpenExisting(ActivateEventName);
                evt.Set();
            }
            catch { }
            Logger.Info("another instance is running, asked it to show UI");
            Shutdown();
            return;
        }
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);

        try { AppPaths.Initialize(); }
        catch (Exception ex) { MessageBox.Show("无法准备 GlintWin 用户数据目录：" + ex.Message, "GlintWin"); Shutdown(1); return; }
        Logger.Init();
        var cfg = Config.Load();
        _config = cfg;
        var dict = CedictDictionary.LoadAll(cfg);
        ConsoleWindow.LastThemeProvider = () => _coordinator?.LastTheme;
        ConsoleWindow.DictionaryInfoText = dict != null
            ? $"{dict.Count:N0} 条 · CC-CEDICT（含拼音）"
            : "未加载 — 请先运行 tools\\convert_cedict.py 生成 Data\\cedict.json";

        _coordinator = new Coordinator(cfg, dict);
        _coordinator.Start(Dispatcher);
        try
        {
            _daily = new DailyCollectionService(cfg, () => _paused);
            _coordinator.CandidateObserved = _daily.Observe;
            _coordinator.CandidatesObserved = _daily.ObserveCandidates;
            _coordinator.CompositionHidden = _daily.Cancel;
        }
        catch (Exception ex) { Logger.Error("daily store unavailable: " + ex.Message); }
        ConsoleWindow.RuntimeStatusText = _coordinator.OcrAvailable ? "离线 OCR 已就绪" : "缺少中文 OCR：请在 Windows 语言设置安装简体中文 OCR 功能";

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

        var activateThread = new Thread(() =>
        {
            while (_activateEvent!.WaitOne())
            {
                Dispatcher.InvokeAsync(OpenMain);
            }
        })
        { IsBackground = true, Name = "glint-activate" };
        activateThread.Start();

        SetupTray();

        // 启动即打开主界面（-tray 用于开机自启静默；--ui 强制打开；首次运行强制打开）
        bool startMinimized = e.Args.Any(a => string.Equals(a, "-tray", StringComparison.OrdinalIgnoreCase));
        bool firstRun = !File.Exists(AppPaths.ConfigFile);
        if (e.Args.Contains("--ui") || firstRun || !startMinimized)
        {
            Dispatcher.InvokeAsync(OpenMain);
        }
    }

    private void SetupTray()
    {
        var iconPath = Path.Combine(AppPaths.Root, "app.ico");
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
            _daily?.RefreshEnabled();
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

    internal void ReloadDictionary()
    {
        if (_config == null) return;
        var dict = CedictDictionary.LoadAll(_config);
        _coordinator?.SetDictionary(dict);
        ConsoleWindow.DictionaryInfoText = $"基础 {dict.BaseCount:N0} + 词包 {dict.PackCounts.Values.Sum():N0} + 自定义 {dict.CustomCount:N0} 条";
        _mainWin?.RefreshStatus();
    }

    private void OpenMain()
    {
        if (_config == null) return;
        if (_mainWin != null)
        {
            _mainWin.Activate();
            return;
        }
        _mainWin = new ConsoleWindow(_config, () => _coordinator?.Dictionary, ReloadDictionary, _daily);
        _mainWin.Closed += (_, _) => _mainWin = null;
        _mainWin.Show();
    }

    private void ExitApp()
    {
        try { _cts?.Cancel(); } catch { }
        try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } } catch { }
        _mainWin?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _cts?.Cancel(); _daily?.Dispose(); _tray?.Dispose(); } catch { }
        base.OnExit(e);
    }
}
