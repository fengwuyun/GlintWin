using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;

namespace GlintWin;

/// <summary>悬浮条主题：皮肤取色（auto）或用户自定义（custom）。Alpha 控制透底程度。</summary>
public sealed record SkinTheme(Color Bg, Color Fg, double RowTextHeightPx, byte Alpha = 216);

/// <summary>
/// 中枢：把「候选窗口可见/移动/隐藏」事件变成
/// 「截图 → 像素哈希去重 → 2x 放大 OCR → 解析 → 皮肤取色 → 查词典/纠错 → 更新悬浮条」的流水线。
/// 防闪烁：只哈希候选行区域（避开拼音框光标闪烁/皮肤动画）并混入窗口坐标（窗口移动必刷新）、
/// 主题色与字号指数平滑、解析偶发失败时短时间保持原样、首显需连续两帧一致。
/// </summary>
public sealed partial class Coordinator
{
    private const double OcrScale = 2.0;   // 小字放大后再识别，显著提升准确率

    private readonly Config _cfg;
    private readonly OcrService _ocr;
    private CedictDictionary? _dict;
    private readonly Channel<WatchEvent> _channel = Channel.CreateBounded<WatchEvent>(
        new BoundedChannelOptions(2) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropOldest });

    private readonly SemaphoreSlim _ocrGate = new(1, 1);
    private readonly object _pillGate = new();
    private NativeMethods.RECT _pillScreenRect;
    private bool _pillScreenRectValid;
    private byte[]? _pixelBuf;
    private byte[]? _ocrPixelBuf;

    private OverlayWindow? _overlay;
    private Dispatcher? _dispatcher;

    // OCR 结果（仅在候选行像素/窗口位置变化时刷新）
    private int _lastHash;
    private IReadOnlyList<OcrLineData> _lastLines = Array.Empty<OcrLineData>();
    private string? _lastTop;
    private string? _lastGloss;
    private string? _lastPinyin;
    private NativeMethods.RECT _lastRowRect;
    private bool _lastHasRow;
    private SkinTheme? _lastTheme;

    // 防闪烁 / 节流状态
    private Rectangle? _hashRowBmp;   // 上次 OCR 的候选行（位图坐标），哈希只看这里
    private Size _hashRowWin;         // 该候选行对应的窗口尺寸（变了就失效）
    private NativeMethods.RECT _lastCaptureRect;
    private long _lastCheckMs;
    private string? _shownTop;        // 悬浮条当前显示的内容
    private string? _shownGloss;
    private string? _shownPinyin;
    private SkinTheme? _shownTheme;
    private string? _pendingTop;      // 首显确认：连续两帧一致才让悬浮条出现
    private string? _prevFrameTop;    // 上一 OCR 帧的候选（用于 OCR 抖动去重）
    private int _untrustedStreak;     // 连续不可信帧数（>=3 隐藏悬浮条防冻结残留）
    private DateTime _lastGoodParse = DateTime.MinValue;
    private bool _pillVisible;

    public Coordinator(Config cfg, CedictDictionary? dict)
    {
        _cfg = cfg;
        _dict = dict;
        _ocr = new OcrService(cfg.OcrLanguage);
    }

    public void Start(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _overlay = new OverlayWindow(_cfg);
        _overlay.ShowOffscreen();
        _ = Task.Run(ConsumeAsync);
    }

    public void Post(WatchEvent e) => _channel.Writer.TryWrite(e);

    /// <summary>最近一次皮肤取色结果（设置窗口预览用）。</summary>
    public SkinTheme? LastTheme => _lastTheme;

    /// <summary>当前词典（词库管理页启用/停用词包后整体替换）。</summary>
    public CedictDictionary? Dictionary => _dict;

    public void SetDictionary(CedictDictionary? dict)
    {
        _dict = dict;
        _lastHash = 0;   // 强制下一帧重新识别
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var e in _channel.Reader.ReadAllAsync())
            {
                try
                {
                    if (!e.Visible)
                        {
                            ResetTransient();
                            if (_pillVisible)
                            {
                                _pillVisible = false;
                                lock (_pillGate) { _pillScreenRectValid = false; }
                                await OnUi(() => _overlay!.HidePill());
                            }
                            continue;
                        }
                    if (!_ocr.Available) continue;
                    await ProcessRectAsync(e.Rect, e.RequireNumbered);
                }
                catch (Exception ex)
                {
                    Logger.Error("consume: " + ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("consumer loop: " + ex);
        }
    }

    private void ResetTransient()
    {
        _lastHash = 0;
        _hashRowBmp = null;
        _lastLines = Array.Empty<OcrLineData>();
        _pixelBuf = null;
        _ocrPixelBuf = null;
        // 注意：_lastTheme 不重置 —— 同一皮肤跨组词保持取色稳定，避免每次组词首帧颜色跳变
        _lastCaptureRect = default;
        _lastCheckMs = 0;
        _shownTop = _shownGloss = _shownPinyin = null;
        _shownTheme = null;
        _pendingTop = null;
        _prevFrameTop = null;
        _untrustedStreak = 0;
        _lastGoodParse = DateTime.MinValue;
    }

    private async Task ProcessRectAsync(NativeMethods.RECT rect, bool strict)
    {
        int w = rect.Right - rect.Left;
        int h = rect.Bottom - rect.Top;
        if (w <= 10 || h <= 10) return;

        // 窗口位置未变时限频采样（内容变化几乎总伴随窗口尺寸变化）
        bool sameRect = _lastCaptureRect.Left == rect.Left && _lastCaptureRect.Top == rect.Top
            && _lastCaptureRect.Right == rect.Right && _lastCaptureRect.Bottom == rect.Bottom;
        long nowMs = Environment.TickCount64;
        if (sameRect && nowMs - _lastCheckMs < 250) return;
        _lastCaptureRect = rect;
        _lastCheckMs = nowMs;

        bool hashChanged;
        string? top, gloss, pinyin;
        NativeMethods.RECT anchor;
        int anchorOffset;
        SkinTheme? theme;

        await _ocrGate.WaitAsync();
        try
        {
            using var bmp = Capture(rect.Left, rect.Top, w, h);
            ErasePill(bmp, rect);
            var bytes = GetPixels(bmp);

            // 哈希区域：优先用上次的候选行（拼音框光标闪烁、皮肤动画都在行外）；
            // 窗口尺寸变了或未知时退化为下半部（候选行所在区域）。
            // 再混入窗口坐标：同一内容出现在新位置时也强制刷新，避免悬浮条留在旧位置。
            var hashRect = _hashRowBmp.HasValue && _hashRowWin.Width == w && _hashRowWin.Height == h
                ? ClampRect(_hashRowBmp.Value, w, h)
                : new Rectangle(0, h * 55 / 100, w, h - h * 55 / 100);
            int hash = HashCode.Combine(HashRegion(bytes, w, hashRect), rect.Left, rect.Top, w, h);
            hashChanged = hash != _lastHash;
            if (hashChanged)
            {
                _lastHash = hash;

                // 2x 放大后 OCR（小字识别准确率显著提升），词坐标除回去
                using var big = new Bitmap(w * 2, h * 2, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(big))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(bmp, 0, 0, w * 2, h * 2);
                }
                var bigBytes = GetPixels(big, enlarged: true);
                _lastLines = ScaleLines(await _ocr.RecognizeAsync(bigBytes, big.Width, big.Height), 1.0 / OcrScale);

                var parsed = CandidateParser.Parse(_lastLines.Select(l => l.Text).ToList());
                _lastPinyin = parsed.Pinyin;

                // 拼音框是彩底浅字，常规 OCR 常整块跳过：整窗增强识别补拿拼音（纠错用）。
                // 注意拼音框位置随皮肤变化（可能在中部），不能按固定比例裁剪，直接增强全窗。
                if (string.IsNullOrEmpty(_lastPinyin))
                {
                    var enhanced = await _ocr.RecognizeEnhancedAsync(bmp, new Rectangle(0, 0, w, h));
                    if (enhanced.Count > 0)
                    {
                        _lastPinyin = CandidateParser.Parse(enhanced).Pinyin;
                        if (_lastPinyin != null)
                            Logger.Info($"pinyin(enhanced)={_lastPinyin}");
                    }
                }

                // 优先：候选行内按词间隙切出候选块，块 0 = 第一候选
                var rowLine = FindRowLine(_lastLines, parsed.Top, strict);
                var chunks = rowLine != null
                    ? CandidateParser.ExtractChunksFromRow(rowLine)
                    : new List<string>();
                string? newTop;
                if (strict && (rowLine is null || CountNumberedMarkers(rowLine.Text) < 2))
                {
                    // 微软拼音通道：候选栏必然包含 ≥2 个数字序号项；
                    // 只有单行数字+汉字（如正文里的「25 天」）不算候选，防止把应用正文误判为候选词
                    newTop = null;
                }
                else
                {
                    newTop = chunks.Count > 0 ? chunks[0] : parsed.Top;
                }

                // 第一候选高亮被 OCR 吞掉（行内无 1 序号）：对行首区域（2 序号之前）做增强识别补读，
                // 否则整帧被拒会导致悬浮条消失/闪烁——这是搜狗通道不稳定的主要来源
                bool firstItemRecovered = false;
                if (!strict && rowLine != null && chunks.Count > 0 && !CandidateParser.HasMarkerOne(rowLine.Text))
                {
                    var rowRectBmp = LineRect(rowLine);
                    double? x2 = null;
                    if (rowRectBmp.HasValue)
                    {
                        foreach (var wd in rowLine.Words)
                        {
                            var txt = wd.Text.TrimStart();
                            if (txt.Length > 0 && (txt[0] == '2' || txt[0] == '２'))
                            {
                                x2 = wd.X;
                                break;
                            }
                        }
                    }
                    if (rowRectBmp.HasValue && x2.HasValue && x2.Value - rowRectBmp.Value.X > 8)
                    {
                        var crop = new Rectangle(
                            rowRectBmp.Value.X,
                            Math.Max(0, rowRectBmp.Value.Y - 2),
                            (int)(x2.Value - rowRectBmp.Value.X - 2),
                            rowRectBmp.Value.Height + 4);
                        var enhanced1 = await _ocr.RecognizeEnhancedAsync(bmp, crop);
                        Logger.Info($"first-item crop=({crop.X},{crop.Y},{crop.Width},{crop.Height}) lines=[{string.Join(" / ", enhanced1)}]");
                        var cand = enhanced1.Count > 0 ? CandidateParser.ExtractTop(string.Join(" ", enhanced1)) : null;
                        if (!string.IsNullOrEmpty(cand))
                        {
                            newTop = cand;
                            firstItemRecovered = true;
                        }
                    }
                }

                var (resolvedTop, resolvedGloss) = ResolveTop(newTop, strict);

                // 帧级去抖：与当前显示的词等长且仅差 1 字 = OCR 单字误读抖动，
                // 需连续两帧一致才接受，消除悬浮条文字闪烁
                if (_pillVisible && resolvedTop != _lastTop &&
                    resolvedTop is not null && _lastTop is not null &&
                    resolvedTop.Length == _lastTop.Length && WithinOneChar(resolvedTop, _lastTop) &&
                    resolvedTop != _prevFrameTop)
                {
                    _prevFrameTop = resolvedTop;
                    resolvedTop = _lastTop;
                    resolvedGloss = _lastGloss;
                }
                else
                {
                    _prevFrameTop = resolvedTop;
                }

                // 帧可信度闸门：第一候选被高亮动画吞掉时（行内无「1」序号），
                // chunks[0] 实际是第二候选，本帧不可信 —— 保持旧值，且不污染主题/行哈希。
                // 例外：行首增强补读成功（firstItemRecovered）则本帧可信
                bool frameTrusted = resolvedTop is null ||
                                    strict ||
                                    firstItemRecovered ||
                                    rowLine is null && resolvedTop == parsed.Top && parsed.Top is null ||
                                    (rowLine != null && CandidateParser.HasMarkerOne(rowLine.Text)) ||
                                    (rowLine is null && newTop is null);
                if (!frameTrusted)
                {
                    _untrustedStreak++;
                    if (_pillVisible && _untrustedStreak >= 3)
                    {
                        // 连续不可信：组词已变化但读不出候选 —— 隐藏悬浮条，避免旧值冻结残留
                        resolvedTop = null;
                        resolvedGloss = null;
                        _lastTop = null;
                        _lastGloss = null;
                    }
                    else
                    {
                        resolvedTop = _lastTop;
                        resolvedGloss = _lastGloss;
                    }
                }
                else
                {
                    _untrustedStreak = 0;
                }

                _lastTop = resolvedTop;
                _lastGloss = resolvedGloss;

                var rowBmp = rowLine is null || !frameTrusted ? null : LineRect(rowLine);
                if (rowBmp.HasValue)
                {
                    _lastRowRect = new NativeMethods.RECT
                    {
                        Left = rect.Left + rowBmp.Value.X,
                        Top = rect.Top + rowBmp.Value.Y,
                        Right = rect.Left + rowBmp.Value.X + rowBmp.Value.Width,
                        Bottom = rect.Top + rowBmp.Value.Y + rowBmp.Value.Height,
                    };
                    _lastHasRow = true;
                    _hashRowBmp = rowBmp;
                    _hashRowWin = new Size(w, h);
                }
                else
                {
                    // 仅复用同尺寸窗口内的行位置，并随窗口移动换算屏幕坐标。
                    _lastHasRow = _hashRowBmp.HasValue && _hashRowWin.Width == w && _hashRowWin.Height == h;
                    if (_lastHasRow)
                    {
                        var previousRow = _hashRowBmp!.Value;
                        _lastRowRect = new NativeMethods.RECT
                        {
                            Left = rect.Left + previousRow.Left, Top = rect.Top + previousRow.Top,
                            Right = rect.Left + previousRow.Right, Bottom = rect.Top + previousRow.Bottom,
                        };
                    }
                }

                var boxRect = new Rectangle(0, 0, w, h * 40 / 100);
                var sampled = !frameTrusted ? _lastTheme
                    : rowBmp.HasValue ? SampleTheme(bytes, w, h, rowBmp, boxRect) : _lastTheme;
                if (sampled is not null)
                {
                    // 对比度归一：浅底强制深字/深底强制浅字，消除皮肤高亮淡入导致的灰色深浅抖动
                    sampled = new SkinTheme(sampled.Bg,
                        EnsureContrast(sampled.Bg, sampled.Fg), sampled.RowTextHeightPx);
                }
                _lastTheme = SmoothTheme(_lastTheme, sampled);

                var themeInfo = _lastTheme != null
                    ? $"{_lastTheme.Bg}/{_lastTheme.Fg}/h{_lastTheme.RowTextHeightPx:F0}"
                    : "default";
                Logger.Info($"top={_lastTop} gloss={_lastGloss} strict={strict} chunks=[{string.Join("|", chunks)}] pinyin={_lastPinyin} theme={themeInfo} lines=[{string.Join(" / ", _lastLines.Select(l => l.Text))}]");
            }
            top = _lastTop;
            gloss = _lastGloss;
            pinyin = _lastPinyin;
            theme = _cfg.PillMode == "custom" ? CustomTheme(_lastTheme) : _lastTheme;
            // 贴合实际候选行下沿，避开皮肤装饰和透明留白；无可靠行坐标时回退窗口。
            (anchor, anchorOffset) = (_lastHasRow ? _lastRowRect : rect, 0);
        }
        finally
        {
            _ocrGate.Release();
        }

        if (string.IsNullOrEmpty(top))
        {
            // 组词中偶发解析失败：短时间内保持原样，不闪隐
            if (_pillVisible && (DateTime.UtcNow - _lastGoodParse).TotalMilliseconds < 500) return;
            if (_pillVisible)
            {
                _pillVisible = false;
                await OnUi(() => _overlay!.HidePill());
            }
            return;
        }
        _lastGoodParse = DateTime.UtcNow;

        // 首显确认：候选窗刚弹出时高亮在淡入，前 1-2 帧常解析出碎片字（囗/亻），
        // 连续两帧同一候选才让悬浮条出现，避免先闪一下错字再纠正
        if (!_pillVisible)
        {
            if (top != _pendingTop)
            {
                _pendingTop = top;
                return;
            }
            _pendingTop = null;
        }

        // 内容没变就不重绘（主题平滑后微小色差也不重绘），位置照常跟随
        bool contentChanged = top != _shownTop || gloss != _shownGloss || pinyin != _shownPinyin;
        bool themeChanged = ThemeDiffers(theme, _shownTheme);
        _pillVisible = true;
        await OnUi(() =>
        {
            if (contentChanged || themeChanged)
            {
                _overlay!.Update(top!, gloss, pinyin, theme);
                _shownTop = top;
                _shownGloss = gloss;
                _shownPinyin = pinyin;
                _shownTheme = theme;
            }
            _overlay!.Place(anchor, anchorOffset);
            if (_overlay!.TryGetPlacedRect(out var pr))
            {
                lock (_pillGate)
                {
                    _pillScreenRect = pr;
                    _pillScreenRectValid = true;
                }
            }
        });
    }

    /// <summary>
    /// 查词典；查不到时用组词拼音做交叉验证纠错（宁缺勿错）。
    /// strict（微软拼音通道）时额外用键盘拼音与候选词典拼音做前缀兼容校验，杜绝正文误报。
    /// </summary>
    private (string? Top, string? Gloss) ResolveTop(string? top, bool strict)
    {
        if (top is null) return (null, null);
        var gloss = _dict?.Lookup(top);
        if (gloss != null)
        {
            if (strict && !PinyinCompatibleWithKeyboard(top)) return (null, null);
            return (top, gloss);
        }

        // 纠错：优先用键盘钩子的权威拼音，其次 OCR 拼音
        var hint = PinyinTracker.CurrentPinyin ?? _lastPinyin;
        if (_dict != null && !string.IsNullOrEmpty(hint))
        {
            var fixedWord = _dict.FuzzyCorrect(top, hint);
            if (fixedWord != null)
            {
                var fixedGloss = _dict.Lookup(fixedWord);
                if (fixedGloss != null)
                {
                    Logger.Info($"fuzzy: {top} -> {fixedWord} (pinyin={hint})");
                    if (strict && !PinyinCompatibleWithKeyboard(fixedWord)) return (null, null);
                    return (fixedWord, fixedGloss);
                }
            }
        }
        return (top, null);
    }

    private bool PinyinCompatibleWithKeyboard(string word)
    {
        var hint = PinyinTracker.CurrentPinyin;
        if (string.IsNullOrEmpty(hint)) return true;      // 无键盘拼音（如搜狗通道未打字）不否决
        var dictPy = _dict?.PinyinOf(word);
        if (dictPy is null) return true;                  // 词典外词无法验证，不否决
        return CedictDictionary.PinyinCompatible(dictPy, hint);
    }

    private static IReadOnlyList<OcrLineData> ScaleLines(IReadOnlyList<OcrLineData> lines, double k) =>
        lines.Select(l => new OcrLineData(l.Text,
            l.Words.Select(x => new OcrWordData(x.Text, x.X * k, x.Y * k, x.Width * k, x.Height * k)).ToList()
        )).ToList();

    private async Task OnUi(Action action) => await _dispatcher!.InvokeAsync(action);

    private static Bitmap Capture(int x, int y, int w, int h)
    {
        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(x, y, 0, 0, new Size(w, h));
        return bmp;
    }

    /// <summary>
    /// 把悬浮条自身像素从截图中抹掉（涂白）：悬浮条位于搜狗候选窗矩形内部，
    /// 不抹掉会被下一帧 OCR 读到，形成「悬浮条文字进入识别→行框膨胀→字号变化→再识别」的反馈回路。
    /// </summary>
    private void ErasePill(Bitmap bmp, NativeMethods.RECT captureRect)
    {
        NativeMethods.RECT pr;
        bool valid;
        lock (_pillGate)
        {
            valid = _pillScreenRectValid;
            pr = _pillScreenRect;
        }
        if (!valid) return;

        var capRect = new Rectangle(captureRect.Left, captureRect.Top,
            captureRect.Right - captureRect.Left, captureRect.Bottom - captureRect.Top);
        var pillRect = new Rectangle(pr.Left, pr.Top, pr.Right - pr.Left, pr.Bottom - pr.Top);
        var inter = Rectangle.Intersect(pillRect, capRect);
        if (inter.Width <= 0 || inter.Height <= 0) return;
        var local = new Rectangle(inter.X - captureRect.Left, inter.Y - captureRect.Top, inter.Width, inter.Height);
        using var g = Graphics.FromImage(bmp);
        g.FillRectangle(System.Drawing.Brushes.White, local);
    }

    private byte[] GetPixels(Bitmap bmp, bool enlarged = false)
    {
        var bd = bmp.LockBits(
            new Rectangle(0, 0, bmp.Width, bmp.Height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            int need = bd.Stride * bd.Height;
            ref byte[]? buffer = ref (enlarged ? ref _ocrPixelBuf : ref _pixelBuf);
            if (buffer is null || buffer.Length < need) buffer = new byte[need];
            Marshal.Copy(bd.Scan0, buffer, 0, need);
            return buffer;
        }
        finally
        {
            bmp.UnlockBits(bd);
        }
    }

    private static Rectangle ClampRect(Rectangle r, int w, int h)
    {
        r.Intersect(new Rectangle(0, 0, w, h));
        return r;
    }

    /// <summary>只对区域内像素的 RGB 做 FNV 哈希（alpha 恒定不参与）。</summary>
    private static int HashRegion(byte[] bgra, int width, Rectangle r)
    {
        unchecked
        {
            uint h = 2166136261;
            for (int y = r.Top; y < r.Bottom; y++)
            {
                int baseIdx = y * width * 4;
                for (int x = r.Left; x < r.Right; x++)
                {
                    int i = baseIdx + x * 4;
                    h ^= bgra[i];
                    h *= 16777619;
                    h ^= bgra[i + 1];
                    h *= 16777619;
                    h ^= bgra[i + 2];
                    h *= 16777619;
                }
            }
            return (int)h;
        }
    }

    private static SkinTheme? SmoothTheme(SkinTheme? old, SkinTheme? now)
    {
        if (now is null) return old;
        if (old is null) return now;
        const double k = 0.5;
        return new SkinTheme(
            LerpColor(old.Bg, now.Bg, k),
            LerpColor(old.Fg, now.Fg, k),
            old.RowTextHeightPx * (1 - k) + now.RowTextHeightPx * k);
    }

    private static bool ThemeDiffers(SkinTheme? a, SkinTheme? b)
    {
        if (a is null && b is null) return false;
        if (a is null || b is null) return true;
        return ColorDist(a.Bg, b.Bg) > 30
            || ColorDist(a.Fg, b.Fg) > 30
            || Math.Abs(a.RowTextHeightPx - b.RowTextHeightPx) > 2.0;
    }

    private static Color LerpColor(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    /// <summary>候选行 = 包含 top 的 OCR 行，退而求其次取含数字+汉字的行。strict 时行内必须含数字。</summary>
    private static OcrLineData? FindRowLine(IReadOnlyList<OcrLineData> lines, string? top, bool strict)
    {
        OcrLineData? best = null;
        foreach (var l in lines)
        {
            if (l.Words.Count == 0) continue;
            var hasDigit = l.Text.Any(char.IsDigit);
            if (top != null && l.Text.Replace(" ", "").Contains(top))
            {
                if (!strict || hasDigit) return l;
                continue;
            }
            if (best == null && hasDigit && l.Text.Any(IsCjk)) best = l;
        }
        return best;
    }

    private static Rectangle? LineRect(OcrLineData line)
    {
        var words = line.Words.Where(w => w.Width > 2).ToList();
        if (words.Count == 0) return null;

        // 排除离群词：拼音框的闪烁光标会被 OCR 读成细高竖条（"\"/"I"），
        // 混进行框会把行高撑到 2 倍，导致字号/悬浮条尺寸逐帧跳动
        var medH = Median(words.Select(w => w.Height));
        var core = words.Where(w => w.Height <= medH * 1.6 + 2).ToList();
        if (core.Count == 0) core = words;

        double minX = double.MaxValue, minY = double.MaxValue, maxX = 0, maxY = 0;
        foreach (var wd in core)
        {
            minX = Math.Min(minX, wd.X);
            minY = Math.Min(minY, wd.Y);
            maxX = Math.Max(maxX, wd.X + wd.Width);
            maxY = Math.Max(maxY, wd.Y + wd.Height);
        }
        if (maxX <= minX || maxY <= minY) return null;
        return new Rectangle(
            (int)Math.Floor(minX), (int)Math.Floor(minY),
            (int)Math.Ceiling(maxX - minX), (int)Math.Ceiling(maxY - minY));
    }

    private static double Median(IEnumerable<double> xs)
    {
        var sorted = xs.OrderBy(x => x).ToList();
        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }

    private static bool IsCjk(char c) => c is >= '\u4e00' and <= '\u9fff';

    private static bool WithinOneChar(string a, string b)
    {
        int diff = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i] && ++diff > 1) return false;
        }
        return true;
    }

    [GeneratedRegex(@"(?:^|\s)\d{1,2}\s*[\.．、]\s*(?=[^\x00-\x7F])|\s\d{1,2}\s+(?=[^\x00-\x7F])")]
    private static partial Regex NumberedMarkerRegex();

    /// <summary>统计行内数字序号标记数（如 “1.” “2 ”）。</summary>
    private static int CountNumberedMarkers(string text) => NumberedMarkerRegex().Matches(text).Count;

    /// <summary>
    /// 在候选行区域统计颜色：最高频色 = 背景色（皮肤）；与背景对比度足够高的最高频色 = 文字色。
    /// 置信度不足时返回 null（悬浮条保持深色主题）。
    /// </summary>
    /// <summary>
    /// 皮肤取色：底色从不透明拼音框区域采样（固有色调，不随透底内容漂移）；
    /// 文字色从候选行按对比度取。任一区域置信度不足时回退。
    /// </summary>
    private SkinTheme? SampleTheme(byte[] bgra, int width, int height, Rectangle? rowRect, Rectangle? boxRect)
    {
        var fgRegion = ClampRect(rowRect ?? new Rectangle(0, height * 55 / 100, width, height * 45 / 100), width, height);
        var bgRegion = ClampRect(boxRect ?? new Rectangle(0, height * 10 / 100, width, height * 40 / 100), width, height);
        if (bgRegion.Width < 20 || bgRegion.Height < 8) return null;

        var bgCounts = new int[4096];
        long bgTotal = 0;
        for (int y = bgRegion.Top; y < bgRegion.Bottom; y++)
        {
            int baseIdx = y * width * 4;
            for (int x = bgRegion.Left; x < bgRegion.Right; x++)
            {
                int i = baseIdx + x * 4;
                bgCounts[((bgra[i + 2] >> 4) << 8) | ((bgra[i + 1] >> 4) << 4) | (bgra[i] >> 4)]++;
                bgTotal++;
            }
        }
        if (bgTotal == 0) return null;

        int bgKey = 0, bgCount = 0;
        for (int k = 0; k < bgCounts.Length; k++)
        {
            if (bgCounts[k] > bgCount)
            {
                bgCount = bgCounts[k];
                bgKey = k;
            }
        }
        if (bgCount < bgTotal * 0.30) return null;   // 背景不占优 → 取色不可信

        Color BgFromKey(int key)
        {
            int rQ = (key >> 8) & 0xF, gQ = (key >> 4) & 0xF, bQ = key & 0xF;
            return Color.FromRgb((byte)((rQ << 4) + 8), (byte)((gQ << 4) + 8), (byte)((bQ << 4) + 8));
        }
        var bg = BgFromKey(bgKey);
        long cum = 0;
        int medKey = bgKey;
        for (int k = 0; k < bgCounts.Length; k++)
        {
            cum += bgCounts[k];
            if (cum >= bgTotal / 2)
            {
                medKey = k;
                break;
            }
        }
        bg = LerpColor(bg, BgFromKey(medKey), 0.35);   // 混入中位色，带出皮肤色调

        // 文字色：候选行区域中 频率×对比度 最高者
        Color? fg = null;
        double bestScore = 0;
        if (fgRegion.Width >= 8 && fgRegion.Height >= 4)
        {
            var fgCounts = new int[4096];
            long fgTotal = 0;
            for (int y = fgRegion.Top; y < fgRegion.Bottom; y++)
            {
                int baseIdx = y * width * 4;
                for (int x = fgRegion.Left; x < fgRegion.Right; x++)
                {
                    int i = baseIdx + x * 4;
                    fgCounts[((bgra[i + 2] >> 4) << 8) | ((bgra[i + 1] >> 4) << 4) | (bgra[i] >> 4)]++;
                    fgTotal++;
                }
            }
            for (int k = 0; k < fgCounts.Length; k++)
            {
                int c = fgCounts[k];
                if (c < Math.Max(8, fgTotal * 0.004)) continue;
                var col = BgFromKey(k);
                double d = ColorDist(col, bg);
                if (d < 110) continue;
                double score = c * d;
                if (score > bestScore)
                {
                    bestScore = score;
                    fg = col;
                }
            }
        }
        var fgColor = fg ?? FallbackFg(bg);

        return new SkinTheme(bg, fgColor, fgRegion.Height, AlphaFromCfg());
    }

    private byte AlphaFromCfg() => (byte)Math.Clamp(_cfg.PillOpacityPercent * 255 / 100, 40, 255);


    private SkinTheme? CustomTheme(SkinTheme? fallback)
    {
        var bg = ParseColor(_cfg.PillBgColor, Color.FromRgb(0x2A, 0x2C, 0x30));
        var fg = ParseColor(_cfg.PillFgColor, Color.FromRgb(0xF2, 0xF5, 0xFA));
        return new SkinTheme(bg, fg, fallback?.RowTextHeightPx ?? 16, AlphaFromCfg());
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        try { return (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex.Trim()); }
        catch { return fallback; }
    }

    private static double ColorDist(Color a, Color b)
    {
        int dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    private static double Luminance(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;

    /// <summary>保证文字与背景的亮度差 ≥ need，不足时把文字推向黑/白。</summary>
    private static Color EnsureContrast(Color bg, Color fg, double need = 90)
    {
        double lb = Luminance(bg), lf = Luminance(fg);
        if (Math.Abs(lf - lb) >= need) return fg;
        if (lf > lb)
        {
            double t = Math.Clamp((need - (lf - lb)) / (255 - lf), 0, 1);
            return LerpColor(fg, Color.FromRgb(255, 255, 255), t);
        }
        double t2 = Math.Clamp((need - (lb - lf)) / Math.Max(lf, 1), 0, 1);
        return LerpColor(fg, Color.FromRgb(0, 0, 0), t2);
    }

    private static Color FallbackFg(Color bg) =>
        Luminance(bg) > 140 ? Color.FromRgb(0x2A, 0x2C, 0x30) : Color.FromRgb(0xF2, 0xF5, 0xFA);
}
