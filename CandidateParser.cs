using System.Text;
using System.Text.RegularExpressions;

namespace GlintWin;

/// <summary>
/// 从 OCR 文本中提取第一候选词。
/// OCR 常见噪声：全角句号「．」、间隔号「·」、顿号、数字与汉字粘连（“2咻好”）、
/// 序号“1.”被高亮样式吞掉。策略：先归一化，再按“数字+分隔符 / 数字+非ASCII”截断。
/// </summary>
public static partial class CandidateParser
{
    // 宽松判定拼音行：全 latin 即可（候选行必含汉字不会误判）。
    // 拼音框光标会被 OCR 读成 l/I/| 混在词尾——拼音音节不以 l 结尾，先剥掉尾部光标字符。
    [GeneratedRegex("^[a-zA-Z][a-zA-Z'|\\s]{1,}$")]
    private static partial Regex PinyinRegex();

    [GeneratedRegex("[|Il]+$")]
    private static partial Regex TrailingCaretRegex();

    [GeneratedRegex("^[a-zA-Z']+")]
    private static partial Regex LeadingLatinRegex();

    [GeneratedRegex(@"^1\s*[\.。]?")]
    private static partial Regex LeadingOneRegex();

    // 边界优先级：数字+分隔符 → 数字+空格+汉字（微软拼音候选行为空格分隔）→ 2-9 后直接跟非 ASCII。
    // 0/1 不作为无分隔边界，避免“第1章”“4G”这类词中数字误切。
    [GeneratedRegex(@"\d{1,2}\s*[\.。]|[2-9](?=[^\x00-\x7F])|\d{1,2}\s+(?=[^\x00-\x7F])")]
    private static partial Regex ItemBoundaryRegex();

    [GeneratedRegex(@"[\u4e00-\u9fff]")]
    private static partial Regex CjkRegex();

    [GeneratedRegex(@"^\d{1,2}\s*[\.。]?")]
    private static partial Regex LeadingIndexRegex();

    [GeneratedRegex(@"^[|\\/\r\n…—,，。;；:：!！\?？.\s]+")]
    private static partial Regex LeadingNoiseRegex();

    [GeneratedRegex(@"[|\\/\r\n…—,，。;；:：!！\?？.\s]+$")]
    private static partial Regex TrailingNoiseRegex();

    public sealed record ParseResult(string? Pinyin, string? Top);

    public static ParseResult Parse(IReadOnlyList<string> lines)
    {
        string? pinyin = null;
        var candParts = new List<string>();
        foreach (var raw in lines)
        {
            var line = TrailingCaretRegex().Replace(raw.Trim(), "");
            if (line.Length == 0) continue;
            if (line.Length >= 2 && PinyinRegex().IsMatch(line))
            {
                // 清洗残留噪声，只留字母和隔音符
                var cleaned = new string(line.ToLowerInvariant()
                    .Where(c => (c >= 'a' && c <= 'z') || c == '\'').ToArray());
                if (cleaned.Length >= 2) pinyin = cleaned;
                continue;
            }
            if (CjkRegex().IsMatch(line)) candParts.Add(line);
        }

        return new ParseResult(pinyin, ExtractTop(string.Join(" ", candParts)));
    }

    /// <summary>
    /// 按词间隙把候选行切成候选块（左→右），返回清洗后的块列表。
    /// 块 0 = 第一候选（高亮项，序号“1.”常被 OCR 吞掉，间隙切分不受影响）。
    /// 间隙阈值随候选字高自适应，避免高 DPI 下块内词被误切开。
    /// </summary>
    public static List<string> ExtractChunksFromRow(OcrLineData row)
    {
        var words = row.Words.OrderBy(w => w.X).ToList();
        var result = new List<string>();
        if (words.Count == 0) return result;

        double avgH = words.Average(w => w.Height);
        double gapThreshold = Math.Max(6.0, avgH * 0.30);

        var chunks = new List<StringBuilder> { new() };
        double prevRight = words[0].X;
        foreach (var w in words)
        {
            if (chunks[^1].Length > 0 && w.X - prevRight > gapThreshold)
                chunks.Add(new StringBuilder());
            chunks[^1].Append(w.Text);
            prevRight = w.X + w.Width;
        }

        foreach (var chunk in chunks)
        {
            var s = CleanChunk(chunk.ToString());
            if (s != null) result.Add(s);
        }
        return result;
    }

    private static string? CleanChunk(string chunk)
    {
        var s = Normalize(chunk);
        if (s.Length == 0) return null;
        if (s.All(c => char.IsDigit(c) || c == '.' || c == ' ')) return null;   // 纯序号块

        // 前导序号：最多两位数字（“1.学习”/“2字习”）。注意“520你”这类数字词会被误去前缀，罕见，可接受。
        s = LeadingIndexRegex().Replace(s, "");
        s = LeadingLatinRegex().Replace(s, "").Trim();
        var m = ItemBoundaryRegex().Match(s);
        if (m.Success && m.Index > 0) s = s[..m.Index];
        s = s.Replace(" ", "").Replace("\u00a0", "");
        s = LeadingNoiseRegex().Replace(s, "");
        s = TrailingNoiseRegex().Replace(s, "");
        return s.Length > 0 && s.Length <= 16 && CjkRegex().IsMatch(s) ? s : null;
    }

    /// <summary>
    /// 行内是否出现「1」序号标记。第一候选被高亮动画吞掉时行内只剩 2/3/4…，
    /// 此时 chunks[0] 实为第二候选，该帧不可信。
    /// 兼容 OCR 把 1 误读成 I/i/l/| 的变体。
    /// </summary>
    public static bool HasMarkerOne(string raw)
    {
        var s = Normalize(raw);
        if (s.Length == 0) return false;
        if (Regex.IsMatch(s, @"(?:^|\s)1\s*[\.。,，]?\s*(?=[^\x00-\x7F])")) return true;
        return Regex.IsMatch(s, @"(?:^|\s)[Iil|]\s*[\.。]\s*(?=[^\x00-\x7F])");
    }

    /// <summary>纯文本兜底路径（词级坐标不可用时）。</summary>
    public static string? ExtractTop(string raw)
    {
        var s = Normalize(raw);
        s = LeadingLatinRegex().Replace(s, "").Trim();
        s = LeadingOneRegex().Replace(s, "").TrimStart();

        var m = ItemBoundaryRegex().Match(s);
        if (m.Success && m.Index > 0) s = s[..m.Index];

        s = s.Replace(" ", "").Replace("\u00a0", "");
        s = LeadingNoiseRegex().Replace(s, "");
        s = TrailingNoiseRegex().Replace(s, "");

        if (s.Length == 0 || s.Length > 16) return null;
        return CjkRegex().IsMatch(s) ? s : null;
    }

    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            sb.Append(ch switch
            {
                >= '０' and <= '９' => (char)(ch - '０' + '0'),
                '．' or '、' or '·' or '・' => '.',
                _ => ch,
            });
        }
        return sb.ToString();
    }
}
