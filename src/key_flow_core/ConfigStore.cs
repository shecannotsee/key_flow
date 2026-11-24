using System.Text.Json;

namespace key_flow_core;

public sealed class JsonConfigStore(string path)
{
    public const int CurrentVersion = 9;
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
        if (version is >= 2 and < CurrentVersion) config.Version = CurrentVersion;
        if (version < CurrentVersion)
        {
            foreach (var profile in config.Profiles ?? [])
            {
                if (version < 5) profile.SequenceMode = profile.Steps?.Count > 1;
                foreach (var step in profile.Steps ?? [])
                {
                    if (version is 0 or 1)
                    {
                        step.WaitAfterMs = Math.Max(0, step.WaitAfterMs - step.HoldMs);
                    }
                    else if (version is >= 2 and < 7)
                    {
                        var oldStartInterval = step.WaitAfterMs;
                        step.HoldMs = RepeatTiming.HoldFor(Math.Clamp(oldStartInterval, 1, 3_600_000));
                        step.WaitAfterMs = Math.Max(0, oldStartInterval - step.HoldMs);
                    }
                }
                if (version < 6)
                {
                    var seconds = Math.Floor(profile.DurationSeconds);
                    var milliseconds = (int)Math.Round((profile.DurationSeconds - seconds) * 1000, MidpointRounding.AwayFromZero);
                    if (milliseconds >= 1000) { seconds++; milliseconds = 0; }
                    profile.DurationSeconds = seconds;
                    profile.DurationMilliseconds = milliseconds;
                    profile.IsInActivationPool = false;
                }
            }
            if (version < 6 && config.Profiles is { Count: > 0 }) config.Profiles[0].IsInActivationPool = true;
        }
        if (version < 9)
            config.Groups = (config.Profiles ?? []).Where(profile => profile.GroupName != "未分组")
                .GroupBy(profile => profile.GroupName)
                .Select(group => new ProfileGroup { Name = group.Key, MemberIds = group.Select(profile => profile.Id).ToList() }).ToList();
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
        Profiles = [new KeyProfile { Name = "我的方案 1", IsInActivationPool = true, Steps = [new("D8", 80)] }]
    };

    private static void Validate(AppConfig config)
    {
        if (config.Version == 0) config.Version = CurrentVersion;
        if (config.Version != CurrentVersion)
            throw new InvalidDataException($"不支持的配置版本: {config.Version}");
        if (config.Profiles is null)
            throw new InvalidDataException("配置列表不能为空值");
        if (config.Groups is null || config.Groups.Any(group => group is null || string.IsNullOrWhiteSpace(group.Name) || group.MemberIds is null))
            throw new InvalidDataException("配置组无效");
        if (config.Profiles.Any(profile => profile is null || string.IsNullOrWhiteSpace(profile.Id)) || config.Profiles.Select(profile => profile.Id).Distinct().Count() != config.Profiles.Count)
            throw new InvalidDataException("方案标识无效或重复");
        var ids = config.Profiles.Select(profile => profile.Id).ToHashSet();
        if (config.Groups.Any(group => group.MemberIds.Any(id => !ids.Contains(id)))) throw new InvalidDataException("配置组包含不存在的方案");
        foreach (var profile in config.Profiles)
        {
            AutomationValidation.Validate(profile);
            if (string.IsNullOrWhiteSpace(profile.Name)) throw new InvalidDataException("配置名称不能为空");
            if (profile.Steps is null || profile.Steps.Count == 0)
                throw new InvalidDataException($"配置 {profile.Name} 没有按键步骤");
            foreach (var step in profile.Steps)
            {
                if (string.IsNullOrWhiteSpace(step.Key)) throw new InvalidDataException($"配置 {profile.Name} 存在空按键");
                if (step.WaitAfterMs is < 0 or > 3_600_000 || step.HoldMs is < 1 or > 60_000)
                    throw new InvalidDataException($"配置 {profile.Name} 的间隔无效");
            }
        }
    }
}
