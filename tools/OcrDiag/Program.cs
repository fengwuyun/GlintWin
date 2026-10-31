// OCR 诊断：对真实截图跑增强识别，保存中间产物，验证管线与字体可读性
// 用法: dotnet run --project tools/OcrDiag -- <图片路径>
using System.Drawing;
using GlintWin;

var path = args.Length > 0 ? args[0] : @"D:\Development\ZCode_workplace\.ime-probe\t1a_crop0.png";
using var bmp = new Bitmap(path);
Console.WriteLine($"image: {path} ({bmp.Width}x{bmp.Height})");

var ocr = new OcrService("zh-Hans-CN");

// 全图 + 上半部各跑一次增强
foreach (var (label, region) in new[]
{
    ("full", new Rectangle(0, 0, bmp.Width, bmp.Height)),
    ("top-half", new Rectangle(0, 0, bmp.Width, bmp.Height / 2)),
})
{
    var lines = await ocr.RecognizeEnhancedAsync(bmp, region,
        Path.Combine(Path.GetDirectoryName(path)!, $"enhanced_{label}.png"));
    Console.WriteLine($"[{label}] lines={lines.Count}");
    foreach (var l in lines) Console.WriteLine($"    [{l}]");
}
Console.WriteLine("DONE");
