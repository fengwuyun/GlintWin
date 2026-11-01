# -*- coding: utf-8 -*-
# GlintWin 补丁：位置锚定 + 可信帧闸门 + 背景采样重构 + 分隔符拒绝
import io

# ---------- CandidateParser: 词块分隔符拒绝 ----------
p = 'CandidateParser.cs'
s = io.open(p, encoding='utf-8').read()
old = '''        s = LeadingNoiseRegex().Replace(s, "");
        s = TrailingNoiseRegex().Replace(s, "");
        return s.Length > 0 && s.Length <= 16 && CjkRegex().IsMatch(s) ? s : null;
    }'''
new = '''        s = LeadingNoiseRegex().Replace(s, "");
        s = TrailingNoiseRegex().Replace(s, "");
        if (s.Length == 0 || s.Length > 16) return null;
        if (s.Contains('|') || s.Contains('[') || s.Contains(']')) return null;   // OCR 分隔符残留
        return CjkRegex().IsMatch(s) ? s : null;
    }'''
assert old in s, 'parser anchor missing'
s = s.replace(old, new)
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('parser ok')

# ---------- Coordinator ----------
p = 'Coordinator.cs'
s = io.open(p, encoding='utf-8').read()

# 1) 窗口底部锚定
old = '''            (anchor, anchorOffset) = _lastHasRow ? (_lastRowRect, 4) : (rect, 6);'''
new = '''            // 产品规则：悬浮条恒在搜狗输入窗（拼音框+候选行）正下方 —— 与 OCR 质量解耦，永不跑到上方
            (anchor, anchorOffset) = (rect, 6);'''
assert old in s, 'anchor anchor missing'
s = s.replace(old, new)

# 2) 不可信帧连续计数
old = '''                if (!frameTrusted)
                {
                    resolvedTop = _lastTop;
                    resolvedGloss = _lastGloss;
                }'''
new = '''                if (!frameTrusted)
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
                }'''
assert old in s, 'trust block missing'
s = s.replace(old, new)

# 3) 字段 + 重置
old = '''    private string? _prevFrameTop;    // 上一 OCR 帧的候选（用于 OCR 抖动去重）'''
new = '''    private string? _prevFrameTop;    // 上一 OCR 帧的候选（用于 OCR 抖动去重）
    private int _untrustedStreak;     // 连续不可信帧数（>=3 隐藏悬浮条防冻结残留）'''
assert old in s, 'field anchor missing'
s = s.replace(old, new)
old = '''        _pendingTop = null;
        _prevFrameTop = null;'''
new = '''        _pendingTop = null;
        _prevFrameTop = null;
        _untrustedStreak = 0;'''
assert old in s, 'reset anchor missing'
s = s.replace(old, new)

# 4) SampleTheme 重构：底色取自不透明拼音框区域
old = '''    private SkinTheme? SampleTheme(byte[] bgra, int width, int height, Rectangle? rowRect)
    {
        var r = rowRect ?? new Rectangle(0, height * 55 / 100, width, height * 40 / 100);
        r = ClampRect(r, width, height);
        if (r.Width < 20 || r.Height < 8) return null;

        var counts = new int[4096];
        long total = 0;
        for (int y = r.Top; y < r.Bottom; y++)
        {
            int baseIdx = y * width * 4;
            for (int x = r.Left; x < r.Right; x++)
            {
                int i = baseIdx + x * 4;
                int key = ((bgra[i + 2] >> 4) << 8) | ((bgra[i + 1] >> 4) << 4) | (bgra[i] >> 4);
                counts[key]++;
                total++;
            }
        }
        if (total == 0) return null;

        int bgKey = 0, bgCount = 0;
        for (int k = 0; k < counts.Length; k++)
        {
            if (counts[k] > bgCount)
            {
                bgCount = counts[k];
                bgKey = k;
            }
        }
        if (bgCount < total * 0.30) return null;   // 背景不占优 → 取色不可信

        Color BgFromKey(int key)
        {
            int rQ = (key >> 8) & 0xF, gQ = (key >> 4) & 0xF, bQ = key & 0xF;
            return Color.FromRgb((byte)((rQ << 4) + 8), (byte)((gQ << 4) + 8), (byte)((bQ << 4) + 8));
        }
        var bg = BgFromKey(bgKey);

        // 混入行区域中位色：最高频色常偏向高亮白，中位色带出候选栏本身的皮肤色调
        long cum = 0;
        int medKey = bgKey;
        for (int k = 0; k < counts.Length; k++)
        {
            cum += counts[k];
            if (cum >= total / 2)
            {
                medKey = k;
                break;
            }
        }
        bg = LerpColor(bg, BgFromKey(medKey), 0.35);

        // 文字色：频率 × 与背景的欧氏距离，取最高分
        Color? fg = null;
        double bestScore = 0;
        for (int k = 0; k < counts.Length; k++)
        {
            int c = counts[k];
            if (c < Math.Max(8, total * 0.004)) continue;
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
        var fgColor = fg ?? FallbackFg(bg);

        return new SkinTheme(bg, fgColor, r.Height, AlphaFromCfg());
    }'''
new = '''    /// <summary>
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
    }'''
assert old in s, 'sampletheme anchor missing'
s = s.replace(old, new)

# 5) 调用处传入 boxRect
old = '''                var sampled = !frameTrusted ? _lastTheme
                    : rowBmp.HasValue ? SampleTheme(bytes, w, h, rowBmp) : _lastTheme;'''
new = '''                var boxRect = new Rectangle(0, 0, w, h * 40 / 100);
                var sampled = !frameTrusted ? _lastTheme
                    : rowBmp.HasValue ? SampleTheme(bytes, w, h, rowBmp, boxRect) : _lastTheme;'''
assert old in s, 'sample call anchor missing'
s = s.replace(old, new)

io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('coordinator ok')
