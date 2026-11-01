using System.IO;
using System.Reflection;
namespace GlintWin;
/// <summary>Writable per-user state; bundled defaults are extracted only when missing.</summary>
public static class AppPaths
{
    public static string Root { get; private set; } = AppContext.BaseDirectory;
    public static string ConfigFile => Path.Combine(Root, "config.json");
    public static void Initialize(string? rootOverride = null, string? legacyOverride = null)
    {
        Root = Path.GetFullPath(rootOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlintWin"));
        Directory.CreateDirectory(Root);
        string legacy = legacyOverride ?? AppContext.BaseDirectory;
        if (!string.Equals(Path.GetFullPath(legacy).TrimEnd(Path.DirectorySeparatorChar), Root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            CopyMissing(Path.Combine(legacy, "config.json"), ConfigFile);
            var data = Path.Combine(legacy, "Data");
            if (Directory.Exists(data))
                foreach (var source in Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories))
                    CopyMissing(source, Path.Combine(Root, "Data", Path.GetRelativePath(data, source)));
        }
        var assembly = typeof(AppPaths).Assembly;
        const string prefix = "GlintWin.Bundle/";
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal)))
        {
            string destination = Path.GetFullPath(Path.Combine(Root, name[prefix.Length..].Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            if (File.Exists(destination)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = assembly.GetManifestResourceStream(name)!;
            var temp = destination + ".extracting";
            using (var output = File.Create(temp)) input.CopyTo(output);
            File.Move(temp, destination, false);
        }
    }
    private static void CopyMissing(string source, string destination)
    {
        if (!File.Exists(source) || File.Exists(destination)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, false);
    }
}
