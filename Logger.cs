using System.IO;

namespace GlintWin;

public static class Logger
{
    private static readonly object Gate = new();
    private static string? _file;

    public static void Init()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GlintWin", "logs");
            Directory.CreateDirectory(dir);
            _file = Path.Combine(dir, $"glint-{DateTime.Now:yyyyMMdd}.log");
            Info("==== GlintWin started ====");
        }
        catch
        {
            _file = null;
        }
    }

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg) => Write("ERR ", msg);

    private static int _writes;

    private static void Write(string level, string msg)
    {
        lock (Gate)
        {
            try
            {
                if (_file == null) return;
                if (++_writes % 512 == 1) RotateIfNeeded();
                File.AppendAllText(_file,
                    $"{DateTime.Now:HH:mm:ss.fff} [{level}] {msg}{Environment.NewLine}");
            }
            catch
            {
                // 日志失败不影响主流程
            }
        }
    }

    /// <summary>单文件超过 5MB 轮转为 .old，防止长期运行日志无限增长。</summary>
    private static void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(_file) || new FileInfo(_file!).Length < 5_000_000) return;
            var old = _file + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(_file, old);
        }
        catch
        {
            // 轮转失败不影响主流程
        }
    }
}
