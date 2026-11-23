using System.IO;

namespace KeyFlow.App;

public static class AppPaths
{
    public static string DataDirectory { get; } = CreateDataDirectory();
    public static string Config => Path.Combine(DataDirectory, "config.json");
    public static string Log => Path.Combine(DataDirectory, "keyflow.log");
    private static string CreateDataDirectory()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyFlow");
        Directory.CreateDirectory(path);
        return path;
    }
    public static void MigrateLegacyConfiguration()
    {
        if (File.Exists(Config)) return;
        var candidates = new List<string> { Path.Combine(AppContext.BaseDirectory, "config.json") };
        for (var parent = new DirectoryInfo(AppContext.BaseDirectory); parent is not null; parent = parent.Parent)
        {
            candidates.Add(Path.Combine(parent.FullName, "src", "InputProbe.App", "bin", "Debug", "net8.0-windows", "config.json"));
            candidates.Add(Path.Combine(parent.FullName, "src", "KeyFlow.App", "bin", "Debug", "net8.0-windows", "config.json"));
        }
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(candidate) || candidate.Equals(Config, StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(candidate, Config, false); // Copy only; preserve original and never overwrite newer data.
            break;
        }
    }
}
