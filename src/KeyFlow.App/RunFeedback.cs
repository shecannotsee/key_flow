using System.IO;
using System.Media;
using KeyFlow.Core;

namespace KeyFlow.App;

// Short rising / falling two-note cues, independent of the system sound theme.
public sealed class RunFeedback
{
    private Task pending = Task.CompletedTask;
    public void Play(bool running, IInputLogger logger)
    {
        pending = pending.ContinueWith(_ =>
        {
            try
            {
                using var wave = new MemoryStream(CreateWave(running));
                using var player = new SoundPlayer(wave);
                player.PlaySync();
            }
            catch (Exception exception) { logger.Error("状态提示音播放失败", exception); }
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    public static byte[] CreateWave(bool running)
    {
        const int rate = 22050, noteSamples = 1764; // Two 80ms notes.
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var size = noteSamples * 2 * sizeof(short);
        writer.Write("RIFF"u8); writer.Write(36 + size); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(size);
        foreach (var frequency in running ? new[] { 660, 990 } : new[] { 990, 440 })
            for (var i = 0; i < noteSamples; i++)
            {
                var envelope = Math.Min(1, Math.Min(i, noteSamples - 1 - i) / 150.0);
                writer.Write((short)(Math.Sin(2 * Math.PI * frequency * i / rate) * 5000 * envelope));
            }
        return stream.ToArray();
    }
}
