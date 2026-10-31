// CandidateParser 回归测试：用例全部来自真机 OCR 实际输出过的乱码形态。
// 运行：dotnet run --project tools/ParserTest
using GlintWin;

var failures = new List<string>();

void Check(string name, string? actual, string? expected)
{
    if (Equals(actual, expected)) Console.WriteLine($"PASS  {name}");
    else
    {
        Console.WriteLine($"FAIL  {name}: expected=[{expected}] actual=[{actual}]");
        failures.Add(name);
    }
}

// ---------- 文本级 ExtractTop（真实日志样例） ----------
Check("数字+全角分隔符", CandidateParser.ExtractTop("1.你好世界 2.你好 3.你号 4.拟好 5.倪好"), "你好世界");
Check("序号与汉字粘连", CandidateParser.ExtractTop("你好世界2咻好3．你号4。拟好5．倪好"), "你好世界");
Check("间隔号粘连", CandidateParser.ExtractTop("学习2·二．3．血冼40系5．雪"), "学习");
Check("首帧碎片", CandidateParser.ExtractTop("你2“那3．呢4就5．奶"), "你");
Check("多音节粘连", CandidateParser.ExtractTop("你好帅2咻好骚3．你好色4咻好瘦5·你好"), "你好帅");
Check("正常带序号", CandidateParser.ExtractTop("1.学习 2.字习 3.血洗 4.学系 5.雪"), "学习");
Check("词内数字不误切", CandidateParser.ExtractTop("1.第1章 2.第一章"), "第1章");
Check("拼音行混入", CandidateParser.ExtractTop("nihao'shijie 1.你好世界 2.你好"), "你好世界");
Check("空文本", CandidateParser.ExtractTop(""), null);
Check("无汉字", CandidateParser.ExtractTop("abcdef 123"), null);

// ---------- Parse：拼音行识别 ----------
var pr = CandidateParser.Parse(new[] { "nihao'shijie", "1.你好世界 2.你好" });
Check("Parse 拼音", pr.Pinyin, "nihao'shijie");
Check("Parse 候选", pr.Top, "你好世界");
Check("拼音混入光标字符", CandidateParser.Parse(new[] { "nihao'shijieIl", "1.你好世界" }).Pinyin, "nihao'shijie");

// ---------- 词级坐标 ExtractChunksFromRow ----------
OcrLineData Row(params (string Text, double X, double W)[] words) =>
    new("row", words.Select(w => new OcrWordData(w.Text, w.X, 0, w.W, 16)).ToList());

// 序号可见：1. 与 候选词 间隙小（同一块），候选之间间隙大
var row1 = Row(("1.", 0, 10), ("学习", 12, 40), ("2.", 70, 10), ("字习", 82, 40), ("3.", 140, 10), ("血洗", 152, 40));
var c1 = CandidateParser.ExtractChunksFromRow(row1);
Check("词级-序号可见 首块", c1.Count > 0 ? c1[0] : null, "学习");
Check("词级-序号可见 块数>=3", c1.Count >= 3 ? "3" : null, "3");

// 序号被高亮吞掉（真机常见）：纯间隙切分
var row2 = Row(("学习", 0, 40), ("字习", 60, 40), ("血洗", 120, 40));
var c2 = CandidateParser.ExtractChunksFromRow(row2);
Check("词级-无序号 首块", c2.Count > 0 ? c2[0] : null, "学习");

// 装饰性噪声块被跳过
var row3 = Row(("❀", 0, 8), ("学习", 20, 40));
var c3 = CandidateParser.ExtractChunksFromRow(row3);
Check("词级-噪声块跳过", c3.Count > 0 ? c3[0] : null, "学习");

// 高 DPI（字高更大 → 间隙阈值自适应）
var row4 = new OcrLineData("row", new OcrWordData[] {
    new("1.", 0, 0, 14, 24), new("你好世界", 16, 0, 120, 24),
    new("2.", 160, 0, 14, 24), new("你好", 176, 0, 80, 24) });
var c4 = CandidateParser.ExtractChunksFromRow(row4);
Check("词级-高DPI 首块", c4.Count > 0 ? c4[0] : null, "你好世界");

Console.WriteLine(failures.Count == 0 ? "\nALL PASS" : $"\n{failures.Count} FAILED");
return failures.Count == 0 ? 0 : 1;
