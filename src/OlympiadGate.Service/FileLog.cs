using OlympiadGate.Core;

namespace OlympiadGate.Service;

public sealed class FileLog
{
    private readonly object _gate = new();

    public void Info(string message) => Write("INFO", message);

    public void Error(string message, Exception? exception = null)
    {
        Write("ERROR", exception == null ? message : message + " " + exception);
    }

    private void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}";
            lock (_gate)
                File.AppendAllText(AppPaths.LogPath, line);
        }
        catch
        {
            // Журнал не должен останавливать службу.
        }
    }
}
