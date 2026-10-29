using System.Text.Json;

namespace InputProbe.Core;

public sealed class JsonConfigStore(string path)
{
    public const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public async Task<AppConfig> LoadAsync()
    {
        if (!File.Exists(path)) return CreateDefault();
        await using var stream = File.OpenRead(path);
        var config = await JsonSerializer.DeserializeAsync<AppConfig>(stream, Options) ?? CreateDefault();
        Validate(config);
        return config;
    }

    public async Task SaveAsync(AppConfig config)
    {
        Validate(config);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, config, Options);
    }

    private static AppConfig CreateDefault() => new()
    {
        Version = CurrentVersion,
        Profiles = [new KeyProfile { Name = "基础序列", Steps = [new("D1"), new("D2")] }]
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
                if (step.WaitAfterMs is < 0 or > 3_600_000)
                    throw new InvalidDataException($"配置 {profile.Name} 的间隔无效");
            }
        }
    }
}
