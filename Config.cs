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

    // 微软拼音通道（光标锚定 OCR）
    public int MspPollIntervalMs { get; set; } = 120;
    public int MspBandWidthPx { get; set; } = 560;
    public int MspBandHeightPx { get; set; } = 120;
    public int MspBandOffsetY { get; set; } = 8;

    // 悬浮条外观（auto = 跟随皮肤取色；custom = 使用自定义颜色）
    public string PillMode { get; set; } = "auto";
    public string PillBgColor { get; set; } = "#FF2A2C30";
    public string PillFgColor { get; set; } = "#FFF2F5FA";
    public int PillOpacityPercent { get; set; } = 85;

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

    public void Save()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "config.json");
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 保存失败不影响运行（下次修改可再试）
        }
    }
}
