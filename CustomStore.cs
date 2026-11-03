using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GlintWin;

/// <summary>自定义词库条目（可编辑模型）。</summary>
public sealed class CustomWord : System.ComponentModel.INotifyPropertyChanged
{
    private string _word;
    private string _pinyin;
    private string _meaning;

    public CustomWord(string word, string pinyin, string meaning)
    {
        _word = word; _pinyin = pinyin; _meaning = meaning;
    }

    public string Word { get => _word; set { _word = value; Pc(nameof(Word)); } }
    public string Pinyin { get => _pinyin; set { _pinyin = value; Pc(nameof(Pinyin)); } }
    public string Meaning { get => _meaning; set { _meaning = value; Pc(nameof(Meaning)); } }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void Pc(string n) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));
}

/// <summary>
/// 自定义词库（Data/custom.json，格式与主词典一致）+ CSV 模板/导入/导出。
/// 自定义词条在合并加载时优先于主词典与主题词包。
/// </summary>
public static class CustomStore
{
    private static string CustomPath => Path.Combine(AppPaths.Root, "Data", "custom.json");
    public static string TemplatePath => Path.Combine(AppPaths.Root, "Data", "custom_template.csv");
    public static string DataDir => Path.Combine(AppPaths.Root, "Data");

    public static List<CustomWord> Load()
    {
        var res = new List<CustomWord>();
        try
        {
            if (!File.Exists(CustomPath)) return res;
            var dto = JsonSerializer.Deserialize<Dictionary<string, CedictDictionary.DictDto>>(File.ReadAllText(CustomPath));
            if (dto == null) return res;
            foreach (var kv in dto.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                res.Add(new CustomWord(kv.Key, kv.Value.Pinyin ?? "",
                    string.Join("; ", kv.Value.Senses ?? Array.Empty<string>())));
        }
        catch (Exception ex)
        {
            Logger.Warn("custom store load: " + ex.Message);
        }
        return res;
    }

    public static void Save(IEnumerable<CustomWord> words)
    {
        var dto = new Dictionary<string, CedictDictionary.DictDto>(StringComparer.Ordinal);
        foreach (var w in words)
        {
            if (string.IsNullOrWhiteSpace(w.Word)) continue;
            var senses = (w.Meaning ?? "")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            dto[w.Word.Trim()] = new CedictDictionary.DictDto(w.Pinyin ?? "", senses);
        }
        File.WriteAllText(CustomPath,
            JsonSerializer.Serialize(dto, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }));
    }

    /// <summary>导入 CSV（词条,拼音,释义1[,释义2…]]；首行表头自动跳过）。</summary>
    public static List<CustomWord> ImportCsv(string path)
    {
        var res = new List<CustomWord>();
        using var parser=new Microsoft.VisualBasic.FileIO.TextFieldParser(path,Encoding.UTF8){TextFieldType=Microsoft.VisualBasic.FileIO.FieldType.Delimited,HasFieldsEnclosedInQuotes=true,TrimWhiteSpace=true};
        parser.SetDelimiters(",");
        while(!parser.EndOfData)
        {
            var cols=parser.ReadFields();
            if(cols==null||cols.Length<3||cols[0].Trim()=="词条")continue;
            var senses=string.Join("; ",cols.Skip(2).Select(c=>c.Trim()).Where(c=>c.Length>0));
            res.Add(new CustomWord(cols[0].Trim(),cols[1].Trim(),senses));
        }        return res;
    }

    public static void MergeInto(IList<CustomWord> target,IEnumerable<CustomWord> imported)
    {
        foreach(var word in imported)
        {
            var key=word.Word.Trim();if(key.Length==0)continue;
            var existing=target.FirstOrDefault(w=>string.Equals(w.Word.Trim(),key,StringComparison.Ordinal));
            if(existing==null)target.Add(new CustomWord(key,word.Pinyin,word.Meaning));
            else {existing.Pinyin=word.Pinyin;existing.Meaning=word.Meaning;}
        }
    }
    public static void ExportCsv(string path, IEnumerable<CustomWord> words)
    {
        var sb = new StringBuilder();
        sb.AppendLine("词条,拼音,释义1,释义2,释义3");
        foreach (var w in words)
        {
            var senses = (w.Meaning ?? "").Split(';');
            sb.Append(Escape(WordText.CleanTail(w.Word))).Append(',').Append(Escape(w.Pinyin)).Append(',')
              .Append(Escape(Col(senses, 0))).Append(',').Append(Escape(Col(senses, 1))).Append(',').AppendLine(Escape(Col(senses, 2)));
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));   // 带 BOM，Excel 直开不乱码
    }

    private static string Col(string[] arr, int i) => i < arr.Length ? arr[i].Trim() : "";

    private static string Escape(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
