using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GlintWin;

/// <summary>
/// CC-CEDICT 本地词典（简体 → 拼音 + 英文释义），启动时一次性加载进内存。
/// 查询顺序：整词精确 → 最长前缀切分（≤6 字）→ 拼音交叉验证的近似纠错
/// （OCR 读错 ≤1 个字且该词拼音与输入框拼音一致时自动纠正）。
/// </summary>
public sealed class CedictDictionary
{
    private sealed record DictDto(
        [property: JsonPropertyName("p")] string Pinyin,
        [property: JsonPropertyName("s")] string[] Senses);

    private sealed record Entry(string Pinyin, string[] Senses);

    private static readonly Regex ParenRegex = new(@"\([^)]*\)");
    private readonly Dictionary<string, Entry> _map;

    private CedictDictionary(Dictionary<string, Entry> map) => _map = map;

    public static CedictDictionary? Load(string path)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<Dictionary<string, DictDto>>(File.ReadAllText(path));
            var map = new Dictionary<string, Entry>(dto?.Count ?? 0, StringComparer.Ordinal);
            if (dto != null)
            {
                foreach (var kv in dto)
                    map[kv.Key] = new Entry(kv.Value.Pinyin ?? "", kv.Value.Senses ?? Array.Empty<string>());
            }
            Logger.Info($"dictionary loaded: {map.Count} entries from {path}");
            return new CedictDictionary(map);
        }
        catch (Exception ex)
        {
            Logger.Warn("dictionary load failed: " + ex.Message);
            return null;
        }
    }

    public string? Lookup(string word)
    {
        if (word.Length == 0) return null;
        if (_map.TryGetValue(word, out var senses)) return Format(senses.Senses);
        if (word.Length is >= 2 and <= 6) return Segment(word);
        return null;
    }

    private string? Segment(string word)
    {
        var parts = new List<string>();
        int i = 0;
        while (i < word.Length && parts.Count < 4)
        {
            int matched = 0;
            int max = Math.Min(4, word.Length - i);
            for (int len = max; len >= 1; len--)
            {
                if (_map.ContainsKey(word.Substring(i, len)))
                {
                    matched = len;
                    break;
                }
            }
            if (matched == 0) return null;
            var gloss = Format(_map[word.Substring(i, matched)].Senses);
            if (gloss is null) return null;
            parts.Add(gloss);
            i += matched;
        }
        return i == word.Length && parts.Count > 0 ? string.Join(" / ", parts) : null;
    }

    /// <summary>
    /// 拼音交叉验证纠错：在词典中找与 <paramref name="word"/> 等长、至多差 1 字、
    /// 且拼音与组词拼音一致的词。拼音对不上的一律不纠正（宁缺勿错）。
    /// </summary>
    public string? FuzzyCorrect(string word, string? compositionPinyin)
    {
        if (compositionPinyin is null || word.Length is < 2 or > 6) return null;
        var norm = NormPinyin(compositionPinyin);
        if (norm.Length == 0) return null;

        string? best = null;
        foreach (var kv in _map)
        {
            if (kv.Key.Length != word.Length) continue;
            if (!WithinOneChar(kv.Key, word)) continue;
            if (!string.Equals(NormPinyin(kv.Value.Pinyin), norm, StringComparison.Ordinal)) continue;
            if (best is null || string.CompareOrdinal(kv.Key, best) < 0) best = kv.Key;
        }
        return best;
    }

    private static bool WithinOneChar(string a, string b)
    {
        int diff = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i] && ++diff > 1) return false;
        }
        return true;
    }

    private static string NormPinyin(string p)
    {
        var sb = new StringBuilder(p.Length);
        foreach (var ch in p)
        {
            if (ch is >= 'A' and <= 'Z') sb.Append((char)(ch + 32));
            else if (ch is >= 'a' and <= 'z') sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>取前两个释义，去掉 “to ” 前缀与括号注释，用 “ · ” 连接。</summary>
    public static string? Format(string[] senses)
    {
        var outs = new List<string>();
        foreach (var raw in senses)
        {
            foreach (var piece in raw.Split(';'))
            {
                var s = ParenRegex.Replace(piece, "").Trim();
                if (s.StartsWith("to ", StringComparison.Ordinal)) s = s[3..].Trim();
                if (s.Length == 0) continue;
                if (!outs.Contains(s, StringComparer.OrdinalIgnoreCase)) outs.Add(s);
                if (outs.Count >= 2) break;
            }
            if (outs.Count >= 2) break;
        }
        return outs.Count > 0 ? string.Join(" · ", outs) : null;
    }
}
