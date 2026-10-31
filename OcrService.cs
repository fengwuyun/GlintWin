using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace GlintWin;

/// <summary>
/// Windows 系统自带离线 OCR（zh-Hans-CN），把候选窗口区域截图转成带坐标的文本行。
/// 词级坐标（BoundingRect）用于定位候选行，供悬浮条贴附与皮肤取色。
/// </summary>
public sealed class OcrService
{
    private readonly OcrEngine? _engine;

    public OcrService(string language)
    {
        try
        {
            _engine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(language))
                   ?? OcrEngine.TryCreateFromUserProfileLanguages();
            Logger.Info(_engine != null
                ? "OCR engine ready: " + _engine.RecognizerLanguage.LanguageTag
                : "OCR engine UNAVAILABLE (语言包缺失)");
        }
        catch (Exception ex)
        {
            Logger.Error("OCR init failed: " + ex.Message);
        }
    }

    public bool Available => _engine != null;

    /// <summary>bgra 为自上而下 BGRA 像素（stride == width*4）。</summary>
    public async Task<IReadOnlyList<OcrLineData>> RecognizeAsync(byte[] bgra, int width, int height)
    {
        if (_engine is null) return Array.Empty<OcrLineData>();

        using var soft = SoftwareBitmap.CreateCopyFromBuffer(
            bgra.AsBuffer(), BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Ignore);
        var result = await _engine.RecognizeAsync(soft).AsTask();

        return result.Lines.Select(l => new OcrLineData(l.Text,
            l.Words.Select(w => new OcrWordData(
                w.Text, w.BoundingRect.X, w.BoundingRect.Y, w.BoundingRect.Width, w.BoundingRect.Height)).ToList()
        )).ToList();
    }

    /// <summary>
    /// 低对比度区域增强识别（拼音框：彩底浅字，常规 OCR 会整块跳过）：
    /// 裁剪 → 灰度 → 自适应对比度拉伸（2%/98% 分位定黑白点，任何配色通用）→ 2x 放大 → OCR。
    /// </summary>
    public async Task<IReadOnlyList<string>> RecognizeEnhancedAsync(System.Drawing.Bitmap src, System.Drawing.Rectangle region, string? debugSave = null)
    {
        if (_engine is null) return Array.Empty<string>();
        region.Intersect(new System.Drawing.Rectangle(0, 0, src.Width, src.Height));
        if (region.Width < 16 || region.Height < 8) return Array.Empty<string>();

        // 1) 裁剪 + 灰度（1x）
        using var gray = new System.Drawing.Bitmap(region.Width, region.Height,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(gray))
        {
            using var ia = new System.Drawing.Imaging.ImageAttributes();
            ia.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix
            {
                Matrix00 = 0.299f, Matrix01 = 0.299f, Matrix02 = 0.299f,
                Matrix10 = 0.587f, Matrix11 = 0.587f, Matrix12 = 0.587f,
                Matrix20 = 0.114f, Matrix21 = 0.114f, Matrix22 = 0.114f,
            });
            g.DrawImage(src, new System.Drawing.Rectangle(0, 0, region.Width, region.Height),
                region.X, region.Y, region.Width, region.Height,
                System.Drawing.GraphicsUnit.Pixel, ia);
        }

        // 2) 自适应对比度拉伸
        var bd = gray.LockBits(new System.Drawing.Rectangle(0, 0, gray.Width, gray.Height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        byte[] buf;
        try
        {
            buf = new byte[bd.Stride * bd.Height];
            Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
        }
        finally
        {
            gray.UnlockBits(bd);
        }

        var hist = new int[256];
        long total = 0;
        for (int i = 0; i < buf.Length; i += 4)
        {
            hist[buf[i]]++;
            total++;
        }
        long loTarget = (long)(total * 0.02), hiTarget = (long)(total * 0.98);
        int lo = 0, hi = 255;
        long cum = 0;
        for (int v = 0; v < 256; v++)
        {
            cum += hist[v];
            if (cum >= loTarget) { lo = v; break; }
        }
        cum = 0;
        for (int v = 0; v < 256; v++)
        {
            cum += hist[v];
            if (cum >= hiTarget) { hi = v; break; }
        }
        if (hi - lo < 12) return Array.Empty<string>();   // 纯色块，无内容
        for (int i = 0; i < buf.Length; i += 4)
        {
            byte g2 = (byte)Math.Clamp((buf[i] - lo) * 255f / (hi - lo), 0, 255);
            buf[i] = g2;
            buf[i + 1] = g2;
            buf[i + 2] = g2;
            buf[i + 3] = 255;
        }
        var bd2 = gray.LockBits(new System.Drawing.Rectangle(0, 0, gray.Width, gray.Height),
            System.Drawing.Imaging.ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(buf, 0, bd2.Scan0, buf.Length);
        }
        finally
        {
            gray.UnlockBits(bd2);
        }

        // 3) 2x 放大
        using var big = new System.Drawing.Bitmap(region.Width * 2, region.Height * 2,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(big))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(gray, 0, 0, big.Width, big.Height);
        }

        // 4) OCR
        if (debugSave != null)
        {
            try { big.Save(debugSave, System.Drawing.Imaging.ImageFormat.Png); } catch { }
        }
        var bd3 = big.LockBits(new System.Drawing.Rectangle(0, 0, big.Width, big.Height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        byte[] bytes;
        try
        {
            bytes = new byte[bd3.Stride * bd3.Height];
            Marshal.Copy(bd3.Scan0, bytes, 0, bytes.Length);
        }
        finally
        {
            big.UnlockBits(bd3);
        }

        using var soft = SoftwareBitmap.CreateCopyFromBuffer(
            bytes.AsBuffer(), BitmapPixelFormat.Bgra8, big.Width, big.Height, BitmapAlphaMode.Ignore);
        var result = await _engine.RecognizeAsync(soft).AsTask();
        return result.Lines.Select(l => l.Text).ToList();
    }
}
