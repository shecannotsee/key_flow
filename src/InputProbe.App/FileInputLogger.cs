using InputProbe.Core;

namespace InputProbe.App;

public sealed class FileInputLogger(string path) : IInputLogger
{
    private readonly object _gate = new();
    public void Info(string message) => Write("INFO", message);
    public void Error(string message, Exception exception) => Write("ERROR", $"{message}: {exception.Message}");
    private void Write(string level, string message)
    {
        lock (_gate)
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}");
    }
}
