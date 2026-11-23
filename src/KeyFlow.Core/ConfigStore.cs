using System.Text.Json;

namespace KeyFlow.Core;

public sealed class JsonConfigStore(string path)
{
    public const int CurrentVersion = 2;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public async Task<AppConfig> LoadAsync()
    {
        if (!File.Exists(path)) return CreateDefault();
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
        var config = document.RootElement.Deserialize<AppConfig>(Options) ?? CreateDefault();
        var version = document.RootElement.TryGetProperty("Version", out var stored) ? stored.GetInt32() : 1;
        if (version is 0 or 1)
        {
            // v1 meant 50ms down + WaitAfterMs after release. Preserve old timing.
            foreach (var profile in config.Profiles ?? [])
                foreach (var step in profile.Steps ?? [])
                {
                    if (step.WaitAfterMs is < 0 or > 3_600_000) throw new InvalidDataException("旧配置间隔无效");
                    step.HoldMs = 50;
                    step.WaitAfterMs += 50;
                }
            config.Version = CurrentVersion;
        }
        Validate(config);
        return config;
    }

    public async Task SaveAsync(AppConfig config)
    {
        Validate(config);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = File.Create(temporary))
                await JsonSerializer.SerializeAsync(stream, config, Options).ConfigureAwait(false);
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static AppConfig CreateDefault() => new()
    {
        Version = CurrentVersion,
        Profiles = [new KeyProfile { Name = "数字 8 · 80ms", Steps = [new("D8", 80)] }]
    };

    private static void Validate(AppConfig config)
    {
        if (config.Version == 0) config.Version = CurrentVersion;
        if (config.Version != CurrentVersion)
            throw new InvalidDataException($"不支持的配置版本: {config.Version}");
        if (config.Profiles is null || config.Profiles.Count == 0)
            throw new InvalidDataException("配置至少需要一个配置档案");
        foreach (var profile in config.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Name)) throw new InvalidDataException("配置名称不能为空");
            if (profile.Steps is null || profile.Steps.Count == 0)
                throw new InvalidDataException($"配置 {profile.Name} 没有按键步骤");
            foreach (var step in profile.Steps)
            {
                if (string.IsNullOrWhiteSpace(step.Key)) throw new InvalidDataException($"配置 {profile.Name} 存在空按键");
                if (step.WaitAfterMs is < 0 or > 3_600_050 || step.HoldMs is < 1 or > 60_000)
                    throw new InvalidDataException($"配置 {profile.Name} 的间隔无效");
            }
        }
    }
}
