using System.IO;

namespace key_flow_app;

public static class AppPaths
{
    public static string DataDirectory { get; } = CreateDataDirectory();
    public static string Config => Path.Combine(DataDirectory, "config.json");
    public static string Log => Path.Combine(DataDirectory, "key_flow.log");
    private static string CreateDataDirectory()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "key_flow");
        Directory.CreateDirectory(path);
        return path;
    }
    public static void MigrateLegacyConfiguration()
        => MigrateLegacyConfiguration(Config, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppContext.BaseDirectory);

    public static void MigrateLegacyConfiguration(string destination, string localData, string baseDirectory)
    {
        if (File.Exists(destination)) return;
        // Historical names are deliberately retained only as read-only migration sources.
        var candidates = new List<string> { Path.Combine(localData, "KeyFlow", "config.json"), Path.Combine(baseDirectory, "config.json") };
        for (var parent = new DirectoryInfo(baseDirectory); parent is not null; parent = parent.Parent)
        {
            candidates.Add(Path.Combine(parent.FullName, "src", "key_flow_app", "bin", "Debug", "net8.0-windows", "config.json"));
            candidates.Add(Path.Combine(parent.FullName, "src", "KeyFlow.App", "bin", "Debug", "net8.0-windows", "config.json"));
            candidates.Add(Path.Combine(parent.FullName, "src", "InputProbe.App", "bin", "Debug", "net8.0-windows", "config.json"));
        }
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(candidate) || candidate.Equals(destination, StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(candidate, destination, false); // Copy only; preserve original and never overwrite newer data.
            break;
        }
    }
}
