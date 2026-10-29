using System.Text.Json;

namespace InputProbe.Core;

public sealed class JsonConfigStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public async Task<AppConfig> LoadAsync()
    {
        if (!File.Exists(path)) return CreateDefault();
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<AppConfig>(stream, Options) ?? CreateDefault();
    }

    public async Task SaveAsync(AppConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, config, Options);
    }

    private static AppConfig CreateDefault() => new()
    {
        Profiles = [new KeyProfile { Name = "基础序列", Steps = [new("D1"), new("D2")] }]
    };
}
