using System.IO;
using key_flow_app;
using Xunit;

namespace key_flow_windows_tests;

public sealed class AppPathsTests
{
    [Fact]
    public void RenameCopiesLegacyConfigWithoutOverwritingOrRemovingOriginal()
    {
        var root = Path.Combine(Path.GetTempPath(), "key_flow_migration_" + Guid.NewGuid());
        var legacy = Path.Combine(root, "KeyFlow", "config.json"); // Migration fixture for the prior product.
        var destination = Path.Combine(root, "key_flow", "config.json");
        var sourceDir = Path.Combine(root, "source");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(legacy, "legacy-user-data");
            File.WriteAllText(Path.Combine(sourceDir, "config.json"), "older-source-data");
            AppPaths.MigrateLegacyConfiguration(destination, root, sourceDir);
            Assert.Equal("legacy-user-data", File.ReadAllText(destination));
            Assert.Equal("legacy-user-data", File.ReadAllText(legacy));
            File.WriteAllText(destination, "new-user-data");
            AppPaths.MigrateLegacyConfiguration(destination, root, sourceDir);
            Assert.Equal("new-user-data", File.ReadAllText(destination));
            Assert.Equal("legacy-user-data", File.ReadAllText(legacy));
        }
        finally { Directory.Delete(root, true); }
    }
}
