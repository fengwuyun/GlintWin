using System.IO;
using System.Text.Json;

namespace GlintWin;

public sealed class Config
{
    public int PollIntervalMs { get; set; } = 40;
    public int OffsetYPx { get; set; } = 8;
    public double FontSizeDip { get; set; } = 15;
    public string CandidateWindowClass { get; set; } = "SoPY_Comp";
    public string DictionaryPath { get; set; } = "Data/cedict.json";
    public string OcrLanguage { get; set; } = "zh-Hans-CN";

    public static Config Load()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "config.json");
            if (File.Exists(path))
            {
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(path), opts) ?? new Config();
            }
        }
        catch
        {
            // 配置损坏时回退默认值
        }
        return new Config();
    }
}
