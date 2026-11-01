# -*- coding: utf-8 -*-
# Coordinator 定位重构：候选带（词级）替代行分组；锚点回到候选行下方
import io

p = 'Coordinator.cs'
s = io.open(p, encoding='utf-8').read()

start = s.index('                _lastLines = ScaleLines(await _ocr.RecognizeAsync(bigBytes, big.Width, big.Height), 1.0 / OcrScale);')
end = s.index('                var sampled = !frameTrusted ? _lastTheme')

NEW = '''                _lastLines = ScaleLines(await _ocr.RecognizeAsync(bigBytes, big.Width, big.Height), 1.0 / OcrScale);
                var parsed = CandidateParser.Parse(_lastLines.Select(l => l.Text).ToList());
                _lastPinyin = parsed.Pinyin;

                // 拼音框是彩底浅字，常规 OCR 常整块跳过：整窗增强识别补拿拼音（纠错用）。
                if (string.IsNullOrEmpty(_lastPinyin))
                {
                    var enhanced = await _ocr.RecognizeAsync(bmp, new Rectangle(0, 0, w, h));
                    if (enhanced.Count > 0)
                    {
                        _lastPinyin = CandidateParser.Parse(enhanced).Pinyin;
                        if (_lastPinyin != null)
                            Logger.Info($"pinyin(enhanced)={_lastPinyin}");
                    }
                }

                // 候选带：窗口下部所有 汉字/序号 词的并集（不依赖 OCR 行分组，
                // 行分组会把候选行拆碎导致锚点/内容不稳）
                var band = FindCandidateBand(_lastLines, w, h);
                List<string> chunks = new();
                string? bandText = null;
                Rectangle? rowBmp = null;
                bool marker1 = false;
                if (band.HasValue)
                {
                    var (bandRect, bandWords) = band.Value;
                    rowBmp = bandRect;
                    chunks = CandidateParser.ExtractChunksFromRow(new OcrLineData("band", bandWords));
                    bandText = string.Join(" ", bandWords.Select(x => x.Text));
                    marker1 = CandidateParser.HasMarkerOne(bandText);
                }

                string? newTop;
                if (band is null || bandText is null)
                {
                    // 屏幕下部没有候选内容 → 无候选（隐藏路径，配合 500ms 防抖）
                    newTop = null;
                }
                else if (strict && CountNumberedMarkers(bandText) < 2)
                {
                    // 微软拼音通道：候选栏必然包含 ≥2 个数字序号项，防止正文误判
                    newTop = null;
                }
                else if (chunks.Count > 0 && marker1)
                {
                    newTop = chunks[0];
                }
                else if (chunks.Count > 0)
                {
                    // 第一候选被高亮动画吞掉（无 1 序号）：对行首区域（2 序号之前）增强补读
                    bool recovered = false;
                    double? x2 = null;
                    foreach (var wd in band.Value.Words)
                    {
                        var txt = wd.Text.TrimStart();
                        if (txt.Length > 0 && (txt[0] == '2' || txt[0] == '２'))
                        {
                            x2 = wd.X;
                            break;
                        }
                    }
                    if (x2.HasValue && x2.Value - rowBmp!.Value.X > 8)
                    {
                        var crop = new Rectangle(
                            rowBmp.Value.X,
                            Math.Max(0, rowBmp.Value.Y - 2),
                            (int)(x2.Value - rowBmp.Value.X - 2),
                            rowBmp.Value.Height + 4);
                        var enhanced1 = await _ocr.RecognizeEnhancedAsync(bmp, crop);
                        var cand = enhanced1.Count > 0 ? CandidateParser.ExtractTop(string.Join(" ", enhanced1)) : null;
                        if (!string.IsNullOrEmpty(cand))
                        {
                            newTop = cand;
                            recovered = true;
                            Logger.Info($"first-item recovered: {cand}");
                        }
                    }
                    if (!recovered) newTop = null;   // 补读失败：宁缺勿错（chunks[0] 此时是第二候选）
                }
                else
                {
                    newTop = null;
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

                // 帧可信度：补读成功或看到 1 序号才接受本帧；否则保持旧值/隐藏
                bool frameTrusted = resolvedTop is null ||
                                    strict ||
                                    recovered ||
                                    marker1;
                if (!frameTrusted)
                {
                    _untrustedStreak++;
                    if (_pillVisible && _untrustedStreak >= 3)
                    {
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

                // 行框/哈希/主题只在可信帧更新，防污染
                if (frameTrusted && rowBmp.HasValue)
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
                else if (!frameTrusted)
                {
                    _lastHasRow = _hashRowBmp.HasValue;
                }

                var boxRect = new Rectangle(0, 0, w, h * 40 / 100);
'''

s = s[:start] + NEW + s[end:]

# 主题采样行修正（rowBmp 现在是 band）

